using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class ProceduralBuildingGenerator : MonoBehaviour
{
    // Groupes de couleurs officiels de Campus Tycoon (Niveaux 1, 2 et 3)
    public enum CampusGroup
    {
        Marron,     // Cafétéria, Salle de TP
        BleuCiel,   // Bibliothèque, Salle de projet
        Rose,       // FabLab, Salle de robotique
        Orange,     // Amphi A, Amphi B
        Rouge,      // Bureau du BDE, Direction
        Or          // Niveau 3 : Incubateur de start-ups, Laboratoire de recherche
    }

    [Header("État Campus Tycoon (Règles du jeu)")]
    [Tooltip("0 = Terrain nu (affiche la bordure/borne du propriétaire si acheté), 1 = Niv.1 (5R), 2 = Niv.2 (12R), 3 = Niv.3 (22R)")]
    [Range(0, 3)] public int buildingLevel = 0;

    [Tooltip("-1 = Aucun propriétaire, 0 = Joueur Humain, 1 = Rival IA 1, 2 = Rival IA 2")]
    [Range(-1, 2)] public int ownerId = -1;

    [Tooltip("Groupe de couleur du terrain sur le campus")]
    public CampusGroup campusGroup = CampusGroup.Orange;

    [Header("Dimensions Géométriques")]
    [Min(0.6f)] public float baseWidth = 1.8f;
    [Min(0.6f)] public float baseDepth = 1.6f;
    [Min(0.05f)] public float foundationHeight = 0.14f;
    [Min(0.3f)] public float floorHeight = 0.55f;
    [Min(0.2f)] public float roofHeight = 0.45f;

    [Header("Palette de Couleurs (Synchronisée avec le Plateau / Personnages)")]
    public Color wallColor = new Color(0.95f, 0.93f, 0.88f); // Pierre claire universitaire
    public Color unownedRoofColor = new Color(0.32f, 0.35f, 0.40f); // Ardoise neutre
    public Color[] playerOwnerColors = new Color[]
    {
        new Color(0.18f, 0.52f, 0.95f), // ID 0 : Joueur 0
        new Color(0.90f, 0.22f, 0.25f), // ID 1 : Joueur 1
        new Color(0.20f, 0.78f, 0.42f)  // ID 2 : Joueur 2
    };

    [Header("Options Techniques")]
    public bool updateCollider = true;

    private Mesh generatedMesh;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();

    // 3 sous-maillages pour colorer indépendamment Socle, Murs et Toit
    private readonly List<int> trisSocleGroup = new List<int>(); // Submesh 0
    private readonly List<int> trisWalls = new List<int>();      // Submesh 1
    private readonly List<int> trisRoofOwner = new List<int>();  // Submesh 2

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private MaterialPropertyBlock propBlock;
    private Color customGroupColorOverride = Color.clear;
    private Coroutine popCoroutine;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        RebuildMesh();
    }

    void OnValidate()
    {
        customGroupColorOverride = Color.clear;
        RebuildMesh();
    }

    /// <summary>
    /// Synchronise la palette de couleurs des propriétaires avec les personnages actifs de la partie.
    /// </summary>
    public void ApplyPlayerPalette(Color[] palette)
    {
        if (palette == null || palette.Length == 0) return;
        playerOwnerColors = new Color[palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            playerOwnerColors[i] = palette[i];
        }
        ApplyVisualColors();
    }

    /// <summary>
    /// Appelé par le moteur logique ou les événements Observer (OnPropertyUpgraded).
    /// </summary>
    public void SetBuildingLevel(int level)
    {
        buildingLevel = Mathf.Clamp(level, 0, 3);
        RebuildMesh();
    }

    /// <summary>
    /// Appelé lors de l'achat (OnPropertyBought) ou de l'initialisation de la case.
    /// Colorie le toit/bordure selon le propriétaire (-1 à 2) et le socle selon la couleur du groupe.
    /// </summary>
    public void SetOwnerVisual(int newOwnerId, Color groupColor)
    {
        ownerId = Mathf.Clamp(newOwnerId, -1, 2);
        customGroupColorOverride = groupColor;
        RebuildMesh();
    }

    /// <summary>
    /// Surcharge pratique utilisant directement l'énumération des groupes de Campus Tycoon.
    /// </summary>
    public void SetCampusBuildingState(int level, int newOwnerId, CampusGroup group, bool playPopAnimation = false)
    {
        buildingLevel = Mathf.Clamp(level, 0, 3);
        ownerId = Mathf.Clamp(newOwnerId, -1, 2);
        campusGroup = group;
        customGroupColorOverride = Color.clear;
        RebuildMesh();

        if (playPopAnimation && Application.isPlaying && gameObject.activeInHierarchy)
        {
            PlayBuildPopAnimation();
        }
    }

    /// <summary>
    /// Animation élastique courte de construction / achat sur l'axe vertical.
    /// </summary>
    public void PlayBuildPopAnimation()
    {
        if (!Application.isPlaying || !gameObject.activeInHierarchy) return;
        if (popCoroutine != null) StopCoroutine(popCoroutine);
        popCoroutine = StartCoroutine(PopScaleCoroutine());
    }

    private IEnumerator PopScaleCoroutine()
    {
        float duration = 0.32f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Overshoot doux (easeOutBack)
            float c1 = 1.70158f;
            float c3 = c1 + 1f;
            float scaleY = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
            scaleY = Mathf.Clamp(scaleY, 0.25f, 1.25f);
            float scaleXZ = Mathf.Lerp(0.88f, 1f, t);
            transform.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);
            yield return null;
        }
        transform.localScale = Vector3.one;
        popCoroutine = null;
    }

    /// <summary>
    /// Reconstruit le maillage procédural complet selon le niveau (0 à 3).
    /// </summary>
    public void RebuildMesh()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();

        if (generatedMesh == null)
        {
            generatedMesh = new Mesh { name = "CampusBuilding_Mesh" };
        }
        else
        {
            generatedMesh.Clear();
        }

        vertices.Clear();
        uvs.Clear();
        trisSocleGroup.Clear();
        trisWalls.Clear();
        trisRoofOwner.Clear();

        // 1. Dalle de fondation aux couleurs du Groupe (Toujours visible, même au Niveau 0)
        AddBox(Vector3.zero, baseWidth, baseDepth, 0f, foundationHeight, trisSocleGroup);

        if (buildingLevel == 0)
        {
            // Niveau 0 : Petite dalle intérieure de terrain constructible
            float slabTop = foundationHeight + 0.03f;
            AddBox(Vector3.zero, baseWidth * 0.82f, baseDepth * 0.82f, foundationHeight, slabTop, trisWalls);

            // Si le terrain est acheté sans amélioration (ownerId >= 0),
            // afficher clairement la couleur du propriétaire (bordure encadrante + borne/enseigne)
            if (ownerId >= 0)
            {
                float borderW = baseWidth * 0.88f;
                float borderD = baseDepth * 0.88f;
                float borderThickness = Mathf.Min(baseWidth, baseDepth) * 0.11f;
                float borderYMax = slabTop + 0.07f;

                float halfZ = (borderD - borderThickness) * 0.5f;
                float halfX = (borderW - borderThickness) * 0.5f;
                AddBox(new Vector3(0f, 0f, -halfZ), borderW, borderThickness, foundationHeight, borderYMax, trisRoofOwner);
                AddBox(new Vector3(0f, 0f, halfZ), borderW, borderThickness, foundationHeight, borderYMax, trisRoofOwner);
                AddBox(new Vector3(-halfX, 0f, 0f), borderThickness, borderD - 2f * borderThickness, foundationHeight, borderYMax, trisRoofOwner);
                AddBox(new Vector3(halfX, 0f, 0f), borderThickness, borderD - 2f * borderThickness, foundationHeight, borderYMax, trisRoofOwner);

                // Borne / panneau de propriété au centre-arrière de la parcelle
                Vector3 signPos = new Vector3(0f, 0f, borderD * 0.18f);
                AddBox(signPos, borderW * 0.12f, borderD * 0.12f, slabTop, slabTop + 0.24f, trisWalls);
                AddBox(signPos, borderW * 0.46f, borderD * 0.24f, slabTop + 0.22f, slabTop + 0.34f, trisRoofOwner);
                AddPitchedRoof(signPos, borderW * 0.50f, borderD * 0.28f, slabTop + 0.34f, slabTop + 0.44f, trisRoofOwner);
            }
        }
        else if (buildingLevel == 1)
        {
            // NIVEAU 1 (5R) : Petit bâtiment de campus (1 étage + porche + toit à 2 pans)
            float w = baseWidth * 0.72f;
            float d = baseDepth * 0.72f;
            float yTop = foundationHeight + floorHeight;

            AddBox(Vector3.zero, w, d, foundationHeight, yTop, trisWalls);

            float porchW = w * 0.38f;
            float porchD = d * 0.22f;
            Vector3 porchCenter = new Vector3(0f, 0f, -(d * 0.5f + porchD * 0.45f));
            AddBox(porchCenter, porchW, porchD, foundationHeight, foundationHeight + floorHeight * 0.65f, trisWalls);
            AddPitchedRoof(porchCenter, porchW * 1.15f, porchD * 1.15f, foundationHeight + floorHeight * 0.65f, foundationHeight + floorHeight * 0.88f, trisRoofOwner);

            AddPitchedRoof(Vector3.zero, w * 1.14f, d * 1.14f, yTop, yTop + roofHeight, trisRoofOwner);
        }
        else if (buildingLevel == 2)
        {
            // NIVEAU 2 (12R) : Bâtiment universitaire moyen (2 étages + aile latérale + cheminée)
            float mainW = baseWidth * 0.62f;
            float mainD = baseDepth * 0.76f;
            float yTopMain = foundationHeight + (floorHeight * 1.85f);

            Vector3 mainCenter = new Vector3(-baseWidth * 0.12f, 0f, 0f);
            AddBox(mainCenter, mainW, mainD, foundationHeight, yTopMain, trisWalls);
            AddPitchedRoof(mainCenter, mainW * 1.14f, mainD * 1.14f, yTopMain, yTopMain + roofHeight, trisRoofOwner);

            AddBox(mainCenter, mainW * 1.04f, mainD * 1.04f, foundationHeight + floorHeight * 0.9f, foundationHeight + floorHeight * 1.0f, trisSocleGroup);

            float wingW = baseWidth * 0.38f;
            float wingD = baseDepth * 0.58f;
            float yTopWing = foundationHeight + (floorHeight * 1.25f);
            Vector3 wingCenter = new Vector3(baseWidth * 0.24f, 0f, -baseDepth * 0.05f);

            AddBox(wingCenter, wingW, wingD, foundationHeight, yTopWing, trisWalls);
            AddPitchedRoof(wingCenter, wingW * 1.14f, wingD * 1.14f, yTopWing, yTopWing + (roofHeight * 0.8f), trisRoofOwner);

            Vector3 chimneyPos = mainCenter + new Vector3(-mainW * 0.28f, 0f, mainD * 0.22f);
            AddBox(chimneyPos, 0.18f, 0.18f, yTopMain + roofHeight * 0.2f, yTopMain + roofHeight * 1.15f, trisWalls);
        }
        else if (buildingLevel >= 3)
        {
            // NIVEAU 3 (22R) : Grand Complexe / Amphi Prestigieux (Double corps + toit mansardé + flèche)
            float mainW = baseWidth * 0.88f;
            float mainD = baseDepth * 0.78f;
            float h1 = foundationHeight + (floorHeight * 1.35f);
            float h2 = h1 + (floorHeight * 1.15f);

            AddBox(Vector3.zero, mainW, mainD, foundationHeight, h1, trisWalls);
            AddBox(Vector3.zero, mainW * 1.05f, mainD * 1.05f, h1, h1 + 0.08f, trisSocleGroup);

            float upperW = mainW * 0.78f;
            float upperD = mainD * 0.78f;
            AddBox(Vector3.zero, upperW, upperD, h1 + 0.08f, h2, trisWalls);

            Vector3 frontCenter = new Vector3(0f, 0f, -(mainD * 0.5f + 0.10f));
            AddBox(frontCenter, mainW * 0.42f, 0.22f, foundationHeight, h2 + 0.05f, trisWalls);
            AddPitchedRoof(frontCenter, mainW * 0.48f, 0.28f, h2 + 0.05f, h2 + roofHeight * 0.75f, trisRoofOwner);

            AddMansardRoof(Vector3.zero, upperW * 1.12f, upperD * 1.12f, upperW * 0.55f, upperD * 0.55f, h2, h2 + roofHeight * 1.15f, trisRoofOwner);

            float spireBaseY = h2 + roofHeight * 1.15f;
            AddBox(Vector3.zero, upperW * 0.22f, upperD * 0.22f, spireBaseY, spireBaseY + 0.22f, trisWalls);
            AddPyramidSpire(Vector3.zero, upperW * 0.26f, upperD * 0.26f, spireBaseY + 0.22f, spireBaseY + 0.22f + roofHeight * 0.95f, trisRoofOwner);
        }

        generatedMesh.SetVertices(vertices);
        generatedMesh.SetUVs(0, uvs);
        generatedMesh.subMeshCount = 3;
        generatedMesh.SetTriangles(trisSocleGroup, 0);
        generatedMesh.SetTriangles(trisWalls, 1);
        generatedMesh.SetTriangles(trisRoofOwner, 2);

        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();

        meshFilter.sharedMesh = generatedMesh;

        EnsureThreeMaterialSlots();
        ApplyVisualColors();

        if (updateCollider && meshCollider != null && generatedMesh.vertexCount > 0)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = generatedMesh;
        }
    }

    private void EnsureThreeMaterialSlots()
    {
        if (meshRenderer == null || meshRenderer.sharedMaterial == null) return;

        Material baseMat = meshRenderer.sharedMaterial;
        Material[] currentMats = meshRenderer.sharedMaterials;

        if (currentMats.Length != 3 || currentMats[0] == null || currentMats[1] == null || currentMats[2] == null)
        {
            meshRenderer.sharedMaterials = new Material[] { baseMat, baseMat, baseMat };
        }
    }

    public void ApplyVisualColors()
    {
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) return;

        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        Color groupCol = customGroupColorOverride != Color.clear
            ? customGroupColorOverride
            : GetColorForCampusGroup(campusGroup);

        Color roofCol = unownedRoofColor;
        if (ownerId >= 0 && ownerId < playerOwnerColors.Length)
        {
            roofCol = playerOwnerColors[ownerId];
        }

        SetSubmeshColor(0, groupCol);
        SetSubmeshColor(1, wallColor);
        SetSubmeshColor(2, roofCol);
    }

    private void SetSubmeshColor(int submeshIndex, Color color)
    {
        if (submeshIndex >= meshRenderer.sharedMaterials.Length) return;

        meshRenderer.GetPropertyBlock(propBlock, submeshIndex);
        propBlock.SetColor(BaseColorId, color);
        propBlock.SetColor(ColorId, color);
        meshRenderer.SetPropertyBlock(propBlock, submeshIndex);
    }

    public static Color GetColorForCampusGroup(CampusGroup group)
    {
        switch (group)
        {
            case CampusGroup.Marron:   return new Color(0.55f, 0.32f, 0.18f);
            case CampusGroup.BleuCiel: return new Color(0.38f, 0.75f, 0.95f);
            case CampusGroup.Rose:     return new Color(0.92f, 0.38f, 0.68f);
            case CampusGroup.Orange:   return new Color(0.95f, 0.55f, 0.15f);
            case CampusGroup.Rouge:    return new Color(0.85f, 0.18f, 0.18f);
            case CampusGroup.Or:       return new Color(0.95f, 0.78f, 0.15f);
            default:                   return Color.gray;
        }
    }

    // ==================== PRIMITIVES GÉOMÉTRIQUES PROCÉDURALES ====================

    private void AddBox(Vector3 center, float w, float d, float yMin, float yMax, List<int> targetTris)
    {
        float hw = w * 0.5f;
        float hd = d * 0.5f;
        float cx = center.x;
        float cz = center.z;

        AddQuad(new Vector3(cx - hw, yMin, cz - hd), new Vector3(cx - hw, yMax, cz - hd), new Vector3(cx + hw, yMax, cz - hd), new Vector3(cx + hw, yMin, cz - hd), targetTris);
        AddQuad(new Vector3(cx + hw, yMin, cz + hd), new Vector3(cx + hw, yMax, cz + hd), new Vector3(cx - hw, yMax, cz + hd), new Vector3(cx - hw, yMin, cz + hd), targetTris);
        AddQuad(new Vector3(cx - hw, yMin, cz + hd), new Vector3(cx - hw, yMax, cz + hd), new Vector3(cx - hw, yMax, cz - hd), new Vector3(cx - hw, yMin, cz - hd), targetTris);
        AddQuad(new Vector3(cx + hw, yMin, cz - hd), new Vector3(cx + hw, yMax, cz - hd), new Vector3(cx + hw, yMax, cz + hd), new Vector3(cx + hw, yMin, cz + hd), targetTris);
        AddQuad(new Vector3(cx - hw, yMax, cz - hd), new Vector3(cx - hw, yMax, cz + hd), new Vector3(cx + hw, yMax, cz + hd), new Vector3(cx + hw, yMax, cz - hd), targetTris);
    }

    private void AddPitchedRoof(Vector3 center, float w, float d, float yBase, float yRidge, List<int> targetTris)
    {
        float hw = w * 0.5f;
        float hd = d * 0.5f;
        float cx = center.x;
        float cz = center.z;

        Vector3 ridgeL = new Vector3(cx - hw, yRidge, cz);
        Vector3 ridgeR = new Vector3(cx + hw, yRidge, cz);

        AddQuad(new Vector3(cx - hw, yBase, cz - hd), ridgeL, ridgeR, new Vector3(cx + hw, yBase, cz - hd), targetTris);
        AddQuad(new Vector3(cx + hw, yBase, cz + hd), ridgeR, ridgeL, new Vector3(cx - hw, yBase, cz + hd), targetTris);

        AddTriangle(new Vector3(cx - hw, yBase, cz + hd), ridgeL, new Vector3(cx - hw, yBase, cz - hd), trisWalls);
        AddTriangle(new Vector3(cx + hw, yBase, cz - hd), ridgeR, new Vector3(cx + hw, yBase, cz + hd), trisWalls);
    }

    private void AddMansardRoof(Vector3 center, float wBot, float dBot, float wTop, float dTop, float yBot, float yTop, List<int> targetTris)
    {
        float hwb = wBot * 0.5f, hdb = dBot * 0.5f;
        float hwt = wTop * 0.5f, hdt = dTop * 0.5f;
        float cx = center.x, cz = center.z;

        AddQuad(new Vector3(cx - hwb, yBot, cz - hdb), new Vector3(cx - hwt, yTop, cz - hdt), new Vector3(cx + hwt, yTop, cz - hdt), new Vector3(cx + hwb, yBot, cz - hdb), targetTris);
        AddQuad(new Vector3(cx + hwb, yBot, cz + hdb), new Vector3(cx + hwt, yTop, cz + hdt), new Vector3(cx - hwt, yTop, cz + hdt), new Vector3(cx - hwb, yBot, cz + hdb), targetTris);
        AddQuad(new Vector3(cx - hwb, yBot, cz + hdb), new Vector3(cx - hwt, yTop, cz + hdt), new Vector3(cx - hwt, yTop, cz - hdt), new Vector3(cx - hwb, yBot, cz - hdb), targetTris);
        AddQuad(new Vector3(cx + hwb, yBot, cz - hdb), new Vector3(cx + hwt, yTop, cz - hdt), new Vector3(cx + hwt, yTop, cz + hdt), new Vector3(cx + hwb, yBot, cz + hdb), targetTris);

        AddQuad(new Vector3(cx - hwt, yTop, cz - hdt), new Vector3(cx - hwt, yTop, cz + hdt), new Vector3(cx + hwt, yTop, cz + hdt), new Vector3(cx + hwt, yTop, cz - hdt), targetTris);
    }

    private void AddPyramidSpire(Vector3 center, float w, float d, float yBase, float yPeak, List<int> targetTris)
    {
        float hw = w * 0.5f, hd = d * 0.5f;
        float cx = center.x, cz = center.z;
        Vector3 peak = new Vector3(cx, yPeak, cz);

        AddTriangle(new Vector3(cx - hw, yBase, cz - hd), peak, new Vector3(cx + hw, yBase, cz - hd), targetTris);
        AddTriangle(new Vector3(cx + hw, yBase, cz + hd), peak, new Vector3(cx - hw, yBase, cz + hd), targetTris);
        AddTriangle(new Vector3(cx - hw, yBase, cz + hd), peak, new Vector3(cx - hw, yBase, cz - hd), targetTris);
        AddTriangle(new Vector3(cx + hw, yBase, cz - hd), peak, new Vector3(cx + hw, yBase, cz + hd), targetTris);
    }

    private void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, List<int> targetTris)
    {
        int i = vertices.Count;
        vertices.Add(v0); vertices.Add(v1); vertices.Add(v2); vertices.Add(v3);
        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));

        targetTris.Add(i); targetTris.Add(i + 1); targetTris.Add(i + 2);
        targetTris.Add(i); targetTris.Add(i + 2); targetTris.Add(i + 3);
    }

    private void AddTriangle(Vector3 v0, Vector3 v1, Vector3 v2, List<int> targetTris)
    {
        int i = vertices.Count;
        vertices.Add(v0); vertices.Add(v1); vertices.Add(v2);
        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0.5f, 1f)); uvs.Add(new Vector2(1f, 0f));

        targetTris.Add(i); targetTris.Add(i + 1); targetTris.Add(i + 2);
    }
}
