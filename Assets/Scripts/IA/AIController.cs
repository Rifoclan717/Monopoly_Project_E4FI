using System;
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

        /// <summary>
        /// Chaque décision de chaque IA, au moment où elle est prise (panneau « cerveau de l'IA », journaux).
        /// Les abonnés ne doivent pas modifier la décision : elle est déjà renvoyée au TurnManager.
        /// </summary>
        public static event Action<AIController, AIDecision> DecisionMade;

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
            var decision = new AIDecision(
                use ? AIActionType.UseExemptionTicket : AIActionType.KeepExemptionTicket, -1, rent,
                use ? $"Annule un loyer de {rent} avec le billet d'absence"
                    : $"Paie le loyer de {rent} et garde le billet pour un plus gros");
            if (use) Log(decision); else Notify(decision);
            return use;
        }

        /// <summary>Le pouvoir du personnage peut être utilisé : maintenant ou plus tard ?</summary>
        public AIDecision DecidePower(AIGameSnapshot snapshot, PowerContext context)
        {
            var decision = Brain.DecidePower(snapshot, context);
            return decision.Action == AIActionType.UsePower ? Log(decision) : Notify(decision);
        }

        /// <summary>Dette supérieure à l'argent : ventes à effectuer, dans l'ordre.</summary>
        public List<AIDecision> ChooseAssetsToSell(AIGameSnapshot snapshot, int amountNeeded) =>
            LogAll(Brain.ChooseAssetsToSell(snapshot, amountNeeded));

        /// <summary>Carte « Amélioration offerte » : terrain choisi, ou -1.</summary>
        public int ChooseFreeUpgrade(AIGameSnapshot snapshot)
        {
            int square = Brain.ChooseFreeUpgrade(snapshot);
            Log(square >= 0
                ? new AIDecision(AIActionType.Upgrade, square, 0, "Amélioration offerte : " + snapshot.Squares[square].Name)
                : new AIDecision(AIActionType.Pass, -1, 0, "Amélioration offerte : aucun monopole, reçoit 60"));
            return square;
        }

        /// <summary>Roulette du BDE (niveau 3) : jouer ou non.</summary>
        public AIDecision DecideRoulette(AIGameSnapshot snapshot, int square) => Log(Brain.DecideRoulette(snapshot, square));

        // ------------------------------------------------------------------

        private AIDecision Log(AIDecision d)
        {
            if (logDecisions) Debug.Log($"[IA {playerIndex} {Character}] {d}");
            return Notify(d);
        }

        private List<AIDecision> LogAll(List<AIDecision> list)
        {
            foreach (var d in list) Log(d);
            return list;
        }

        /// <summary>Signale la décision aux abonnés, sans jamais laisser une erreur d'affichage bloquer la partie.</summary>
        private AIDecision Notify(AIDecision d)
        {
            if (d == null || DecisionMade == null) return d;
            try { DecisionMade(this, d); }
            catch (Exception e) { Debug.LogWarning($"[IA] Erreur d'un abonné à DecisionMade : {e.Message}"); }
            return d;
        }
    }
}
