using CampusTycoon;
using UnityEngine;
using UnityEngine.InputSystem;

public class MoverTester : MonoBehaviour
{
    [Header("Références (Auto-détectées et créées si vides)")]
    public PawnSplineMover pawn;
    public ProceduralBoardGenerator board;
    public ProceduralDiceRoller diceRoller;
    public CampusHUDController hud;

    [Header("Joueur Actif en Test (0 = Bleu, 1 = Rouge IA1, 2 = Vert IA2)")]
    [Range(0, 2)] public int activeStudentId = 0;

    private readonly int[] studentMoney = { 1000, 1000, 1000 };
    private int currentTurn = 1;
    private readonly int[] tileOwner = new int[30];
    private readonly int[] tileBuildingLevel = new int[30];

    void Start()
    {
        for (int i = 0; i < tileOwner.Length; i++)
        {
            tileOwner[i] = -1;
            tileBuildingLevel[i] = 0;
        }

        if (board == null) board = FindFirstObjectByType<ProceduralBoardGenerator>();

        if (board != null)
        {
            board.EnsureThreeStudentPawns();
            pawn = board.GetStudentPawn(activeStudentId);
        }
        else if (pawn == null)
        {
            pawn = FindFirstObjectByType<PawnSplineMover>();
        }

        if (diceRoller == null)
        {
            diceRoller = FindFirstObjectByType<ProceduralDiceRoller>();
            if (diceRoller == null && board != null)
            {
                GameObject diceObj = new GameObject("CampusDiceRoller");
                diceObj.transform.position = board.transform.position;
                diceRoller = diceObj.AddComponent<ProceduralDiceRoller>();
                diceRoller.sharedDiceMaterial = board.EnsureValidMaterial();
            }
        }

        if (hud == null)
        {
            hud = FindFirstObjectByType<CampusHUDController>();
            if (hud == null)
            {
                hud = gameObject.AddComponent<CampusHUDController>();
            }
        }

        CampusEvents.RaiseTurnStarted(activeStudentId, currentTurn, 15);
    }

    void Update()
    {
        if (Keyboard.current == null || board == null) return;

        pawn = board.GetStudentPawn(activeStudentId);
        if (pawn == null) return;

        // [TAB] : Changer d'étudiant actif (0 = Bleu Humain -> 1 = Rouge IA1 -> 2 = Vert IA2)
        if (Keyboard.current.tabKey.wasPressedThisFrame && !pawn.IsMoving)
        {
            activeStudentId = (activeStudentId + 1) % 3;
            if (activeStudentId == 0) currentTurn = Mathf.Min(15, currentTurn + 1);
            pawn = board.GetStudentPawn(activeStudentId);
            CampusEvents.RaiseTurnStarted(activeStudentId, currentTurn, 15);
        }

        // [ESPACE] : Lancer les 2 dés (ou ignorer l'animation) et avancer sur la Spline
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            if (diceRoller != null && diceRoller.IsRolling)
            {
                diceRoller.SkipRollAnimation();
                return;
            }
            if (pawn.IsMoving) return;

            int playerId = activeStudentId;
            int d1 = Random.Range(1, 7);
            int d2 = Random.Range(1, 7);
            int total = d1 + d2;

            CampusEvents.RaiseDiceRolled(playerId, d1, d2);

            diceRoller.RollDice(d1, d2, (r1, r2) =>
            {
                pawn.MoveStepsOnBoard(
                    board,
                    total,
                    onPassedLaRentree: () =>
                    {
                        studentMoney[playerId] += 150;
                        CampusEvents.RaiseMoneyChanged(playerId, studentMoney[playerId], 150);
                    },
                    onMovementFinished: (landedIdx) =>
                    {
                        CampusEvents.RaisePlayerMoved(playerId, landedIdx, total, false);
                        EvaluateLandedTile(playerId, landedIdx);
                    });
            });
        }

