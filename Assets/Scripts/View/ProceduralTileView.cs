using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider))]
public class ProceduralTileView : MonoBehaviour
{
    public enum TileCategory
    {
        Corner,     // La Rentrée, Colle, Foyer étudiant, Rattrapage
        Property,   // Terrains constructibles (Marron à Or)
        Station,    // RER A, Bus du campus
        EventCard,  // Carte Événement
        Company,    // Machine à café
        Tax,        // Frais de scolarité
        Special     // Roulette du BDE (Niveau 3)
    }

    [Header("Identité de la Case (Campus Tycoon)")]
    public int tileIndex;
    public string tileName;
    public int price;
    public TileCategory category;
    public ProceduralBuildingGenerator.CampusGroup propertyGroup;

    [Header("Références Visuelles & Ancrages")]
    public ProceduralBuildingGenerator buildingInstance;
    public Transform buildingAnchor;
    public Transform[] pawnSlots = new Transform[3];
    public Transform[] collePrisonSlots = new Transform[3];

    [Header("Marqueurs Spéciaux (Gares / Compagnie / Roulette / Sélection)")]
    public GameObject specialOwnerMarkerRoot;
    public MeshRenderer[] specialOwnerRenderers = new MeshRenderer[0];
    public Transform rouletteWheelTransform;

    private Mesh tileMesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propBlock;
    private TextMesh labelTextMesh;
    private GameObject highlightFrame;
    private bool isHighlighted;

    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> trisBaseSlab = new List<int>();   // Submesh 0 : Dalle trottoir
    private readonly List<int> trisHeaderBand = new List<int>(); // Submesh 1 : Bandeau couleur

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public void InitializeTile(
        int index,
        string nameLabel,
        int cost,
        TileCategory cat,
        ProceduralBuildingGenerator.CampusGroup group,
        float width,
        float depth,
        float height,
        Material sharedMat)
    {
        tileIndex = index;
        tileName = nameLabel;
        price = cost;
        category = cat;
        propertyGroup = group;

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        if (sharedMat != null)
        {
            meshRenderer.sharedMaterials = new Material[] { sharedMat, sharedMat };
        }

        BuildProceduralTileMesh(width, depth, height);
        ApplyTileColors();
        SetupAnchorsAndLabel(width, depth, height);
        SetupHighlightIndicator(width, depth, height, sharedMat);

        BoxCollider col = GetComponent<BoxCollider>();
        col.size = new Vector3(width, height + 0.1f, depth);
        col.center = new Vector3(0f, (height + 0.1f) * 0.5f, 0f);
    }

    /// <summary>
    /// Crée un marqueur visuel de propriété (bordure + fanion) pour les Gares et la Machine à café.
    /// Masqué tant que la case est libre, coloré aux couleurs du propriétaire dès l'achat.
    /// </summary>
    public void SetupSpecialOwnerMarker(float width, float depth, Material sharedMat)
    {
        if (buildingAnchor == null) return;

        Transform existing = buildingAnchor.Find("SpecialOwnerMarker");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        GameObject root = new GameObject("SpecialOwnerMarker");
        root.transform.SetParent(buildingAnchor, false);
        root.transform.localPosition = Vector3.zero;

        List<MeshRenderer> rends = new List<MeshRenderer>();

        // 1. Cadre de sol coloré autour du kiosque / quai
        float bw = width * 0.78f;
        float bd = depth * 0.34f;
        float t = 0.09f;
        rends.Add(CreateMarkerPart(root.transform, "Border_Front", new Vector3(0f, 0.045f, -bd * 0.5f), new Vector3(bw, 0.07f, t), sharedMat));
        rends.Add(CreateMarkerPart(root.transform, "Border_Back", new Vector3(0f, 0.045f, bd * 0.5f), new Vector3(bw, 0.07f, t), sharedMat));
        rends.Add(CreateMarkerPart(root.transform, "Border_Left", new Vector3(-bw * 0.5f, 0.045f, 0f), new Vector3(t, 0.07f, bd), sharedMat));
        rends.Add(CreateMarkerPart(root.transform, "Border_Right", new Vector3(bw * 0.5f, 0.045f, 0f), new Vector3(t, 0.07f, bd), sharedMat));

        // 2. Mât + Fanion propriétaire bien visible en hauteur
        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "FlagPole";
        pole.transform.SetParent(root.transform, false);
        pole.transform.localPosition = new Vector3(bw * 0.44f, 0.42f, -bd * 0.25f);
        pole.transform.localScale = new Vector3(0.06f, 0.42f, 0.06f);
        DisableCollider(pole);
        MeshRenderer poleMr = pole.GetComponent<MeshRenderer>();
        if (sharedMat != null) poleMr.sharedMaterial = sharedMat;
        ApplyBlockColor(poleMr, new Color(0.88f, 0.88f, 0.90f));

        rends.Add(CreateMarkerPart(
            root.transform,
            "OwnerFlag",
            new Vector3(bw * 0.44f - 0.20f, 0.72f, -bd * 0.25f),
            new Vector3(0.38f, 0.22f, 0.06f),
            sharedMat));

        specialOwnerMarkerRoot = root;
        specialOwnerRenderers = rends.ToArray();
        specialOwnerMarkerRoot.SetActive(false);
    }

