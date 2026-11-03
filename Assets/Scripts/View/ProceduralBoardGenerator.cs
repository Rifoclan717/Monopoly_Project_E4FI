using System.Collections.Generic;
using CampusTycoon;
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

    [Header("Source Unique des Données (Modèle LevelData)")]
    [Tooltip("Asset LevelData du niveau. S'il est renseigné, le plateau lit ses SquareData comme unique source.")]
    public LevelData levelData;

    [Header("Configuration Campus Tycoon (Secours tant que LevelData est vide)")]
    [Tooltip("1 = Première semaine (16 cases), 2 = Mi-semestre (20 cases), 3 = Semestre en folie (24 cases)")]
    [Range(1, 3)] public int campusLevel = 2;

    [Tooltip("Niveau initial des bâtiments (0 pour une vraie partie : terrains nus au départ)")]
    [Range(0, 3)] public int previewBuildingLevel = 0;

    [Tooltip("Si activé en mode aperçu (previewBuildingLevel > 0), varie les tailles de maisons (Niv 1 à 3)")]
    public bool livelyCampusShowcase = false;

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

    [Header("Pions des 3 Étudiants (0 = Bleu Humain, 1 = Rouge IA1, 2 = Vert IA2)")]
    public PawnSplineMover[] studentPawns = new PawnSplineMover[3];

    public static readonly Color[] StudentPawnColors = new Color[]
    {
        new Color(0.18f, 0.52f, 0.95f), // 0 : Joueur Humain (Bleu)
        new Color(0.90f, 0.22f, 0.25f), // 1 : Rival IA 1 (Rouge)
        new Color(0.20f, 0.78f, 0.42f)  // 2 : Rival IA 2 (Vert)
    };

    [Header("Cases Générées (Lecture Seule)")]
    public List<ProceduralTileView> generatedTiles = new List<ProceduralTileView>();

    private Transform tilesContainer;
    private int lastBuiltLevel = -1;
    private int lastPreviewBuildingLevel = -1;
    private bool lastShowcaseState = false;
    private LevelData lastLevelData = null;

    // Suivi interne de l'état visuel des cases pour la mise à jour par événements (Tâche 5)
    private readonly List<int> tileOwners = new List<int>();
    private readonly List<int> tileBuildingLevels = new List<int>();

    void Awake()
    {
        EnsureValidMaterial();
    }

    void OnEnable()
    {
        // Tâche 5 : Vue pilotée par les événements de CampusEvents
        CampusEvents.OnPropertyBought += HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded += HandlePropertyUpgraded;
        CampusEvents.OnPlayerBankrupt += HandlePlayerBankrupt;
    }

    void OnDisable()
    {
        CampusEvents.OnPropertyBought -= HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded -= HandlePropertyUpgraded;
        CampusEvents.OnPlayerBankrupt -= HandlePlayerBankrupt;
    }

    void Start()
    {
        // Tâche 3 : dès qu'une vraie partie démarre en Play Mode, les terrains commencent au niveau 0
        previewBuildingLevel = 0;
        livelyCampusShowcase = false;

        if (levelData != null && levelData.SquareCount > 0)
        {
            GenerateCampusBoard(levelData);
        }
        else
        {
            GenerateCampusBoard(campusLevel);
        }

        EnsureThreeStudentPawns();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (lastBuiltLevel != campusLevel ||
            lastPreviewBuildingLevel != previewBuildingLevel ||
            lastShowcaseState != livelyCampusShowcase ||
            lastLevelData != levelData)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    if (levelData != null && levelData.SquareCount > 0)
                        GenerateCampusBoard(levelData);
                    else
                        GenerateCampusBoard(campusLevel);
                }
            };
        }
    }
