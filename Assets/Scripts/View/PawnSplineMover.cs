using System;
using System.Collections;
using UnityEngine;

public class PawnSplineMover : MonoBehaviour
{
    [Header("Identité de l'Étudiant")]
    [Tooltip("0 = Humain, 1 = IA 1, 2 = IA 2")]
    [Range(0, 2)] public int studentId = 0;
    public int currentTileIndex = 0;
    public bool isLockedInColle = false;

    [Header("Paramètres d'Animation (Spline & Quaternions)")]
    [Tooltip("Durée d'un saut entre deux cases adjacentes")]
    public float stepDuration = 0.28f;
    [Tooltip("Hauteur de l'arc de Bézier standard")]
    public float jumpHeight = 0.85f;
    [Tooltip("Hauteur de la grande parabole quand l'étudiant est envoyé en Colle")]
    public float colleArcHeight = 3.2f;
    [Tooltip("Pause très courte entre deux cases (effet jeu de plateau)")]
    public float hopPause = 0.03f;

    [Header("Règles Campus Tycoon")]
    [Tooltip("1x = Humain, 2x = Tour IA (4-6s), 5x = Fin de partie accélérée")]
    [Range(0.5f, 10f)] public float speedMultiplier = 1f;

    public bool IsMoving { get; private set; }
    private Coroutine activeMoveRoutine;

    /// <summary>
    /// Place immédiatement le pion sur une case (utile au lancement de la partie).
    /// </summary>
    public void SnapToTile(ProceduralBoardGenerator board, int tileIndex, bool inColle = false)
    {
        if (board == null || board.generatedTiles.Count == 0) return;

        currentTileIndex = ((tileIndex % board.generatedTiles.Count) + board.generatedTiles.Count) % board.generatedTiles.Count;
        isLockedInColle = inColle;
        transform.position = board.GetPawnWorldPosition(currentTileIndex, studentId, isLockedInColle);
        transform.rotation = Quaternion.identity;
    }

    /// <summary>
    /// Fait avancer (steps > 0) ou reculer (steps < 0, ex: -3 pour Panne de RER) le pion case par case.
    /// Appelle onPassedLaRentree si le pion franchit ou s'arrête sur la case 0 (+150€).
    /// </summary>
    public void MoveStepsOnBoard(
        ProceduralBoardGenerator board,
        int steps,
        Action onPassedLaRentree = null,
        Action<int> onMovementFinished = null)
    {
        if (board == null || board.generatedTiles.Count == 0) return;

        if (activeMoveRoutine != null) StopCoroutine(activeMoveRoutine);
        activeMoveRoutine = StartCoroutine(MoveStepsCoroutine(board, steps, onPassedLaRentree, onMovementFinished));
    }

    /// <summary>
    /// Envoie directement le pion en Colle (case Colle) par une haute courbe de Bézier sans passer par La Rentrée.
    /// </summary>
    public void SendDirectlyToColle(
        ProceduralBoardGenerator board,
        int colleTileIndex,
        Action onArrivedInColle = null)
    {
        if (board == null || board.generatedTiles.Count == 0) return;

        if (activeMoveRoutine != null) StopCoroutine(activeMoveRoutine);
        activeMoveRoutine = StartCoroutine(SendToColleCoroutine(board, colleTileIndex, onArrivedInColle));
    }

    /// <summary>
    /// Saut simple vers une coordonnée 3D cible.
    /// </summary>
    public void MoveToTarget(Vector3 targetPosition)
    {
        if (activeMoveRoutine != null) StopCoroutine(activeMoveRoutine);
        activeMoveRoutine = StartCoroutine(JumpAlongBezierCurve(transform.position, targetPosition, jumpHeight, stepDuration));
    }