    /// <summary>
    /// Met à jour la couleur du propriétaire sur une Gare ou une Compagnie (-1 = libre / masqué).
    /// </summary>
    public void SetSpecialOwnerVisual(int ownerId, Color ownerColor)
    {
        if (specialOwnerMarkerRoot == null) return;

        bool owned = ownerId >= 0;
        specialOwnerMarkerRoot.SetActive(owned);
        if (!owned) return;

        for (int i = 0; i < specialOwnerRenderers.Length; i++)
        {
            ApplyBlockColor(specialOwnerRenderers[i], ownerColor);
        }
    }

    /// <summary>
    /// Active ou désactive le surlignage doré lors de l'inspection d'une case au clic.
    /// </summary>
    public void SetHighlighted(bool highlighted)
    {
        isHighlighted = highlighted;
        if (highlightFrame != null)
        {
            highlightFrame.SetActive(isHighlighted);
        }
    }

    private MeshRenderer CreateMarkerPart(Transform parent, string partName, Vector3 pos, Vector3 scale, Material sharedMat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = partName;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        DisableCollider(go);
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (sharedMat != null) mr.sharedMaterial = sharedMat;
        return mr;
    }

    private void SetupHighlightIndicator(float width, float depth, float height, Material sharedMat)
    {
        Transform existing = transform.Find("SelectionHighlight");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        GameObject root = new GameObject("SelectionHighlight");
        root.transform.SetParent(transform, false);
        float y = height + 0.04f;
        float t = 0.09f;
        Color gold = new Color(1.0f, 0.84f, 0.16f);

        MeshRenderer[] bars = new MeshRenderer[4];
        bars[0] = CreateMarkerPart(root.transform, "HL_Front", new Vector3(0f, y, -depth * 0.5f + t * 0.5f), new Vector3(width, 0.05f, t), sharedMat);
        bars[1] = CreateMarkerPart(root.transform, "HL_Back", new Vector3(0f, y, depth * 0.5f - t * 0.5f), new Vector3(width, 0.05f, t), sharedMat);
        bars[2] = CreateMarkerPart(root.transform, "HL_Left", new Vector3(-width * 0.5f + t * 0.5f, y, 0f), new Vector3(t, 0.05f, depth), sharedMat);
        bars[3] = CreateMarkerPart(root.transform, "HL_Right", new Vector3(width * 0.5f - t * 0.5f, y, 0f), new Vector3(t, 0.05f, depth), sharedMat);

        for (int i = 0; i < bars.Length; i++) ApplyBlockColor(bars[i], gold);
        root.SetActive(false);
        highlightFrame = root;
    }

    private static void DisableCollider(GameObject go)
    {
        Collider c = go.GetComponent<Collider>();
        if (c != null) c.enabled = false;
    }

    private void ApplyBlockColor(MeshRenderer mr, Color col)
    {
        if (mr == null) return;
        if (propBlock == null) propBlock = new MaterialPropertyBlock();
        mr.GetPropertyBlock(propBlock);
        propBlock.SetColor(BaseColorId, col);
        propBlock.SetColor(ColorId, col);
        mr.SetPropertyBlock(propBlock);
    }

