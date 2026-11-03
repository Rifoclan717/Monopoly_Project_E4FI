using System;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Probabilités d'arrivée sur les cases avec 2d6.
    /// Remplace l'approximation uniforme 1/N : les sommes de 2d6 ne sont pas uniformes
    /// et la case Rattrapage renvoie en colle.
    ///
    /// Approximations assumées : les cartes qui déplacent le pion sont ignorées,
    /// et le rejeu sur double est compté comme un bonus moyen de 1/6 d'arrivée par tour.
    /// </summary>
    public static class BoardProbabilities
    {
        /// <summary>DiceSum[k] = probabilité que 2d6 donne k (k de 2 à 12).</summary>
        public static readonly double[] DiceSum = BuildDiceSum();

        /// <summary>Un tour sur six se termine par un double, qui donne un déplacement de plus.</summary>
        public const double DoublesBonus = 1.0 / 6.0;

        private static double[] BuildDiceSum()
        {
            var p = new double[13];
            for (int a = 1; a <= 6; a++)
                for (int b = 1; b <= 6; b++)
                    p[a + b] += 1.0 / 36.0;
            return p;
        }

        /// <summary>
        /// result[k][s] = nombre moyen d'arrivées sur la case s pendant le k-ième tour à venir
        /// d'un joueur qui part de <paramref name="start"/>.
        /// </summary>
        public static double[][] LandingByTurn(AIGameSnapshot snap, int start, int turns)
        {
            int n = snap.SquareCount;
            turns = Math.Max(0, turns);
            var result = new double[turns][];

            var position = new double[n];
            position[Mod(start, n)] = 1.0;

            for (int k = 0; k < turns; k++)
            {
                var landing = OneMove(position, n);

                var visits = new double[n];
                for (int s = 0; s < n; s++) visits[s] = landing[s] * (1.0 + DoublesBonus);
                result[k] = visits;

                // Position de départ du tour suivant : Rattrapage renvoie en colle.
                position = landing;
                if (snap.GoToDetentionIndex >= 0 && snap.DetentionIndex >= 0)
                {
                    position[snap.DetentionIndex] += position[snap.GoToDetentionIndex];
                    position[snap.GoToDetentionIndex] = 0;
                }
            }
            return result;
        }

        /// <summary>Distribution de la case d'arrivée après un seul lancer depuis <paramref name="start"/>.</summary>
        public static double[] SingleRoll(AIGameSnapshot snap, int start)
        {
            int n = snap.SquareCount;
            var position = new double[n];
            position[Mod(start, n)] = 1.0;
            return OneMove(position, n);
        }

        /// <summary>Probabilité de passer par la case Départ (index 0) en un lancer depuis <paramref name="start"/>.</summary>
        public static double PassStartProbability(AIGameSnapshot snap, int start)
        {
            int n = snap.SquareCount;
            double p = 0;
            for (int d = 2; d <= 12; d++)
                if (Mod(start, n) + d >= n) p += DiceSum[d];
            return p;
        }

        private static double[] OneMove(double[] position, int n)
        {
            var landing = new double[n];
            for (int from = 0; from < n; from++)
            {
                if (position[from] == 0) continue;
                for (int d = 2; d <= 12; d++)
                    landing[(from + d) % n] += position[from] * DiceSum[d];
            }
            return landing;
        }

        private static int Mod(int x, int n) => ((x % n) + n) % n;
    }
}
