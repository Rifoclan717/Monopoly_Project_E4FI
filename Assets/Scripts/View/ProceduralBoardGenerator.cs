using System.Collections.Generic;
using UnityEngine;

public class ProceduralBoardGenerator : MonoBehaviour
{
    [System.Serializable]
    public struct CampusSquareDefinition
    {
        public string name;
        public ProceduralTileView.TileCategory category;
        public ProceduralBuildingGenerator.CampusGroup group;
        public int price;

        public CampusSquareDefinition(
            string name,
            ProceduralTileView.TileCategory category,
            int price = 0,
            ProceduralBuildingGenerator.CampusGroup group = ProceduralBuildingGenerator.CampusGroup.Marron)
        {
            this.name = name;
            this.category = category;
            this.price = price;
            this.group = group;
        }
    }

    [Header("Configuration Campus Tycoon (Niveaux 1 à 3)")]
    [Tooltip("1 = Première semaine (16 cases), 2 = Mi-semestre (20 cases), 3 = Semestre en folie (24 cases)")]
    [Range(1, 3)] public int campusLevel = 2;

    [Tooltip("Niveau initial des bâtiments (mets 1, 2 ou 3 pour voir les maisons dès le départ, ou 0 pour terrain nu)")]
    [Range(0, 3)] public int previewBuildingLevel = 1;

    [Tooltip("Si activé, varie les tailles de maisons (Niv 1 à 3) pour donner un vrai look de ville/campus vivant")]
    public bool livelyCampusShowcase = true;

    [Header("Dimensions Géométriques du Plateau")]
    public float regularTileWidth = 2.4f;
    public float tileDepth = 3.2f;
    public float cornerTileSize = 3.2f;
    public float tileHeight = 0.25f;
    public float tileGap = 0.08f;

    [Header("Références Prefabs & Caméra (Auto-gérés si vides)")]
    public ProceduralBuildingGenerator buildingPrefab;
    public Material sharedBoardMaterial;
    public Transform cameraTargetToCenter;

    [Header("Cases Générées (Lecture Seule)")]
    public List<ProceduralTileView> generatedTiles = new List<ProceduralTileView>();

    private Transform tilesContainer;
    private int lastBuiltLevel = -1;
    private int lastPreviewBuildingLevel = -1;
    private bool lastShowcaseState = false;

    void Awake()
    {
        EnsureValidMaterial();
    }

    void Start()
    {
        GenerateCampusBoard(campusLevel);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (lastBuiltLevel != campusLevel || lastPreviewBuildingLevel != previewBuildingLevel || lastShowcaseState != livelyCampusShowcase)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    GenerateCampusBoard(campusLevel);
                }
            };
        }
    }
