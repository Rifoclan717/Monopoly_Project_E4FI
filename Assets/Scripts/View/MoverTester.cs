using UnityEngine;
using UnityEngine.InputSystem;

public class MoverTester : MonoBehaviour
{
    [Header("Références (Auto-détectées et créées si vides)")]
    public PawnSplineMover pawn;
    public ProceduralBoardGenerator board;
    public ProceduralDiceRoller diceRoller;
    public CampusHUDController hud;

    private int humanMoney = 1000;
    private int currentTurn = 1;
    private readonly int[] tileOwner = new int[30];
    private readonly int[] tileBuildingLevel = new int[30];

    void Start()
    {
        for (int i = 0; i < tileOwner.Length; i++) tileOwner[i] = -1;

        if (board == null) board = FindFirstObjectByType<ProceduralBoardGenerator>();
        if (pawn == null) pawn = FindFirstObjectByType<PawnSplineMover>();

        // Colorie le pion en bleu vif (Étudiant Joueur) avec le matériau valide du plateau
        if (pawn != null && board != null)
        {
            MeshRenderer mr = pawn.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = board.EnsureValidMaterial();
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                mr.GetPropertyBlock(block);
                block.SetColor("_BaseColor", new Color(0.18f, 0.52f, 0.95f));
                block.SetColor("_Color", new Color(0.18f, 0.52f, 0.95f));
                mr.SetPropertyBlock(block);
            }
            pawn.SnapToTile(board, 0);
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

        CampusEvents.RaiseTurnStarted(0, currentTurn, 15);
    }

    void Update()
    {
        if (Keyboard.current == null || board == null || pawn == null) return;

        // [ESPACE] : Lancer les 2 dés (ou ignorer l'animation) et avancer sur la Spline
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            if (diceRoller != null && diceRoller.IsRolling)
            {
                diceRoller.SkipRollAnimation();
                return;
            }
            if (pawn.IsMoving) return;

            int d1 = Random.Range(1, 7);
            int d2 = Random.Range(1, 7);
            int total = d1 + d2;

            CampusEvents.RaiseDiceRolled(0, d1, d2);

            diceRoller.RollDice(d1, d2, (r1, r2) =>
            {
                pawn.MoveStepsOnBoard(
                    board,
                    total,
                    onPassedLaRentree: () =>
                    {
                        humanMoney += 150;
                        CampusEvents.RaiseMoneyChanged(0, humanMoney, 150);
                    },
                    onMovementFinished: (landedIdx) =>
                    {
                        CampusEvents.RaisePlayerMoved(0, landedIdx, total, false);
                        EvaluateLandedTile(landedIdx);
                    });
            });
        }

        // [A] : Acheter le terrain ou construire un niveau supérieur (1 -> 2 -> 3) sur la case actuelle !
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
                        // Achat initial du terrain -> Toit Bleu (Joueur 0)
                        tileOwner[idx] = 0;
                        tileBuildingLevel[idx] = 1;
                        humanMoney -= tile.price;
                        board.UpdatePropertyVisual(idx, 0, 1);
                        CampusEvents.RaiseMoneyChanged(0, humanMoney, -tile.price);
                        CampusEvents.RaisePropertyBought(0, idx, tile.price);
                    }
                    else if (tileBuildingLevel[idx] < 3)
                    {
                        // Amélioration du bâtiment (Niveau 2 puis Niveau 3 Amphi !)
                        tileBuildingLevel[idx]++;
                        humanMoney -= 50;
                        board.UpdatePropertyVisual(idx, 0, tileBuildingLevel[idx]);
                        CampusEvents.RaiseMoneyChanged(0, humanMoney, -50);
                        CampusEvents.RaisePropertyUpgraded(0, idx, tileBuildingLevel[idx]);
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

        // [K] : Piocher une des 12 Cartes Événement officielles
        if (Keyboard.current.kKey.wasPressedThisFrame && !pawn.IsMoving)
        {
            TriggerRandomCampusCard();
        }

        // [C] : Tester le saut parabolique direct en Colle
        if (Keyboard.current.cKey.wasPressedThisFrame && !pawn.IsMoving)
        {
            int colleIdx = GetColleTileIndex();
            pawn.SendDirectlyToColle(board, colleIdx);
            CampusEvents.RaisePlayerMoved(0, colleIdx, 0, true);
        }

        // [T] : Mode Turbo (x3 pour simuler la vitesse des tours d'IA)
        if (Keyboard.current.tKey.wasPressedThisFrame)
        {
            pawn.speedMultiplier = (pawn.speedMultiplier > 1.5f) ? 1f : 3f;
        }
    }

    private void EvaluateLandedTile(int tileIndex)
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
            TriggerRandomCampusCard();
        }
        else if (tile.category == ProceduralTileView.TileCategory.Property && hud != null)
        {
            hud.StartHumanDecisionTimer($"Case {tile.tileName} ({tile.price}€) : Acheter / Améliorer ?");
        }
    }

    private void TriggerRandomCampusCard()
    {
        int cardPick = Random.Range(1, 5);
        if (cardPick == 1)
        {
            CampusEvents.RaiseCardDrawn(0, 2, "Bourse de mérite", "Recevez 100 € !");
            humanMoney += 100;
            CampusEvents.RaiseMoneyChanged(0, humanMoney, 100);
        }
        else if (cardPick == 2)
        {
            CampusEvents.RaiseCardDrawn(0, 4, "Panne de RER", "Reculez de 3 cases !");
            pawn.MoveStepsOnBoard(board, -3);
        }
        else if (cardPick == 3)
        {
            CampusEvents.RaiseCardDrawn(0, 1, "Soirée BDE", "Payez 60 € !");
            humanMoney -= 60;
            CampusEvents.RaiseMoneyChanged(0, humanMoney, -60);
        }
        else
        {
            CampusEvents.RaiseCardDrawn(0, 7, "Hackathon gagné", "Chaque rival vous donne 40 € (+80 €) !");
            humanMoney += 80;
            CampusEvents.RaiseMoneyChanged(0, humanMoney, 80);
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