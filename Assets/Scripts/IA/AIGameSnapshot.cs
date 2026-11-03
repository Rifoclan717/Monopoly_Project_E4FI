using System;
using System.Collections.Generic;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Copie, en C# pur, des données fixes d'une case.
    /// L'IA ne manipule pas directement les ScriptableObject : on peut ainsi la tester hors Unity.
    /// </summary>
    public sealed class SquareInfo
    {
        public readonly string Name;
        public readonly SquareType Type;
        public readonly ColorGroup Group;
        public readonly int Price;
        /// <summary>Terrain : [sans monopole, monopole, niv. 1, niv. 2, niv. 3]. Gare : [1 gare, 2 gares]. Compagnie : [multiplicateur].</summary>
        public readonly int[] Rents;
        public readonly int UpgradeCost;
        /// <summary>Taxe : montant. Foyer : bonus. Roulette : mise.</summary>
        public readonly int Amount;

        public SquareInfo(string name, SquareType type, ColorGroup group = ColorGroup.None,
                          int price = 0, int[] rents = null, int upgradeCost = 0, int amount = 0)
        {
            Name = name;
            Type = type;
            Group = group;
            Price = price;
            Rents = rents ?? Array.Empty<int>();
            UpgradeCost = upgradeCost;
            Amount = amount;
        }

        public bool IsOwnable =>
            Type == SquareType.Property || Type == SquareType.Station || Type == SquareType.Utility;

        public int MaxLevel => Type == SquareType.Property ? Math.Max(0, Rents.Length - 2) : 0;

        public static SquareInfo From(SquareData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var rents = new int[data.Rents.Count];
            for (int i = 0; i < rents.Length; i++) rents[i] = data.Rents[i];
            return new SquareInfo(data.DisplayName, data.Type, data.Group, data.Price, rents, data.UpgradeCost, data.Amount);
        }
    }

    /// <summary>État d'un joueur au moment où l'IA réfléchit.</summary>
    public sealed class PlayerInfo
    {
        public int Index;
        public CharacterId Character;
        public int Money;
        public int Position;
        public bool IsBankrupt;
        public bool InDetention;
        public bool HasExemptionTicket;
        public bool PowerUsed;
        /// <summary>Tours qu'il reste à jouer à ce joueur, sans compter le tour en cours.</summary>
        public int TurnsRemaining;

        public PlayerInfo Clone() => (PlayerInfo)MemberwiseClone();
    }

    /// <summary>
    /// Vue en lecture seule de la partie, remplie par la logique du jeu avant chaque décision de l'IA.
    /// L'IA ne modifie jamais l'état réel : la logique reste la source de vérité.
    /// </summary>
    public sealed class AIGameSnapshot
    {
        public const int NoOwner = -1;

        public readonly List<SquareInfo> Squares;
        public readonly List<PlayerInfo> Players;
        /// <summary>Propriétaire de chaque case (index de joueur) ou NoOwner.</summary>
        public readonly int[] Owners;
        /// <summary>Niveau d'amélioration de chaque case (0 à 3).</summary>
        public readonly int[] Levels;
        /// <summary>Cases déjà améliorées pendant le tour en cours (un niveau par terrain et par tour).</summary>
        public readonly HashSet<int> UpgradedThisTurn = new HashSet<int>();

        /// <summary>Événement du semestre actif, ou null.</summary>
        public GlobalEventType? ActiveEvent;
        public int StartBonus = 150;
        public int DetentionFine = 50;

        public AIGameSnapshot(IList<SquareInfo> squares, IList<PlayerInfo> players)
        {
            if (squares == null || squares.Count == 0) throw new ArgumentException("Plateau vide", nameof(squares));
            Squares = new List<SquareInfo>(squares);
            Players = new List<PlayerInfo>(players);
            Owners = new int[Squares.Count];
            Levels = new int[Squares.Count];
            for (int i = 0; i < Owners.Length; i++) Owners[i] = NoOwner;

            DetentionIndex = Squares.FindIndex(s => s.Type == SquareType.Detention);
            GoToDetentionIndex = Squares.FindIndex(s => s.Type == SquareType.GoToDetention);
        }

        public int SquareCount => Squares.Count;
        public int DetentionIndex { get; }
        public int GoToDetentionIndex { get; }

        public PlayerInfo Player(int index) => Players[index];

        public IEnumerable<PlayerInfo> Opponents(int me)
        {
            foreach (var p in Players)
                if (p.Index != me && !p.IsBankrupt) yield return p;
        }

        public IEnumerable<int> OwnedBy(int player)
        {
            for (int s = 0; s < Owners.Length; s++)
                if (Owners[s] == player) yield return s;
        }

        public IEnumerable<int> GroupMembers(ColorGroup group)
        {
            if (group == ColorGroup.None) yield break;
            for (int s = 0; s < Squares.Count; s++)
                if (Squares[s].Type == SquareType.Property && Squares[s].Group == group) yield return s;
        }

        public bool HasMonopoly(int player, ColorGroup group) => HasMonopoly(player, group, Owners);

        public bool HasMonopoly(int player, ColorGroup group, int[] owners)
        {
            if (group == ColorGroup.None || player < 0) return false;
            bool any = false;
            foreach (int s in GroupMembers(group))
            {
                any = true;
                if (owners[s] != player) return false;
            }
            return any;
        }

        /// <summary>Fortune = argent + prix d'achat des terrains + coût des améliorations.</summary>
        public int NetWorth(int player)
        {
            int total = Players[player].Money;
            foreach (int s in OwnedBy(player))
                total += Squares[s].Price + Levels[s] * Squares[s].UpgradeCost;
            return total;
        }

        public AIGameSnapshot Clone()
        {
            var players = new List<PlayerInfo>(Players.Count);
            foreach (var p in Players) players.Add(p.Clone());
            var copy = new AIGameSnapshot(Squares, players)
            {
                ActiveEvent = ActiveEvent,
                StartBonus = StartBonus,
                DetentionFine = DetentionFine
            };
            Array.Copy(Owners, copy.Owners, Owners.Length);
            Array.Copy(Levels, copy.Levels, Levels.Length);
            foreach (int s in UpgradedThisTurn) copy.UpgradedThisTurn.Add(s);
            return copy;
        }
    }
}
