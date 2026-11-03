using System;
using System.Collections.Generic;
using System.Reflection;

namespace CampusTycoon.AI.Simulation
{
    /// <summary>
    /// Teste AISnapshotBuilder sur les vraies classes du modèle (SquareData, LevelData, CharacterData, Player, BoardSquare).
    /// Les champs privés sérialisés des ScriptableObject sont remplis par réflexion, comme le ferait l'Inspector.
    /// </summary>
    public static class BridgeScenarios
    {
        private static int passed, failed;

        public static bool RunAll()
        {
            passed = failed = 0;
            Console.WriteLine("=== Pont état du jeu -> IA (AISnapshotBuilder) ===");

            var level = MakeLevel(out var squareAssets);
            var board = new List<BoardSquare>();
            for (int i = 0; i < squareAssets.Count; i++) board.Add(new BoardSquare(i, squareAssets[i]));

            var players = new List<Player>
            {
                new Player(0, MakeCharacter(CharacterId.Major, PowerType.DoubleRent), true, level.StartingMoney),
                new Player(1, MakeCharacter(CharacterId.Partygoer, PowerType.CancelPayment), false, level.StartingMoney),
                new Player(2, MakeCharacter(CharacterId.Techie, PowerType.Reroll), false, level.StartingMoney)
            };

            // Situation de partie : le Techos (2) a le monopole orange, avec un niveau sur Amphi A.
            board[16].SetOwner(2); board[17].SetOwner(2); board[16].Upgrade();
            board[1].SetOwner(0);
            players[2].RemoveMoney(400);
            players[2].SetPosition(12);
            players[1].SetPosition(5); players[1].SetInDetention(true);
            players[0].GiveExemptionTicket();
            players[0].UsePower();

            var turnsPlayed = new[] { 4, 4, 3 };
            var snap = AISnapshotBuilder.Build(level, board, players, turnsPlayed, GlobalEventType.BackToSchoolSale, new[] { 16 });

            Check(snap.SquareCount == 20 && snap.Squares[16].Name == "Amphi A" && snap.Squares[16].Price == 180, "Cases recopiées depuis SquareData");
            Check(snap.Squares[16].Rents.Length == 5 && snap.Squares[16].Rents[2] == 90, "Loyers recopiés (niv. 1 = 90)");
            Check(snap.Owners[16] == 2 && snap.Owners[17] == 2 && snap.Owners[1] == 0 && snap.Owners[6] == AIGameSnapshot.NoOwner, "Propriétaires recopiés depuis BoardSquare");
            Check(snap.Levels[16] == 1 && snap.Levels[17] == 0, "Niveaux d'amélioration recopiés");
            Check(snap.Player(2).Money == 600 && snap.Player(2).Position == 12, "Argent et position du joueur");
            Check(snap.Player(1).InDetention && snap.Player(0).HasExemptionTicket && snap.Player(0).PowerUsed, "Colle, billet et pouvoir");
            Check(snap.Player(2).Character == CharacterId.Techie, "Personnage lu dans CharacterData");
            Check(snap.Player(0).TurnsRemaining == 11 && snap.Player(2).TurnsRemaining == 12, "Tours restants = tours max - tours commencés");
            Check(snap.StartBonus == 150 && snap.DetentionFine == 50, "Bonus de La Rentrée et amende lus dans LevelData");
            Check(snap.ActiveEvent == GlobalEventType.BackToSchoolSale && snap.UpgradedThisTurn.Contains(16), "Événement actif et améliorations du tour");

            // L'IA fonctionne de bout en bout sur ce snapshot.
            var techie = new AIBrain(2, AIParameters.Defaults(CharacterId.Techie), seed: 1);
            var ups = techie.DecideUpgrades(snap);
            Check(ups.Count == 1 && ups[0].Square == 17, "Techos : améliore Amphi B (Amphi A déjà fait ce tour, soldes à 50)",
                ups.Count > 0 ? ups[0].ToString() : "aucune");
            var buy = techie.DecideBuy(snap, 6);
            Check(buy.Action == AIActionType.Buy, "Techos : achète la Bibliothèque (600 - 100 >= 300)", buy.ToString());

            bool threw = false;
            try { AISnapshotBuilder.Build(level, board, players, new[] { 1, 1 }); } catch (ArgumentException) { threw = true; }
            Check(threw, "Erreur claire si les tours joués ne correspondent pas aux joueurs");

            Console.WriteLine($"--> {passed} réussis, {failed} échoués\n");
            return failed == 0;
        }

        // ------------------------------------------------------------------

        private static void Check(bool condition, string label, string detail = "")
        {
            if (condition) { passed++; Console.WriteLine("  [OK]    " + label); }
            else { failed++; Console.WriteLine("  [ECHEC] " + label + (detail.Length > 0 ? "  -> " + detail : "")); }
        }

        internal static void Set(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?? throw new InvalidOperationException($"Champ {field} introuvable dans {target.GetType().Name}");
            f.SetValue(target, value);
        }

        internal static LevelData MakeLevel(out List<SquareData> squares)
        {
            squares = new List<SquareData>();
            foreach (var info in Boards.Level2())
            {
                var sd = UnityEngine.ScriptableObject.CreateInstance<SquareData>();
                Set(sd, "displayName", info.Name);
                Set(sd, "type", info.Type);
                Set(sd, "group", info.Group);
                Set(sd, "price", info.Price);
                Set(sd, "rents", info.Rents);
                Set(sd, "upgradeCost", info.UpgradeCost);
                Set(sd, "amount", info.Amount);
                squares.Add(sd);
            }

            var level = UnityEngine.ScriptableObject.CreateInstance<LevelData>();
            Set(level, "levelNumber", 2);
            Set(level, "squares", squares);
            Set(level, "maxTurns", 15);
            return level;
        }

        internal static CharacterData MakeCharacter(CharacterId id, PowerType power)
        {
            var c = UnityEngine.ScriptableObject.CreateInstance<CharacterData>();
            Set(c, "id", id);
            Set(c, "power", power);
            return c;
        }
    }
}
