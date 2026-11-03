using System;
using System.Collections.Generic;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Pont entre l'état réel de la partie (Player, BoardSquare, LevelData) et l'IA.
    /// Construit, avant chaque décision, la photo en lecture seule que l'IA analyse.
    ///
    /// Les deux informations que le modèle ne porte pas encore (tours joués et événement
    /// du semestre actif) sont passées en paramètres : c'est le GameManager qui les connaît.
    /// </summary>
    public static class AISnapshotBuilder
    {
        /// <param name="level">Niveau en cours (tours max, bonus de La Rentrée, amende de colle).</param>
        /// <param name="board">Cases de la partie, dans l'ordre du plateau (board[i].Index == i).</param>
        /// <param name="players">Joueurs, dans l'ordre de leur Index (players[i].Index == i).</param>
        /// <param name="turnsPlayed">
        /// Nombre de tours commencés par chaque joueur, tour en cours compris (les rejeux sur double ne comptent pas).
        /// Exemple : au début du 3e tour du joueur 1, turnsPlayed[1] = 3.
        /// </param>
        /// <param name="activeEvent">Événement du semestre en cours, ou null.</param>
        /// <param name="upgradedThisTurn">Cases déjà améliorées pendant le tour en cours (un niveau par terrain et par tour).</param>
        public static AIGameSnapshot Build(
            LevelData level,
            IReadOnlyList<BoardSquare> board,
            IReadOnlyList<Player> players,
            IReadOnlyList<int> turnsPlayed,
            GlobalEventType? activeEvent = null,
            IEnumerable<int> upgradedThisTurn = null)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (board == null || board.Count == 0) throw new ArgumentException("Plateau vide", nameof(board));
            if (players == null || players.Count == 0) throw new ArgumentException("Aucun joueur", nameof(players));
            if (turnsPlayed == null || turnsPlayed.Count != players.Count)
                throw new ArgumentException("Il faut un nombre de tours joués par joueur", nameof(turnsPlayed));

            var squares = new List<SquareInfo>(board.Count);
            for (int i = 0; i < board.Count; i++)
            {
                if (board[i].Index != i)
                    throw new ArgumentException($"La case {i} a l'index {board[i].Index} : le plateau doit être dans l'ordre", nameof(board));
                squares.Add(SquareInfo.From(board[i].Data));
            }

            var infos = new List<PlayerInfo>(players.Count);
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.Index != i)
                    throw new ArgumentException($"Le joueur {i} a l'index {p.Index} : la liste doit être dans l'ordre", nameof(players));

                infos.Add(new PlayerInfo
                {
                    Index = p.Index,
                    Character = p.Character.Id,
                    Money = p.Money,
                    Position = p.Position,
                    IsBankrupt = p.IsBankrupt,
                    InDetention = p.InDetention,
                    HasExemptionTicket = p.HasExemptionTicket,
                    PowerUsed = p.PowerUsed,
                    TurnsRemaining = Math.Max(0, level.MaxTurns - turnsPlayed[i])
                });
            }

            var snap = new AIGameSnapshot(squares, infos)
            {
                ActiveEvent = activeEvent,
                StartBonus = level.PassStartBonus,
                DetentionFine = level.DetentionFine
            };

            for (int i = 0; i < board.Count; i++)
            {
                snap.Owners[i] = board[i].OwnerIndex;
                snap.Levels[i] = board[i].UpgradeLevel;
            }

            if (upgradedThisTurn != null)
                foreach (int s in upgradedThisTurn) snap.UpgradedThisTurn.Add(s);

            return snap;
        }
    }
}
