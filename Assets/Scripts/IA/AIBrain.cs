using System;
using System.Collections.Generic;
using System.Globalization;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Cerveau d'une IA, en C# pur. Pour chaque décision : on élimine les actions illégales,
    /// on applique les contraintes de liquidité, on calcule le score des actions restantes,
    /// puis on applique la personnalité (chances d'achat, de construction, de pouvoir).
    ///
    /// Le cerveau ne modifie jamais le snapshot reçu : la logique du jeu applique la décision.
    /// </summary>
    public sealed class AIBrain
    {
        public AIParameters Parameters { get; }
        public int PlayerIndex { get; }
        private readonly Random rng;

        public AIBrain(int playerIndex, AIParameters parameters, int seed)
        {
            PlayerIndex = playerIndex;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            rng = seed == 0 ? new Random() : new Random(seed);
        }

        private bool Roll(float chance) => chance >= 1f || (chance > 0f && rng.NextDouble() < chance);

        private static string Pct(float chance) => ((int)Math.Round(chance * 100)).ToString(CultureInfo.InvariantCulture) + " %";

        // ------------------------------------------------------------------
        // Achat sur terrain libre
        // ------------------------------------------------------------------

        public AIDecision DecideBuy(AIGameSnapshot snap, int square)
        {
            var p = Parameters;
            var sq = snap.Squares[square];
            int me = PlayerIndex;

            if (!sq.IsOwnable || snap.Owners[square] != AIGameSnapshot.NoOwner)
                return new AIDecision(AIActionType.Pass, square, 0, sq.Name + " n'est pas achetable");

            if (!UtilityScorer.RespectsReserve(snap, me, sq.Price, p))
                return new AIDecision(AIActionType.Pass, square, 0,
                    string.Format(CultureInfo.InvariantCulture, "{0} : il resterait {1}, réserve de {2}",
                        sq.Name, snap.Player(me).Money - sq.Price, p.CashReserve));

            double score = UtilityScorer.BuyScore(snap, me, square, p, out string reason);

            if (p.BuyOnlyIfProfitable && !UtilityScorer.IsProfitableForTechie(snap, me, square))
                return new AIDecision(AIActionType.Pass, square, score, reason + " ; pas rentable (un rival est déjà dans le groupe)");

            if (score <= 0)
                return new AIDecision(AIActionType.Pass, square, score, reason);

            if (!Roll(p.BuyChance))
                return new AIDecision(AIActionType.Pass, square, score, reason + " ; hésite (chance d'achat " + Pct(p.BuyChance) + ")");

            return new AIDecision(AIActionType.Buy, square, score, reason);
        }

        // ------------------------------------------------------------------
        // Améliorations (phase d'actions facultatives)
        // ------------------------------------------------------------------

        /// <summary>
        /// Améliorations à effectuer ce tour, dans l'ordre. Chaque terrain gagne au plus un niveau.
        /// Les scores sont recalculés après chaque amélioration, car l'argent disponible baisse.
        /// </summary>
        public List<AIDecision> DecideUpgrades(AIGameSnapshot snap)
        {
            var p = Parameters;
            int me = PlayerIndex;
            var sim = snap.Clone();
            var result = new List<AIDecision>();
            var considered = new HashSet<int>();

            while (true)
            {
                int best = -1;
                double bestScore = 0;
                string bestReason = null;

                foreach (int s in sim.OwnedBy(me))
                {
                    if (considered.Contains(s) || !UtilityScorer.CanUpgrade(sim, me, s)) continue;
                    if (!UtilityScorer.RespectsReserve(sim, me, UtilityScorer.UpgradeCost(sim, s), p)) continue;

                    double score = UtilityScorer.UpgradeScore(sim, me, s, p, out string reason);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = s;
                        bestReason = reason;
                    }
                }

                if (best < 0) break;
                considered.Add(best);

                // Personnalité : le Fêtard ne construit qu'une fois sur trois environ.
                if (!Roll(p.BuildChance)) continue;

                int cost = UtilityScorer.UpgradeCost(sim, best);
                sim.Player(me).Money -= cost;
                sim.Levels[best]++;
                sim.UpgradedThisTurn.Add(best);
                result.Add(new AIDecision(AIActionType.Upgrade, best, bestScore, bestReason));
            }
            return result;
        }

        /// <summary>Carte « Amélioration offerte » : terrain choisi, ou -1 (la carte donne alors 60).</summary>
        public int ChooseFreeUpgrade(AIGameSnapshot snap) => UtilityScorer.BestFreeUpgrade(snap, PlayerIndex);

        // ------------------------------------------------------------------
        // Colle et billet d'absence
        // ------------------------------------------------------------------

        /// <summary>
        /// Début de tour en colle : attendre (score 0), payer l'amende, ou utiliser le billet d'absence.
        /// Score jouer = valeur espérée du lancer depuis la colle - coût de l'option.
        /// </summary>
        public AIDecision DecideDetention(AIGameSnapshot snap)
        {
            var p = Parameters;
            int me = PlayerIndex;
            var player = snap.Player(me);
            int from = snap.DetentionIndex >= 0 ? snap.DetentionIndex : player.Position;
            double play = UtilityScorer.DetentionPlayValue(snap, me, from, p);

            var best = new AIDecision(AIActionType.WaitInDetention, -1, 0,
                string.Format(CultureInfo.InvariantCulture, "Valeur d'un lancer {0:0} : attendre", play));

            if (player.HasExemptionTicket)
            {
                double keep = UtilityScorer.TicketKeepValue(snap, me);
                double s = play - keep;
                if (s > best.Score)
                    best = new AIDecision(AIActionType.UseExemptionTicket, -1, s,
                        string.Format(CultureInfo.InvariantCulture, "Lancer {0:0} > valeur du billet gardé {1:0}", play, keep));
            }

            int fine = snap.DetentionFine;
            if (player.Money >= fine)
            {
                double s = play - fine - UtilityScorer.RiskDelta(snap, me, fine, p);
                if (s > best.Score)
                    best = new AIDecision(AIActionType.PayDetentionFine, -1, s,
                        string.Format(CultureInfo.InvariantCulture, "Lancer {0:0} > amende {1}", play, fine));
            }
            return best;
        }

        /// <summary>Utiliser le billet d'absence pour annuler ce loyer ?</summary>
        public bool ShouldUseTicketOnRent(AIGameSnapshot snap, int rent) =>
            UtilityScorer.ShouldUseTicketOnRent(snap, PlayerIndex, rent);

        // ------------------------------------------------------------------
        // Pouvoir du personnage
        // ------------------------------------------------------------------

        public AIDecision DecidePower(AIGameSnapshot snap, PowerContext ctx)
        {
            var p = Parameters;
            int me = PlayerIndex;
            var player = snap.Player(me);

            if (player.PowerUsed)
                return Keep("pouvoir déjà utilisé");

            switch (p.Power)
            {
                case PowerType.DoubleRent:
                {
                    if (ctx.Moment != PowerMoment.CollectingRent) return Keep("pas un encaissement");
                    if (ctx.Amount < p.PowerTriggerAmount) return Keep("loyer trop faible");
                    if (p.SmartPower)
                    {
                        double best = RentEstimator.MaxOwnRent(snap, me);
                        bool lastChance = player.TurnsRemaining <= 1;
                        if (!lastChance && ctx.Amount < 0.6 * best)
                            return Keep(string.Format(CultureInfo.InvariantCulture, "attend un plus gros loyer (max actuel {0:0})", best));
                        return Use(ctx.Amount, "Dossier béton : loyer doublé (+" + ctx.Amount + ")");
                    }
                    return Roll(p.PowerTriggerChance)
                        ? Use(ctx.Amount, "Dossier béton : loyer doublé (+" + ctx.Amount + ")")
                        : Keep("hésite");
                }

                case PowerType.CancelPayment:
                {
                    if (ctx.Moment != PowerMoment.PayingRent && ctx.Moment != PowerMoment.PayingTax)
                        return Keep("pas un paiement");
                    if (p.SmartPower)
                    {
                        bool ruin = ctx.Amount > player.Money;
                        double worst = Math.Max(RentEstimator.MaxRentAgainst(snap, me), ctx.Amount);
                        bool lastChance = player.TurnsRemaining <= 1 && ctx.Amount >= p.PowerTriggerAmount;
                        if (ruin || lastChance || (ctx.Amount >= p.PowerTriggerAmount && ctx.Amount >= 0.6 * worst))
                            return Use(ctx.Amount, "Soirée mémorable : paiement de " + ctx.Amount + " annulé");
                        return Keep("garde le pouvoir pour un plus gros paiement");
                    }
                    if (ctx.Amount < p.PowerTriggerAmount) return Keep("paiement trop faible");
                    return Roll(p.PowerTriggerChance)
                        ? Use(ctx.Amount, "Soirée mémorable : paiement de " + ctx.Amount + " annulé")
                        : Keep("hésite");
                }

                default: // Reroll
                {
                    if (ctx.Moment != PowerMoment.DiceRolled || ctx.FromPosition < 0) return Keep("pas un lancer");
                    int n = snap.SquareCount;
                    int landing = (ctx.FromPosition + ctx.DiceTotal) % n;
                    double valueNow = UtilityScorer.LandingValue(snap, me, landing, p)
                                      + (ctx.FromPosition + ctx.DiceTotal >= n ? snap.StartBonus : 0);
                    bool toDetention = snap.Squares[landing].Type == SquareType.GoToDetention;
                    double rent = snap.Owners[landing] != AIGameSnapshot.NoOwner && snap.Owners[landing] != me
                        ? RentEstimator.Rent(snap, landing) : 0;

                    if (p.SmartPower)
                    {
                        double expected = UtilityScorer.ExpectedRollValue(snap, me, ctx.FromPosition, p);
                        double gain = expected - valueNow;
                        if ((toDetention || rent >= p.PowerTriggerAmount) && gain > 20)
                            return Use(gain, string.Format(CultureInfo.InvariantCulture,
                                "Relance : arrivée sur {0} vaut {1:0}, un nouveau lancer vaut {2:0}",
                                snap.Squares[landing].Name, valueNow, expected));
                        return Keep("le lancer actuel est acceptable");
                    }

                    if (!toDetention && rent < p.PowerTriggerAmount) return Keep("lancer acceptable");
                    return Roll(p.PowerTriggerChance)
                        ? Use(-valueNow, "Relance pour éviter " + snap.Squares[landing].Name)
                        : Keep("hésite");
                }
            }
        }

        private static AIDecision Use(double score, string reason) => new AIDecision(AIActionType.UsePower, -1, score, reason);
        private static AIDecision Keep(string reason) => new AIDecision(AIActionType.KeepPower, -1, 0, reason);

        // ------------------------------------------------------------------
        // Vente forcée et Roulette
        // ------------------------------------------------------------------

        public List<AIDecision> ChooseAssetsToSell(AIGameSnapshot snap, int amountNeeded) =>
            UtilityScorer.PlanForcedSale(snap, PlayerIndex, amountNeeded, Parameters);

        /// <summary>Roulette du BDE (niveau 3) : gain net moyen +33, joué si la réserve le permet.</summary>
        public AIDecision DecideRoulette(AIGameSnapshot snap, int square)
        {
            var p = Parameters;
            int stake = snap.Squares[square].Amount;
            if (!UtilityScorer.RespectsReserve(snap, PlayerIndex, stake, p))
                return new AIDecision(AIActionType.SkipRoulette, square, 0, "réserve insuffisante");

            double score = UtilityScorer.RouletteExpectedGain - UtilityScorer.RiskDelta(snap, PlayerIndex, stake, p);
            if (score <= 0) return new AIDecision(AIActionType.SkipRoulette, square, score, "trop risqué");
            if (!Roll(p.RouletteChance)) return new AIDecision(AIActionType.SkipRoulette, square, score, "pas envie de parier");
            return new AIDecision(AIActionType.PlayRoulette, square, score,
                string.Format(CultureInfo.InvariantCulture, "gain moyen +{0:0}", UtilityScorer.RouletteExpectedGain));
        }
    }
}