    private void BuildProceduralTileMesh(float width, float depth, float height)
    {
        if (tileMesh == null)
        {
            tileMesh = new Mesh { name = $"TileMesh_{tileIndex}" };
        }
        else
        {
            tileMesh.Clear();
        }

        vertices.Clear();
        uvs.Clear();
        trisBaseSlab.Clear();
        trisHeaderBand.Clear();

        AddBeveledBox(Vector3.zero, width, depth, 0f, height, 0.06f, trisBaseSlab);

        float bandYMin = height;
        float bandYMax = height + 0.05f;

        if (category == TileCategory.Property)
        {
            float bandDepth = depth * 0.34f;
            Vector3 bandCenter = new Vector3(0f, 0f, (depth * 0.5f) - (bandDepth * 0.5f) - 0.05f);
            AddBeveledBox(bandCenter, width * 0.92f, bandDepth, bandYMin, bandYMax, 0.02f, trisHeaderBand);
        }
        else if (category == TileCategory.Corner)
        {
            AddBeveledBox(Vector3.zero, width * 0.86f, depth * 0.86f, bandYMin, bandYMax + 0.03f, 0.03f, trisHeaderBand);
        }
        else
        {
            AddBeveledBox(new Vector3(0f, 0f, depth * 0.18f), width * 0.76f, depth * 0.24f, bandYMin, bandYMax, 0.02f, trisHeaderBand);
        }

        tileMesh.SetVertices(vertices);
        tileMesh.SetUVs(0, uvs);
        tileMesh.subMeshCount = 2;
        tileMesh.SetTriangles(trisBaseSlab, 0);
        tileMesh.SetTriangles(trisHeaderBand, 1);

        tileMesh.RecalculateNormals();
        tileMesh.RecalculateBounds();

        meshFilter.sharedMesh = tileMesh;
    }

    private void ApplyTileColors()
    {
        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        Color slabColor = (category == TileCategory.Corner)
            ? new Color(0.86f, 0.84f, 0.78f)
            : new Color(0.92f, 0.93f, 0.95f);

        Color headerColor;
        switch (category)
        {
            case TileCategory.Property:
                headerColor = ProceduralBuildingGenerator.GetColorForCampusGroup(propertyGroup);
                break;
            case TileCategory.Station:
                headerColor = new Color(0.18f, 0.22f, 0.30f);
                break;
            case TileCategory.EventCard:
                headerColor = new Color(0.55f, 0.25f, 0.85f);
                break;
            case TileCategory.Company:
                headerColor = new Color(0.42f, 0.26f, 0.14f);
                break;
            case TileCategory.Tax:
                headerColor = new Color(0.75f, 0.15f, 0.20f);
                break;
            case TileCategory.Special:
                headerColor = new Color(0.95f, 0.72f, 0.10f);
                break;
            default:
                headerColor = new Color(0.25f, 0.60f, 0.52f);
                break;
        }

        SetSubmeshColor(0, slabColor);
        SetSubmeshColor(1, headerColor);
    }

    private void SetSubmeshColor(int submeshIndex, Color col)
    {
        if (meshRenderer == null || submeshIndex >= meshRenderer.sharedMaterials.Length) return;
        meshRenderer.GetPropertyBlock(propBlock, submeshIndex);
        propBlock.SetColor(BaseColorId, col);
        propBlock.SetColor(ColorId, col);
        meshRenderer.SetPropertyBlock(propBlock, submeshIndex);
    }

