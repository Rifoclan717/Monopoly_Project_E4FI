using System;
using System.Collections.Generic;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Calcule les loyers selon les règles Campus Tycoon, et les revenus ou dépenses espérés.
    /// Toutes les méthodes acceptent des tableaux owners / levels hypothétiques,
    /// ce qui permet de comparer « avant » et « après » une action.
    /// </summary>
    public static class RentEstimator
    {
        /// <summary>Moyenne de 2d6, utilisée pour la Machine à café.</summary>
        public const double AverageDiceSum = 7.0;

        /// <summary>
        /// Loyer qu'un visiteur paie en s'arrêtant sur <paramref name="square"/>.
        /// Terrain : R sans monopole, 2R en monopole, puis niveaux 1 à 3 (valeurs de SquareData).
        /// </summary>
        public static double Rent(AIGameSnapshot snap, int square, int[] owners, int[] levels, bool applyEvent)
        {
            var sq = snap.Squares[square];
            int owner = owners[square];
            if (owner == AIGameSnapshot.NoOwner || sq.Rents.Length == 0) return 0;

            switch (sq.Type)
            {
                case SquareType.Property:
                {
                    int level = levels[square];
                    double rent;
                    if (level > 0)
                        rent = sq.Rents[Math.Min(1 + level, sq.Rents.Length - 1)];
                    else if (sq.Rents.Length > 1 && snap.HasMonopoly(owner, sq.Group, owners))
                        rent = sq.Rents[1];
                    else
                        rent = sq.Rents[0];

                    if (applyEvent && snap.ActiveEvent == GlobalEventType.OpenDay) rent *= 2;
                    return rent;
                }

                case SquareType.Station:
                {
                    if (applyEvent && snap.ActiveEvent == GlobalEventType.TransportStrike) return 0;
                    int count = 0;
                    for (int s = 0; s < owners.Length; s++)
                        if (owners[s] == owner && snap.Squares[s].Type == SquareType.Station) count++;
                    return sq.Rents[Math.Min(count, sq.Rents.Length) - 1];
                }

                case SquareType.Utility:
                    return sq.Rents[0] * AverageDiceSum;

                default:
                    return 0;
            }
        }

        public static double Rent(AIGameSnapshot snap, int square, bool applyEvent = true) =>
            Rent(snap, square, snap.Owners, snap.Levels, applyEvent);

        /// <summary>
        /// Revenu de loyers espéré pour <paramref name="owner"/> sur les tours restants des adversaires,
        /// avec la répartition de propriété donnée. L'événement actif ne compte que pour le prochain tour.
        /// </summary>
        public static double ExpectedIncome(AIGameSnapshot snap, int owner, int[] owners, int[] levels)
        {
            double total = 0;
            foreach (var opp in snap.Opponents(owner))
            {
                var byTurn = BoardProbabilities.LandingByTurn(snap, opp.Position, opp.TurnsRemaining);
                for (int k = 0; k < byTurn.Length; k++)
                {
                    var visits = byTurn[k];
                    for (int s = 0; s < owners.Length; s++)
                    {
                        if (owners[s] != owner || visits[s] == 0) continue;
                        total += visits[s] * Rent(snap, s, owners, levels, applyEvent: k == 0);
                    }
                }
            }
            return total;
        }

        public static double ExpectedIncome(AIGameSnapshot snap, int owner) =>
            ExpectedIncome(snap, owner, snap.Owners, snap.Levels);

        /// <summary>
        /// Dépenses espérées de <paramref name="player"/> (loyers adverses + taxes) sur ses <paramref name="turns"/> prochains tours.
        /// </summary>
        public static double ExpectedPayments(AIGameSnapshot snap, int player, int turns)
        {
            var me = snap.Player(player);
            turns = Math.Min(turns, Math.Max(1, me.TurnsRemaining + 1));
            var byTurn = BoardProbabilities.LandingByTurn(snap, me.Position, turns);

            double total = 0;
            for (int k = 0; k < byTurn.Length; k++)
            {
                for (int s = 0; s < snap.SquareCount; s++)
                {
                    double v = byTurn[k][s];
                    if (v == 0) continue;
                    var sq = snap.Squares[s];
                    if (sq.Type == SquareType.Tax)
                        total += v * sq.Amount;
                    else if (snap.Owners[s] != AIGameSnapshot.NoOwner && snap.Owners[s] != player)
                        total += v * Rent(snap, s, snap.Owners, snap.Levels, applyEvent: k == 0);
                }
            }
            return total;
        }

        /// <summary>Plus gros loyer qu'un adversaire peut actuellement réclamer à <paramref name="player"/>.</summary>
        public static double MaxRentAgainst(AIGameSnapshot snap, int player)
        {
            double max = 0;
            for (int s = 0; s < snap.SquareCount; s++)
            {
                int owner = snap.Owners[s];
                if (owner == AIGameSnapshot.NoOwner || owner == player) continue;
                max = Math.Max(max, Rent(snap, s));
            }
            return max;
        }

        /// <summary>Plus gros loyer que <paramref name="player"/> peut actuellement encaisser.</summary>
        public static double MaxOwnRent(AIGameSnapshot snap, int player)
        {
            double max = 0;
            foreach (int s in snap.OwnedBy(player)) max = Math.Max(max, Rent(snap, s));
            return max;
        }

        internal static int[] Copy(int[] source)
        {
            var copy = new int[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
