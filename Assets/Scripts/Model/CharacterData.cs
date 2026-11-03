using UnityEngine;

namespace CampusTycoon
{
    /// <summary>Personnage jouable .</summary>
    [CreateAssetMenu(fileName = "CHAR_Nouveau", menuName = "Campus Tycoon/Personnage", order = 3)]
    public class CharacterData : ScriptableObject
    {
        [Header("Identité")]
        [SerializeField] private CharacterId id;
        [SerializeField] private string displayName = "Nouveau personnage";
        [TextArea(2, 4)] [SerializeField] private string description;
        [SerializeField] private Sprite portrait;
        [SerializeField] private Color tokenColor = Color.white;

        [Header("Pouvoir (une fois par partie)")]
        [SerializeField] private PowerType power;
        [SerializeField] private string powerName;
        [TextArea(2, 4)] [SerializeField] private string powerDescription;

        public CharacterId Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Portrait => portrait;
        public Color TokenColor => tokenColor;
        public PowerType Power => power;
        public string PowerName => powerName;
        public string PowerDescription => powerDescription;
    }
}