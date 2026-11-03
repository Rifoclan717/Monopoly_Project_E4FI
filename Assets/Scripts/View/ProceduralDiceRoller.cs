using System;
using System.Collections;
using UnityEngine;

public class ProceduralDiceRoller : MonoBehaviour
{
    [Header("Paramètres Campus Tycoon (<= 2 secondes, ignorable)")]
    [Tooltip("Durée du lancer (Règle Campus Tycoon : 2 secondes maximum)")]
    [Range(0.4f, 2.0f)] public float rollDuration = 1.35f;
    public float throwArcHeight = 2.4f;
    public float diceScale = 0.65f;
    public Vector3 centerTableOffset = new Vector3(0f, 0.45f, 0f);

    [Header("Matériau Partagé (Optionnel)")]
    public Material sharedDiceMaterial;

    public bool IsRolling { get; private set; }
    private bool skipRequested = false;

    private Transform die1Transform;
    private Transform die2Transform;
    private Coroutine rollCoroutine;

    // Quaternions cibles amenant exactement la face N (1 à 6) vers le haut (+Y)
    private static readonly Quaternion[] FaceUpRotations = new Quaternion[]
    {
        Quaternion.identity,                           // Index 0 (inutilisé)
        Quaternion.Euler(0f, 0f, 0f),                  // 1 au-dessus (+Y)
        Quaternion.Euler(-90f, 0f, 0f),                // 2 au-dessus (était en +Z)
        Quaternion.Euler(0f, 0f, 90f),                 // 3 au-dessus (était en +X)
        Quaternion.Euler(0f, 0f, -90f),                // 4 au-dessus (était en -X)
        Quaternion.Euler(90f, 0f, 0f),                 // 5 au-dessus (était en -Z)
        Quaternion.Euler(180f, 0f, 0f)                 // 6 au-dessus (était en -Y)
    };

    void Awake()
    {
        EnsureDiceCreated();
    }

    /// <summary>
    /// Permet d'ignorer/passer immédiatement l'animation de lancer (exigé par les règles).
    /// </summary>
    public void SkipRollAnimation()
    {
        if (IsRolling)
        {
            skipRequested = true;
        }
    }

    /// <summary>
    /// Lance 2 dés (ou 1 seul dé pour la Roulette du BDE) vers un résultat précis (1 à 6 par dé).
    /// </summary>
    public void RollDice(int die1Value, int die2Value, Action<int, int> onRollCompleted = null, bool singleDieMode = false)
    {
        EnsureDiceCreated();

        die1Value = Mathf.Clamp(die1Value, 1, 6);
        die2Value = Mathf.Clamp(die2Value, 1, 6);

        if (rollCoroutine != null) StopCoroutine(rollCoroutine);
        rollCoroutine = StartCoroutine(RollDiceCoroutine(die1Value, die2Value, singleDieMode, onRollCompleted));
    }

