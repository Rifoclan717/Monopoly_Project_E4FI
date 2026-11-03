using System;
using System.Collections.Generic;
using System.Globalization;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Fonctions d'utilité de l'IA. Elles sont déterministes : l'aléatoire lié à la personnalité est appliqué par AIBrain.
    ///
    /// Remarque importante sur les règles Campus Tycoon : la fortune finale compte le prix d'achat
    /// des terrains et le coût payé des améliorations. Acheter ou construire ne fait donc pas baisser
    /// la fortune ; le vrai coût est le risque de devoir revendre à 50 % faute de liquidités.
    /// C'est pourquoi le prix n'est pas soustrait du score (contrairement au premier jet du PDF) :
    /// il intervient à travers la pénalité de liquidité.
    /// </summary>
    public static class UtilityScorer
    {
        /// <summary>Valeur moyenne d'une carte Événement, calculée sur le paquet de 12 cartes (environ +25).</summary>
        public const double EventCardValue = 25;
        /// <summary>Coût estimé d'un envoi en colle : un tour perdu ou 50 payés.</summary>
        public const double GoToDetentionCost = 40;
        /// <summary>Gain net moyen de la Roulette du BDE pour une mise de 50 : (0+0+50+50+150+250)/6 - 50.</summary>
        public const double RouletteExpectedGain = 500.0 / 6.0 - 50.0;

        // ------------------------------------------------------------------
        // Liquidité
        // ------------------------------------------------------------------

        /// <summary>
        /// Pénalité de risque pour un joueur qui garderait <paramref name="moneyAfter"/> en caisse :
        /// dépenses espérées sur ses 2 prochains tours + la moitié du plus gros loyer adverse.
        /// Le manque éventuel est compté à 50 %, la perte subie en revendant à la banque.
        /// </summary>
        public static double LiquidityPenalty(AIGameSnapshot snap, int me, int moneyAfter, AIParameters p)
        {
            if (moneyAfter < 0) return 1e9;
            double exposure = RentEstimator.ExpectedPayments(snap, me, 2)
                              + 0.5 * RentEstimator.MaxRentAgainst(snap, me);
            double shortfall = Math.Max(0, exposure - moneyAfter);
            return p.RiskWeight * shortfall * 0.5;
        }

        /// <summary>Hausse de la pénalité de liquidité si l'on dépense <paramref name="cost"/>.</summary>
        public static double RiskDelta(AIGameSnapshot snap, int me, int cost, AIParameters p)
        {
            int money = snap.Player(me).Money;
            return LiquidityPenalty(snap, me, money - cost, p) - LiquidityPenalty(snap, me, money, p);
        }

        public static bool RespectsReserve(AIGameSnapshot snap, int me, int cost, AIParameters p) =>
            snap.Player(me).Money - cost >= p.CashReserve;

        // ------------------------------------------------------------------
        // Achat
        // ------------------------------------------------------------------

        /// <summary>
        /// Score achat = poids revenu × revenu locatif gagné (synergies de monopole et de gares comprises)
        ///             + poids blocage × revenu maximal qu'un rival aurait tiré de ce terrain
        ///             + poids monopole × potentiel d'amélioration si l'achat complète un groupe
        ///             - hausse du risque de liquidité.
        /// </summary>
        public static double BuyScore(AIGameSnapshot snap, int me, int square, AIParameters p, out string reason)
        {
            var sq = snap.Squares[square];
            var owners = snap.Owners;
            var after = RentEstimator.Copy(owners);
            after[square] = me;

            double gain = RentEstimator.ExpectedIncome(snap, me, after, snap.Levels)
                          - RentEstimator.ExpectedIncome(snap, me, owners, snap.Levels);

            double denial = 0;
            foreach (var opp in snap.Opponents(me))
            {
                var alt = RentEstimator.Copy(owners);
                alt[square] = opp.Index;
                double oppGain = RentEstimator.ExpectedIncome(snap, opp.Index, alt, snap.Levels)
                                 - RentEstimator.ExpectedIncome(snap, opp.Index, owners, snap.Levels);
                denial = Math.Max(denial, oppGain);
            }

            bool completes = sq.Type == SquareType.Property && snap.HasMonopoly(me, sq.Group, after);
            double potential = completes ? UpgradePotential(snap, me, sq.Group, after) : 0;

            double risk = RiskDelta(snap, me, sq.Price, p);

            double score = p.IncomeWeight * gain + p.BlockWeight * denial + p.MonopolyWeight * potential - risk;

            reason = string.Format(CultureInfo.InvariantCulture,
                "{0} : revenu espéré +{1:0}, blocage rival {2:0}{3}, risque -{4:0}",
                sq.Name, gain, denial, completes ? ", complète le monopole" : string.Empty, risk);
            return score;
        }

        /// <summary>
        /// Définition des règles pour le Techos : terrain qui complète ou démarre un groupe
        /// où aucun rival ne possède déjà un terrain, ou gare.
        /// </summary>
        public static bool IsProfitableForTechie(AIGameSnapshot snap, int me, int square)
        {
            var sq = snap.Squares[square];
            if (sq.Type == SquareType.Station) return true;
            if (sq.Type != SquareType.Property) return false;

            foreach (int s in snap.GroupMembers(sq.Group))
            {
                int owner = snap.Owners[s];
                if (s != square && owner != AIGameSnapshot.NoOwner && owner != me) return false;
            }
            return true;
        }

        /// <summary>Revenu supplémentaire si tous les terrains du groupe passaient au niveau 1.</summary>
        private static double UpgradePotential(AIGameSnapshot snap, int me, ColorGroup group, int[] owners)
        {
            var levels = RentEstimator.Copy(snap.Levels);
            foreach (int s in snap.GroupMembers(group))
                if (levels[s] == 0) levels[s] = 1;

            return RentEstimator.ExpectedIncome(snap, me, owners, levels)
                   - RentEstimator.ExpectedIncome(snap, me, owners, snap.Levels);
        }

        // ------------------------------------------------------------------
        // Amélioration
        // ------------------------------------------------------------------

        /// <summary>Coût d'un niveau, divisé par deux pendant les Soldes de la rentrée.</summary>
        public static int UpgradeCost(AIGameSnapshot snap, int square)
        {
            int cost = snap.Squares[square].UpgradeCost;
            return snap.ActiveEvent == GlobalEventType.BackToSchoolSale ? cost / 2 : cost;
        }

        /// <summary>Amélioration légale : terrain à soi, en monopole, niveau max non atteint, pas déjà amélioré ce tour, argent suffisant.</summary>
        public static bool CanUpgrade(AIGameSnapshot snap, int me, int square)
        {
            var sq = snap.Squares[square];
            return sq.Type == SquareType.Property
                   && snap.Owners[square] == me
                   && snap.HasMonopoly(me, sq.Group)
                   && snap.Levels[square] < sq.MaxLevel
                   && !snap.UpgradedThisTurn.Contains(square)
                   && snap.Player(me).Money >= UpgradeCost(snap, square);
        }

        /// <summary>Score amélioration = poids revenu × gain marginal de loyer sur les tours adverses restants - hausse du risque.</summary>
        public static double UpgradeScore(AIGameSnapshot snap, int me, int square, AIParameters p, out string reason)
        {
            double gain = UpgradeGain(snap, me, square);
            int cost = UpgradeCost(snap, square);
            double risk = RiskDelta(snap, me, cost, p);
            double score = p.IncomeWeight * gain - risk;

            reason = string.Format(CultureInfo.InvariantCulture,
                "{0} niv. {1} -> {2} : revenu espéré +{3:0} pour {4}{5}, risque -{6:0}",
                snap.Squares[square].Name, snap.Levels[square], snap.Levels[square] + 1, gain, cost,
                snap.ActiveEvent == GlobalEventType.BackToSchoolSale ? " (soldes)" : string.Empty, risk);
            return score;
        }

        public static double UpgradeGain(AIGameSnapshot snap, int me, int square)
        {
            var levels = RentEstimator.Copy(snap.Levels);
            levels[square]++;
            return RentEstimator.ExpectedIncome(snap, me, snap.Owners, levels)
                   - RentEstimator.ExpectedIncome(snap, me, snap.Owners, snap.Levels);
        }

        /// <summary>Carte « Amélioration offerte » : terrain en monopole qui gagne le plus, ou -1.</summary>
        public static int BestFreeUpgrade(AIGameSnapshot snap, int me)
        {
            int best = -1;
            double bestGain = double.NegativeInfinity;
            foreach (int s in snap.OwnedBy(me))
            {
                var sq = snap.Squares[s];
                if (sq.Type != SquareType.Property || !snap.HasMonopoly(me, sq.Group) || snap.Levels[s] >= sq.MaxLevel)
                    continue;
                double g = UpgradeGain(snap, me, s);
                if (g > bestGain) { bestGain = g; best = s; }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Valeur d'une case d'arrivée (colle, Relance)
        // ------------------------------------------------------------------

        /// <summary>Valeur, pour <paramref name="me"/>, de s'arrêter sur <paramref name="square"/> maintenant.</summary>
        public static double LandingValue(AIGameSnapshot snap, int me, int square, AIParameters p)
        {
            var sq = snap.Squares[square];
            int owner = snap.Owners[square];

            switch (sq.Type)
            {
                case SquareType.Tax: return -sq.Amount;
                case SquareType.StudentLounge: return sq.Amount;
                case SquareType.EventCard: return EventCardValue;
                case SquareType.GoToDetention: return -GoToDetentionCost;
                case SquareType.Roulette:
                    return RespectsReserve(snap, me, sq.Amount, p) ? Math.Max(0, RouletteExpectedGain) : 0;
            }

            if (!sq.IsOwnable) return 0;
            if (owner == me) return 0;
            if (owner != AIGameSnapshot.NoOwner) return -RentEstimator.Rent(snap, square);

            // Terrain libre : occasion d'achat, si la réserve le permet.
            if (!RespectsReserve(snap, me, sq.Price, p)) return 0;
            return Math.Max(0, BuyScore(snap, me, square, p, out _));
        }

        /// <summary>Valeur espérée d'un lancer depuis <paramref name="from"/> (bonus de Départ compris).</summary>
        public static double ExpectedRollValue(AIGameSnapshot snap, int me, int from, AIParameters p)
        {
            var land = BoardProbabilities.SingleRoll(snap, from);
            double value = BoardProbabilities.PassStartProbability(snap, from) * snap.StartBonus;
            for (int s = 0; s < land.Length; s++)
                if (land[s] > 0) value += land[s] * LandingValue(snap, me, s, p);
            return value;
        }

        /// <summary>
        /// Valeur de jouer plutôt que d'attendre en colle. Attendre ne retarde pas seulement le lancer :
        /// le nombre de tours étant fixe, c'est un déplacement de moins sur la partie, donc en moyenne
        /// une part du bonus de La Rentrée en moins (7 × 7/6 cases sur un plateau de N cases).
        /// </summary>
        public static double DetentionPlayValue(AIGameSnapshot snap, int me, int from, AIParameters p)
        {
            double immediate = ExpectedRollValue(snap, me, from, p);
            double passStart = BoardProbabilities.PassStartProbability(snap, from) * snap.StartBonus;
            if (snap.Player(me).TurnsRemaining <= 0) return immediate;

            double tempo = snap.StartBonus * RentEstimator.AverageDiceSum * (1.0 + BoardProbabilities.DoublesBonus) / snap.SquareCount;
            return immediate - passStart + tempo;
        }

        // ------------------------------------------------------------------
        // Billet d'absence
        // ------------------------------------------------------------------

        /// <summary>Valeur de garder le billet pour annuler un futur loyer.</summary>
        public static double TicketKeepValue(AIGameSnapshot snap, int me)
        {
            if (snap.Player(me).TurnsRemaining <= 0) return 0;
            return 0.5 * RentEstimator.MaxRentAgainst(snap, me);
        }

        public static bool ShouldUseTicketOnRent(AIGameSnapshot snap, int me, int rent)
        {
            var player = snap.Player(me);
            if (!player.HasExemptionTicket || rent <= 0) return false;
            if (rent > player.Money) return true;                  // évite une vente forcée
            if (player.TurnsRemaining <= 1) return true;            // dernière occasion
            return rent >= Math.Max(40, 0.8 * RentEstimator.MaxRentAgainst(snap, me));
        }

        // ------------------------------------------------------------------
        // Vente forcée
        // ------------------------------------------------------------------

        /// <summary>
        /// Liste des ventes à effectuer pour réunir <paramref name="amountNeeded"/>.
        /// Les améliorations d'un terrain partent avant le terrain. Par défaut, on vend ce qui vaut le moins ;
        /// en difficile, ce qui coûte le moins en revenus futurs par unité d'argent récupérée.
        /// Si tout vendre ne suffit pas, la liste contient tout (le joueur sera éliminé).
        /// </summary>
        public static List<AIDecision> PlanForcedSale(AIGameSnapshot snap, int me, int amountNeeded, AIParameters p)
        {
            var sim = snap.Clone();
            var plan = new List<AIDecision>();
            int raised = 0;

            while (raised < amountNeeded)
            {
                int best = -1;
                bool bestIsUpgrade = false;
                int bestCash = 0;
                double bestKey = double.PositiveInfinity;
                double bestLoss = 0;
                double incomeNow = p.SmartSelling ? RentEstimator.ExpectedIncome(sim, me) : 0;

                foreach (int s in sim.OwnedBy(me))
                {
                    var sq = sim.Squares[s];
                    bool isUpgrade = sim.Levels[s] > 0;
                    int cash = (isUpgrade ? sq.UpgradeCost : sq.Price) / 2;
                    if (cash <= 0) continue;

                    double loss = 0;
                    if (p.SmartSelling)
                    {
                        var owners = RentEstimator.Copy(sim.Owners);
                        var levels = RentEstimator.Copy(sim.Levels);
                        if (isUpgrade) levels[s]--; else owners[s] = AIGameSnapshot.NoOwner;
                        loss = incomeNow - RentEstimator.ExpectedIncome(sim, me, owners, levels);
                    }

                    // Clé de tri : plus petite valeur d'abord (règle), ou perte de revenu par unité d'argent (difficile).
                    double key = p.SmartSelling ? loss / cash : cash + loss * 1e-6;
                    if (key < bestKey)
                    {
                        bestKey = key;
                        best = s;
                        bestIsUpgrade = isUpgrade;
                        bestCash = cash;
                        bestLoss = loss;
                    }
                }

                if (best < 0) break;

                if (bestIsUpgrade) sim.Levels[best]--;
                else sim.Owners[best] = AIGameSnapshot.NoOwner;
                sim.Player(me).Money += bestCash;
                raised += bestCash;

                string what = bestIsUpgrade ? "un niveau de " + sim.Squares[best].Name : sim.Squares[best].Name;
                plan.Add(new AIDecision(
                    bestIsUpgrade ? AIActionType.SellUpgrade : AIActionType.SellProperty,
                    best, -bestLoss,
                    string.Format(CultureInfo.InvariantCulture, "Vend {0} pour {1} ({2}/{3})", what, bestCash, raised, amountNeeded)));
            }
            return plan;
        }
    }
}
