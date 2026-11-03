using System.Collections.Generic;
using UnityEngine;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Point d'entrée de l'IA pour la machine à états du tour.
    /// Un AIController par rival. La logique construit un AIGameSnapshot, appelle la méthode
    /// correspondant à la décision attendue, puis applique elle-même le résultat.
    ///
    /// Exemple (état « Résoudre » sur un terrain libre) :
    ///     var decision = ai.DecideBuy(snapshot, square);
    ///     if (decision.Action == AIActionType.Buy) logic.Buy(ai.PlayerIndex, square);
    /// </summary>
    public class AIController : MonoBehaviour
    {
        [SerializeField] private int playerIndex = 1;
        [Tooltip("Laisser vide pour utiliser les valeurs du document de règles du personnage choisi")]
        [SerializeField] private AIProfile profile;
        [SerializeField] private CharacterId character = CharacterId.Major;
        [SerializeField] private AIDifficulty difficulty = AIDifficulty.Normal;
        [Tooltip("0 = aléatoire. Une valeur fixe rend les parties reproductibles (tests).")]
        [SerializeField] private int seed;
        [SerializeField] private bool logDecisions = true;

        private AIBrain brain;

        public int PlayerIndex => playerIndex;
        public CharacterId Character => profile != null ? profile.Character : character;
        public AIDifficulty Difficulty => difficulty;
        public AIParameters Parameters => Brain.Parameters;

        private AIBrain Brain
        {
            get
            {
                if (brain == null) Rebuild();
                return brain;
            }
        }

        /// <summary>Configuration par code (création des rivaux en début de partie).</summary>
        public void Configure(int index, CharacterId id, AIDifficulty level, AIProfile customProfile = null, int randomSeed = 0)
        {
            playerIndex = index;
            character = id;
            difficulty = level;
            profile = customProfile;
            seed = randomSeed;
            Rebuild();
        }

        /// <summary>Configuration à partir du joueur et du niveau (difficulté lue dans LevelData).</summary>
        public void Configure(Player player, LevelData level, AIProfile customProfile = null, int randomSeed = 0) =>
            Configure(player.Index, player.Character.Id, level.Difficulty, customProfile, randomSeed);

        private void Rebuild()
        {
            var source = profile != null ? profile : AIProfile.CreateDefault(character);
            brain = new AIBrain(playerIndex, source.ToParameters(difficulty), seed);
        }

        private void OnValidate() => brain = null;

        // ------------------------------------------------------------------
        // Décisions
        // ------------------------------------------------------------------

        /// <summary>Terrain libre : acheter ou passer.</summary>
        public AIDecision DecideBuy(AIGameSnapshot snapshot, int square) => Log(Brain.DecideBuy(snapshot, square));

        /// <summary>Phase d'actions facultatives : améliorations à effectuer, dans l'ordre.</summary>
        public List<AIDecision> DecideUpgrades(AIGameSnapshot snapshot) => LogAll(Brain.DecideUpgrades(snapshot));

        /// <summary>Début de tour en colle : attendre, payer 50 ou utiliser le billet d'absence.</summary>
        public AIDecision DecideDetention(AIGameSnapshot snapshot) => Log(Brain.DecideDetention(snapshot));

        /// <summary>Loyer à payer : utiliser le billet d'absence ?</summary>
        public bool ShouldUseTicketOnRent(AIGameSnapshot snapshot, int rent)
        {
            bool use = Brain.ShouldUseTicketOnRent(snapshot, rent);
            if (use && logDecisions) Debug.Log($"[IA {playerIndex} {Character}] Billet d'absence utilisé sur un loyer de {rent}");
            return use;
        }

        /// <summary>Le pouvoir du personnage peut être utilisé : maintenant ou plus tard ?</summary>
        public AIDecision DecidePower(AIGameSnapshot snapshot, PowerContext context)
        {
            var decision = Brain.DecidePower(snapshot, context);
            return decision.Action == AIActionType.UsePower ? Log(decision) : decision;
        }

        /// <summary>Dette supérieure à l'argent : ventes à effectuer, dans l'ordre.</summary>
        public List<AIDecision> ChooseAssetsToSell(AIGameSnapshot snapshot, int amountNeeded) =>
            LogAll(Brain.ChooseAssetsToSell(snapshot, amountNeeded));

        /// <summary>Carte « Amélioration offerte » : terrain choisi, ou -1.</summary>
        public int ChooseFreeUpgrade(AIGameSnapshot snapshot) => Brain.ChooseFreeUpgrade(snapshot);

        /// <summary>Roulette du BDE (niveau 3) : jouer ou non.</summary>
        public AIDecision DecideRoulette(AIGameSnapshot snapshot, int square) => Log(Brain.DecideRoulette(snapshot, square));

        // ------------------------------------------------------------------

        private AIDecision Log(AIDecision d)
        {
            if (logDecisions) Debug.Log($"[IA {playerIndex} {Character}] {d}");
            return d;
        }

        private List<AIDecision> LogAll(List<AIDecision> list)
        {
            if (logDecisions) foreach (var d in list) Debug.Log($"[IA {playerIndex} {Character}] {d}");
            return list;
        }
    }
}