    private IEnumerator RollDiceCoroutine(int val1, int val2, bool singleDie, Action<int, int> onCompleted)
    {
        IsRolling = true;
        skipRequested = false;

        die1Transform.gameObject.SetActive(true);
        die2Transform.gameObject.SetActive(!singleDie);

        Vector3 center = transform.position + centerTableOffset;
        Vector3 start1 = center + new Vector3(-1.8f, 1.8f, -2.2f);
        Vector3 start2 = center + new Vector3(1.8f, 1.8f, -2.2f);

        Vector3 end1 = singleDie ? center : center + new Vector3(-0.65f, 0f, 0f);
        Vector3 end2 = center + new Vector3(0.65f, 0f, 0f);

        // Rotation finale exacte par produit de Quaternions (Yaw aléatoire sur Y * Face cible vers le haut)
        Quaternion finalRot1 = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up) * FaceUpRotations[val1];
        Quaternion finalRot2 = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up) * FaceUpRotations[val2];

        // Axes de rotation rapides pendant le vol
        Vector3 spinAxis1 = new Vector3(1f, 0.7f, 0.4f).normalized;
        Vector3 spinAxis2 = new Vector3(-0.6f, 1f, -0.5f).normalized;

        float elapsed = 0f;

        while (elapsed < rollDuration && !skipRequested)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / rollDuration);

            // Trajectoire avec rebond amorti au sol
            float bounceOffset = ComputeDiceBounceHeight(t) * throwArcHeight;

            die1Transform.position = Vector3.Lerp(start1, end1, t) + Vector3.up * bounceOffset;
            if (!singleDie)
            {
                die2Transform.position = Vector3.Lerp(start2, end2, t) + Vector3.up * bounceOffset;
            }

            // Interpolation par Quaternions : tourbillon rapide au début, verrouillage Slerp sur la bonne face à la fin
            float spinAngle = (1f - t) * (1f - t) * 1080f;
            Quaternion spinQuat1 = Quaternion.AngleAxis(spinAngle, spinAxis1);
            Quaternion spinQuat2 = Quaternion.AngleAxis(spinAngle, spinAxis2);

            die1Transform.rotation = Quaternion.Slerp(spinQuat1 * finalRot1, finalRot1, t * t);
            if (!singleDie)
            {
                die2Transform.rotation = Quaternion.Slerp(spinQuat2 * finalRot2, finalRot2, t * t);
            }

            yield return null;
        }

        // Ancrage final exact (garantit la bonne face même si l'animation est ignorée)
        die1Transform.position = end1;
        die1Transform.rotation = finalRot1;

        if (!singleDie)
        {
            die2Transform.position = end2;
            die2Transform.rotation = finalRot2;
        }

        IsRolling = false;
        rollCoroutine = null;
        onCompleted?.Invoke(val1, singleDie ? 0 : val2);
    }

    private float ComputeDiceBounceHeight(float t)
    {
        // Premier grand arc (0 -> 0.65), puis petit rebond sur le plateau (0.65 -> 1.0)
        if (t < 0.65f)
        {
            float nt = t / 0.65f;
            return Mathf.Sin(nt * Mathf.PI);
        }
        else
        {
            float nt = (t - 0.65f) / 0.35f;
            return Mathf.Sin(nt * Mathf.PI) * 0.25f;
        }
    }

    private void EnsureDiceCreated()
    {
        if (die1Transform == null) die1Transform = CreateProceduralDie("Die_1");
        if (die2Transform == null) die2Transform = CreateProceduralDie("Die_2");
    }

    /// <summary>
    /// Construit procéduralement un dé 3D avec ses points (1 à 6) sur chaque face.
    /// </summary>
    private Transform CreateProceduralDie(string dieName)
    {
        Transform existing = transform.Find(dieName);
        if (existing != null) return existing;

        GameObject dieRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        dieRoot.name = dieName;
        dieRoot.transform.SetParent(transform, false);
        dieRoot.transform.localScale = Vector3.one * diceScale;

        // Désactive le collider pour ne pas gêner les pions ni la caméra
        Collider c = dieRoot.GetComponent<Collider>();
        if (c != null) c.enabled = false;

        MeshRenderer mr = dieRoot.GetComponent<MeshRenderer>();
        if (sharedDiceMaterial != null) mr.sharedMaterial = sharedDiceMaterial;
        ApplyColorBlock(mr, new Color(0.98f, 0.97f, 0.94f)); // Blanc ivoire

        // Placement des points (pips) sur les 6 faces conformément à FaceUpRotations :
        // +Y = 1, -Y = 6, +Z = 2, -Z = 5, +X = 3, -X = 4
        AddFacePips(dieRoot.transform, Vector3.up, Vector3.forward, Vector3.right, 1);
        AddFacePips(dieRoot.transform, Vector3.down, Vector3.forward, Vector3.left, 6);
        AddFacePips(dieRoot.transform, Vector3.forward, Vector3.up, Vector3.right, 2);
        AddFacePips(dieRoot.transform, Vector3.back, Vector3.up, Vector3.left, 5);
        AddFacePips(dieRoot.transform, Vector3.right, Vector3.up, Vector3.back, 3);
        AddFacePips(dieRoot.transform, Vector3.left, Vector3.up, Vector3.forward, 4);

        return dieRoot.transform;
    }

    private void AddFacePips(Transform die, Vector3 normal, Vector3 upAxis, Vector3 rightAxis, int value)
    {
        float d = 0.25f;
        Vector2[] offsets;

        switch (value)
        {
            case 1: offsets = new[] { Vector2.zero }; break;
            case 2: offsets = new[] { new Vector2(-d, -d), new Vector2(d, d) }; break;
            case 3: offsets = new[] { new Vector2(-d, -d), Vector2.zero, new Vector2(d, d) }; break;
            case 4: offsets = new[] { new Vector2(-d, -d), new Vector2(-d, d), new Vector2(d, -d), new Vector2(d, d) }; break;
            case 5: offsets = new[] { new Vector2(-d, -d), new Vector2(-d, d), Vector2.zero, new Vector2(d, -d), new Vector2(d, d) }; break;
            default: offsets = new[] { new Vector2(-d, -d), new Vector2(-d, 0f), new Vector2(-d, d), new Vector2(d, -d), new Vector2(d, 0f), new Vector2(d, d) }; break;
        }

        foreach (Vector2 off in offsets)
        {
            GameObject pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pip.name = $"Pip_{value}";
            pip.transform.SetParent(die, false);
            Collider col = pip.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            pip.transform.localPosition = normal * 0.505f + rightAxis * off.x + upAxis * off.y;
            pip.transform.localScale = new Vector3(0.14f, 0.14f, 0.14f);

            MeshRenderer mr = pip.GetComponent<MeshRenderer>();
            if (sharedDiceMaterial != null) mr.sharedMaterial = sharedDiceMaterial;
            // Point rouge pour l'As (1), noir pour les autres faces
            ApplyColorBlock(mr, value == 1 ? new Color(0.85f, 0.15f, 0.15f) : new Color(0.10f, 0.10f, 0.12f));
        }
    }

    private void ApplyColorBlock(MeshRenderer mr, Color col)
    {
        if (mr == null) return;
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        mr.GetPropertyBlock(block);
        block.SetColor("_BaseColor", col);
        block.SetColor("_Color", col);
        mr.SetPropertyBlock(block);
    }
}