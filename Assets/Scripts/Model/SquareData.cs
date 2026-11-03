using System;
using System.Collections.Generic;
using UnityEngine;

namespace CampusTycoon
{
    /// <summary>Données fixes d'une case du plateau </summary>
    [CreateAssetMenu(fileName = "SQ_NouvelleCase", menuName = "Campus Tycoon/Case", order = 0)]
    public class SquareData : ScriptableObject
    {
        [Header("Identité")]
        [SerializeField] private string displayName = "Nouvelle case";
        [SerializeField] private SquareType type = SquareType.Property;
        [Tooltip("Groupe de couleur. None pour tout ce qui n'est pas un terrain.")]
        [SerializeField] private ColorGroup group = ColorGroup.None;

        [Header("Achat : terrain, gare, compagnie")]
        [Min(0)] [SerializeField] private int price;
        [Tooltip("Terrain : 5 valeurs [sans monopole, monopole, niv. 1, niv. 2, niv. 3]\nGare : 2 valeurs [1 gare, 2 gares]\nCompagnie : 1 valeur (multiplicateur des dés)")]
        [SerializeField] private int[] rents = Array.Empty<int>();
        [Tooltip("Coût d'un étage")]
        [Min(0)] [SerializeField] private int upgradeCost;

        [Header("Autres cases")]
        [Tooltip("Taxe : montant à payer. Foyer : bonus. Roulette : mise.")]
        [Min(0)] [SerializeField] private int amount;

        public string DisplayName => displayName;
        public SquareType Type => type;
        public ColorGroup Group => group;
        public int Price => price;
        public IReadOnlyList<int> Rents => rents;
        public int UpgradeCost => upgradeCost;
        public int Amount => amount;

        public bool IsOwnable =>
            type == SquareType.Property || type == SquareType.Station || type == SquareType.Utility;

        /// <summary>Nombre de loyers attendus pour un type de case </summary>
        public static int ExpectedRentCount(SquareType squareType)
        {
            switch (squareType)
            {
                case SquareType.Property: return 5;
                case SquareType.Station: return 2;
                case SquareType.Utility: return 1;
                default: return 0;
            }
        }

        private void OnValidate()
        {
            if (rents == null) rents = Array.Empty<int>();

            int expected = ExpectedRentCount(type);
            if (rents.Length != expected) Array.Resize(ref rents, expected);

            if (type != SquareType.Property) group = ColorGroup.None;
        }
    }
}