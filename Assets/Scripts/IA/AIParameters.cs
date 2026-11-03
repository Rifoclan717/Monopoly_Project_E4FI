using System;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Paramètres effectifs d'une IA, après application de la difficulté.
    /// C# pur : AIProfile (ScriptableObject) sert à les régler dans l'Inspector.
    /// </summary>
    [Serializable]
    public sealed class AIParameters
    {
        public CharacterId Character;

        // --- Personnalité (document de règles, niveau 2) ---
        /// <summary>Chance d'acheter un terrain abordable.</summary>
        public float BuyChance = 0.9f;
        /// <summary>Techos : achète à 100 % si le terrain est « rentable », sinon jamais.</summary>
        public bool BuyOnlyIfProfitable;
        /// <summary>Argent à conserver après tout achat, amélioration ou mise.</summary>
        public int CashReserve = 50;
        /// <summary>Chance de construire à chaque occasion.</summary>
        public float BuildChance = 1f;
        /// <summary>Montant minimal (loyer encaissé, payé ou évité) qui déclenche le pouvoir.</summary>
        public int PowerTriggerAmount = 40;
        /// <summary>Chance de déclencher le pouvoir quand la condition est remplie.</summary>
        public float PowerTriggerChance = 1f;
        /// <summary>Chance de jouer à la Roulette du BDE quand c'est possible (niveau 3).</summary>
        public float RouletteChance = 1f;

        // --- Poids de la fonction d'utilité ---
        /// <summary>Poids du revenu de loyers espéré.</summary>
        public float IncomeWeight = 1f;
        /// <summary>Poids du revenu qu'on retire aux rivaux en prenant le terrain.</summary>
        public float BlockWeight = 0.5f;
        /// <summary>Poids du potentiel d'amélioration quand un achat complète un monopole.</summary>
        public float MonopolyWeight = 0.5f;
        /// <summary>Aversion au risque de manquer de liquidités.</summary>
        public float RiskWeight = 1f;

        // --- Réglages liés à la difficulté ---
        /// <summary>Niveau 3 : utilise le pouvoir au meilleur moment plutôt qu'au premier déclencheur.</summary>
        public bool SmartPower;
        /// <summary>Niveau 3 : en vente forcée, vend ce qui coûte le moins en revenus futurs.</summary>
        public bool SmartSelling;

        public PowerType Power => PowerOf(Character);

        public static PowerType PowerOf(CharacterId character)
        {
            switch (character)
            {
                case CharacterId.Major: return PowerType.DoubleRent;
                case CharacterId.Partygoer: return PowerType.CancelPayment;
                default: return PowerType.Reroll;
            }
        }

        public AIParameters Clone() => (AIParameters)MemberwiseClone();

        /// <summary>Valeurs du document de règles (niveau 2, difficulté normale).</summary>
        public static AIParameters Defaults(CharacterId character)
        {
            switch (character)
            {
                case CharacterId.Major: // Ambitieux et agressif
                    return new AIParameters
                    {
                        Character = character,
                        BuyChance = 0.9f, CashReserve = 50, BuildChance = 1f,
                        PowerTriggerAmount = 40, PowerTriggerChance = 1f,
                        RouletteChance = 0.5f,
                        BlockWeight = 0.8f, MonopolyWeight = 0.8f, RiskWeight = 0.6f
                    };

                case CharacterId.Partygoer: // Chaotique et imprévisible
                    return new AIParameters
                    {
                        Character = character,
                        BuyChance = 0.55f, CashReserve = 0, BuildChance = 0.3f,
                        PowerTriggerAmount = 30, PowerTriggerChance = 0.25f,
                        RouletteChance = 1f,
                        BlockWeight = 0.2f, MonopolyWeight = 0.3f, RiskWeight = 0.3f
                    };

                default: // Techos : calculateur et prudent
                    return new AIParameters
                    {
                        Character = CharacterId.Techie,
                        BuyChance = 1f, BuyOnlyIfProfitable = true, CashReserve = 300, BuildChance = 1f,
                        PowerTriggerAmount = 60, PowerTriggerChance = 1f,
                        RouletteChance = 1f,
                        BlockWeight = 0.5f, MonopolyWeight = 0.6f, RiskWeight = 1.5f
                    };
            }
        }
    }

    /// <summary>
    /// Module les paramètres selon la difficulté du niveau (section « Réglage des IA selon le niveau »).
    /// </summary>
    public static class AIDifficultyModifier
    {
        public static AIParameters Apply(AIParameters source, AIDifficulty difficulty)
        {
            var p = source.Clone();
            switch (difficulty)
            {
                case AIDifficulty.Easy:
                    // Chance d'acheter -30 points, réserve nulle, pouvoir une fois sur deux.
                    p.BuyChance = Clamp01(p.BuyChance - 0.30f);
                    p.CashReserve = 0;
                    p.PowerTriggerChance *= 0.5f;
                    p.BlockWeight = 0f;
                    p.MonopolyWeight *= 0.5f;
                    p.SmartPower = false;
                    p.SmartSelling = false;
                    break;

                case AIDifficulty.Hard:
                    // Chance d'acheter +10 points (max 100 %), réserve +50 %, pouvoir au meilleur moment.
                    p.BuyChance = Clamp01(p.BuyChance + 0.10f);
                    p.CashReserve = (int)Math.Round(p.CashReserve * 1.5f);
                    p.SmartPower = true;
                    p.SmartSelling = true;
                    break;
            }
            return p;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
