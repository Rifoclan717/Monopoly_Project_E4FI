using UnityEngine;

namespace CampusTycoon
{
    /// <summary>Un événement du semestre </summary>
    [CreateAssetMenu(fileName = "GEV_Nouveau", menuName = "Campus Tycoon/Événement du semestre", order = 2)]
    public class GlobalEventData : ScriptableObject
    {
        [SerializeField] private GlobalEventType type;
        [SerializeField] private string title = "Nouvel événement";
        [TextArea(2, 4)] [SerializeField] private string description;
        [Tooltip("Durée en tours de table (un tour de chaque joueur).")]
        [Min(1)] [SerializeField] private int durationRounds = 1;
        [Tooltip("Valeur utile à l'effet (ex. 20 par terrain pour le Forum entreprises).")]
        [Min(0)] [SerializeField] private int amount;

        public GlobalEventType Type => type;
        public string Title => title;
        public string Description => description;
        public int DurationRounds => durationRounds;
        public int Amount => amount;
    }
}