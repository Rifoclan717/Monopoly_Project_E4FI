using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Fait parler les IA aux moments forts (achat, monopole, loyer, colle, pouvoir, faillite).
    /// Écoute le bus CampusEvents, choisit qui parle et quoi dire (AIBarks), puis envoie le texte à la bulle.
    ///
    /// Branchement, une fois par scène :
    ///   1. Ajouter ce composant sur un objet de la scène.
    ///   2. Dans l'Inspector, relier « On Speech » à CampusHUDController.ShowAISpeech (string, float).
    ///   3. En début de partie, appeler Bind(board, rivals) avec les cases et les AIController des rivaux.
    /// Sans Bind, les rivaux sont cherchés dans la scène et les cases sont nommées « ce terrain ».
    /// </summary>
    public class AIBarkController : MonoBehaviour
    {
        [Serializable] public class SpeechEvent : UnityEvent<string, float> { }

        [Tooltip("Durée d'affichage d'une réplique, en secondes")]
        [Min(0.5f)] [SerializeField] private float lineDuration = 4.5f;
        [Tooltip("Délai minimal entre deux répliques ordinaires, en secondes")]
        [Min(0f)] [SerializeField] private float cooldown = 3f;
        [Tooltip("0 = aléatoire. Une valeur fixe rend les répliques reproductibles (tests).")]
        [SerializeField] private int seed;
        [SerializeField] private bool logLines;

        [Tooltip("Relier à CampusHUDController.ShowAISpeech (string, float)")]
        [SerializeField] private SpeechEvent onSpeech = new SpeechEvent();

        /// <summary>Pour le code : joueur qui parle, personnage, réplique.</summary>
        public event Action<AIBarkLine> OnBark;

        private readonly Dictionary<int, CharacterId> rivals = new Dictionary<int, CharacterId>();
        private IReadOnlyList<BoardSquare> board;
        private AIBarks barks;
        private readonly System.Random pick = new System.Random();

        public SpeechEvent OnSpeech => onSpeech;

        /// <summary>À appeler en début de partie : les cases (noms, monopoles) et les rivaux pilotés par l'IA.</summary>
        public void Bind(IReadOnlyList<BoardSquare> squares, IEnumerable<AIController> aiPlayers)
        {
            board = squares;
            rivals.Clear();
            if (aiPlayers != null)
                foreach (var ai in aiPlayers)
                    if (ai != null) rivals[ai.PlayerIndex] = ai.Character;
        }

        private AIBarks Barks => barks ?? (barks = new AIBarks(seed, cooldown));

        private void OnEnable()
        {
            CampusEvents.OnPropertyBought += HandleBought;
            CampusEvents.OnPropertyUpgraded += HandleUpgraded;
            CampusEvents.OnRentPaid += HandleRent;
            CampusEvents.OnPlayerMoved += HandleMoved;
            CampusEvents.OnPowerUsed += HandlePower;
            CampusEvents.OnPlayerBankrupt += HandleBankrupt;
        }

        private void OnDisable()
        {
            CampusEvents.OnPropertyBought -= HandleBought;
            CampusEvents.OnPropertyUpgraded -= HandleUpgraded;
            CampusEvents.OnRentPaid -= HandleRent;
            CampusEvents.OnPlayerMoved -= HandleMoved;
            CampusEvents.OnPowerUsed -= HandlePower;
            CampusEvents.OnPlayerBankrupt -= HandleBankrupt;
        }

        private void Start()
        {
            if (rivals.Count > 0) return;
            foreach (var ai in FindObjectsByType<AIController>(FindObjectsSortMode.None))
                rivals[ai.PlayerIndex] = ai.Character;
        }

        // ------------------------------------------------------------------
        // Réactions aux événements du jeu
        // ------------------------------------------------------------------

        private void HandleBought(int playerId, int tileIndex, int price)
        {
            if (rivals.TryGetValue(playerId, out var me))
            {
                var moment = CompletesMonopoly(playerId, tileIndex) ? BarkMoment.MonopolyCompleted : BarkMoment.Bought;
                Say(playerId, me, moment, tileIndex, price);
            }
            else if (TryPickRival(playerId, out int other))
            {
                Say(other, rivals[other], BarkMoment.RivalBought, tileIndex, price);
            }
        }

        private void HandleUpgraded(int playerId, int tileIndex, int newLevel)
        {
            if (rivals.TryGetValue(playerId, out var me)) Say(playerId, me, BarkMoment.Upgraded, tileIndex, newLevel);
        }

        private void HandleRent(int tenantId, int ownerId, int tileIndex, int amount)
        {
            if (rivals.TryGetValue(ownerId, out var owner)) Say(ownerId, owner, BarkMoment.RentCollected, tileIndex, amount);
            else if (rivals.TryGetValue(tenantId, out var tenant)) Say(tenantId, tenant, BarkMoment.RentPaid, tileIndex, amount);
        }

        private void HandleMoved(int playerId, int targetTileIndex, int stepsDelta, bool sentToColle)
        {
            if (sentToColle && rivals.TryGetValue(playerId, out var me)) Say(playerId, me, BarkMoment.SentToDetention, -1, 0);
        }

        private void HandlePower(int playerId, string characterName, string powerName, string description)
        {
            if (rivals.TryGetValue(playerId, out var me)) Say(playerId, me, BarkMoment.PowerUsed, -1, 0);
        }

        private void HandleBankrupt(int playerId, int creditorId)
        {
            if (rivals.TryGetValue(playerId, out var me))
                Say(playerId, me, BarkMoment.Bankrupt, -1, 0);
            else if (rivals.TryGetValue(creditorId, out var creditor))
                Say(creditorId, creditor, BarkMoment.RivalBankrupt, -1, 0);
            else if (TryPickRival(playerId, out int other))
                Say(other, rivals[other], BarkMoment.RivalBankrupt, -1, 0);
        }

        // ------------------------------------------------------------------

        private void Say(int speaker, CharacterId character, BarkMoment moment, int tileIndex, int amount)
        {
            var line = Barks.TryBark(speaker, character, moment, Time.unscaledTime, SquareName(tileIndex), amount);
            if (line == null) return;

            if (logLines) Debug.Log($"[Répliques] {line.Formatted}");
            OnBark?.Invoke(line);
            onSpeech?.Invoke(line.Formatted, lineDuration);
        }

        /// <summary>Un rival au hasard, autre que <paramref name="excluded"/>.</summary>
        private bool TryPickRival(int excluded, out int rival)
        {
            var candidates = new List<int>();
            foreach (var id in rivals.Keys) if (id != excluded) candidates.Add(id);
            rival = candidates.Count > 0 ? candidates[pick.Next(candidates.Count)] : -1;
            return rival >= 0;
        }

        private string SquareName(int tileIndex)
        {
            if (board == null || tileIndex < 0 || tileIndex >= board.Count) return null;
            return board[tileIndex].Data.DisplayName;
        }

        /// <summary>L'achat de cette case donne-t-il le groupe complet au joueur ?</summary>
        private bool CompletesMonopoly(int playerId, int tileIndex)
        {
            if (board == null || tileIndex < 0 || tileIndex >= board.Count) return false;
            var data = board[tileIndex].Data;
            if (data.Type != SquareType.Property || data.Group == ColorGroup.None) return false;

            foreach (var sq in board)
            {
                if (sq.Data.Type != SquareType.Property || sq.Data.Group != data.Group) continue;
                if (sq.Index != tileIndex && sq.OwnerIndex != playerId) return false;
            }
            return true;
        }

        /// <summary>Pour les tests hors Unity : branche les événements sans passer par OnEnable.</summary>
        internal void EnableForTests() => OnEnable();
        internal void DisableForTests() => OnDisable();
    }
}