#endif

    // ==================== RÉACTIONS AUX ÉVÉNEMENTS (TÂCHE 5) ====================

    private void HandlePropertyBought(int playerId, int tileIndex, int price)
    {
        // Lors d'un achat, le terrain est acquis au niveau 0 (terrain nu avec bordure/borne propriétaire)
        UpdatePropertyVisual(tileIndex, playerId, 0);
    }

    private void HandlePropertyUpgraded(int playerId, int tileIndex, int newLevel)
    {
        UpdatePropertyVisual(tileIndex, playerId, newLevel);
    }

    private void HandlePlayerBankrupt(int playerId, int creditorId)
    {
        for (int i = 0; i < generatedTiles.Count; i++)
        {
            if (i < tileOwners.Count && tileOwners[i] == playerId)
            {
                if (creditorId >= 0)
                {
                    int currentLvl = (i < tileBuildingLevels.Count) ? tileBuildingLevels[i] : 0;
                    UpdatePropertyVisual(i, creditorId, currentLvl);
                }
                else
                {
                    UpdatePropertyVisual(i, -1, 0);
                }
            }
        }
    }

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

    // ==================== CONVERSIONS MODÈLE (LevelData / SquareData) -> VUE 3D ====================

    /// <summary>
    /// Tâche 2 : Convertit le groupe de couleur du modèle (ColorGroup) vers celui de la vue 3D (CampusGroup).
    /// Les 6 groupes sont dans le même ordre.
    /// </summary>
    public static ProceduralBuildingGenerator.CampusGroup ToCampusGroup(ColorGroup group)
    {
        switch (group)
        {
            case ColorGroup.Brown:     return ProceduralBuildingGenerator.CampusGroup.Marron;
            case ColorGroup.LightBlue: return ProceduralBuildingGenerator.CampusGroup.BleuCiel;
            case ColorGroup.Pink:      return ProceduralBuildingGenerator.CampusGroup.Rose;
            case ColorGroup.Orange:    return ProceduralBuildingGenerator.CampusGroup.Orange;
            case ColorGroup.Red:       return ProceduralBuildingGenerator.CampusGroup.Rouge;
            case ColorGroup.Gold:      return ProceduralBuildingGenerator.CampusGroup.Or;
            default:                   return ProceduralBuildingGenerator.CampusGroup.Marron;
        }
    }

    /// <summary>
    /// Tâche 2 : Convertit le type de case du modèle (SquareType) vers la catégorie visuelle (TileCategory).
    /// Start, Detention, StudentLounge et GoToDetention donnent Corner ; Utility donne Company ; Roulette donne Special.
    /// </summary>
    public static ProceduralTileView.TileCategory ToTileCategory(SquareType type)
    {
        switch (type)
        {
            case SquareType.Start:
            case SquareType.Detention:
            case SquareType.StudentLounge:
            case SquareType.GoToDetention:
                return ProceduralTileView.TileCategory.Corner;
            case SquareType.Property:
                return ProceduralTileView.TileCategory.Property;
            case SquareType.Station:
                return ProceduralTileView.TileCategory.Station;
            case SquareType.Utility:
                return ProceduralTileView.TileCategory.Company;
            case SquareType.Tax:
                return ProceduralTileView.TileCategory.Tax;
            case SquareType.EventCard:
                return ProceduralTileView.TileCategory.EventCard;
            case SquareType.Roulette:
                return ProceduralTileView.TileCategory.Special;
            default:
                return ProceduralTileView.TileCategory.Corner;
        }
    }

    /// <summary>
    /// Convertit un asset SquareData en définition visuelle CampusSquareDefinition.
    /// </summary>
    public static CampusSquareDefinition FromSquareData(SquareData sq)
    {
        if (sq == null)
        {
            return new CampusSquareDefinition("Case vide", ProceduralTileView.TileCategory.Corner);
        }

        ProceduralTileView.TileCategory cat = ToTileCategory(sq.Type);
        ProceduralBuildingGenerator.CampusGroup grp = ToCampusGroup(sq.Group);

        int displayedCost = 0;
        if (sq.IsOwnable)
        {
            displayedCost = sq.Price;
        }
        else if (sq.Type == SquareType.Tax || sq.Type == SquareType.Roulette)
        {
            displayedCost = sq.Amount;
        }

        return new CampusSquareDefinition(sq.DisplayName, cat, displayedCost, grp);
    }

    // ==================== GÉNÉRATION DU PLATEAU ====================

    /// <summary>
    /// Tâche 2 & 3 : Génère le plateau 3D directement depuis un asset LevelData.
    /// Force previewBuildingLevel = 0 en partie réelle pour que tous les terrains démarrent nus.
    /// </summary>
    public void GenerateCampusBoard(LevelData data)
    {
        if (data == null || data.SquareCount == 0)
        {
            GenerateCampusBoard(campusLevel);
            return;
        }

        EnsureValidMaterial();

        levelData = data;
        lastLevelData = data;
        campusLevel = Mathf.Clamp(data.LevelNumber, 1, 3);
        lastBuiltLevel = campusLevel;

        if (Application.isPlaying)
        {
            previewBuildingLevel = 0;
            livelyCampusShowcase = false;
        }
        lastPreviewBuildingLevel = previewBuildingLevel;
        lastShowcaseState = livelyCampusShowcase;

        CampusSquareDefinition[] layout = new CampusSquareDefinition[data.SquareCount];
        for (int i = 0; i < data.SquareCount; i++)
        {
            layout[i] = FromSquareData(data.Squares[i]);
        }

        BuildBoardFromLayout(layout);
    }

    [ContextMenu("Regénérer le Plateau Campus Tycoon")]
    public void GenerateCampusBoard(int level)
    {
        EnsureValidMaterial();

        campusLevel = Mathf.Clamp(level, 1, 3);
        lastBuiltLevel = campusLevel;
        lastPreviewBuildingLevel = previewBuildingLevel;
        lastShowcaseState = livelyCampusShowcase;
        lastLevelData = levelData;

        // Si un LevelData correspondant est assigné, on l'utilise en priorité
        if (levelData != null && levelData.SquareCount > 0)
        {
            CampusSquareDefinition[] dataLayout = new CampusSquareDefinition[levelData.SquareCount];
            for (int i = 0; i < levelData.SquareCount; i++)
            {
                dataLayout[i] = FromSquareData(levelData.Squares[i]);
            }
            BuildBoardFromLayout(dataLayout);
            return;
        }

        // Secours tant que les assets LevelData de Franck ne sont pas sur main
        CampusSquareDefinition[] fallbackLayout = GetLayoutForLevel(campusLevel);
        BuildBoardFromLayout(fallbackLayout);
    }

    private void BuildBoardFromLayout(CampusSquareDefinition[] layout)
    {
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

        // 4. Repositionnement des 3 pions sur la case 0 si présents
        SnapAllPawnsToStart();
    }

    // ==================== GESTION DES 3 PIONS ÉTUDIANTS (TÂCHE 4) ====================

    /// <summary>
    /// Tâche 4 : Garantit l'existence des 3 pions étudiants (0 = Bleu Humain, 1 = Rouge IA 1, 2 = Vert IA 2)
    /// et les place sur les 3 emplacements (PawnSlot_0..2) de la case 0 (La Rentrée).
    /// </summary>
    [ContextMenu("Créer / Replacer les 3 Pions Étudiants")]
    public void EnsureThreeStudentPawns()
    {
        EnsureValidMaterial();

        if (studentPawns == null || studentPawns.Length != 3)
        {
            studentPawns = new PawnSplineMover[3];
        }

        // Récupère les pions déjà présents dans la scène s'ils ne sont pas encore référencés
        PawnSplineMover[] existingInScene = FindObjectsByType<PawnSplineMover>(FindObjectsSortMode.None);
        foreach (PawnSplineMover p in existingInScene)
        {
            int id = Mathf.Clamp(p.studentId, 0, 2);
            if (studentPawns[id] == null)
            {
                studentPawns[id] = p;
            }
        }

        string[] pawnNames = { "StudentPawn_0_Player", "StudentPawn_1_AI1", "StudentPawn_2_AI2" };

        for (int i = 0; i < 3; i++)
        {
            if (studentPawns[i] == null)
            {
                studentPawns[i] = CreateProceduralStudentPawn(i, pawnNames[i]);
            }
            else
            {
                studentPawns[i].studentId = i;
                studentPawns[i].gameObject.name = pawnNames[i];
                ApplyPawnColor(studentPawns[i].gameObject, StudentPawnColors[i]);
            }

            studentPawns[i].SnapToTile(this, 0, false);
        }
    }

    /// <summary>
    /// Renvoie le pion de l'étudiant demandé (0 = Humain, 1 = IA 1, 2 = IA 2).
    /// </summary>
    public PawnSplineMover GetStudentPawn(int playerId)
    {
        int clamped = Mathf.Clamp(playerId, 0, 2);
        if (studentPawns == null || studentPawns.Length <= clamped || studentPawns[clamped] == null)
        {
            EnsureThreeStudentPawns();
        }
        return studentPawns[clamped];
    }

    /// <summary>
    /// Replace les pions existants sur les 3 emplacements de la case 0.
    /// </summary>
    public void SnapAllPawnsToStart()
    {
        if (generatedTiles.Count == 0 || studentPawns == null) return;
        for (int i = 0; i < studentPawns.Length; i++)
        {
            if (studentPawns[i] != null)
            {
                studentPawns[i].studentId = i;
                studentPawns[i].SnapToTile(this, 0, false);
            }
        }
    }

    private PawnSplineMover CreateProceduralStudentPawn(int studentId, string objName)
    {
        GameObject pawnRoot = new GameObject(objName);
        pawnRoot.transform.SetParent(transform, true);

        // Corps du pion (cylindre/capsule dont la base repose exactement à y = 0)
        CreateColoredPrimitive(
            PrimitiveType.Cylinder,
            "Pawn_Base",
            pawnRoot.transform,
            new Vector3(0f, 0.06f, 0f),
            new Vector3(0.36f, 0.06f, 0.36f),
            StudentPawnColors[studentId]);

        CreateColoredPrimitive(
            PrimitiveType.Cylinder,
            "Pawn_Body",
            pawnRoot.transform,
            new Vector3(0f, 0.24f, 0f),
            new Vector3(0.24f, 0.16f, 0.24f),
            StudentPawnColors[studentId]);

        CreateColoredPrimitive(
            PrimitiveType.Sphere,
            "Pawn_Head",
            pawnRoot.transform,
            new Vector3(0f, 0.48f, 0f),
            new Vector3(0.26f, 0.26f, 0.26f),
            StudentPawnColors[studentId]);

        PawnSplineMover mover = pawnRoot.AddComponent<PawnSplineMover>();
        mover.studentId = studentId;
        return mover;
    }

    private void ApplyPawnColor(GameObject pawnObj, Color col)
    {
        MeshRenderer[] renderers = pawnObj.GetComponentsInChildren<MeshRenderer>(true);
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (MeshRenderer mr in renderers)
        {
            if (sharedBoardMaterial != null) mr.sharedMaterial = sharedBoardMaterial;
            mr.GetPropertyBlock(block);
            block.SetColor("_BaseColor", col);
            block.SetColor("_Color", col);
            mr.SetPropertyBlock(block);
        }
    }

    // ==================== CONSTRUCTION GÉOMÉTRIQUE DES CASES & DÉCOR ====================

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

        int displayedLevel = previewBuildingLevel;
        int displayedOwner = -1;

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

            if (livelyCampusShowcase && previewBuildingLevel > 0)
            {
                displayedLevel = 1 + (index % 3); // Alterne Niv 1, 2 et 3 uniquement en mode aperçu vitrine
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
        tileOwners.Add(displayedOwner);
        tileBuildingLevels.Add(displayedLevel);
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
        tileOwners.Clear();
        tileBuildingLevels.Clear();

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

    /// <summary>
    /// Met à jour l'affichage d'un terrain (propriétaire -1..2 et niveau d'amélioration 0..3).
    /// Au niveau 0 avec ownerId >= 0, affiche la bordure et la borne aux couleurs du propriétaire.
    /// </summary>
    public void UpdatePropertyVisual(int tileIndex, int ownerId, int buildingLevel)
    {
        if (tileIndex < 0 || tileIndex >= generatedTiles.Count) return;

        if (tileIndex < tileOwners.Count) tileOwners[tileIndex] = ownerId;
        if (tileIndex < tileBuildingLevels.Count) tileBuildingLevels[tileIndex] = buildingLevel;

        ProceduralTileView tile = generatedTiles[tileIndex];
        if (tile != null && tile.buildingInstance != null)
        {
            tile.buildingInstance.SetCampusBuildingState(buildingLevel, ownerId, tile.propertyGroup);
        }
    }

    /// <summary>
    /// Réinitialise tous les bâtiments du plateau à l'état non-possédé (ownerId = -1, niveau = 0).
    /// </summary>
    public void ResetAllPropertiesForNewGame()
    {
        previewBuildingLevel = 0;
        livelyCampusShowcase = false;
        for (int i = 0; i < generatedTiles.Count; i++)
        {
            UpdatePropertyVisual(i, -1, 0);
        }
        SnapAllPawnsToStart();
    }

    public Vector3 GetPawnWorldPosition(int tileIndex, int playerId, bool inColle = false)
    {
        if (generatedTiles.Count == 0) return transform.position;
        int wrappedIndex = ((tileIndex % generatedTiles.Count) + generatedTiles.Count) % generatedTiles.Count;
        return generatedTiles[wrappedIndex].GetStudentPawnPosition(playerId, inColle);
    }

    /// <summary>
    /// Disposition de secours (utilisée uniquement tant que les assets LevelData de Franck ne sont pas assignés).
    /// </summary>
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
