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

    private Mesh tileMesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propBlock;
    private TextMesh labelTextMesh;

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

        BoxCollider col = GetComponent<BoxCollider>();
        col.size = new Vector3(width, height, depth);
        col.center = new Vector3(0f, height * 0.5f, 0f);
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

        // 1. Socle principal biseauté (Submesh 0)
        AddBeveledBox(Vector3.zero, width, depth, 0f, height, 0.06f, trisBaseSlab);

        // 2. Bandeau supérieur en relief (Submesh 1)
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

        float pawnZ = (category == TileCategory.Property) ? -depth * 0.22f : -depth * 0.15f;
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
        labelTr.localPosition = new Vector3(0f, topY + 0.02f, -depth * 0.05f);
        labelTr.localRotation = Quaternion.Euler(90f, 0f, 0f);

        labelTextMesh = labelTr.GetComponent<TextMesh>();
        if (labelTextMesh == null)
        {
            labelTextMesh = labelTr.gameObject.AddComponent<TextMesh>();
        }

        string formattedName = tileName.Replace(" – ", "\n").Replace(" de ", " de\n");
        labelTextMesh.text = price > 0
            ? $"[{tileIndex}] {formattedName}\n({price}€)"
            : $"[{tileIndex}]\n{formattedName}";

        labelTextMesh.fontSize = 36;
        labelTextMesh.characterSize = 0.045f;
        labelTextMesh.anchor = TextAnchor.MiddleCenter;
        labelTextMesh.alignment = TextAlignment.Center;
        labelTextMesh.color = new Color(0.12f, 0.12f, 0.15f);
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