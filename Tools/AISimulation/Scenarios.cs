using System;
using System.Collections.Generic;

namespace CampusTycoon.AI.Simulation
{
    /// <summary>Scénarios de vérification : chaque cas teste une règle de comportement attendue.</summary>
    public static class Scenarios
    {
        private static int passed, failed;

        public static bool RunAll()
        {
            passed = failed = 0;
            Console.WriteLine("=== Scénarios de vérification ===");

            DiceProbabilities();
            RentRules();
            CompletingMonopolyScoresHigher();
            ReserveIsRespected();
            TechieSkipsContestedGroups();
            BlockingRivalMonopoly();
            UpgradePrefersBestGain();
            UpgradeOncePerSquareAndKeepsReserve();
            SaleDuringBackToSchoolSale();
            ForcedSaleOrder();
            DetentionChoice();
            PowersFollowRules();
            DifficultyModifiers();

            Console.WriteLine($"--> {passed} réussis, {failed} échoués\n");
            return failed == 0;
        }

        // ------------------------------------------------------------------

        private static AIGameSnapshot NewGame(int money = 1000, int turns = 12)
        {
            var players = new List<PlayerInfo>();
            var chars = new[] { CharacterId.Major, CharacterId.Partygoer, CharacterId.Techie };
            for (int i = 0; i < 3; i++)
                players.Add(new PlayerInfo { Index = i, Character = chars[i], Money = money, Position = 0, TurnsRemaining = turns });
            return new AIGameSnapshot(Boards.Level2(), players);
        }

        private static AIBrain Brain(int index, CharacterId id, AIDifficulty d = AIDifficulty.Normal) =>
            new AIBrain(index, AIDifficultyModifier.Apply(AIParameters.Defaults(id), d), seed: 1);

        private static void Check(bool condition, string label, string detail = "")
        {
            if (condition) { passed++; Console.WriteLine("  [OK]    " + label); }
            else { failed++; Console.WriteLine("  [ECHEC] " + label + (detail.Length > 0 ? "  -> " + detail : "")); }
        }

        // ------------------------------------------------------------------

        private static void DiceProbabilities()
        {
            var g = NewGame();
            var land = BoardProbabilities.SingleRoll(g, 0);
            double sum = 0; foreach (var v in land) sum += v;
            Check(Math.Abs(sum - 1) < 1e-9, "2d6 : les probabilités d'arrivée somment à 1");
            Check(Math.Abs(land[7] - 6.0 / 36) < 1e-9, "2d6 : 7 est le résultat le plus probable (6/36)");

            var byTurn = BoardProbabilities.LandingByTurn(g, 0, 15);
            double rattrapage = 0, colle = 0;
            for (int k = 0; k < 15; k++) { rattrapage += byTurn[k][15]; colle += byTurn[k][5]; }
            Check(byTurn[1][6] > byTurn[1][4], "Après Rattrapage, les cases juste après la colle sont plus visitées",
                $"case 6 : {byTurn[1][6]:0.000}, case 4 : {byTurn[1][4]:0.000}");
        }

        private static void RentRules()
        {
            var g = NewGame();
            g.Owners[16] = 0;
            Check(RentEstimator.Rent(g, 16) == 18, "Loyer sans monopole = R");
            g.Owners[17] = 0;
            Check(RentEstimator.Rent(g, 16) == 36, "Loyer en monopole = 2R");
            g.Levels[16] = 3;
            Check(RentEstimator.Rent(g, 16) == 396, "Loyer niveau 3 = 22R");
            g.ActiveEvent = GlobalEventType.OpenDay;
            Check(RentEstimator.Rent(g, 16) == 792, "Journée portes ouvertes : loyer doublé");
            g.ActiveEvent = null;

            g.Owners[4] = 1;
            Check(RentEstimator.Rent(g, 4) == 25, "1 gare = 25");
            g.Owners[14] = 1;
            Check(RentEstimator.Rent(g, 4) == 60, "2 gares = 60");
            g.ActiveEvent = GlobalEventType.TransportStrike;
            Check(RentEstimator.Rent(g, 4) == 0, "Grève du RER : gares à 0");
            g.ActiveEvent = null;

            g.Owners[9] = 2;
            Check(RentEstimator.Rent(g, 9) == 35, "Machine à café = 5 × 7 en moyenne");
        }