        // [A] : Acheter le terrain nu (Niveau 0 avec bordure/borne propriétaire) puis améliorer (Niv. 1 -> 2 -> 3)
        if (Keyboard.current.aKey.wasPressedThisFrame && !pawn.IsMoving)
        {
            int idx = pawn.currentTileIndex;
            if (idx >= 0 && idx < board.generatedTiles.Count)
            {
                ProceduralTileView tile = board.generatedTiles[idx];
                if (tile.category == ProceduralTileView.TileCategory.Property)
                {
                    if (tileOwner[idx] == -1)
                    {
                        // Tâche 3 : Achat initial du terrain au niveau 0 (bordure + borne propriétaire)
                        tileOwner[idx] = activeStudentId;
                        tileBuildingLevel[idx] = 0;
                        studentMoney[activeStudentId] -= tile.price;
                        board.UpdatePropertyVisual(idx, activeStudentId, 0);
                        CampusEvents.RaiseMoneyChanged(activeStudentId, studentMoney[activeStudentId], -tile.price);
                        CampusEvents.RaisePropertyBought(activeStudentId, idx, tile.price);
                    }
                    else if (tileOwner[idx] == activeStudentId && tileBuildingLevel[idx] < 3)
                    {
                        // Amélioration du bâtiment (Niveau 1 -> Niveau 2 -> Niveau 3 Amphi)
                        tileBuildingLevel[idx]++;
                        studentMoney[activeStudentId] -= 50;
                        board.UpdatePropertyVisual(idx, activeStudentId, tileBuildingLevel[idx]);
                        CampusEvents.RaiseMoneyChanged(activeStudentId, studentMoney[activeStudentId], -50);
                        CampusEvents.RaisePropertyUpgraded(activeStudentId, idx, tileBuildingLevel[idx]);
                    }
                }
            }
        }

        // [E] : Déclencher un Événement du Semestre aléatoire (Bannière officielle)
        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            string[,] events = {
                { "Journée portes ouvertes", "Tous les loyers de terrains sont doublés" },
                { "Grève du RER", "Les gares ne rapportent aucun loyer" },
                { "Soldes de la rentrée", "Les améliorations coûtent moitié prix" },
                { "Forum entreprises", "Chaque étudiant reçoit 20€ par terrain possédé" }
            };
            int r = Random.Range(0, 4);
            CampusEvents.RaiseGlobalEventStarted(events[r, 0], events[r, 1]);
        }

        // [K] : Piocher une des Cartes Événement officielles
        if (Keyboard.current.kKey.wasPressedThisFrame && !pawn.IsMoving)
        {
            TriggerRandomCampusCard(activeStudentId);
        }

        // [C] : Tester le saut parabolique direct en Colle
        if (Keyboard.current.cKey.wasPressedThisFrame && !pawn.IsMoving)
        {
            int colleIdx = GetColleTileIndex();
            pawn.SendDirectlyToColle(board, colleIdx);
            CampusEvents.RaisePlayerMoved(activeStudentId, colleIdx, 0, true);
        }

        // [T] : Mode Turbo (x3 pour simuler la vitesse des tours d'IA)
        if (Keyboard.current.tKey.wasPressedThisFrame)
        {
            float newSpeed = (pawn.speedMultiplier > 1.5f) ? 1f : 3f;
            for (int i = 0; i < 3; i++)
            {
                PawnSplineMover p = board.GetStudentPawn(i);
                if (p != null) p.speedMultiplier = newSpeed;
            }
        }
    }

    private void EvaluateLandedTile(int playerId, int tileIndex)
    {
        ProceduralTileView tile = board.generatedTiles[tileIndex];

        if (tile.tileName == "Rattrapage")
        {
            int colleIdx = GetColleTileIndex();
            pawn.SendDirectlyToColle(board, colleIdx);
            return;
        }

        if (tile.category == ProceduralTileView.TileCategory.EventCard)
        {
            TriggerRandomCampusCard(playerId);
        }
        else if (tile.category == ProceduralTileView.TileCategory.Property && hud != null)
        {
            hud.StartHumanDecisionTimer($"Case {tile.tileName} ({tile.price}€) : Acheter / Améliorer ?");
        }
    }

    private void TriggerRandomCampusCard(int playerId)
    {
        int cardPick = Random.Range(1, 5);
        if (cardPick == 1)
        {
            CampusEvents.RaiseCardDrawn(playerId, 2, "Bourse de mérite", "Recevez 100 € !");
            studentMoney[playerId] += 100;
            CampusEvents.RaiseMoneyChanged(playerId, studentMoney[playerId], 100);
        }
        else if (cardPick == 2)
        {
            CampusEvents.RaiseCardDrawn(playerId, 4, "Panne de RER", "Reculez de 3 cases !");
            pawn.MoveStepsOnBoard(board, -3);
        }
        else if (cardPick == 3)
        {
            CampusEvents.RaiseCardDrawn(playerId, 1, "Soirée BDE", "Payez 60 € !");
            studentMoney[playerId] -= 60;
            CampusEvents.RaiseMoneyChanged(playerId, studentMoney[playerId], -60);
        }
        else
        {
            CampusEvents.RaiseCardDrawn(playerId, 7, "Hackathon gagné", "Chaque rival vous donne 40 € (+80 €) !");
            studentMoney[playerId] += 80;
            CampusEvents.RaiseMoneyChanged(playerId, studentMoney[playerId], 80);
        }
    }

    private int GetColleTileIndex()
    {
        for (int i = 0; i < board.generatedTiles.Count; i++)
        {
            if (board.generatedTiles[i].tileName == "Colle") return i;
        }
        return 5;
    }
}
