using System;
using System.Collections.Generic;
using System.Globalization;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Journal des dernières décisions de chaque IA (C# pur, testable hors Unity).
    /// Alimente le panneau « cerveau de l'IA » : action, score et justification, pour la soutenance.
    /// </summary>
    public sealed class AIDecisionLog
    {
        public sealed class Entry
        {
            public readonly int Player;
            public readonly CharacterId Character;
            public readonly AIDecision Decision;
            /// <summary>Numéro d'ordre global : permet de repérer la décision la plus récente.</summary>
            public readonly int Sequence;

            public Entry(int player, CharacterId character, AIDecision decision, int sequence)
            {
                Player = player;
                Character = character;
                Decision = decision;
                Sequence = sequence;
            }
        }

        private readonly int capacityPerPlayer;
        private readonly SortedDictionary<int, List<Entry>> byPlayer = new SortedDictionary<int, List<Entry>>();
        private readonly Dictionary<int, CharacterId> characters = new Dictionary<int, CharacterId>();
        private int sequence;

        public AIDecisionLog(int capacityPerPlayer = 6)
        {
            this.capacityPerPlayer = Math.Max(1, capacityPerPlayer);
        }

        public int LastSequence => sequence;

        public void Add(int player, CharacterId character, AIDecision decision)
        {
            if (decision == null) return;
            if (!byPlayer.TryGetValue(player, out var list))
            {
                list = new List<Entry>();
                byPlayer[player] = list;
            }
            characters[player] = character;
            list.Insert(0, new Entry(player, character, decision, ++sequence));
            if (list.Count > capacityPerPlayer) list.RemoveAt(list.Count - 1);
        }

        /// <summary>Joueurs pilotés par l'IA déjà vus, dans l'ordre de leur index.</summary>
        public IEnumerable<int> Players => byPlayer.Keys;

        public CharacterId CharacterOf(int player) =>
            characters.TryGetValue(player, out var c) ? c : CharacterId.Major;

        /// <summary>Décisions d'un joueur, la plus récente en premier.</summary>
        public IReadOnlyList<Entry> For(int player) =>
            byPlayer.TryGetValue(player, out var list) ? (IReadOnlyList<Entry>)list : Array.Empty<Entry>();

        public void ClearPlayer(int player)
        {
            byPlayer.Remove(player);
            characters.Remove(player);
        }

        public void Clear()
        {
            byPlayer.Clear();
            characters.Clear();
        }

        // ------------------------------------------------------------------
        // Mise en forme pour l'affichage
        // ------------------------------------------------------------------

        /// <summary>Libellé court en français de chaque action.</summary>
        public static string Label(AIActionType action)
        {
            switch (action)
            {
                case AIActionType.Buy: return "Achète";
                case AIActionType.Pass: return "Passe";
                case AIActionType.Upgrade: return "Améliore";
                case AIActionType.PayDetentionFine: return "Paie la colle";
                case AIActionType.UseExemptionTicket: return "Utilise le billet";
                case AIActionType.KeepExemptionTicket: return "Garde le billet";
                case AIActionType.WaitInDetention: return "Attend en colle";
                case AIActionType.UsePower: return "Pouvoir !";
                case AIActionType.KeepPower: return "Garde son pouvoir";
                case AIActionType.SellUpgrade: return "Vend un niveau";
                case AIActionType.SellProperty: return "Vend un terrain";
                case AIActionType.PlayRoulette: return "Joue à la Roulette";
                case AIActionType.SkipRoulette: return "Évite la Roulette";
                default: return action.ToString();
            }
        }

        /// <summary>Les actions qui font avancer la partie (affichées en couleur dans le panneau).</summary>
        public static bool IsActive(AIActionType action) =>
            action == AIActionType.Buy || action == AIActionType.Upgrade || action == AIActionType.UsePower ||
            action == AIActionType.PayDetentionFine || action == AIActionType.UseExemptionTicket ||
            action == AIActionType.PlayRoulette;

        /// <summary>« Achète · score 61 » : l'action et son score, sans la justification.</summary>
        public static string Headline(AIDecision decision) =>
            string.Format(CultureInfo.InvariantCulture, "{0} · score {1:0}", Label(decision.Action), decision.Score);

        /// <summary>Paramètres de personnalité, en une ligne lisible par le jury.</summary>
        public static string DescribeParameters(AIParameters p, AIDifficulty difficulty)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} · achat {1:0} % · réserve {2} · construit {3:0} %{4}",
                DifficultyLabel(difficulty), p.BuyChance * 100, p.CashReserve, p.BuildChance * 100,
                p.BuyOnlyIfProfitable ? " · terrains rentables seulement" : string.Empty);
        }

        public static string DifficultyLabel(AIDifficulty difficulty)
        {
            switch (difficulty)
            {
                case AIDifficulty.Easy: return "Facile";
                case AIDifficulty.Hard: return "Difficile";
                default: return "Normal";
            }
        }
    }
}