        private static void CompletingMonopolyScoresHigher()
        {
            var g = NewGame();
            g.Owners[16] = 0; // le Major a Amphi A
            var p = AIParameters.Defaults(CharacterId.Major);
            double complete = UtilityScorer.BuyScore(g, 0, 17, p, out string r1);
            double isolated = UtilityScorer.BuyScore(g, 0, 11, p, out string r2);
            Check(complete > isolated, "Compléter un monopole vaut plus qu'un terrain isolé",
                $"Amphi B {complete:0} vs FabLab {isolated:0}");
            Console.WriteLine("          " + r1);
        }

        private static void ReserveIsRespected()
        {
            var g = NewGame(money: 350);
            var d = Brain(2, CharacterId.Techie).DecideBuy(g, 6); // Bibliothèque 100 : il resterait 250 < 300
            Check(d.Action == AIActionType.Pass, "Techos : ne descend pas sous sa réserve de 300", d.ToString());

            g.Player(0).Money = 160;
            d = Brain(0, CharacterId.Major).DecideBuy(g, 6);   // resterait 60 >= 50
            Check(d.Action == AIActionType.Buy, "Major : achète si la réserve de 50 est respectée", d.ToString());
        }

        private static void TechieSkipsContestedGroups()
        {
            var g = NewGame();
            g.Owners[16] = 0; // un rival possède Amphi A
            var techie = Brain(2, CharacterId.Techie);
            Check(techie.DecideBuy(g, 17).Action == AIActionType.Pass, "Techos : ne prend pas un groupe déjà entamé par un rival");
            Check(techie.DecideBuy(g, 11).Action == AIActionType.Buy, "Techos : démarre un groupe libre");
            Check(techie.DecideBuy(g, 4).Action == AIActionType.Buy, "Techos : achète une gare");
        }

        private static void BlockingRivalMonopoly()
        {
            var g = NewGame();
            g.Owners[18] = 1; // le Fêtard a le Bureau du BDE
            var p = AIParameters.Defaults(CharacterId.Major);
            double block = UtilityScorer.BuyScore(g, 0, 19, p, out string reason);
            var g2 = NewGame();
            double free = UtilityScorer.BuyScore(g2, 0, 19, p, out _);
            Check(block > free, "Bloquer le monopole d'un rival augmente le score", $"{block:0} vs {free:0}");
            Console.WriteLine("          " + reason);
        }

        private static void UpgradePrefersBestGain()
        {
            var g = NewGame(money: 2000);
            g.Owners[1] = g.Owners[2] = 0;    // marron
            g.Owners[16] = g.Owners[17] = 0;  // orange
            var ups = Brain(0, CharacterId.Major).DecideUpgrades(g);
            Check(ups.Count == 4, "Major : améliore tout ce qui est possible (un niveau par terrain)", ups.Count.ToString());
            Check(ups.Count > 0 && (ups[0].Square == 16 || ups[0].Square == 17), "Les amphis passent avant la cafétéria",
                ups.Count > 0 ? ups[0].ToString() : "aucune");
        }

        private static void UpgradeOncePerSquareAndKeepsReserve()
        {
            var g = NewGame(money: 700);
            g.Owners[16] = g.Owners[17] = 2;
            var ups = Brain(2, CharacterId.Techie).DecideUpgrades(g);
            // 700 - 100 = 600, 600 - 100 = 500 : deux améliorations possibles au-dessus de 300
            Check(ups.Count == 2, "Techos : construit tant qu'il lui reste 300", ups.Count.ToString());

            g.Player(2).Money = 380;
            ups = Brain(2, CharacterId.Techie).DecideUpgrades(g);
            Check(ups.Count == 0, "Techos : ne construit pas sous 300 après paiement", ups.Count.ToString());

            g.UpgradedThisTurn.Add(16);
            g.UpgradedThisTurn.Add(17);
            g.Player(2).Money = 5000;
            Check(Brain(2, CharacterId.Techie).DecideUpgrades(g).Count == 0, "Un seul niveau par terrain et par tour");
        }

        private static void SaleDuringBackToSchoolSale()
        {
            var g = NewGame(money: 360);
            g.Owners[16] = g.Owners[17] = 2;
            g.ActiveEvent = GlobalEventType.BackToSchoolSale;
            var ups = Brain(2, CharacterId.Techie).DecideUpgrades(g);
            Check(ups.Count == 1, "Soldes : coût divisé par deux (360 - 50 >= 300)", ups.Count.ToString());
        }

