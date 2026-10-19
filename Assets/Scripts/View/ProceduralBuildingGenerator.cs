using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class ProceduralBuildingGenerator : MonoBehaviour
{
    [Header("Paramètres Monopoly")]
    [Tooltip("0 = Terrain nu, 1 à 4 = Maisons (toit en pente), 5 = Hôtel (Gratte-ciel à retraits)")]
    [Range(0, 5)] public int buildingLevel = 1;

    [Header("Dimensions Géométriques")]
    [Min(0.5f)] public float baseWidth = 2.0f;
    [Min(0.5f)] public float baseDepth = 2.0f;
    [Min(0.05f)] public float foundationHeight = 0.15f;
    [Min(0.3f)] public float floorHeight = 0.8f;
    [Min(0.2f)] public float roofHeight = 0.6f;

    [Header("Options Techniques")]
    [Tooltip("Met à jour le MeshCollider pour les collisions caméra / souris")]
    public bool updateCollider = true;

    // Buffers de construction géométrique
    private Mesh generatedMesh;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<int> triangles = new List<int>();
    private readonly List<Vector2> uvs = new List<Vector2>();

    private MeshFilter meshFilter;
    private MeshCollider meshCollider;

    void Awake()
    {
        RebuildMesh();
    }

    // Appelé automatiquement dans l'éditeur dès qu'on modifie une valeur dans l'Inspector
    void OnValidate()
    {
        RebuildMesh();
    }

    /// <summary>
    /// Méthode publique que le moteur logique ou l'UI pourra appeler en cours de partie.
    /// </summary>
    public void SetBuildingLevel(int level)
    {
        buildingLevel = Mathf.Clamp(level, 0, 5);
        RebuildMesh();
    }

    /// <summary>
    /// Reconstruit intégralement le maillage 3D (Sommets, Triangles, UVs, Normales).
    /// </summary>
    public void RebuildMesh()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();

        if (generatedMesh == null)
        {
            generatedMesh = new Mesh
            {
                name = "ProceduralBuilding_Mesh"
            };
        }
        else
        {
            generatedMesh.Clear();
        }

        vertices.Clear();
        triangles.Clear();
        uvs.Clear();

        // 1. Génération systématique de la dalle de fondation (présente dès le niveau 0)
        AddBoxSection(
            width: baseWidth,
            depth: baseDepth,
            yBottom: 0f,
            yTop: foundationHeight,
            includeTopCap: buildingLevel == 0
        );

        // 2. Génération des étages selon le niveau d'investissement Monopoly
        if (buildingLevel >= 1 && buildingLevel <= 4)
        {
            // Niveaux 1 à 4 : Maison évolutive avec toit à deux pans
            float bodyWidth = baseWidth * 0.85f;
            float bodyDepth = baseDepth * 0.85f;
            float yBottom = foundationHeight;
            float yTop = yBottom + (buildingLevel * floorHeight);

            // Corps du bâtiment (sans face supérieure car recouverte par le toit)
            AddBoxSection(bodyWidth, bodyDepth, yBottom, yTop, includeTopCap: false);

            // Toit prismatique à deux pans
            AddPitchedRoof(bodyWidth * 1.08f, bodyDepth * 1.08f, yTop, yTop + roofHeight);
        }
        else if (buildingLevel == 5)
        {
            // Niveau 5 (Hôtel) : Gratte-ciel à 3 niveaux en retrait (Style Art Déco / Setbacks)
            float currentY = foundationHeight;

            // Section basse (3 étages, 85% de l'emprise)
            float h1 = floorHeight * 2.5f;
            AddBoxSection(baseWidth * 0.85f, baseDepth * 0.85f, currentY, currentY + h1, includeTopCap: true);
            currentY += h1;

            // Section médiane en retrait (2 étages, 62% de l'emprise)
            float h2 = floorHeight * 2.0f;
            AddBoxSection(baseWidth * 0.62f, baseDepth * 0.62f, currentY, currentY + h2, includeTopCap: true);
            currentY += h2;

            // Sommet de la tour (1.5 étage, 40% de l'emprise)
            float h3 = floorHeight * 1.5f;
            AddBoxSection(baseWidth * 0.40f, baseDepth * 0.40f, currentY, currentY + h3, includeTopCap: true);
            currentY += h3;

            // Flèche pyramidale au sommet de l'hôtel
            AddPyramidSpire(baseWidth * 0.22f, baseDepth * 0.22f, currentY, currentY + roofHeight * 1.4f);
        }

        // 3. Assignation des tableaux mathématiques au Mesh Unity
        generatedMesh.SetVertices(vertices);
        generatedMesh.SetTriangles(triangles, 0);
        generatedMesh.SetUVs(0, uvs);

        // 4. Calcul automatique des vecteurs normaux (éclairage par face) et de la boîte englobante
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();

        meshFilter.sharedMesh = generatedMesh;

        // 5. Mise à jour du MeshCollider (uniquement en Play Mode ou hors validation instable)
        if (updateCollider && meshCollider != null && generatedMesh.vertexCount > 0)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = generatedMesh;
        }
    }

    /// <summary>
    /// Ajoute un prisme rectangulaire (4 murs verticaux + toit plat optionnel).
    /// </summary>
    private void AddBoxSection(float width, float depth, float yBottom, float yTop, bool includeTopCap)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;

        // Face Avant (-Z)
        AddQuad(
            new Vector3(-hw, yBottom, -hd),
            new Vector3(-hw, yTop, -hd),
            new Vector3(hw, yTop, -hd),
            new Vector3(hw, yBottom, -hd)
        );

        // Face Arrière (+Z)
        AddQuad(
            new Vector3(hw, yBottom, hd),
            new Vector3(hw, yTop, hd),
            new Vector3(-hw, yTop, hd),
            new Vector3(-hw, yBottom, hd)
        );

        // Face Gauche (-X)
        AddQuad(
            new Vector3(-hw, yBottom, hd),
            new Vector3(-hw, yTop, hd),
            new Vector3(-hw, yTop, -hd),
            new Vector3(-hw, yBottom, -hd)
        );

        // Face Droite (+X)
        AddQuad(
            new Vector3(hw, yBottom, -hd),
            new Vector3(hw, yTop, -hd),
            new Vector3(hw, yTop, hd),
            new Vector3(hw, yBottom, hd)
        );

        // Face Supérieure (+Y)
        if (includeTopCap)
        {
            AddQuad(
                new Vector3(-hw, yTop, -hd),
                new Vector3(-hw, yTop, hd),
                new Vector3(hw, yTop, hd),
                new Vector3(hw, yTop, -hd)
            );
        }
    }

    /// <summary>
    /// Ajoute un toit à deux pans (2 pentes rectangulaires + 2 pignons triangulaires).
    /// </summary>
    private void AddPitchedRoof(float width, float depth, float yBase, float yRidge)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;

        Vector3 ridgeLeft = new Vector3(-hw, yRidge, 0f);
        Vector3 ridgeRight = new Vector3(hw, yRidge, 0f);

        // Pente Avant (-Z)
        AddQuad(
            new Vector3(-hw, yBase, -hd),
            ridgeLeft,
            ridgeRight,
            new Vector3(hw, yBase, -hd)
        );

        // Pente Arrière (+Z)
        AddQuad(
            new Vector3(hw, yBase, hd),
            ridgeRight,
            ridgeLeft,
            new Vector3(-hw, yBase, hd)
        );

        // Pignon Gauche (-X)
        AddTriangle(
            new Vector3(-hw, yBase, hd),
            ridgeLeft,
            new Vector3(-hw, yBase, -hd)
        );

        // Pignon Droit (+X)
        AddTriangle(
            new Vector3(hw, yBase, -hd),
            ridgeRight,
            new Vector3(hw, yBase, hd)
        );
    }

    /// <summary>
    /// Ajoute une flèche pyramidale à 4 faces au sommet d'un gratte-ciel.
    /// </summary>
    private void AddPyramidSpire(float width, float depth, float yBase, float yPeak)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;
        Vector3 peak = new Vector3(0f, yPeak, 0f);

        // Face Avant (-Z)
        AddTriangle(new Vector3(-hw, yBase, -hd), peak, new Vector3(hw, yBase, -hd));
        // Face Arrière (+Z)
        AddTriangle(new Vector3(hw, yBase, hd), peak, new Vector3(-hw, yBase, hd));
        // Face Gauche (-X)
        AddTriangle(new Vector3(-hw, yBase, hd), peak, new Vector3(-hw, yBase, -hd));
        // Face Droite (+X)
        AddTriangle(new Vector3(hw, yBase, -hd), peak, new Vector3(hw, yBase, hd));
    }

    /// <summary>
    /// Génère une face quadrilatérale (2 triangles en sens horaire / Clockwise) avec ses propres sommets.
    /// </summary>
    private void AddQuad(Vector3 bottomLeft, Vector3 topLeft, Vector3 topRight, Vector3 bottomRight)
    {
        int startIndex = vertices.Count;

        vertices.Add(bottomLeft);  // 0
        vertices.Add(topLeft);     // 1
        vertices.Add(topRight);    // 2
        vertices.Add(bottomRight); // 3

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));

        // Triangle 1 (Clockwise : 0 -> 1 -> 2)
        triangles.Add(startIndex + 0);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex + 2);

        // Triangle 2 (Clockwise : 0 -> 2 -> 3)
        triangles.Add(startIndex + 0);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 3);
    }

    /// <summary>
    /// Génère une face triangulaire (sens horaire / Clockwise) avec ses propres sommets.
    /// </summary>
    private void AddTriangle(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        int startIndex = vertices.Count;

        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0.5f, 1f));
        uvs.Add(new Vector2(1f, 0f));

        triangles.Add(startIndex + 0);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex + 2);
    }
}