#endif

    /// <summary>
    /// Crée automatiquement un matériau compatible Unity 6 URP / Standard si aucun n'est assigné (élimine le rose fluo).
    /// </summary>
    public Material EnsureValidMaterial()
    {
        if (sharedBoardMaterial != null) return sharedBoardMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Standard");

        if (shader != null)
        {
            sharedBoardMaterial = new Material(shader)
            {
                name = "Auto_Campus_URP_Mat",
                enableInstancing = true
            };
        }
        return sharedBoardMaterial;
    }

    [ContextMenu("Regénérer le Plateau Campus Tycoon")]
    public void GenerateCampusBoard(int level)
    {
        EnsureValidMaterial();

        campusLevel = Mathf.Clamp(level, 1, 3);
        lastBuiltLevel = campusLevel;
        lastPreviewBuildingLevel = previewBuildingLevel;
        lastShowcaseState = livelyCampusShowcase;

        CampusSquareDefinition[] layout = GetLayoutForLevel(campusLevel);
        int totalTiles = layout.Length;
        int tilesPerSide = totalTiles / 4;
        int regularCountPerSide = tilesPerSide - 1;

        ClearExistingBoard();

        float innerStripLength = regularCountPerSide * regularTileWidth + (regularCountPerSide + 1) * tileGap;
        float halfBoardSpan = (innerStripLength * 0.5f) + (cornerTileSize * 0.5f);

        // 1. Cour centrale du Campus avec pelouse, allées, fontaine et arbres low-poly
        BuildCentralCampusPlaza(innerStripLength - tileGap);

        // 2. Génération en anneau des 16, 20 ou 24 cases
        for (int i = 0; i < totalTiles; i++)
        {
            int sideIndex = i / tilesPerSide;
            int indexInSide = i % tilesPerSide;
            bool isCorner = (indexInSide == 0);

            Vector3 unrotatedPos;
            if (isCorner)
            {
                unrotatedPos = new Vector3(halfBoardSpan, 0f, -halfBoardSpan);
            }
            else
            {
                float firstRegularOffset = halfBoardSpan - (cornerTileSize * 0.5f) - tileGap - (regularTileWidth * 0.5f);
                float step = regularTileWidth + tileGap;
                float xPos = firstRegularOffset - ((indexInSide - 1) * step);
                unrotatedPos = new Vector3(xPos, 0f, -halfBoardSpan);
            }

            Quaternion sideRotation = Quaternion.AngleAxis(sideIndex * 90f, Vector3.up);
            Vector3 worldPos = transform.position + (sideRotation * unrotatedPos);

            CreateTileAndBuilding(i, layout[i], isCorner, worldPos, sideRotation);
        }

        // 3. Recentrage automatique de CameraTarget
        if (cameraTargetToCenter == null)
        {
            OrbitalCamera cam = FindFirstObjectByType<OrbitalCamera>();
            if (cam != null && cam.target != null) cameraTargetToCenter = cam.target;
        }
        if (cameraTargetToCenter != null)
        {
            cameraTargetToCenter.position = transform.position;
        }
    }

    private void CreateTileAndBuilding(
        int index,
        CampusSquareDefinition def,
        bool isCorner,
        Vector3 position,
        Quaternion rotation)
    {
        GameObject tileObj = new GameObject($"Tile_{index:00}_{def.name}");
        tileObj.transform.SetParent(tilesContainer, true);
        tileObj.transform.position = position;
        tileObj.transform.rotation = rotation;

        ProceduralTileView tileView = tileObj.AddComponent<ProceduralTileView>();
        float w = isCorner ? cornerTileSize : regularTileWidth;
        float d = isCorner ? cornerTileSize : tileDepth;

        tileView.InitializeTile(
            index: index,
            nameLabel: def.name,
            cost: def.price,
            cat: def.category,
            group: def.group,
            width: w,
            depth: d,
            height: tileHeight,
            sharedMat: sharedBoardMaterial
        );

        if (def.category == ProceduralTileView.TileCategory.Property)
        {
            GameObject bObj = new GameObject($"Building_{def.name}");
            bObj.transform.SetParent(tileView.buildingAnchor, false);
            bObj.transform.localPosition = Vector3.zero;
            // Orienté vers le centre du plateau
            bObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            MeshRenderer bRenderer = bObj.AddComponent<MeshRenderer>();
            bRenderer.sharedMaterial = sharedBoardMaterial;

            ProceduralBuildingGenerator building = bObj.AddComponent<ProceduralBuildingGenerator>();
            building.baseWidth = w * 0.76f;
            building.baseDepth = d * 0.36f;

            int displayedLevel = previewBuildingLevel;
            int displayedOwner = -1;

            if (livelyCampusShowcase && previewBuildingLevel > 0)
            {
                displayedLevel = 1 + (index % 3); // Alterne Niv 1, 2 et 3 pour un campus très visuel
            }

            building.SetCampusBuildingState(displayedLevel, displayedOwner, def.group);
            tileView.buildingInstance = building;
        }
        else
        {
            // Ajoute un petit élément 3D sur les Gares, Coins et Cases spéciales pour habiller toute la map
            CreateSpecialTileLandmark(tileView, def.category, w, d);
        }

        generatedTiles.Add(tileView);
    }

    /// <summary>
    /// Habillage 3D des cases non-propriétés (Gares RER/Bus, Machine à café, Événements, Coins).
    /// </summary>
    private void CreateSpecialTileLandmark(ProceduralTileView tileView, ProceduralTileView.TileCategory cat, float w, float d)
    {
        if (cat == ProceduralTileView.TileCategory.Station)
        {
            // Abri de quai RER / Bus moderne
            Transform roof = CreateColoredPrimitive(PrimitiveType.Cube, "Station_Canopy", tileView.buildingAnchor,
                new Vector3(0f, 0.42f, 0f), new Vector3(w * 0.65f, 0.08f, d * 0.25f), new Color(0.18f, 0.25f, 0.38f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Pillar_L", roof.parent,
                new Vector3(-w * 0.24f, 0.21f, 0f), new Vector3(0.08f, 0.21f, 0.08f), new Color(0.75f, 0.78f, 0.82f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Pillar_R", roof.parent,
                new Vector3(w * 0.24f, 0.21f, 0f), new Vector3(0.08f, 0.21f, 0.08f), new Color(0.75f, 0.78f, 0.82f));
        }
        else if (cat == ProceduralTileView.TileCategory.EventCard)
        {
            // Borne Événement violette
            CreateColoredPrimitive(PrimitiveType.Cube, "Event_Totem", tileView.buildingAnchor,
                new Vector3(0f, 0.32f, 0f), new Vector3(0.35f, 0.55f, 0.18f), new Color(0.62f, 0.28f, 0.90f));
        }
        else if (cat == ProceduralTileView.TileCategory.Company)
        {
            // Kiosque Machine à Café
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Coffee_Cup", tileView.buildingAnchor,
                new Vector3(0f, 0.28f, 0f), new Vector3(0.38f, 0.28f, 0.38f), new Color(0.48f, 0.28f, 0.14f));
        }
        else if (cat == ProceduralTileView.TileCategory.Special)
        {
            // Roue dorée pour la Roulette du BDE
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Roulette_Wheel", tileView.buildingAnchor,
                new Vector3(0f, 0.15f, 0f), new Vector3(0.65f, 0.08f, 0.65f), new Color(0.95f, 0.75f, 0.12f));
        }
    }

    /// <summary>
    /// Crée un parc central de Campus détaillé (pelouse verte, allées croisées en pierre, fontaine centrale et arbres).
    /// </summary>
    private void BuildCentralCampusPlaza(float plazaSize)
    {
        // Pelouse verte principale
        CreateColoredPrimitive(PrimitiveType.Cube, "Central_Campus_Lawn", tilesContainer,
            new Vector3(0f, tileHeight * 0.35f, 0f),
            new Vector3(plazaSize, tileHeight * 0.7f, plazaSize),
            new Color(0.28f, 0.58f, 0.34f));

        float pathY = tileHeight * 0.72f;

        // Deux allées piétonnes en croix
        CreateColoredPrimitive(PrimitiveType.Cube, "Walkway_NS", tilesContainer,
            new Vector3(0f, pathY, 0f),
            new Vector3(plazaSize * 0.16f, 0.03f, plazaSize * 0.96f),
            new Color(0.85f, 0.83f, 0.78f));

        CreateColoredPrimitive(PrimitiveType.Cube, "Walkway_EW", tilesContainer,
            new Vector3(0f, pathY, 0f),
            new Vector3(plazaSize * 0.96f, 0.03f, plazaSize * 0.16f),
            new Color(0.85f, 0.83f, 0.78f));

        // Fontaine / Place centrale (où atterrissent les dés)
        CreateColoredPrimitive(PrimitiveType.Cylinder, "Central_Plaza_Ring", tilesContainer,
            new Vector3(0f, pathY + 0.02f, 0f),
            new Vector3(plazaSize * 0.34f, 0.04f, plazaSize * 0.34f),
            new Color(0.78f, 0.76f, 0.72f));

        CreateColoredPrimitive(PrimitiveType.Cylinder, "Central_Water_Basin", tilesContainer,
            new Vector3(0f, pathY + 0.04f, 0f),
            new Vector3(plazaSize * 0.26f, 0.04f, plazaSize * 0.26f),
            new Color(0.25f, 0.65f, 0.88f));

        // 4 Bosquets d'arbres low-poly dans les 4 quadrants du campus
        float treeOffset = plazaSize * 0.30f;
        SpawnLowPolyTree(new Vector3(-treeOffset, pathY, -treeOffset), 1.0f);
        SpawnLowPolyTree(new Vector3(treeOffset, pathY, -treeOffset), 1.15f);
        SpawnLowPolyTree(new Vector3(-treeOffset, pathY, treeOffset), 1.1f);
        SpawnLowPolyTree(new Vector3(treeOffset, pathY, treeOffset), 0.95f);
    }

    private void SpawnLowPolyTree(Vector3 basePos, float scale)
    {
        GameObject treeRoot = new GameObject("Campus_Tree");
        treeRoot.transform.SetParent(tilesContainer, false);
        treeRoot.transform.localPosition = basePos;
        treeRoot.transform.localScale = Vector3.one * scale;

        // Tronc
        CreateColoredPrimitive(PrimitiveType.Cylinder, "Trunk", treeRoot.transform,
            new Vector3(0f, 0.25f, 0f), new Vector3(0.16f, 0.25f, 0.16f), new Color(0.45f, 0.28f, 0.15f));

        // Feuillage en 2 sphères superposées
        CreateColoredPrimitive(PrimitiveType.Sphere, "Canopy_Bottom", treeRoot.transform,
            new Vector3(0f, 0.65f, 0f), new Vector3(0.75f, 0.55f, 0.75f), new Color(0.20f, 0.52f, 0.25f));
        CreateColoredPrimitive(PrimitiveType.Sphere, "Canopy_Top", treeRoot.transform,
            new Vector3(0f, 0.98f, 0f), new Vector3(0.52f, 0.45f, 0.52f), new Color(0.26f, 0.62f, 0.30f));
    }

    private Transform CreateColoredPrimitive(PrimitiveType type, string objName, Transform parent, Vector3 localPos, Vector3 localScale, Color col)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = objName;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localScale = localScale;

        Collider c = obj.GetComponent<Collider>();
        if (c != null) c.enabled = false;

        MeshRenderer mr = obj.GetComponent<MeshRenderer>();
        if (sharedBoardMaterial != null) mr.sharedMaterial = sharedBoardMaterial;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        mr.GetPropertyBlock(block);
        block.SetColor("_BaseColor", col);
        block.SetColor("_Color", col);
        mr.SetPropertyBlock(block);

        return obj.transform;
    }

    private void ClearExistingBoard()
    {
        generatedTiles.Clear();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name == "Generated_Board_Container")
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        GameObject containerObj = new GameObject("Generated_Board_Container");
        containerObj.transform.SetParent(transform, false);
        tilesContainer = containerObj.transform;
    }

    public void UpdatePropertyVisual(int tileIndex, int ownerId, int buildingLevel)
    {
        if (tileIndex < 0 || tileIndex >= generatedTiles.Count) return;
        ProceduralTileView tile = generatedTiles[tileIndex];
        if (tile != null && tile.buildingInstance != null)
        {
            tile.buildingInstance.SetCampusBuildingState(buildingLevel, ownerId, tile.propertyGroup);
        }
    }

    public Vector3 GetPawnWorldPosition(int tileIndex, int playerId, bool inColle = false)
    {
        if (generatedTiles.Count == 0) return transform.position;
        int wrappedIndex = ((tileIndex % generatedTiles.Count) + generatedTiles.Count) % generatedTiles.Count;
        return generatedTiles[wrappedIndex].GetStudentPawnPosition(playerId, inColle);
    }

    private CampusSquareDefinition[] GetLayoutForLevel(int level)
    {
        var C = ProceduralTileView.TileCategory.Corner;
        var P = ProceduralTileView.TileCategory.Property;
        var S = ProceduralTileView.TileCategory.Station;
        var E = ProceduralTileView.TileCategory.EventCard;
        var Co = ProceduralTileView.TileCategory.Company;
        var T = ProceduralTileView.TileCategory.Tax;
        var Sp = ProceduralTileView.TileCategory.Special;

        var Marron = ProceduralBuildingGenerator.CampusGroup.Marron;
        var BleuCiel = ProceduralBuildingGenerator.CampusGroup.BleuCiel;
        var Rose = ProceduralBuildingGenerator.CampusGroup.Rose;
        var Orange = ProceduralBuildingGenerator.CampusGroup.Orange;
        var Rouge = ProceduralBuildingGenerator.CampusGroup.Rouge;
        var Or = ProceduralBuildingGenerator.CampusGroup.Or;

        if (level == 1)
        {
            return new CampusSquareDefinition[]
            {
                new CampusSquareDefinition("La Rentrée", C),
                new CampusSquareDefinition("Cafétéria", P, 60, Marron),
                new CampusSquareDefinition("Salle de TP", P, 60, Marron),
                new CampusSquareDefinition("RER A – Noisy-Champs", S, 150),
                new CampusSquareDefinition("Colle", C),
                new CampusSquareDefinition("Bibliothèque", P, 100, BleuCiel),
                new CampusSquareDefinition("Frais de scolarité", T, 100),
                new CampusSquareDefinition("Salle de projet", P, 100, BleuCiel),
                new CampusSquareDefinition("Foyer étudiant", C),
                new CampusSquareDefinition("FabLab", P, 140, Rose),
                new CampusSquareDefinition("Événement", E),
                new CampusSquareDefinition("Salle de robotique", P, 140, Rose),
                new CampusSquareDefinition("Rattrapage", C),
                new CampusSquareDefinition("Bus du campus", S, 150),
                new CampusSquareDefinition("Amphi A", P, 180, Orange),
                new CampusSquareDefinition("Amphi B", P, 180, Orange)
            };
        }
        else if (level == 3)
        {
            return new CampusSquareDefinition[]
            {
                new CampusSquareDefinition("La Rentrée", C),
                new CampusSquareDefinition("Cafétéria", P, 60, Marron),
                new CampusSquareDefinition("Salle de TP", P, 60, Marron),
                new CampusSquareDefinition("Événement", E),
                new CampusSquareDefinition("RER A – Noisy-Champs", S, 150),
                new CampusSquareDefinition("Bibliothèque", P, 100, BleuCiel),
                new CampusSquareDefinition("Colle", C),
                new CampusSquareDefinition("Frais de scolarité", T, 100),
                new CampusSquareDefinition("Salle de projet", P, 100, BleuCiel),
                new CampusSquareDefinition("Machine à café", Co, 120),
                new CampusSquareDefinition("FabLab", P, 140, Rose),
                new CampusSquareDefinition("Salle de robotique", P, 140, Rose),
                new CampusSquareDefinition("Foyer étudiant", C),
                new CampusSquareDefinition("Événement", E),
                new CampusSquareDefinition("Bus du campus", S, 150),
                new CampusSquareDefinition("Amphi A", P, 180, Orange),
                new CampusSquareDefinition("Amphi B", P, 180, Orange),
                new CampusSquareDefinition("Roulette du BDE", Sp, 50),
                new CampusSquareDefinition("Rattrapage", C),
                new CampusSquareDefinition("Bureau du BDE", P, 240, Rouge),
                new CampusSquareDefinition("Événement", E),
                new CampusSquareDefinition("Direction", P, 260, Rouge),
                new CampusSquareDefinition("Incubateur de start-ups", P, 300, Or),
                new CampusSquareDefinition("Laboratoire de recherche", P, 320, Or)
            };
        }
        else
        {
            return new CampusSquareDefinition[]
            {
                new CampusSquareDefinition("La Rentrée", C),
                new CampusSquareDefinition("Cafétéria", P, 60, Marron),
                new CampusSquareDefinition("Salle de TP", P, 60, Marron),
                new CampusSquareDefinition("Événement", E),
                new CampusSquareDefinition("RER A – Noisy-Champs", S, 150),
                new CampusSquareDefinition("Colle", C),
                new CampusSquareDefinition("Bibliothèque", P, 100, BleuCiel),
                new CampusSquareDefinition("Frais de scolarité", T, 100),
                new CampusSquareDefinition("Salle de projet", P, 100, BleuCiel),
                new CampusSquareDefinition("Machine à café", Co, 120),
                new CampusSquareDefinition("Foyer étudiant", C),
                new CampusSquareDefinition("FabLab", P, 140, Rose),
                new CampusSquareDefinition("Événement", E),
                new CampusSquareDefinition("Salle de robotique", P, 140, Rose),
                new CampusSquareDefinition("Bus du campus", S, 150),
                new CampusSquareDefinition("Rattrapage", C),
                new CampusSquareDefinition("Amphi A", P, 180, Orange),
                new CampusSquareDefinition("Amphi B", P, 180, Orange),
                new CampusSquareDefinition("Bureau du BDE", P, 240, Rouge),
                new CampusSquareDefinition("Direction", P, 260, Rouge)
            };
        }
    }
}