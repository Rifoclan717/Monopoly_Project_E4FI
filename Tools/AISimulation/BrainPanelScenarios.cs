using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusTycoon.AI.Simulation
{
    /// <summary>Teste le panneau « cerveau de l'IA » : journal, événement DecisionMade et remise à zéro entre deux parties.</summary>
    public static class BrainPanelScenarios
    {
        private static int passed, failed;

        public static bool RunAll()
        {
            passed = failed = 0;
            Console.WriteLine("=== Panneau « cerveau de l'IA » (AIDecisionLog, AIBrainPanel) ===");

            LogBasics();
            ControllerEvents();
            PanelCollects();

            Console.WriteLine($"--> {passed} réussis, {failed} échoués\n");
            return failed == 0;
        }

        private static void Check(bool condition, string label, string detail = "")
        {
            if (condition) { passed++; Console.WriteLine("  [OK]    " + label); }
            else { failed++; Console.WriteLine("  [ECHEC] " + label + (detail.Length > 0 ? "  -> " + detail : "")); }
        }

        private static void LogBasics()
        {
            var log = new AIDecisionLog(capacityPerPlayer: 3);
            for (int i = 0; i < 5; i++)
                log.Add(2, CharacterId.Techie, new AIDecision(AIActionType.Buy, i, 10 * i, "décision " + i));
            log.Add(1, CharacterId.Partygoer, new AIDecision(AIActionType.Pass, 4, -3, "trop cher"));

            var techie = log.For(2);
            Check(techie.Count == 3, "Journal limité à la capacité (3 dernières décisions)", techie.Count.ToString());
            Check(techie[0].Decision.Reason == "décision 4" && techie[2].Decision.Reason == "décision 2", "La décision la plus récente est en premier");
            Check(log.Players.SequenceEqual(new[] { 1, 2 }), "Joueurs listés dans l'ordre de leur index");
            Check(log.CharacterOf(1) == CharacterId.Partygoer, "Personnage mémorisé par joueur");

            bool allLabelled = Enum.GetValues(typeof(AIActionType)).Cast<AIActionType>()
                .All(a => AIDecisionLog.Label(a) != a.ToString());
            Check(allLabelled, "Chaque type d'action a un libellé en français");

            Check(AIDecisionLog.Headline(new AIDecision(AIActionType.Buy, 6, 61.4, "x")) == "Achète · score 61", "Titre court : action et score arrondi");

            var p = AIDifficultyModifier.Apply(AIParameters.Defaults(CharacterId.Techie), AIDifficulty.Normal);
            string desc = AIDecisionLog.DescribeParameters(p, AIDifficulty.Normal);
            Check(desc.Contains("réserve 300") && desc.Contains("Normal") && desc.Contains("rentables"),
                "Réglages lisibles pour le jury (réserve, difficulté, règle du Techos)", desc);

            log.ClearPlayer(2);
            Check(log.For(2).Count == 0 && log.For(1).Count == 1, "Remise à zéro d'un seul joueur");
        }

        private static AIGameSnapshot Snapshot()
        {
            var players = new List<PlayerInfo>();
            var chars = new[] { CharacterId.Major, CharacterId.Partygoer, CharacterId.Techie };
            for (int i = 0; i < 3; i++)
                players.Add(new PlayerInfo { Index = i, Character = chars[i], Money = 1000, TurnsRemaining = 10 });
            return new AIGameSnapshot(Boards.Level2(), players);
        }

        private static void ControllerEvents()
        {
            var heard = new List<(int player, AIDecision decision)>();
            Action<AIController, AIDecision> listener = (c, d) => heard.Add((c.PlayerIndex, d));
            AIController.DecisionMade += listener;
            try
            {
                var techie = new AIController();
                techie.Configure(2, CharacterId.Techie, AIDifficulty.Normal, randomSeed: 1);
                BridgeScenarios.Set(techie, "logDecisions", false);
                var g = Snapshot();

                techie.DecideBuy(g, 6);
                Check(heard.Count == 1 && heard[0].player == 2 && heard[0].decision.Action == AIActionType.Buy,
                    "DecideBuy signale sa décision au panneau");

                g.Player(2).HasExemptionTicket = true;
                techie.ShouldUseTicketOnRent(g, 10);
                Check(heard.Count == 2 && heard[1].decision.Action == AIActionType.KeepExemptionTicket,
                    "Billet gardé sur un petit loyer : signalé aussi", heard.Count > 1 ? heard[1].decision.ToString() : "rien");

                g.Owners[16] = g.Owners[17] = 2;
                int chosen = techie.ChooseFreeUpgrade(g);
                Check(chosen >= 16 && heard.Count == 3 && heard[2].decision.Action == AIActionType.Upgrade
                      && heard[2].decision.Reason.StartsWith("Amélioration offerte : Amphi"),
                    "Amélioration offerte : terrain choisi signalé avec son nom", heard.Count > 2 ? heard[2].decision.Reason : "rien");

                int before = heard.Count;
                techie.DecideUpgrades(g);
                Check(heard.Count > before && heard.Skip(before).All(h => h.decision.Action == AIActionType.Upgrade),
                    "Chaque amélioration de la phase Actions est signalée");

                // Un abonné qui plante ne doit jamais casser la partie.
                Action<AIController, AIDecision> broken = (c, d) => throw new InvalidOperationException("panne d'affichage");
                AIController.DecisionMade += broken;
                AIDecision result = null;
                try { result = techie.DecideBuy(g, 11); } catch { }
                AIController.DecisionMade -= broken;
                Check(result != null, "Une erreur dans le panneau ne bloque pas la décision de l'IA");
            }
            finally
            {
                AIController.DecisionMade -= listener;
            }
        }

        private static void PanelCollects()
        {
            var panel = new AIBrainPanel();
            panel.EnableForTests();
            try
            {
                var major = new AIController(); major.Configure(1, CharacterId.Major, AIDifficulty.Hard, randomSeed: 2);
                var techie = new AIController(); techie.Configure(2, CharacterId.Techie, AIDifficulty.Hard, randomSeed: 3);
                BridgeScenarios.Set(major, "logDecisions", false);
                BridgeScenarios.Set(techie, "logDecisions", false);
                var g = Snapshot();

                major.DecideBuy(g, 6);
                techie.DecideBuy(g, 8);
                techie.DecideDetention(g);
                Check(panel.Log.For(1).Count == 1 && panel.Log.For(2).Count == 2, "Le panneau range les décisions par IA");
                Check(panel.Log.CharacterOf(2) == CharacterId.Techie, "Le panneau connaît le personnage de chaque IA");

                // Nouvelle partie : un nouveau contrôleur pour le même joueur repart d'un journal vide.
                var techie2 = new AIController(); techie2.Configure(2, CharacterId.Techie, AIDifficulty.Easy, randomSeed: 4);
                BridgeScenarios.Set(techie2, "logDecisions", false);
                techie2.DecideBuy(g, 11);
                Check(panel.Log.For(2).Count == 1 && panel.Log.For(1).Count == 1, "Nouveau contrôleur (nouvelle partie) : journal de ce joueur remis à zéro");

                // Premier tour du joueur 0 : tout est remis à zéro.
                CampusEvents.RaiseTurnStarted(0, 1, 15);
                Check(!panel.Log.Players.Any(), "Début de partie (tour 1 du joueur 0) : journal vidé");

                CampusEvents.RaiseTurnStarted(0, 2, 15);
                major.DecideBuy(g, 6);
                Check(panel.Log.For(1).Count == 1, "Les tours suivants ne vident pas le journal");
            }
            finally
            {
                panel.DisableForTests();
            }

            int countBefore = 0;
            var p2 = new AIBrainPanel();
            p2.EnableForTests();
            p2.DisableForTests();
            var c = new AIController(); c.Configure(1, CharacterId.Major, AIDifficulty.Normal, randomSeed: 5);
            BridgeScenarios.Set(c, "logDecisions", false);
            c.DecideBuy(Snapshot(), 6);
            Check(p2.Log.For(1).Count == countBefore, "Panneau désactivé : il n'écoute plus (désabonnement correct)");
        }
    }
}
