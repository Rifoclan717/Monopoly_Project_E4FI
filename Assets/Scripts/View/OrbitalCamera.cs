using UnityEngine;
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
    [Tooltip("Vitesse de rotation au clavier Q/D (degrés par seconde)")]
    public float keyboardYawSpeed = 90f;
    [Tooltip("Angle minimum : vue basse proche du plateau")]
    [Range(5f, 45f)] public float minPitchAngle = 15f;
    [Tooltip("Angle maximum : vue haute depuis le ciel (bloqué avant 90° contre le Gimbal Lock)")]
    [Range(45f, 89f)] public float maxPitchAngle = 85f;
    [Tooltip("Fluidité de l'interpolation Slerp (plus c'est haut, plus c'est réactif)")]
    public float rotationSmoothing = 12f;

    [Header("Contrôles Zoom (Molette)")]
    public float defaultDistance = 15f;
    public float minZoomDistance = 5f;
    public float maxZoomDistance = 30f;
    [Tooltip("Amplitude du zoom par cran de molette")]
    public float zoomStep = 2.5f;
    [Tooltip("Fluidité de l'amortissement du zoom")]
    public float zoomSmoothing = 10f;

    [Header("Collisions Environnement (Style RDR)")]
    [Tooltip("Active la détection d'obstacles entre le plateau et la caméra")]
    public bool enableCollision = true;
    public float collisionRadius = 0.3f;
    public LayerMask collisionMask = ~0; // Tous les layers par défaut

    // État mathématique interne (Sphérique + Quaternions)
    private float targetYaw = 0f;
    private float targetPitch = 45f;
    private float targetDistance;
    private float currentDistance;
    private Quaternion currentRotation;

    void Start()
    {
        if (target == null)
        {
            Debug.LogError("[OrbitalCamera] Aucune cible (Target) assignée dans l'Inspector !");
            enabled = false;
            return;
        }

        // Initialisation des distances
        targetDistance = Mathf.Clamp(defaultDistance, minZoomDistance, maxZoomDistance);
        currentDistance = targetDistance;

        // Construction du Quaternion initial
        currentRotation = ComputeOrbitalQuaternion(targetYaw, targetPitch);
        ApplyCameraTransform( instantly: true );
    }

    void LateUpdate()
    {
        if (target == null) return;

        float dt = Time.deltaTime;

        HandleRotationInput(dt);
        HandleZoomInput();
        UpdateCameraWithQuaternions(dt);
    }

    /// <summary>
    /// Lit les entrées (Clic droit maintenu + Souris, ou Clavier Q/D) de façon indépendante du framerate.
    /// </summary>
    private void HandleRotationInput(float dt)
    {
        // 1. Rotation à la souris : uniquement quand le CLIC DROIT est maintenu
        if (Mouse.current != null && Mouse.current.rightButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();

            // Axe horizontal (Gauche/Droite) -> fait tourner autour du plateau
            targetYaw += mouseDelta.x * mouseSensitivity * dt;

            // Axe vertical :
            // Souris vers le Haut (mouseDelta.y > 0) -> augmente le pitch vers maxPitchAngle (vue "depuis le ciel")
            // Souris vers le BAS (mouseDelta.y < 0) -> diminue le pitch vers minPitchAngle (vue plus basse)
            targetPitch += mouseDelta.y * mouseSensitivity * dt;
        }

        // 2. Rotation optionnelle au clavier (Q/D ou Flèches) avec Time.deltaTime
        if (Keyboard.current != null)
        {
            float keyboardInput = 0f;
            if (Keyboard.current.qKey.isPressed || Keyboard.current.leftArrowKey.isPressed) keyboardInput -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) keyboardInput += 1f;

            targetYaw += keyboardInput * keyboardYawSpeed * dt;
        }

        // Maintien de l'angle horizontal dans [-180, 180] pour la propreté numérique
        targetYaw = Mathf.Repeat(targetYaw + 180f, 360f) - 180f;

        // Clamping strict de la hauteur pour ne jamais passer sous le plateau ni retourner la caméra à 90°
        targetPitch = Mathf.Clamp(targetPitch, minPitchAngle, maxPitchAngle);
    }

    /// <summary>
    /// Normalise le scroll sur Windows, macOS et Trackpad.
    /// Scroll Haut (> 0) = Zoom (rapproche). Scroll Bas (< 0) = Dézoom (éloigne).
    /// </summary>
    private void HandleZoomInput()
    {
        if (Mouse.current == null) return;

        float rawScroll = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Abs(rawScroll) > 0.001f)
        {
            // Sur Windows, un cran de molette envoie ±120. Sur Mac/Trackpad, les valeurs sont plus petites.
            // On normalise pour qu'un cran standard corresponde à une impulsion de ±1.
            float normalizedScroll = Mathf.Abs(rawScroll) >= 10f
                ? rawScroll / 120f
                : Mathf.Sign(rawScroll) * Mathf.Clamp(Mathf.Abs(rawScroll) * 0.25f, 0.1f, 1f);

            normalizedScroll = Mathf.Clamp(normalizedScroll, -3f, 3f);

            // Scroll vers l'avant (> 0) diminue la distance (Zoom)
            // Scroll vers l'arrière (< 0) augmente la distance (Dézoom)
            targetDistance -= normalizedScroll * zoomStep;
            targetDistance = Mathf.Clamp(targetDistance, minZoomDistance, maxZoomDistance);
        }
    }

    /// <summary>
    /// Calcule le Quaternion cible et applique l'interpolation sphérique (Slerp) + gestion de collision.
    /// </summary>
    private void UpdateCameraWithQuaternions(float dt)
    {
        // 1. Calcul du Quaternion cible par produit de Hamilton (Yaw monde * Pitch local)
        Quaternion targetRotation = ComputeOrbitalQuaternion(targetYaw, targetPitch);

        // 2. Facteurs d'amortissement exponentiel (strictement indépendants du framerate)
        float rotFactor = 1f - Mathf.Exp(-rotationSmoothing * dt);
        float zoomFactor = 1f - Mathf.Exp(-zoomSmoothing * dt);

        // 3. Interpolation Sphérique (Slerp) sur les Quaternions et linéaire sur le rayon
        currentRotation = Quaternion.Slerp(currentRotation, targetRotation, rotFactor);
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, zoomFactor);

        ApplyCameraTransform(instantly: false);
    }

    /// <summary>
    /// Produit de deux Quaternions unitaires sans passer par les angles d'Euler du Transform.
    /// </summary>
    private Quaternion ComputeOrbitalQuaternion(float yawDegrees, float pitchDegrees)
    {
        Quaternion yawQuat = Quaternion.AngleAxis(yawDegrees, Vector3.up);
        Quaternion pitchQuat = Quaternion.AngleAxis(pitchDegrees, Vector3.right);

        // Multiplication non-commutative : l'ordre Yaw * Pitch est fondamental
        return yawQuat * pitchQuat;
    }

    /// <summary>
    /// Positionne et oriente la caméra uniquement à partir du Quaternion courant et vérifie les collisions.
    /// </summary>
    private void ApplyCameraTransform(bool instantly)
    {
        Vector3 focusPoint = target.position + targetOffset;

        // Le Quaternion oriente le vecteur arrière (Vector3.back) pour placer la caméra sur la sphère orbitale
        Vector3 orbitalDirection = currentRotation * Vector3.back;
        float finalDistance = currentDistance;

        // Gestion des collisions caméra avec l'environnement (SphereCast du pivot vers la caméra)
        if (enableCollision && !instantly)
        {
            if (Physics.SphereCast(focusPoint, collisionRadius, orbitalDirection, out RaycastHit hit, currentDistance, collisionMask, QueryTriggerInteraction.Ignore))
            {
                // Si un obstacle coupe la vue, on rapproche la caméra devant l'obstacle
                finalDistance = Mathf.Clamp(hit.distance, minZoomDistance * 0.5f, currentDistance);
            }
        }

        // Application directe de la position et du Quaternion (aucun appel à transform.LookAt nécessaire)
        transform.position = focusPoint + orbitalDirection * finalDistance;
        transform.rotation = currentRotation;
    }
}