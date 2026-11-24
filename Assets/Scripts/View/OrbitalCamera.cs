using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class OrbitalCamera : MonoBehaviour
{
    [Header("Cible du Plateau")]
    [Tooltip("Point central autour duquel la caméra orbite")]
    public Transform target;
    public Vector3 targetOffset = new Vector3(0f, 0.5f, 0f);

    [Header("Contrôles Rotation (Style Bad North)")]
    [Tooltip("Vitesse de rotation à la souris (degrés par seconde)")]
    public float mouseSensitivity = 18f;
    [Tooltip("Vitesse de rotation au clavier A/Q/D ou Flèches (degrés par seconde)")]
    public float keyboardYawSpeed = 90f;
    [Tooltip("Angle minimum : vue basse proche du plateau")]
    [Range(5f, 45f)] public float minPitchAngle = 18f;
    [Tooltip("Angle maximum : vue haute depuis le ciel (bloqué avant 90° contre le Gimbal Lock)")]
    [Range(45f, 89f)] public float maxPitchAngle = 84f;
    [Tooltip("Fluidité de l'interpolation Slerp (plus c'est haut, plus c'est réactif)")]
    public float rotationSmoothing = 12f;

    [Header("Contrôles Zoom (Molette)")]
    public float defaultDistance = 18.5f;
    public float minZoomDistance = 6f;
    public float maxZoomDistance = 36f;
    [Tooltip("Amplitude du zoom par cran de molette")]
    public float zoomStep = 2.5f;
    [Tooltip("Fluidité de l'amortissement du zoom")]
    public float zoomSmoothing = 10f;

    [Header("Collisions Environnement (Style RDR)")]
    [Tooltip("Active la détection d'obstacles extérieurs entre le plateau et la caméra (ignore les tuiles et bâtiments du plateau)")]
    public bool enableCollision = true;
    public float collisionRadius = 0.3f;
    public LayerMask collisionMask = ~0;

    [Header("Mode Menu Principal (Orbite Automatique)")]
    public bool autoOrbitMenuMode = false;
    public float autoOrbitSpeed = 8f;

    // État mathématique interne (Sphérique + Quaternions)
    private float targetYaw = 0f;
    private float targetPitch = 46f;
    private float targetDistance;
    private float currentDistance;
    private Quaternion currentRotation;
    private float defaultPitch = 46f;
    private float defaultYaw = 0f;

    void Start()
    {
        if (target == null)
        {
            Debug.LogError("[OrbitalCamera] Aucune cible (Target) assignée dans l'Inspector !");
            enabled = false;
            return;
        }

        maxZoomDistance = Mathf.Max(maxZoomDistance, 34f);
        targetDistance = Mathf.Clamp(defaultDistance, minZoomDistance, maxZoomDistance);
        currentDistance = targetDistance;

        currentRotation = ComputeOrbitalQuaternion(targetYaw, targetPitch);
        ApplyCameraTransform(instantly: true);
    }

    void LateUpdate()
    {
        if (target == null) return;

        float dt = Time.unscaledDeltaTime;

        if (autoOrbitMenuMode)
        {
            targetYaw = Mathf.Repeat(targetYaw + autoOrbitSpeed * dt + 180f, 360f) - 180f;
            targetPitch = Mathf.Lerp(targetPitch, 42f, 1f - Mathf.Exp(-3f * dt));
            UpdateCameraWithQuaternions(dt);
            return;
        }

        // Ne pas bouger la caméra tant que le jeu est en pause (Time.timeScale == 0)
        if (Time.timeScale <= 0.001f) return;

        HandleRotationInput(dt);
        HandleZoomInput();
        HandleResetInput();
        UpdateCameraWithQuaternions(dt);
    }

    /// <summary>
    /// Adapte automatiquement la distance de vue selon la taille du plateau (16, 20 ou 24 cases)
    /// pour que les 4 coins restent confortablement visibles.
    /// </summary>
    public void AdaptToBoardSize(int totalTiles)
    {
        maxZoomDistance = Mathf.Max(maxZoomDistance, 36f);
        if (totalTiles <= 16)
        {
            defaultDistance = 16.5f;
        }
        else if (totalTiles <= 20)
        {
            defaultDistance = 20f;
        }
        else
        {
            defaultDistance = 24.5f;
        }

        targetDistance = Mathf.Clamp(defaultDistance, minZoomDistance, maxZoomDistance);
        if (currentDistance <= 0.01f)
        {
            currentDistance = targetDistance;
        }
    }

    /// <summary>
    /// Recentre doucement la caméra sur la vue par défaut du plateau.
    /// </summary>
    public void ResetView()
    {
        targetYaw = defaultYaw;
        targetPitch = defaultPitch;
        targetDistance = Mathf.Clamp(defaultDistance, minZoomDistance, maxZoomDistance);
    }

    private void HandleResetInput()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            ResetView();
        }
    }

    private static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>
    /// Lit les entrées (Clic droit maintenu + Souris, ou Clavier A/Q/D et Flèches) compatible AZERTY et QWERTY.
    /// </summary>
    private void HandleRotationInput(float dt)
    {
        // 1. Rotation à la souris : uniquement quand le CLIC DROIT est maintenu hors UI
        if (Mouse.current != null && Mouse.current.rightButton.isPressed && !IsPointerOverUI())
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            targetYaw += mouseDelta.x * mouseSensitivity * dt;
            targetPitch += mouseDelta.y * mouseSensitivity * dt;
        }

        // 2. Rotation clavier : Flèches Gauche/Droite + A/Q (gauche, AZERTY & QWERTY) / D (droite)
        if (Keyboard.current != null)
        {
            float keyboardInput = 0f;
            if (Keyboard.current.leftArrowKey.isPressed ||
                Keyboard.current.qKey.isPressed ||
                Keyboard.current.aKey.isPressed)
            {
                keyboardInput -= 1f;
            }
            if (Keyboard.current.rightArrowKey.isPressed ||
                Keyboard.current.dKey.isPressed)
            {
                keyboardInput += 1f;
            }

            targetYaw += keyboardInput * keyboardYawSpeed * dt;
        }

        targetYaw = Mathf.Repeat(targetYaw + 180f, 360f) - 180f;
        targetPitch = Mathf.Clamp(targetPitch, minPitchAngle, maxPitchAngle);
    }

    /// <summary>
    /// Normalise le scroll sur Windows, macOS et Trackpad. Ignore le scroll au-dessus des panneaux UI.
    /// </summary>
    private void HandleZoomInput()
    {
        if (Mouse.current == null || IsPointerOverUI()) return;

        float rawScroll = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Abs(rawScroll) > 0.001f)
        {
            float normalizedScroll = Mathf.Abs(rawScroll) >= 10f
                ? rawScroll / 120f
                : Mathf.Sign(rawScroll) * Mathf.Clamp(Mathf.Abs(rawScroll) * 0.25f, 0.1f, 1f);

            normalizedScroll = Mathf.Clamp(normalizedScroll, -3f, 3f);

            targetDistance -= normalizedScroll * zoomStep;
            targetDistance = Mathf.Clamp(targetDistance, minZoomDistance, maxZoomDistance);
        }
    }

    private void UpdateCameraWithQuaternions(float dt)
    {
        Quaternion targetRotation = ComputeOrbitalQuaternion(targetYaw, targetPitch);

        float rotFactor = 1f - Mathf.Exp(-rotationSmoothing * dt);
        float zoomFactor = 1f - Mathf.Exp(-zoomSmoothing * dt);

        currentRotation = Quaternion.Slerp(currentRotation, targetRotation, rotFactor);
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, zoomFactor);

        ApplyCameraTransform(instantly: false);
    }

    private Quaternion ComputeOrbitalQuaternion(float yawDegrees, float pitchDegrees)
    {
        Quaternion yawQuat = Quaternion.AngleAxis(yawDegrees, Vector3.up);
        Quaternion pitchQuat = Quaternion.AngleAxis(pitchDegrees, Vector3.right);
        return yawQuat * pitchQuat;
    }

    private void ApplyCameraTransform(bool instantly)
    {
        Vector3 focusPoint = target.position + targetOffset;
        Vector3 orbitalDirection = currentRotation * Vector3.back;
        float finalDistance = currentDistance;

        // Gestion des collisions caméra : ignore les éléments du plateau (tuiles, bâtiments Niv.3, pions)
        // afin d'éviter les sauts brusques lors d'une orbite rasante autour d'un Amphi.
        if (enableCollision && !instantly)
        {
            RaycastHit[] hits = Physics.SphereCastAll(
                focusPoint,
                collisionRadius,
                orbitalDirection,
                currentDistance,
                collisionMask,
                QueryTriggerInteraction.Ignore);

            float closestObstacle = currentDistance;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null) continue;
                if (col.GetComponentInParent<ProceduralBoardGenerator>() != null) continue;
                if (col.GetComponentInParent<ProceduralTileView>() != null) continue;
                if (col.GetComponentInParent<ProceduralBuildingGenerator>() != null) continue;
                if (col.GetComponentInParent<PawnSplineMover>() != null) continue;

                if (hits[i].distance < closestObstacle)
                {
                    closestObstacle = hits[i].distance;
                }
            }

            finalDistance = Mathf.Clamp(closestObstacle, minZoomDistance * 0.6f, currentDistance);
        }

        transform.position = focusPoint + orbitalDirection * finalDistance;
        transform.rotation = currentRotation;
    }
}