    private void SetupAnchorsAndLabel(float width, float depth, float height)
    {
        float topY = height + 0.06f;

        buildingAnchor = GetOrCreateChild("BuildingAnchor");
        buildingAnchor.localPosition = new Vector3(0f, topY, depth * 0.28f);
        buildingAnchor.localRotation = Quaternion.identity;

        float pawnZ = (category == TileCategory.Property) ? -depth * 0.28f : -depth * 0.22f;
        float spacingX = width * 0.26f;

        for (int i = 0; i < 3; i++)
        {
            pawnSlots[i] = GetOrCreateChild($"PawnSlot_{i}");
            float offsetX = (i - 1) * spacingX;
            pawnSlots[i].localPosition = new Vector3(offsetX, topY, pawnZ);

            collePrisonSlots[i] = GetOrCreateChild($"ColleSlot_{i}");
            collePrisonSlots[i].localPosition = new Vector3(offsetX * 0.8f, topY + 0.03f, depth * 0.20f);
        }

        Transform labelTr = GetOrCreateChild("TileLabel3D");
        labelTr.localPosition = new Vector3(0f, topY + 0.03f, -depth * 0.04f);
        labelTr.localRotation = Quaternion.Euler(90f, 0f, 0f);

        labelTextMesh = labelTr.GetComponent<TextMesh>();
        if (labelTextMesh == null)
        {
            labelTextMesh = labelTr.gameObject.AddComponent<TextMesh>();
        }

        string formattedName = FormatTileName(tileName);
        labelTextMesh.text = price > 0
            ? $"[{tileIndex}] {formattedName}\n{price} €"
            : $"[{tileIndex}]\n{formattedName}";

        labelTextMesh.fontSize = 36;
        // Ajuste la taille de police pour les noms longs afin d'éviter tout débordement en Niveau 3 (24 cases)
        int maxLineLen = GetLongestLineLength(labelTextMesh.text);
        labelTextMesh.characterSize = maxLineLen > 14 ? 0.034f : (maxLineLen > 11 ? 0.039f : 0.043f);
        labelTextMesh.anchor = TextAnchor.MiddleCenter;
        labelTextMesh.alignment = TextAlignment.Center;
        labelTextMesh.color = new Color(0.10f, 0.11f, 0.14f);
    }

    private static string FormatTileName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        string s = raw
            .Replace(" – ", "\n")
            .Replace(" - ", "\n")
            .Replace(" de ", " de\n")
            .Replace(" du ", " du\n")
            .Replace(" à ", " à\n");

        // Garde au maximum 3 lignes lisibles pour les noms très longs (ex. Incubateur de start-ups)
        string[] words = s.Split(' ');
        if (words.Length >= 2 && !s.Contains("\n") && s.Length > 12)
        {
            int mid = words.Length / 2;
            s = string.Join(" ", words, 0, mid) + "\n" + string.Join(" ", words, mid, words.Length - mid);
        }
        return s;
    }

    private static int GetLongestLineLength(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int max = 0;
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > max) max = lines[i].Length;
        }
        return max;
    }

    private Transform GetOrCreateChild(string childName)
    {
        Transform existing = transform.Find(childName);
        if (existing != null) return existing;

        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    public Vector3 GetStudentPawnPosition(int playerId, bool isLockedInColle = false)
    {
        int clampedId = Mathf.Clamp(playerId, 0, 2);
        if (isLockedInColle && collePrisonSlots[clampedId] != null)
        {
            return collePrisonSlots[clampedId].position;
        }
        return pawnSlots[clampedId] != null ? pawnSlots[clampedId].position : transform.position + Vector3.up * 0.3f;
    }

    private void AddBeveledBox(Vector3 center, float w, float d, float yMin, float yMax, float bevel, List<int> targetTris)
    {
        float hw = w * 0.5f;
        float hd = d * 0.5f;
        float b = Mathf.Min(bevel, hw * 0.3f, hd * 0.3f);
        float cx = center.x;
        float cz = center.z;

        Vector3 bBL = new Vector3(cx - hw, yMin, cz - hd);
        Vector3 bTL = new Vector3(cx - hw, yMin, cz + hd);
        Vector3 bTR = new Vector3(cx + hw, yMin, cz + hd);
        Vector3 bBR = new Vector3(cx + hw, yMin, cz - hd);

        Vector3 tBL = new Vector3(cx - hw + b, yMax, cz - hd + b);
        Vector3 tTL = new Vector3(cx - hw + b, yMax, cz + hd - b);
        Vector3 tTR = new Vector3(cx + hw - b, yMax, cz + hd - b);
        Vector3 tBR = new Vector3(cx + hw - b, yMax, cz - hd + b);

        AddQuad(bBL, tBL, tBR, bBR, targetTris);
        AddQuad(bTR, tTR, tTL, bTL, targetTris);
        AddQuad(bTL, tTL, tBL, bBL, targetTris);
        AddQuad(bBR, tBR, tTR, bTR, targetTris);
        AddQuad(tBL, tTL, tTR, tBR, targetTris);
    }

    private void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, List<int> targetTris)
    {
        int i = vertices.Count;
        vertices.Add(v0); vertices.Add(v1); vertices.Add(v2); vertices.Add(v3);
        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));

        targetTris.Add(i); targetTris.Add(i + 1); targetTris.Add(i + 2);
        targetTris.Add(i); targetTris.Add(i + 2); targetTris.Add(i + 3);
    }
}