    private IEnumerator MoveStepsCoroutine(
        ProceduralBoardGenerator board,
        int steps,
        Action onPassedLaRentree,
        Action<int> onMovementFinished)
    {
        IsMoving = true;
        isLockedInColle = false;

        int totalTiles = board.generatedTiles.Count;
        int direction = steps >= 0 ? 1 : -1;
        int remainingSteps = Mathf.Abs(steps);

        for (int i = 0; i < remainingSteps; i++)
        {
            currentTileIndex = ((currentTileIndex + direction) % totalTiles + totalTiles) % totalTiles;

            Vector3 nextPos = board.GetPawnWorldPosition(currentTileIndex, studentId, false);
            yield return StartCoroutine(JumpAlongBezierCurve(transform.position, nextPos, jumpHeight, stepDuration));

            // Règle Campus Tycoon : Passer par La Rentrée (case 0) ou s'y arrêter en avançant rapporte 150€
            if (direction > 0 && currentTileIndex == 0)
            {
                Debug.Log($"[Campus Tycoon] Étudiant {studentId} passe par La Rentrée (Case 0) : +150€ !");
                onPassedLaRentree?.Invoke();
            }

            if (hopPause > 0f && i < remainingSteps - 1)
            {
                yield return new WaitForSeconds(hopPause / Mathf.Max(0.1f, speedMultiplier));
            }
        }

        IsMoving = false;
        activeMoveRoutine = null;
        onMovementFinished?.Invoke(currentTileIndex);
    }

    private IEnumerator SendToColleCoroutine(
        ProceduralBoardGenerator board,
        int colleTileIndex,
        Action onArrivedInColle)
    {
        IsMoving = true;
        currentTileIndex = colleTileIndex;
        isLockedInColle = true;

        Vector3 collePos = board.GetPawnWorldPosition(colleTileIndex, studentId, true);

        // Trajectoire parabolique haute (style expulsion directe vers la salle de Colle)
        yield return StartCoroutine(JumpAlongBezierCurve(transform.position, collePos, colleArcHeight, stepDuration * 2.2f));

        Debug.Log($"[Campus Tycoon] Étudiant {studentId} envoyé directement en Colle (Case {colleTileIndex}) !");
        IsMoving = false;
        activeMoveRoutine = null;
        onArrivedInColle?.Invoke();
    }

    /// <summary>
    /// Calcule la courbe de Bézier quadratique P(t) et oriente le pion selon la tangente B'(t) par Quaternions.
    /// </summary>
    private IEnumerator JumpAlongBezierCurve(Vector3 startPos, Vector3 endPos, float arcHeight, float baseDuration)
    {
        IsMoving = true;
        float elapsedTime = 0f;
        float actualDuration = Mathf.Max(0.04f, baseDuration / Mathf.Max(0.1f, speedMultiplier));

        // Point de contrôle P1 au sommet de l'arche
        Vector3 controlPoint = (startPos + endPos) * 0.5f + Vector3.up * arcHeight;

        // Direction horizontale finale pour l'atterrissage propre
        Vector3 flatDir = new Vector3(endPos.x - startPos.x, 0f, endPos.z - startPos.z);
        Quaternion flatRotation = flatDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(flatDir.normalized, Vector3.up)
            : transform.rotation;

        while (elapsedTime < actualDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / actualDuration);

            // 1. Position sur la Spline de Bézier Quadratique : B(t) = (1-t)²P0 + 2(1-t)tP1 + t²P2
            transform.position = CalculateQuadraticBezierPoint(t, startPos, controlPoint, endPos);

            // 2. Vecteur Tangent (dérivée B'(t)) pour incliner le pion dans le sens du saut avec un Quaternion
            Vector3 tangent = CalculateQuadraticBezierTangent(t, startPos, controlPoint, endPos);
            if (tangent.sqrMagnitude > 0.001f)
            {
                Quaternion tangentRot = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                // On mêle l'inclinaison de la tangente et la rotation horizontale pour un mouvement souple
                Quaternion desiredRot = Quaternion.Slerp(flatRotation, tangentRot, 0.35f);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-18f * Time.deltaTime));
            }

            yield return null;
        }

        transform.position = endPos;
        transform.rotation = flatRotation;
        IsMoving = false;
    }

    public static Vector3 CalculateQuadraticBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        float u = 1f - t;
        return (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
    }

    public static Vector3 CalculateQuadraticBezierTangent(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        // Dérivée exacte : B'(t) = 2(1-t)(P1 - P0) + 2t(P2 - P1)
        return 2f * (1f - t) * (p1 - p0) + 2f * t * (p2 - p1);
    }
}