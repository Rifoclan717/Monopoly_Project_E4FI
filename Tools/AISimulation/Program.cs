using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace CampusTycoon.AI.Simulation
{
    public static class Program
    {
        private static readonly CharacterId[] Cast = { CharacterId.Major, CharacterId.Partygoer, CharacterId.Techie };

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            int games = args.Length > 0 && int.TryParse(args[0], out int n) ? n : 300;

            bool ok = Scenarios.RunAll();
            ok &= BridgeScenarios.RunAll();
            ok &= BarkScenarios.RunAll();
            var sw = Stopwatch.StartNew();

            SameDifficulty(games);
            DifficultyLadder(games);

            Console.WriteLine($"Temps total des simulations : {sw.Elapsed.TotalSeconds:0.0} s");
            return ok ? 0 : 1;
        }

        private static AIParameters Params(CharacterId c, AIDifficulty d) =>
            AIDifficultyModifier.Apply(AIParameters.Defaults(c), d);

        /// <summary>Les trois personnages au même niveau : équilibre entre personnalités.</summary>
        private static void SameDifficulty(int games)
        {
            foreach (AIDifficulty d in Enum.GetValues(typeof(AIDifficulty)))
            {
                var wins = new int[3];
                var worth = new double[3];
                var bankrupt = new int[3];
                var purchases = new double[3];
                var upgrades = new double[3];
                var powers = new double[3];

                for (int k = 0; k < games; k++)
                {
                    var sim = new GameSimulator(Cast.Select(c => Params(c, d)).ToArray(), seed: 1000 + k);
                    int w = sim.Play();
                    if (w >= 0) wins[w]++;
                    for (int i = 0; i < 3; i++)
                    {
                        worth[i] += sim.Stats.FinalWorth[i];
                        if (sim.Stats.Bankrupt[i]) bankrupt[i]++;
                        purchases[i] += sim.Stats.Purchases[i];
                        upgrades[i] += sim.Stats.Upgrades[i];
                        powers[i] += sim.Stats.PowersUsed[i];
                    }
                }

                Console.WriteLine($"=== {games} parties, les 3 IA en difficulté {d} ===");
                Console.WriteLine("  Personnage  Victoires  Fortune moy.  Faillites  Achats  Améliorations  Pouvoir utilisé");
                for (int i = 0; i < 3; i++)
                    Console.WriteLine($"  {Cast[i],-10}  {100.0 * wins[i] / games,7:0.0} %  {worth[i] / games,12:0}  {100.0 * bankrupt[i] / games,7:0.0} %  {purchases[i] / games,6:0.0}  {upgrades[i] / games,13:0.0}  {100.0 * powers[i] / games,13:0} %");
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Vérifie que la difficulté a un effet : un personnage joue en Easy / Normal / Hard
        /// contre les deux autres en Normal. Son taux de victoire doit monter avec la difficulté.
        /// </summary>
        private static void DifficultyLadder(int games)
        {
            Console.WriteLine($"=== Échelle de difficulté : un personnage testé contre deux rivaux en Normal ({games} parties par case) ===");
            Console.WriteLine("  Personnage   Easy      Normal    Hard");
            foreach (var tested in Cast)
            {
                var line = $"  {tested,-10}";
                foreach (AIDifficulty d in Enum.GetValues(typeof(AIDifficulty)))
                {
                    int wins = 0;
                    for (int k = 0; k < games; k++)
                    {
                        var ps = Cast.Select(c => Params(c, c == tested ? d : AIDifficulty.Normal)).ToArray();
                        var sim = new GameSimulator(ps, seed: 5000 + k);
                        if (Cast[sim.Play()] == tested) wins++;
                    }
                    line += $"  {100.0 * wins / games,5:0.0} % ";
                }
                Console.WriteLine(line);
            }
            Console.WriteLine();
        }
    }
}
