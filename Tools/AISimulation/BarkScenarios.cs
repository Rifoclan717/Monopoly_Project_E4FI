using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusTycoon.AI.Simulation
{
    /// <summary>Teste les répliques des IA : textes, rythme, et réactions aux événements de CampusEvents.</summary>
    public static class BarkScenarios
    {
        private static int passed, failed;

        public static bool RunAll()
        {
            passed = failed = 0;
            Console.WriteLine("=== Répliques des IA (AIBarks, AIBarkController) ===");

            Library();
            Rhythm();
            Placeholders();
            Controller();

            Console.WriteLine($"--> {passed} réussis, {failed} échoués\n");
            return failed == 0;
        }

        private static void Check(bool condition, string label, string detail = "")
        {
            if (condition) { passed++; Console.WriteLine("  [OK]    " + label); }
            else { failed++; Console.WriteLine("  [ECHEC] " + label + (detail.Length > 0 ? "  -> " + detail : "")); }
        }

        private static readonly CharacterId[] Cast = { CharacterId.Major, CharacterId.Partygoer, CharacterId.Techie };

        // ------------------------------------------------------------------

        private static void Library()
        {
            bool complete = Cast.All(c => AIBarks.AllMoments().All(m => AIBarks.Lines(c, m).Count >= 2));
            Check(complete, "Chaque personnage a au moins 2 répliques pour chacun des 10 moments");

            Check(AIBarks.Lines(CharacterId.Major, BarkMoment.RentCollected).Contains("Merci pour le loyer !")
                  && AIBarks.Lines(CharacterId.Techie, BarkMoment.RivalBought).Contains("Tu vas regretter cet amphi…")
                  && AIBarks.Lines(CharacterId.Partygoer, BarkMoment.Bought).Contains("Soirée chez moi ce soir !"),
                "Les 3 exemples du document de règles sont présents");

            int longest = Cast.SelectMany(c => AIBarks.AllMoments().SelectMany(m => AIBarks.Lines(c, m))).Max(l => l.Length);
            Check(longest <= 60, "Répliques courtes (60 caractères au plus)", longest.ToString());
        }

        private static void Rhythm()
        {
            var barks = new AIBarks(seed: 7, cooldownSeconds: 3);
            var first = barks.TryBark(1, CharacterId.Partygoer, BarkMoment.RentCollected, 0, amount: 100);
            var tooSoon = barks.TryBark(2, CharacterId.Techie, BarkMoment.RentCollected, 1, amount: 100);
            var important = barks.TryBark(2, CharacterId.Techie, BarkMoment.SentToDetention, 1.5);
            var later = barks.TryBark(2, CharacterId.Techie, BarkMoment.RentCollected, 10, amount: 100);
            Check(first != null && tooSoon == null, "Pas deux répliques ordinaires en moins de 3 s");
            Check(important != null, "Les moments importants (colle, monopole, pouvoir, faillite) passent toujours");
            Check(later != null, "Après le délai, l'IA reparle");

            var b2 = new AIBarks(seed: 3, cooldownSeconds: 0);
            string previous = null;
            bool repeated = false;
            for (int i = 0; i < 60; i++)
            {
                var l = b2.TryBark(1, CharacterId.Major, BarkMoment.MonopolyCompleted, i);
                if (l.Text == previous) repeated = true;
                previous = l.Text;
            }
            Check(!repeated, "Jamais deux fois de suite la même réplique");

            var b3 = new AIBarks(seed: 5, cooldownSeconds: 0);
            int spoken = Enumerable.Range(0, 1000).Count(i => b3.TryBark(1, CharacterId.Major, BarkMoment.RentCollected, i, amount: 12) != null);
            Check(spoken > 300 && spoken < 500, "Petit loyer : l'IA ne réagit qu'environ 4 fois sur 10", spoken + " / 1000");
        }

        private static void Placeholders()
        {
            var barks = new AIBarks(seed: 11, cooldownSeconds: 0);
            bool amphiOutside = false, unfilled = false;
            for (int i = 0; i < 300; i++)
            {
                var l = barks.TryBark(2, CharacterId.Techie, BarkMoment.RivalBought, i, "FabLab", 140);
                if (l == null) continue;
                if (l.Text.IndexOf("amphi", StringComparison.OrdinalIgnoreCase) >= 0) amphiOutside = true;
                if (l.Text.Contains("{")) unfilled = true;
            }
            Check(!amphiOutside, "« Tu vas regretter cet amphi… » n'est dit que sur un amphi");
            Check(!unfilled, "Les marqueurs {case} et {montant} sont toujours remplacés");

            var b2 = new AIBarks(seed: 1, cooldownSeconds: 0);
            var any = b2.TryBark(0, CharacterId.Major, BarkMoment.RentCollected, 0, null, 52);
            Check(any != null && any.Formatted.StartsWith("Le Major : « ") && any.Formatted.EndsWith(" »"),
                "Format de la bulle : « Le Major : « … » »", any?.Formatted ?? "null");
        }

        // ------------------------------------------------------------------

        private static void Controller()
        {
            var level = BridgeScenarios.MakeLevel(out var squares);
            var board = new List<BoardSquare>();
            for (int i = 0; i < squares.Count; i++) board.Add(new BoardSquare(i, squares[i]));

            var partygoer = new AIController(); partygoer.Configure(1, CharacterId.Partygoer, AIDifficulty.Normal);
            var techie = new AIController(); techie.Configure(2, CharacterId.Techie, AIDifficulty.Normal);

            var ctrl = new AIBarkController();
            BridgeScenarios.Set(ctrl, "seed", 42);
            ctrl.Bind(board, new[] { partygoer, techie });

            var heard = new List<(string text, float duration)>();
            ctrl.OnSpeech.AddListener((t, d) => heard.Add((t, d)));
            ctrl.EnableForTests();

            float now = 100f;
            UnityEngine.Time.unscaledTime = now;

            // Le Techos complète le groupe orange : réplique de monopole, toujours dite.
            board[16].SetOwner(2);
            board[17].SetOwner(2);
            CampusEvents.RaisePropertyBought(2, 17, 180);
            bool monopoly = heard.Count == 1 && heard[0].text.StartsWith("Le Techos")
                            && AIBarks.Lines(CharacterId.Techie, BarkMoment.MonopolyCompleted).Any(l => heard[0].text.Contains(l));
            Check(monopoly, "Achat qui complète un groupe → réplique de monopole du Techos", heard.Count > 0 ? heard[0].text : "rien");
            Check(heard.Count == 1 && Math.Abs(heard[0].duration - 4.5f) < 1e-4, "Durée d'affichage transmise à la bulle (4,5 s)");

            // Le Fêtard part en colle : moment important, passe malgré le délai.
            CampusEvents.RaisePlayerMoved(1, 5, 0, true);
            Check(heard.Count == 2 && heard[1].text.StartsWith("Le Fêtard"), "Envoi en colle → réplique du Fêtard", heard.Count > 1 ? heard[1].text : "rien");

            // Un déplacement normal ne déclenche rien.
            CampusEvents.RaisePlayerMoved(1, 9, 4, false);
            Check(heard.Count == 2, "Déplacement ordinaire : silence");

            // L'humain paie 90 au Techos, 10 s plus tard : c'est le propriétaire qui parle.
            UnityEngine.Time.unscaledTime = now += 10;
            CampusEvents.RaiseRentPaid(0, 2, 16, 90);
            Check(heard.Count == 3 && heard[2].text.StartsWith("Le Techos"), "Loyer encaissé → réplique du propriétaire", heard.Count > 2 ? heard[2].text : "rien");

            // L'humain utilise son pouvoir : aucune IA ne parle à sa place.
            UnityEngine.Time.unscaledTime = now += 10;
            CampusEvents.RaisePowerUsed(0, "Le Major", "Dossier béton", "");
            Check(heard.Count == 3, "Pouvoir de l'humain : les IA ne parlent pas à sa place");

            // L'humain fait faillite face au Fêtard : le créancier réagit.
            CampusEvents.RaisePlayerBankrupt(0, 1);
            Check(heard.Count == 4 && heard[3].text.StartsWith("Le Fêtard")
                  && AIBarks.Lines(CharacterId.Partygoer, BarkMoment.RivalBankrupt).Any(l => heard[3].text.Contains(l)),
                "Faillite d'un rival → le créancier réagit", heard.Count > 3 ? heard[3].text : "rien");

            // Désabonné : plus rien.
            ctrl.DisableForTests();
            UnityEngine.Time.unscaledTime = now += 10;
            CampusEvents.RaisePlayerMoved(2, 5, 0, true);
            Check(heard.Count == 4, "Composant désactivé : plus aucune réplique (désabonnement correct)");

            Console.WriteLine("          Exemples entendus : " + string.Join(" | ", heard.Select(h => h.text)));
        }
    }
}
