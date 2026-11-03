using System;

namespace CampusTycoon
{
    /// <summary>
    /// État d'une case pendant une partie : propriétaire et niveau d'amélioration.
    /// </summary>
    public class BoardSquare
    {
        public const int NoOwner = -1;
        public const int MaxUpgradeLevel = 3;

        public int Index { get; }
        public SquareData Data { get; }
        public int OwnerIndex { get; private set; } = NoOwner;
        public int UpgradeLevel { get; private set; }

        public bool IsOwnable => Data.IsOwnable;
        public bool IsOwned => OwnerIndex != NoOwner;
        public bool CanBeUpgraded => Data.Type == SquareType.Property && IsOwned && UpgradeLevel < MaxUpgradeLevel;

        public BoardSquare(int index, SquareData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Index = index;
            Data = data;
        }

        public void SetOwner(int playerIndex)
        {
            if (!IsOwnable) throw new InvalidOperationException($"La case {Data.DisplayName} ne peut pas être achetée.");
            if (playerIndex < 0) throw new ArgumentOutOfRangeException(nameof(playerIndex));
            OwnerIndex = playerIndex;
        }

        ///<summary>Rend la case à la banque </summary>
        public void ClearOwner()
        {
            OwnerIndex = NoOwner;
            UpgradeLevel = 0;
        }

        public void Upgrade()
        {
            if (!CanBeUpgraded) throw new InvalidOperationException($"La case {Data.DisplayName} ne peut pas être améliorée.");
            UpgradeLevel++;
        }

        public void Downgrade()
        {
            if (UpgradeLevel == 0) throw new InvalidOperationException($"La case {Data.DisplayName} n'a aucun étage.");
            UpgradeLevel--;
        }
    }
}