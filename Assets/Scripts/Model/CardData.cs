using UnityEngine;

namespace CampusTycoon
{
    /// <summary>Carte Événement</summary>
    [CreateAssetMenu(fileName = "CARD_Nouvelle", menuName = "Campus Tycoon/Carte Événement", order = 1)]
    public class CardData : ScriptableObject
    {
        [SerializeField] private string title = "Nouvelle carte";
        [TextArea(2, 4)] [SerializeField] private string description;
        [SerializeField] private CardEffect effect;
        [Tooltip("Montant ou nombre de cases (négatif = reculer). Ignoré si l'effet n'en a pas besoin.")]
        [SerializeField] private int amount;

        public string Title => title;
        public string Description => description;
        public CardEffect Effect => effect;
        public int Amount => amount;
    }
}