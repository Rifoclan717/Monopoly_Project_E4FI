using UnityEngine;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Profil d'IA d'un personnage, réglable dans l'Inspector.
    /// Les valeurs par défaut sont celles du document de règles (niveau 2).
    /// Menu : Campus Tycoon > IA > Créer les profils par défaut.
    /// </summary>
    [CreateAssetMenu(fileName = "AI_NouveauProfil", menuName = "Campus Tycoon/Profil d'IA", order = 10)]
    public class AIProfile : ScriptableObject
    {
        [Header("Personnage")]
        [SerializeField] private CharacterId character = CharacterId.Major;

        [Header("Achat")]
        [Tooltip("Chance d'acheter un terrain abordable")]
        [Range(0f, 1f)] [SerializeField] private float buyChance = 0.9f;
        [Tooltip("Techos : n'achète que les terrains « rentables » (groupe sans rival, ou gare)")]
        [SerializeField] private bool buyOnlyIfProfitable;
        [Tooltip("Argent à conserver après un achat, une amélioration ou une mise")]
        [Min(0)] [SerializeField] private int cashReserve = 50;

        [Header("Construction")]
        [Tooltip("Chance de construire à chaque occasion")]
        [Range(0f, 1f)] [SerializeField] private float buildChance = 1f;

        [Header("Pouvoir")]
        [Tooltip("Montant minimal qui déclenche le pouvoir (loyer encaissé, payé ou évité)")]
        [Min(0)] [SerializeField] private int powerTriggerAmount = 40;
        [Tooltip("Chance de déclencher le pouvoir quand la condition est remplie")]
        [Range(0f, 1f)] [SerializeField] private float powerTriggerChance = 1f;

        [Header("Roulette du BDE (niveau 3)")]
        [Range(0f, 1f)] [SerializeField] private float rouletteChance = 1f;

        [Header("Poids de la fonction d'utilité")]
        [Min(0f)] [SerializeField] private float incomeWeight = 1f;
        [Tooltip("Intérêt de prendre un terrain pour en priver un rival")]
        [Min(0f)] [SerializeField] private float blockWeight = 0.5f;
        [Tooltip("Intérêt de compléter un monopole (potentiel d'amélioration)")]
        [Min(0f)] [SerializeField] private float monopolyWeight = 0.5f;
        [Tooltip("Aversion au risque de manquer de liquidités")]
        [Min(0f)] [SerializeField] private float riskWeight = 1f;

        public CharacterId Character => character;

        /// <summary>Paramètres effectifs pour la difficulté du niveau.</summary>
        public AIParameters ToParameters(AIDifficulty difficulty)
        {
            var p = new AIParameters
            {
                Character = character,
                BuyChance = buyChance,
                BuyOnlyIfProfitable = buyOnlyIfProfitable,
                CashReserve = cashReserve,
                BuildChance = buildChance,
                PowerTriggerAmount = powerTriggerAmount,
                PowerTriggerChance = powerTriggerChance,
                RouletteChance = rouletteChance,
                IncomeWeight = incomeWeight,
                BlockWeight = blockWeight,
                MonopolyWeight = monopolyWeight,
                RiskWeight = riskWeight
            };
            return AIDifficultyModifier.Apply(p, difficulty);
        }

        /// <summary>Recopie les valeurs du document de règles pour ce personnage.</summary>
        public void LoadDefaults(CharacterId id)
        {
            var d = AIParameters.Defaults(id);
            character = d.Character;
            buyChance = d.BuyChance;
            buyOnlyIfProfitable = d.BuyOnlyIfProfitable;
            cashReserve = d.CashReserve;
            buildChance = d.BuildChance;
            powerTriggerAmount = d.PowerTriggerAmount;
            powerTriggerChance = d.PowerTriggerChance;
            rouletteChance = d.RouletteChance;
            incomeWeight = d.IncomeWeight;
            blockWeight = d.BlockWeight;
            monopolyWeight = d.MonopolyWeight;
            riskWeight = d.RiskWeight;
        }

        [ContextMenu("Charger les valeurs du document de règles")]
        private void LoadDefaultsForCurrentCharacter() => LoadDefaults(character);

        /// <summary>Profil créé à la volée, si aucun asset n'est assigné.</summary>
        public static AIProfile CreateDefault(CharacterId id)
        {
            var profile = CreateInstance<AIProfile>();
            profile.name = "AI_" + id;
            profile.LoadDefaults(id);
            return profile;
        }
    }
}
