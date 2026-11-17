#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CampusTycoon.AI;

namespace CampusTycoon.EditorTools
{
    /// <summary>
    /// Crée/met à jour tous les assets de données avec les valeurs du document de règles 
    /// Relancer le menu ne crée pas de doublon : les assets existants sont mis à jour.
    /// </summary>
    public static class CampusDataGenerator
    {
        private const string Root = "Assets/Data";

        [MenuItem("Campus Tycoon/Données/Générer les assets des niveaux 1 à 3")]
        public static void GenerateAll()
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "Data");
            foreach (string folder in new[] { "Squares", "Cards", "GlobalEvents", "Characters", "Levels" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{folder}")) AssetDatabase.CreateFolder(Root, folder);

            Dictionary<string, SquareData> squares = CreateSquares();
            List<CardData> cards = CreateCards();
            List<GlobalEventData> events = CreateGlobalEvents();
            CreateCharacters();
            List<LevelData> levels = CreateLevels(squares, cards, events);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            foreach (LevelData level in levels) level.Validate();
            Debug.Log($"[CampusDataGenerator] {squares.Count} cases, {cards.Count} cartes, {events.Count} événements, 3 personnages et {levels.Count} niveaux dans {Root}.");
        }

        // Cases 

        private static Dictionary<string, SquareData> CreateSquares()
        {
            var squares = new Dictionary<string, SquareData>();

            void Add(string id, string name, SquareType type, ColorGroup group = ColorGroup.None,
                     int price = 0, int[] rents = null, int upgradeCost = 0, int amount = 0)
            {
                var asset = LoadOrCreate<SquareData>($"{Root}/Squares/SQ_{id}.asset");
                var so = new SerializedObject(asset);
                Prop(so, "displayName").stringValue = name;
                Prop(so, "type").enumValueIndex = (int)type;
                Prop(so, "group").enumValueIndex = (int)group;
                Prop(so, "price").intValue = price;
                SetInts(Prop(so, "rents"), rents ?? new int[0]);
                Prop(so, "upgradeCost").intValue = upgradeCost;
                Prop(so, "amount").intValue = amount;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                squares[id] = asset;
            }

            Add("LaRentree", "La Rentrée", SquareType.Start);
            Add("Colle", "Colle", SquareType.Detention);
            Add("FoyerEtudiant", "Foyer étudiant", SquareType.StudentLounge, amount: 50);
            Add("Rattrapage", "Rattrapage", SquareType.GoToDetention);
            Add("Evenement", "Événement", SquareType.EventCard);
            Add("FraisScolarite", "Frais de scolarité", SquareType.Tax, amount: 100);

            Add("Cafeteria", "Cafétéria", SquareType.Property, ColorGroup.Brown, 60, new[] { 6, 12, 30, 72, 132 }, 50);
            Add("SalleTP", "Salle de TP", SquareType.Property, ColorGroup.Brown, 60, new[] { 6, 12, 30, 72, 132 }, 50);
            Add("Bibliotheque", "Bibliothèque", SquareType.Property, ColorGroup.LightBlue, 100, new[] { 10, 20, 50, 120, 220 }, 50);
            Add("SalleProjet", "Salle de projet", SquareType.Property, ColorGroup.LightBlue, 100, new[] { 10, 20, 50, 120, 220 }, 50);
            Add("FabLab", "FabLab", SquareType.Property, ColorGroup.Pink, 140, new[] { 14, 28, 70, 168, 308 }, 100);
            Add("SalleRobotique", "Salle de robotique", SquareType.Property, ColorGroup.Pink, 140, new[] { 14, 28, 70, 168, 308 }, 100);
            Add("AmphiA", "Amphi A", SquareType.Property, ColorGroup.Orange, 180, new[] { 18, 36, 90, 216, 396 }, 100);
            Add("AmphiB", "Amphi B", SquareType.Property, ColorGroup.Orange, 180, new[] { 18, 36, 90, 216, 396 }, 100);
            Add("BureauBDE", "Bureau du BDE", SquareType.Property, ColorGroup.Red, 240, new[] { 24, 48, 120, 288, 528 }, 150);
            Add("Direction", "Direction", SquareType.Property, ColorGroup.Red, 260, new[] { 26, 52, 130, 312, 572 }, 150);
            Add("Incubateur", "Incubateur de start-ups", SquareType.Property, ColorGroup.Gold, 300, new[] { 30, 60, 150, 360, 660 }, 200);
            Add("LaboRecherche", "Laboratoire de recherche", SquareType.Property, ColorGroup.Gold, 320, new[] { 32, 64, 160, 384, 704 }, 200);

            Add("RER_A", "RER A – Noisy-Champs", SquareType.Station, price: 150, rents: new[] { 25, 60 });
            Add("BusCampus", "Bus du campus", SquareType.Station, price: 150, rents: new[] { 25, 60 });
            Add("MachineCafe", "Machine à café", SquareType.Utility, price: 120, rents: new[] { 5 });
            Add("RouletteBDE", "Roulette du BDE", SquareType.Roulette, amount: 50);

            return squares;
        }

        // Cartes 

        private static List<CardData> CreateCards()
        {
            var cards = new List<CardData>();

            void Add(string id, string title, string description, CardEffect effect, int amount)
            {
                var asset = LoadOrCreate<CardData>($"{Root}/Cards/CARD_{id}.asset");
                var so = new SerializedObject(asset);
                Prop(so, "title").stringValue = title;
                Prop(so, "description").stringValue = description;
                Prop(so, "effect").enumValueIndex = (int)effect;
                Prop(so, "amount").intValue = amount;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                cards.Add(asset);
            }

            Add("01_SoireeBDE", "Soirée BDE", "Payez 60.", CardEffect.PayMoney, 60);
            Add("02_BourseMerite", "Bourse de mérite", "Recevez 100.", CardEffect.GainMoney, 100);
            Add("03_ProfAbsent", "Le prof est absent", "Relancez les dés et rejouez un déplacement.", CardEffect.RerollAndMove, 0);
            Add("04_PanneRER", "Panne de RER", "Reculez de 3 cases.", CardEffect.MoveSteps, -3);
            Add("05_StageDecroche", "Stage décroché", "Avancez jusqu'à La Rentrée et touchez 150.", CardEffect.MoveToStart, 0);
            Add("06_Rattrapage", "Rattrapage obligatoire", "Allez en colle.", CardEffect.GoToDetention, 0);
            Add("07_Hackathon", "Hackathon gagné", "Chaque rival vous donne 40.", CardEffect.CollectFromEachRival, 40);
            Add("08_Photocopies", "Photocopies hors de prix", "Payez 40.", CardEffect.PayMoney, 40);
            Add("09_AmeliorationOfferte", "Amélioration offerte", "Un terrain en monopole gagne un étage gratuit, sinon recevez 60.", CardEffect.FreeUpgrade, 60);
            Add("10_BilletAbsence", "Billet d'absence", "À garder : sortez de colle ou annulez un loyer, une fois.", CardEffect.ExemptionTicket, 0);
            Add("11_PanneWifi", "Panne de wifi", "Passez votre prochain tour.", CardEffect.SkipNextTurn, 0);
            Add("12_Remboursement", "Remboursement de frais", "Recevez 50.", CardEffect.GainMoney, 50);

            return cards;
        }

        // Événements du semestre 

        private static List<GlobalEventData> CreateGlobalEvents()
        {
            var events = new List<GlobalEventData>();

            void Add(string id, GlobalEventType type, string title, string description, int amount)
            {
                var asset = LoadOrCreate<GlobalEventData>($"{Root}/GlobalEvents/GEV_{id}.asset");
                var so = new SerializedObject(asset);
                Prop(so, "type").enumValueIndex = (int)type;
                Prop(so, "title").stringValue = title;
                Prop(so, "description").stringValue = description;
                Prop(so, "durationRounds").intValue = 1;
                Prop(so, "amount").intValue = amount;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                events.Add(asset);
            }

            Add("PortesOuvertes", GlobalEventType.OpenDay, "Journée portes ouvertes", "Tous les loyers de terrains sont doublés.", 0);
            Add("GreveRER", GlobalEventType.TransportStrike, "Grève du RER", "Les gares ne rapportent aucun loyer.", 0);
            Add("SoldesRentree", GlobalEventType.BackToSchoolSale, "Soldes de la rentrée", "Les étages coûtent moitié prix.", 0);
            Add("ForumEntreprises", GlobalEventType.CareerFair, "Forum entreprises", "Chaque joueur reçoit 20 par terrain possédé.", 20);

            return events;
        }

        // Personnages 

        private static void CreateCharacters()
        {
            void Add(string file, CharacterId id, string name, string description, string colorHex,
                     PowerType power, string powerName, string powerDescription)
            {
                var asset = LoadOrCreate<CharacterData>($"{Root}/Characters/CHAR_{file}.asset");
                var so = new SerializedObject(asset);
                Prop(so, "id").enumValueIndex = (int)id;
                Prop(so, "displayName").stringValue = name;
                Prop(so, "description").stringValue = description;
                ColorUtility.TryParseHtmlString(colorHex, out Color color);
                Prop(so, "tokenColor").colorValue = color;
                Prop(so, "power").enumValueIndex = (int)power;
                Prop(so, "powerName").stringValue = powerName;
                Prop(so, "powerDescription").stringValue = powerDescription;

                // Profil d'IA, si le champ existe déjà dans CharacterData.
                SerializedProperty profile = so.FindProperty("aiProfile");
                if (profile != null)
                    profile.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AIProfile>($"{Root}/AI/AI_{id}.asset");

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }

            Add("Major", CharacterId.Major, "Le Major de promo", "Ambitieux, il achète tout et construit vite.", "#C93C3C",
                PowerType.DoubleRent, "Dossier béton", "Double un loyer qu'un rival doit vous payer.");
            Add("Fetard", CharacterId.Partygoer, "Le Fêtard du BDE", "Imprévisible, il dépense sans compter.", "#7A4DE0",
                PowerType.CancelPayment, "Soirée mémorable", "Annule un loyer ou une taxe à payer.");
            Add("Techos", CharacterId.Techie, "Le Techos", "Calculateur, il n'investit que dans ce qui rapporte.", "#17805F",
                PowerType.Reroll, "Relance", "Relance les dés et garde le second résultat.");
        }

        // Niveaux 

        private static List<LevelData> CreateLevels(Dictionary<string, SquareData> squares,
                                                    List<CardData> cards, List<GlobalEventData> events)
        {
            var levels = new List<LevelData>();

            void Add(int number, string file, string name, int startingMoney, int maxTurns, int[] eventTurns,
                     AIDifficulty difficulty, params string[] order)
            {
                var asset = LoadOrCreate<LevelData>($"{Root}/Levels/LVL_{number}_{file}.asset");
                var so = new SerializedObject(asset);
                Prop(so, "levelNumber").intValue = number;
                Prop(so, "levelName").stringValue = name;
                SetObjects(Prop(so, "squares"), order.Select(key => squares[key]).ToList());
                Prop(so, "startingMoney").intValue = startingMoney;
                Prop(so, "passStartBonus").intValue = 150;
                Prop(so, "detentionFine").intValue = 50;
                Prop(so, "maxTurns").intValue = maxTurns;
                SetObjects(Prop(so, "cardDeck"), cards);
                SetObjects(Prop(so, "globalEvents"), eventTurns.Length > 0 ? events : new List<GlobalEventData>());
                SetInts(Prop(so, "globalEventTurns"), eventTurns);
                Prop(so, "aiDifficulty").enumValueIndex = (int)difficulty;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                levels.Add(asset);
            }

            // Niveaux 1 et 3 
            Add(1, "PremiereSemaine", "Première semaine", 1200, 12, new int[0], AIDifficulty.Easy,
                "LaRentree", "Cafeteria", "SalleTP", "RER_A", "Colle", "Bibliotheque", "FraisScolarite", "SalleProjet",
                "FoyerEtudiant", "FabLab", "Evenement", "SalleRobotique", "Rattrapage", "BusCampus", "AmphiA", "AmphiB");

            Add(2, "MiSemestre", "Mi-semestre", 1000, 15, new[] { 5, 10 }, AIDifficulty.Normal,
                "LaRentree", "Cafeteria", "SalleTP", "Evenement", "RER_A", "Colle", "Bibliotheque", "FraisScolarite",
                "SalleProjet", "MachineCafe", "FoyerEtudiant", "FabLab", "Evenement", "SalleRobotique", "BusCampus",
                "Rattrapage", "AmphiA", "AmphiB", "BureauBDE", "Direction");

            Add(3, "SemestreEnFolie", "Semestre en folie", 1000, 15, new[] { 4, 8, 12 }, AIDifficulty.Hard,
                "LaRentree", "Cafeteria", "SalleTP", "Evenement", "RER_A", "Bibliotheque", "Colle", "FraisScolarite",
                "SalleProjet", "MachineCafe", "FabLab", "SalleRobotique", "FoyerEtudiant", "Evenement", "BusCampus",
                "AmphiA", "AmphiB", "RouletteBDE", "Rattrapage", "BureauBDE", "Evenement", "Direction",
                "Incubateur", "LaboRecherche");

            return levels;
        }

        // Outils

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static SerializedProperty Prop(SerializedObject so, string name) =>
            so.FindProperty(name) ?? throw new System.InvalidOperationException(
                $"Champ « {name} » introuvable dans {so.targetObject.GetType().Name} : le nom a-t-il changé ?");

        private static void SetInts(SerializedProperty array, IList<int> values)
        {
            array.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).intValue = values[i];
        }

        private static void SetObjects<T>(SerializedProperty array, IList<T> values) where T : Object
        {
            array.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
#endif