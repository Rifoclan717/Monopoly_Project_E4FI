using System;

namespace CampusTycoon
{
    /// <summary>Loyer d'une case</summary>
    public static class RentCalculator
    {
        /// <param name="diceTotal">Total des dés du visiteur (utile pour la Machine à café)</param>
        public static int Rent(GameState state, BoardSquare square, int diceTotal)
        {
            if (!square.IsOwned) return 0;

            SquareData data = square.Data;
            var rents = data.Rents;
            if (rents.Count == 0) return 0;

            switch (data.Type)
            {
                case SquareType.Property:
                {
                    int rent;
                    if (square.UpgradeLevel > 0)
                        rent = rents[Math.Min(1 + square.UpgradeLevel, rents.Count - 1)];
                    else if (state.HasMonopoly(square.OwnerIndex, data.Group))
                        rent = rents[1];
                    else
                        rent = rents[0];

                    if (state.ActiveEvent == GlobalEventType.OpenDay) rent *= 2;
                    return rent;
                }

                case SquareType.Station:
                {
                    if (state.ActiveEvent == GlobalEventType.TransportStrike) return 0;
                    int count = state.CountOwned(square.OwnerIndex, SquareType.Station);
                    return rents[Math.Min(count, rents.Count) - 1];
                }

                case SquareType.Utility:
                    return rents[0] * diceTotal;

                default:
                    return 0;
            }
        }
    }
}