using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusTycoon.AI.Simulation
{
    /// <summary>
    /// Moteur de partie simplifié, uniquement pour tester et régler l'IA (ce n'est pas la logique du jeu).
    /// Règles du niveau 2 : 3 joueurs, 1 000, +150 à la Rentrée, 15 tours, doubles, colle, 12 cartes,
    /// pouvoirs, événements du semestre aux fins des tours 5 et 10, ventes forcées et faillite.
    /// </summary>
    public sealed class GameSimulator
    {
        public const int MaxTurns = 15;
        private readonly Random rng;
        private readonly AIGameSnapshot g;
        private readonly AIBrain[] brains;
        private readonly int[] turnsPlayed;
        private readonly bool[] skipNext;
        private readonly List<int> deck;
        private int deckPos;
        private readonly List<GlobalEventType> eventsLeft;

        public readonly Stats Stats = new Stats();

        public GameSimulator(AIParameters[] parameters, int seed)
        {
            rng = new Random(seed);
            var players = new List<PlayerInfo>();
            for (int i = 0; i < parameters.Length; i++)
                players.Add(new PlayerInfo { Index = i, Character = parameters[i].Character, Money = 1000, TurnsRemaining = MaxTurns });
            g = new AIGameSnapshot(Boards.Level2(), players);

            brains = new AIBrain[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) brains[i] = new AIBrain(i, parameters[i], seed * 31 + i + 1);

            turnsPlayed = new int[parameters.Length];
            skipNext = new bool[parameters.Length];
            deck = Enumerable.Range(1, 12).OrderBy(_ => rng.Next()).ToList();
            eventsLeft = Enum.GetValues(typeof(GlobalEventType)).Cast<GlobalEventType>().ToList();
        }

        private PlayerInfo P(int i) => g.Player(i);
        private int Roll() => rng.Next(1, 7);

        /// <summary>Joue la partie et renvoie l'index du gagnant.</summary>
        public int Play()
        {
            for (int round = 1; round <= MaxTurns; round++)
            {
                for (int i = 0; i < g.Players.Count; i++)
                {
                    if (P(i).IsBankrupt) continue;
                    turnsPlayed[i]++;
                    UpdateTurnsRemaining();
                    PlayTurn(i);
                    g.UpgradedThisTurn.Clear();
                    if (g.Players.Count(p => !p.IsBankrupt) <= 1) return Finish();
                }

                if (g.ActiveEvent == GlobalEventType.CareerFair)
                    foreach (var p in g.Players.Where(p => !p.IsBankrupt))
                        p.Money += 20 * g.OwnedBy(p.Index).Count(s => g.Squares[s].Type == SquareType.Property);
                g.ActiveEvent = null;

                if ((round == 5 || round == 10) && eventsLeft.Count > 0)
                {
                    int k = rng.Next(eventsLeft.Count);
                    g.ActiveEvent = eventsLeft[k];
                    eventsLeft.RemoveAt(k);
                }
            }
            return Finish();
        }

        private void UpdateTurnsRemaining()
        {
            foreach (var p in g.Players) p.TurnsRemaining = Math.Max(0, MaxTurns - turnsPlayed[p.Index]);
        }

        private int Finish()
        {
            int best = -1, bestWorth = int.MinValue;
            foreach (var p in g.Players)
            {
                int w = p.IsBankrupt ? -1 : g.NetWorth(p.Index);
                Stats.FinalWorth[p.Index] = Math.Max(0, w);
                if (p.IsBankrupt) Stats.Bankrupt[p.Index] = true;
                if (w > bestWorth) { bestWorth = w; best = p.Index; }
            }
            return best;
        }

        // ------------------------------------------------------------------

        private void PlayTurn(int i)
        {
            var me = P(i);
            if (skipNext[i]) { skipNext[i] = false; return; }

            if (me.InDetention)
            {
                var d = brains[i].DecideDetention(g);
                me.InDetention = false;
                switch (d.Action)
                {
                    case AIActionType.WaitInDetention: Stats.DetentionWaits[i]++; return;
                    case AIActionType.UseExemptionTicket: me.HasExemptionTicket = false; break;
                    default: Pay(i, -1, g.DetentionFine); Stats.DetentionPays[i]++; if (me.IsBankrupt) return; break;
                }
            }

            for (int roll = 0; roll < 2; roll++)       // un seul rejeu sur double
            {
                int a = Roll(), b = Roll();
                if (!me.PowerUsed && brains[i].Parameters.Power == PowerType.Reroll)
                {
                    var ctx = new PowerContext { Moment = PowerMoment.DiceRolled, FromPosition = me.Position, DiceTotal = a + b };
                    if (brains[i].DecidePower(g, ctx).Action == AIActionType.UsePower)
                    {
                        me.PowerUsed = true; Stats.PowersUsed[i]++;
                        a = Roll(); b = Roll();
                    }
                }

                Move(i, a + b, true);
                Resolve(i, a + b, 0);
                if (me.IsBankrupt || me.InDetention) return;
                if (a != b) break;
            }

            foreach (var up in brains[i].DecideUpgrades(g))
            {
                int cost = UtilityScorer.UpgradeCost(g, up.Square);
                if (!UtilityScorer.CanUpgrade(g, i, up.Square)) continue;
                me.Money -= cost;
                g.Levels[up.Square]++;
                g.UpgradedThisTurn.Add(up.Square);
                Stats.Upgrades[i]++;
            }
        }

        private void Move(int i, int steps, bool collectStart)
        {
            var me = P(i);
            int n = g.SquareCount;
            int target = me.Position + steps;
            if (collectStart && target >= n) me.Money += g.StartBonus;
            me.Position = ((target % n) + n) % n;
        }

        private void SendToDetention(int i)
        {
            P(i).Position = g.DetentionIndex;
            P(i).InDetention = true;
            Stats.Detentions[i]++;
        }

        private void Resolve(int i, int dice, int depth)
        {
            var me = P(i);
            int s = me.Position;
            var sq = g.Squares[s];

            switch (sq.Type)
            {
                case SquareType.Property:
                case SquareType.Station:
                case SquareType.Utility:
                {
                    int owner = g.Owners[s];
                    if (owner == AIGameSnapshot.NoOwner)
                    {
                        if (me.Money >= sq.Price && brains[i].DecideBuy(g, s).Action == AIActionType.Buy)
                        {
                            me.Money -= sq.Price;
                            g.Owners[s] = i;
                            Stats.Purchases[i]++;
                        }
                    }
                    else if (owner != i)
                    {
                        int rent = sq.Type == SquareType.Utility ? sq.Rents[0] * dice : (int)RentEstimator.Rent(g, s);
                        if (rent <= 0) break;

                        var o = P(owner);
                        if (!o.PowerUsed && brains[owner].Parameters.Power == PowerType.DoubleRent &&
                            brains[owner].DecidePower(g, new PowerContext { Moment = PowerMoment.CollectingRent, Amount = rent }).Action == AIActionType.UsePower)
                        { o.PowerUsed = true; Stats.PowersUsed[owner]++; rent *= 2; }

                        if (me.HasExemptionTicket && brains[i].ShouldUseTicketOnRent(g, rent))
                        { me.HasExemptionTicket = false; Stats.TicketsUsed[i]++; break; }

                        if (!me.PowerUsed && brains[i].Parameters.Power == PowerType.CancelPayment &&
                            brains[i].DecidePower(g, new PowerContext { Moment = PowerMoment.PayingRent, Amount = rent }).Action == AIActionType.UsePower)
                        { me.PowerUsed = true; Stats.PowersUsed[i]++; break; }

                        Pay(i, owner, rent);
                        Stats.RentPaid[i] += rent;
                    }
                    break;
                }

                case SquareType.Tax:
                    if (!me.PowerUsed && brains[i].Parameters.Power == PowerType.CancelPayment &&
                        brains[i].DecidePower(g, new PowerContext { Moment = PowerMoment.PayingTax, Amount = sq.Amount }).Action == AIActionType.UsePower)
                    { me.PowerUsed = true; Stats.PowersUsed[i]++; break; }
                    Pay(i, -1, sq.Amount);
                    break;

                case SquareType.StudentLounge:
                    me.Money += sq.Amount;
                    break;

                case SquareType.GoToDetention:
                    SendToDetention(i);
                    break;

                case SquareType.EventCard:
                    if (depth < 3) DrawCard(i, depth);
                    break;
            }
        }

        private void DrawCard(int i, int depth)
        {
            var me = P(i);
            int card;
            do { card = deck[deckPos++ % deck.Count]; }
            while (card == 10 && g.Players.Any(p => p.HasExemptionTicket));  // le billet est détenu par un joueur

            switch (card)
            {
                case 1: Pay(i, -1, 60); break;
                case 2: me.Money += 100; break;
                case 3: { int d = Roll() + Roll(); Move(i, d, true); Resolve(i, d, depth + 1); break; }
                case 4: Move(i, -3, false); Resolve(i, 0, depth + 1); break;
                case 5: me.Position = 0; me.Money += g.StartBonus; break;
                case 6: SendToDetention(i); break;
                case 7:
                    foreach (var o in g.Opponents(i).ToList())
                    {
                        Pay(o.Index, i, 40);
                    }
                    break;
                case 8: Pay(i, -1, 40); break;
                case 9:
                {
                    int s = brains[i].ChooseFreeUpgrade(g);
                    if (s >= 0) { g.Levels[s]++; Stats.Upgrades[i]++; }
                    else me.Money += 60;
                    break;
                }
                case 10: me.HasExemptionTicket = true; break;
                case 11: skipNext[i] = true; break;
                case 12: me.Money += 50; break;
            }
        }

        /// <summary>Paiement avec vente forcée puis faillite si nécessaire. creditor = -1 pour la banque.</summary>
        private void Pay(int payer, int creditor, int amount)
        {
            var p = P(payer);
            if (p.IsBankrupt) return;

            if (p.Money < amount)
            {
                foreach (var sale in brains[payer].ChooseAssetsToSell(g, amount - p.Money))
                {
                    if (p.Money >= amount) break;
                    var sq = g.Squares[sale.Square];
                    if (sale.Action == AIActionType.SellUpgrade && g.Levels[sale.Square] > 0)
                    { g.Levels[sale.Square]--; p.Money += sq.UpgradeCost / 2; }
                    else if (sale.Action == AIActionType.SellProperty && g.Owners[sale.Square] == payer && g.Levels[sale.Square] == 0)
                    { g.Owners[sale.Square] = AIGameSnapshot.NoOwner; p.Money += sq.Price / 2; }
                    Stats.Sales[payer]++;
                }
            }

            if (p.Money >= amount)
            {
                p.Money -= amount;
                if (creditor >= 0) P(creditor).Money += amount;
                return;
            }

            // Faillite : le créancier reçoit l'argent restant, les terrains retournent à la banque.
            if (creditor >= 0) P(creditor).Money += p.Money;
            p.Money = 0;
            p.IsBankrupt = true;
            for (int s = 0; s < g.SquareCount; s++)
                if (g.Owners[s] == payer) { g.Owners[s] = AIGameSnapshot.NoOwner; g.Levels[s] = 0; }
        }
    }

    public sealed class Stats
    {
        public readonly int[] FinalWorth = new int[3];
        public readonly bool[] Bankrupt = new bool[3];
        public readonly int[] Purchases = new int[3];
        public readonly int[] Upgrades = new int[3];
        public readonly int[] Sales = new int[3];
        public readonly int[] RentPaid = new int[3];
        public readonly int[] PowersUsed = new int[3];
        public readonly int[] TicketsUsed = new int[3];
        public readonly int[] Detentions = new int[3];
        public readonly int[] DetentionPays = new int[3];
        public readonly int[] DetentionWaits = new int[3];
    }
}
