using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CampusTycoon
{
    /// <summary>Tout ce qui définit un niveau (asset dans Assets/Data/Levels).</summary>
    [CreateAssetMenu(fileName = "LVL_Nouveau", menuName = "Campus Tycoon/Niveau", order = 10)]
    public class LevelData : ScriptableObject
    {
        [Header("Identité")]
        [Min(1)] [SerializeField] private int levelNumber = 1;
        [SerializeField] private string levelName = "Nouveau niveau";

        [Header("Plateau (index 0 = La Rentrée, puis dans le sens du jeu)")]
        [SerializeField] private List<SquareData> squares = new List<SquareData>();

        [Header("Économie")]
        [Min(0)] [SerializeField] private int startingMoney = 1000;
        [Min(0)] [SerializeField] private int passStartBonus = 150;
        [Min(0)] [SerializeField] private int detentionFine = 50;

        [Header("Durée")]
        [Min(1)] [SerializeField] private int maxTurns = 15;

        [Header("Cartes et événements du semestre")]
        [SerializeField] private List<CardData> cardDeck = new List<CardData>();
        [SerializeField] private List<GlobalEventData> globalEvents = new List<GlobalEventData>();
        [Tooltip("Tours à la fin desquels un événement du semestre se déclenche (vide = aucun).")]
        [SerializeField] private int[] globalEventTurns = new int[0];

        [Header("IA")]
        [SerializeField] private AIDifficulty aiDifficulty = AIDifficulty.Normal;

        public int LevelNumber => levelNumber;
        public string LevelName => levelName;
        public IReadOnlyList<SquareData> Squares => squares;
        public int SquareCount => squares.Count;
        public int StartingMoney => startingMoney;
        public int PassStartBonus => passStartBonus;
        public int DetentionFine => detentionFine;
        public int MaxTurns => maxTurns;
        public IReadOnlyList<CardData> CardDeck => cardDeck;
        public IReadOnlyList<GlobalEventData> GlobalEvents => globalEvents;
        public IReadOnlyList<int> GlobalEventTurns => globalEventTurns;
        public AIDifficulty Difficulty => aiDifficulty;

        /// <summary>Index case du type demandé </summary>
        public int IndexOf(SquareType squareType) =>
            squares.FindIndex(s => s != null && s.Type == squareType);

        [ContextMenu("Vérifier le niveau")]
        private void ValidateFromMenu() => Validate();

        /// <summary>Vérifie et affiche le résultat dans la console </summary>
        public bool Validate()
        {
            var errors = new List<string>();

            if (squares.Count == 0)
                errors.Add("Le plateau est vide.");
            else if (squares[0] == null || squares[0].Type != SquareType.Start)
                errors.Add("La case 0 doit être La Rentrée (type Start).");

            for (int i = 0; i < squares.Count; i++)
                if (squares[i] == null) errors.Add($"La case {i} n'est pas renseignée.");

            int detentionCount = squares.Count(s => s != null && s.Type == SquareType.Detention);
            if (detentionCount != 1)
                errors.Add($"Il faut exactement 1 case Colle ({detentionCount} trouvée(s)).");

            var groups = squares
                .Where(s => s != null && s.Type == SquareType.Property)
                .GroupBy(s => s.Group);
            foreach (var colorGroup in groups)
                if (colorGroup.Key == ColorGroup.None || colorGroup.Count() != 2)
                    errors.Add($"Groupe {colorGroup.Key} : {colorGroup.Count()} terrain(s), 2 attendus.");

            bool hasEventSquare = squares.Any(s => s != null && s.Type == SquareType.EventCard);
            if (hasEventSquare && cardDeck.Count == 0)
                errors.Add("Le plateau a des cases Événement mais le paquet de cartes est vide.");
            if (cardDeck.Any(c => c == null))
                errors.Add("Une carte du paquet n'est pas renseignée.");

            foreach (int turn in globalEventTurns)
                if (turn < 1 || turn > maxTurns)
                    errors.Add($"Tour d'événement {turn} en dehors de la partie (1 à {maxTurns}).");
            if (globalEventTurns.Length > globalEvents.Count)
                errors.Add($"{globalEventTurns.Length} événements prévus mais {globalEvents.Count} disponibles.");

            if (errors.Count == 0)
            {
                Debug.Log($"[LevelData] {name} : OK, {squares.Count} cases.", this);
                return true;
            }

            Debug.LogError($"[LevelData] {name} : {errors.Count} problème(s)\n- " + string.Join("\n- ", errors), this);
            return false;
        }
    }
}