        private static void ForcedSaleOrder()
        {
            var g = NewGame(money: 0);
            g.Owners[1] = g.Owners[2] = 0; g.Levels[1] = 1;  // marron, un niveau sur Cafétéria
            g.Owners[19] = 0;                                 // Direction
            var plan = Brain(0, CharacterId.Major).ChooseAssetsToSell(g, 60);
            Check(plan.Count >= 1 && plan[0].Action == AIActionType.SellUpgrade && plan[0].Square == 1,
                "Vente forcée : l'amélioration (25) part en premier", plan.Count > 0 ? plan[0].ToString() : "vide");
            Check(plan.TrueForAll(d => d.Square != 19), "Vente forcée : garde la Direction (vaut le plus)");

            int total = 0; foreach (var d in Brain(0, CharacterId.Major).ChooseAssetsToSell(g, 10000)) total++;
            Check(total == 4, "Vente forcée impossible : tout est vendu", total.ToString());
        }

        private static void DetentionChoice()
        {
            var empty = NewGame();
            empty.Player(1).Position = 5; empty.Player(1).InDetention = true;
            var d = Brain(1, CharacterId.Partygoer).DecideDetention(empty);
            Check(d.Action == AIActionType.PayDetentionFine, "Colle, plateau libre : paie 50 pour aller acheter", d.ToString());

            var danger = NewGame();
            danger.Player(1).Position = 5; danger.Player(1).InDetention = true;
            foreach (int s in new[] { 6, 8, 11, 13 }) { danger.Owners[s] = 0; danger.Levels[s] = 3; }
            danger.Owners[16] = danger.Owners[17] = 2; danger.Levels[16] = danger.Levels[17] = 2;
            d = Brain(1, CharacterId.Partygoer).DecideDetention(danger);
            Check(d.Action == AIActionType.WaitInDetention, "Colle, loyers énormes devant : attend", d.ToString());
        }

        private static void PowersFollowRules()
        {
            var g = NewGame();
            var major = Brain(0, CharacterId.Major);
            Check(major.DecidePower(g, new PowerContext { Moment = PowerMoment.CollectingRent, Amount = 30 }).Action == AIActionType.KeepPower,
                "Major : garde son pouvoir sous 40");
            Check(major.DecidePower(g, new PowerContext { Moment = PowerMoment.CollectingRent, Amount = 50 }).Action == AIActionType.UsePower,
                "Major : double un loyer d'au moins 40");

            g.Owners[16] = 0; g.Owners[17] = 0; g.Levels[16] = 2;  // le Major peut encaisser 216
            var hard = Brain(0, CharacterId.Major, AIDifficulty.Hard);
            Check(hard.DecidePower(g, new PowerContext { Moment = PowerMoment.CollectingRent, Amount = 50 }).Action == AIActionType.KeepPower,
                "Major difficile : attend un plus gros loyer");

            var techie = Brain(2, CharacterId.Techie);
            var toDetention = new PowerContext { Moment = PowerMoment.DiceRolled, FromPosition = 7, DiceTotal = 8 };
            Check(techie.DecidePower(g, toDetention).Action == AIActionType.UsePower, "Techos : relance pour éviter Rattrapage");
            var big = new PowerContext { Moment = PowerMoment.DiceRolled, FromPosition = 10, DiceTotal = 6 };
            Check(techie.DecidePower(g, big).Action == AIActionType.UsePower, "Techos : relance pour éviter un loyer de 216");
            var fine = new PowerContext { Moment = PowerMoment.DiceRolled, FromPosition = 0, DiceTotal = 6 };
            Check(techie.DecidePower(g, fine).Action == AIActionType.KeepPower, "Techos : garde la relance sur un lancer sans danger");

            g.Player(0).PowerUsed = true;
            Check(major.DecidePower(g, new PowerContext { Moment = PowerMoment.CollectingRent, Amount = 500 }).Action == AIActionType.KeepPower,
                "Pouvoir utilisable une seule fois");
        }

        private static void DifficultyModifiers()
        {
            var easy = AIDifficultyModifier.Apply(AIParameters.Defaults(CharacterId.Major), AIDifficulty.Easy);
            var hard = AIDifficultyModifier.Apply(AIParameters.Defaults(CharacterId.Techie), AIDifficulty.Hard);
            var hardMajor = AIDifficultyModifier.Apply(AIParameters.Defaults(CharacterId.Major), AIDifficulty.Hard);
            Check(Math.Abs(easy.BuyChance - 0.6f) < 1e-5 && easy.CashReserve == 0 && Math.Abs(easy.PowerTriggerChance - 0.5f) < 1e-5,
                "Niveau 1 : achat -30 points, réserve 0, pouvoir une fois sur deux");
            Check(hard.CashReserve == 450 && Math.Abs(hardMajor.BuyChance - 1f) < 1e-5 && hard.SmartPower,
                "Niveau 3 : réserve +50 %, achat +10 points plafonné à 100 %, pouvoir au meilleur moment");
        }
    }
}
