using System;
using CampusTycoon;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Permet d'inspecter n'importe quelle case du plateau en cliquant dessus (Raycast sur ProceduralTileView).
/// Émet OnTileSelected pour que le HUD affiche la carte détaillée de la case (loyers, propriétaire, niveau).
/// </summary>
public class CampusTileInspector : MonoBehaviour
{
    public event Action<ProceduralTileView, BoardSquare, SquareData> OnTileSelected;

    public ProceduralBoardGenerator board;
    public TurnManager turnManager;
    public CampusGameLauncher launcher;

    public ProceduralTileView SelectedTile { get; private set; }

    private Vector2 pointerDownPos;
    private bool pointerDownValid;

    void Start()
    {
        if (board == null) board = FindFirstObjectByType<ProceduralBoardGenerator>();
        if (turnManager == null) turnManager = FindFirstObjectByType<TurnManager>();
        if (launcher == null) launcher = FindFirstObjectByType<CampusGameLauncher>();
    }

    void Update()
    {
        if (Time.timeScale <= 0.001f) return;
        if (launcher != null && launcher.IsInMainMenu) return;
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            pointerDownValid = EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
            pointerDownPos = Mouse.current.position.ReadValue();
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame && pointerDownValid)
        {
            pointerDownValid = false;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 upPos = Mouse.current.position.ReadValue();
            if (Vector2.Distance(pointerDownPos, upPos) > 12f) return; // Ignore les glissements

            TrySelectTileAtScreenPosition(upPos);
        }
    }

    public void ClearSelection()
    {
        if (SelectedTile != null)
        {
            SelectedTile.SetHighlighted(false);
            SelectedTile = null;
        }
        OnTileSelected?.Invoke(null, null, null);
    }

    private void TrySelectTileAtScreenPosition(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 150f))
        {
            ProceduralTileView tile = hit.collider.GetComponentInParent<ProceduralTileView>();
            if (tile != null)
            {
                if (SelectedTile == tile)
                {
                    ClearSelection();
                    return;
                }

                if (SelectedTile != null) SelectedTile.SetHighlighted(false);
                SelectedTile = tile;
                SelectedTile.SetHighlighted(true);

                BoardSquare liveSquare = null;
                SquareData data = null;

                if (turnManager != null && turnManager.State != null &&
                    tile.tileIndex >= 0 && tile.tileIndex < turnManager.State.Board.Count)
                {
                    liveSquare = turnManager.State.Board[tile.tileIndex];
                    data = liveSquare.Data;
                }
                else if (board != null && board.levelData != null &&
                         tile.tileIndex >= 0 && tile.tileIndex < board.levelData.SquareCount)
                {
                    data = board.levelData.Squares[tile.tileIndex];
                }

                if (CampusAudioManager.Instance != null) CampusAudioManager.Instance.PlayUIClick();
                OnTileSelected?.Invoke(tile, liveSquare, data);
                return;
            }
        }

        if (SelectedTile != null)
        {
            ClearSelection();
        }
    }
}
