using System.Collections.Generic;
using System.Linq;

namespace CampusTycoon
{
    /// <summary>État complet d'une partie. Seul le TurnManager le modifie.</summary>
    public class GameState
    {
        public const int BankIndex = -1;

        public LevelData Level { get; }
        public List<Player> Players { get; } = new List<Player>();
        public List<BoardSquare> Board { get; } = new List<BoardSquare>();

        /// <summary>Joueur dont c'est le tour (0 = humain, 1 et 2 = IA).</summary>
        public int CurrentPlayerIndex { get; set; }
        /// <summary>Tour de table en cours, de 1 à Level.MaxTurns.</summary>
        public int Round { get; set; } = 1;
        /// <summary>
        /// Tours commencés par chaque joueur, tour en cours compris.
        /// Les rejeux sur double ne comptent pas.
        /// </summary>
        public int[] TurnsStarted { get; }
        /// <summary>Événement du semestre actif, ou null.</summary>
        public GlobalEventType? ActiveEvent { get; set; }
        /// <summary>Amélioration des terrains : 1 niveau par terrain et par tour</summary>
        public HashSet<int> UpgradedThisTurn { get; } = new HashSet<int>();

        public GameState(LevelData level, IList<CharacterData> characters)
        {
            Level = level;
            for (int i = 0; i < level.SquareCount; i++)
                Board.Add(new BoardSquare(i, level.Squares[i]));
            for (int i = 0; i < characters.Count; i++)
                Players.Add(new Player(i, characters[i], i == 0, level.StartingMoney));
            TurnsStarted = new int[characters.Count];
        }

        public int SquareCount => Board.Count;
        public Player CurrentPlayer => Players[CurrentPlayerIndex];
        public IEnumerable<Player> ActivePlayers => Players.Where(p => !p.IsBankrupt);

        public IEnumerable<BoardSquare> OwnedBy(int player) => Board.Where(s => s.OwnerIndex == player);

        public int CountOwned(int player, SquareType type) =>
            Board.Count(s => s.OwnerIndex == player && s.Data.Type == type);

        public bool HasMonopoly(int player, ColorGroup group)
        {
            if (group == ColorGroup.None || player < 0) return false;
            var members = Board.Where(s => s.Data.Type == SquareType.Property && s.Data.Group == group).ToList();
            return members.Count > 0 && members.All(s => s.OwnerIndex == player);
        }

        /// <summary>Fortune = argent + prix des terrains + coût des améliorations.</summary>
        public int NetWorth(int player)
        {
            int total = Players[player].Money;
            foreach (BoardSquare s in OwnedBy(player))
                total += s.Data.Price + s.UpgradeLevel * s.Data.UpgradeCost;
            return total;
        }
    }
}