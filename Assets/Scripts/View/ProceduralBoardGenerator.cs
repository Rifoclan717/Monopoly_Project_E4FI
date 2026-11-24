using System.Collections;
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
        public SquareType squareType;

        public CampusSquareDefinition(
            string name,
            ProceduralTileView.TileCategory category,
            int price = 0,
            ProceduralBuildingGenerator.CampusGroup group = ProceduralBuildingGenerator.CampusGroup.Marron,
            SquareType squareType = SquareType.Property)
        {
            this.name = name;
            this.category = category;
            this.price = price;
            this.group = group;
            this.squareType = squareType;
        }
    }

    [Header("Source Unique des Données (Modèle LevelData - Décision 2)")]
    [Tooltip("Asset LevelData du niveau (Assets/Data/Levels/LVL_1..3). Unique source de vérité des cases.")]
    public LevelData levelData;

    [Header("Configuration Campus Tycoon (Sélecteur Rapide Niveaux 1 à 3)")]
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

    [Header("Références Prefabs & Caméra (Mat_Campus_URP recommandé pour Build .exe)")]
    public ProceduralBuildingGenerator buildingPrefab;
    public Material sharedBoardMaterial;
    public Transform cameraTargetToCenter;

    [Header("Pions des 3 Étudiants (0 = Humain, 1 = IA 1, 2 = IA 2)")]
    public PawnSplineMover[] studentPawns = new PawnSplineMover[3];

    public static readonly Color[] StudentPawnColors = new Color[]
    {
        new Color(0.18f, 0.52f, 0.95f), // 0 : Défaut Bleu
        new Color(0.90f, 0.22f, 0.25f), // 1 : Défaut Rouge
        new Color(0.20f, 0.78f, 0.42f)  // 2 : Défaut Vert
    };

    [Header("Cases Générées (Lecture Seule)")]
    public List<ProceduralTileView> generatedTiles = new List<ProceduralTileView>();

    private Transform tilesContainer;
    private int lastBuiltLevel = -1;
    private int lastPreviewBuildingLevel = -1;
    private bool lastShowcaseState = false;
    private LevelData lastLevelData = null;

    // Palette active des 3 joueurs (synchronisée avec CharacterData.TokenColor dès qu'une partie démarre)
    private readonly Color[] activePlayerColors = new Color[3]
    {
        new Color(0.18f, 0.52f, 0.95f),
        new Color(0.90f, 0.22f, 0.25f),
        new Color(0.20f, 0.78f, 0.42f)
    };
    private readonly CharacterId[] activeCharacterIds = new CharacterId[3]
    {
        CharacterId.Major,
        CharacterId.Partygoer,
        CharacterId.Techie
    };

    // Suivi interne de l'état visuel des cases pour les réactions aux événements Observer
    private readonly List<int> tileOwners = new List<int>();
    private readonly List<int> tileBuildingLevels = new List<int>();

    // Ambiance lumineuse dynamique selon l'événement du semestre
    private Light mainDirectionalLight;
    private Color defaultLightColor = new Color(1f, 0.96f, 0.86f);
    private float defaultLightIntensity = 1.05f;
    private Color targetLightColor = new Color(1f, 0.96f, 0.86f);
    private float targetLightIntensity = 1.05f;
    private TurnManager cachedTurnManager;
    private bool characterVisualsSyncedFromState = false;

    void Awake()
    {
        EnsureValidMaterial();
        CacheMainLight();
    }

    void OnEnable()
    {
        CampusEvents.OnPropertyBought += HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded += HandlePropertyUpgraded;
        CampusEvents.OnPlayerBankrupt += HandlePlayerBankrupt;
        CampusEvents.OnMoneyChanged += HandleMoneyChanged;
        CampusEvents.OnDetentionChanged += HandleDetentionChanged;
        CampusEvents.OnGlobalEventStarted += HandleGlobalEventStarted;
    }

    void OnDisable()
    {
        CampusEvents.OnPropertyBought -= HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded -= HandlePropertyUpgraded;
        CampusEvents.OnPlayerBankrupt -= HandlePlayerBankrupt;
        CampusEvents.OnMoneyChanged -= HandleMoneyChanged;
        CampusEvents.OnDetentionChanged -= HandleDetentionChanged;
        CampusEvents.OnGlobalEventStarted -= HandleGlobalEventStarted;
    }

    void Start()
    {
        previewBuildingLevel = 0;
        livelyCampusShowcase = false;

        if (levelData == null)
        {
            levelData = ResolveLevelDataAsset(campusLevel);
        }

        if (levelData != null && levelData.SquareCount > 0)
        {
            GenerateCampusBoard(levelData);
        }

        EnsureThreeStudentPawns();
        TrySyncCharactersFromTurnManager();
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        if (!characterVisualsSyncedFromState)
        {
            TrySyncCharactersFromTurnManager();
        }

        // Restaure automatiquement l'éclairage neutre quand l'événement du semestre se termine
        if (cachedTurnManager != null && cachedTurnManager.State != null && cachedTurnManager.State.ActiveEvent == null)
        {
            targetLightColor = defaultLightColor;
            targetLightIntensity = defaultLightIntensity;
        }

        if (mainDirectionalLight != null)
        {
            float t = 1f - Mathf.Exp(-3f * Time.deltaTime);
            mainDirectionalLight.color = Color.Lerp(mainDirectionalLight.color, targetLightColor, t);
            mainDirectionalLight.intensity = Mathf.Lerp(mainDirectionalLight.intensity, targetLightIntensity, t);
        }
    }

    private void CacheMainLight()
    {
        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type == LightType.Directional)
            {
                mainDirectionalLight = lights[i];
                defaultLightColor = mainDirectionalLight.color;
                defaultLightIntensity = mainDirectionalLight.intensity;
                targetLightColor = defaultLightColor;
                targetLightIntensity = defaultLightIntensity;
                break;
            }
        }
    }

    private void TrySyncCharactersFromTurnManager()
    {
        if (cachedTurnManager == null) cachedTurnManager = FindFirstObjectByType<TurnManager>();
        if (cachedTurnManager == null || cachedTurnManager.State == null) return;

        List<CharacterData> chars = new List<CharacterData>(3);
        for (int i = 0; i < cachedTurnManager.State.Players.Count && i < 3; i++)
        {
            chars.Add(cachedTurnManager.State.Players[i].Character);
        }

        if (chars.Count == 3)
        {
            ApplyCharacterVisuals(chars);
            characterVisualsSyncedFromState = true;
        }
    }

    /// <summary>
    /// Renvoie la couleur officielle d'un joueur (synchronisée entre pions 3D, bâtiments, gares et HUD).
    /// </summary>
    public Color GetPlayerColor(int playerId)
    {
        if (playerId >= 0 && playerId < activePlayerColors.Length)
        {
            return activePlayerColors[playerId];
        }
        return Color.gray;
    }

    /// <summary>
    /// Synchronise les couleurs et accessoires 3D des 3 pions + bâtiments avec les CharacterData choisis.
    /// </summary>
    public void ApplyCharacterVisuals(IList<CharacterData> characters)
    {
        if (characters == null || characters.Count == 0) return;

        for (int i = 0; i < 3 && i < characters.Count; i++)
        {
            if (characters[i] == null) continue;
            activeCharacterIds[i] = characters[i].Id;
            Color col = characters[i].TokenColor;
            activePlayerColors[i] = (col.a > 0.1f && (col.r + col.g + col.b) > 0.1f) ? col : StudentPawnColors[i];
        }

        // Propage la palette sur tous les bâtiments existants
        for (int i = 0; i < generatedTiles.Count; i++)
        {
            ProceduralTileView tile = generatedTiles[i];
            if (tile == null) continue;
            if (tile.buildingInstance != null)
            {
                tile.buildingInstance.ApplyPlayerPalette(activePlayerColors);
            }
            if (i < tileOwners.Count && tileOwners[i] >= 0)
            {
                tile.SetSpecialOwnerVisual(tileOwners[i], GetPlayerColor(tileOwners[i]));
            }
        }

        EnsureThreeStudentPawns();
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Campus Tycoon/Vue 3D/Installer le Plateau 3D dans la Scène")]
    public static void SetupBoardInCurrentScene()
    {
        string[] oldTestObjects = { "Plane", "Cube", "Cube (1)", "ProceduralBuilding" };
        foreach (string oldName in oldTestObjects)
        {
            GameObject oldObj = GameObject.Find(oldName);
            if (oldObj != null && oldObj.transform.parent == null)
            {
                oldObj.SetActive(false);
            }
        }

        ProceduralBoardGenerator board = FindFirstObjectByType<ProceduralBoardGenerator>();
        if (board == null)
        {
            GameObject boardGo = new GameObject("CampusBoard_3D");
            board = boardGo.AddComponent<ProceduralBoardGenerator>();
            boardGo.AddComponent<MoverTester>();
        }
        else if (board.GetComponent<MoverTester>() == null &&
                 FindFirstObjectByType<MoverTester>() == null &&
                 FindFirstObjectByType<TurnManager>() == null)
        {
            board.gameObject.AddComponent<MoverTester>();
        }

        if (board.levelData == null)
        {
            board.levelData = ResolveLevelDataAsset(board.campusLevel);
        }
        board.GenerateCampusBoard(board.levelData);
        board.EnsureThreeStudentPawns();
        UnityEditor.Selection.activeGameObject = board.gameObject;
    }

    void OnValidate()
    {
        if (lastBuiltLevel != campusLevel ||
            lastPreviewBuildingLevel != previewBuildingLevel ||
            lastShowcaseState != livelyCampusShowcase ||
            lastLevelData != levelData)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;

                if (lastBuiltLevel != campusLevel && (levelData == null || levelData.LevelNumber != campusLevel))
                {
                    LevelData resolved = ResolveLevelDataAsset(campusLevel);
                    if (resolved != null) levelData = resolved;
                }
                else if (levelData == null)
                {
                    levelData = ResolveLevelDataAsset(campusLevel);
                }

                if (levelData != null && levelData.SquareCount > 0)
                {
                    GenerateCampusBoard(levelData);
                }
            };
        }
    }
#endif

    public static LevelData ResolveLevelDataAsset(int targetLevel)
    {
        int clamped = Mathf.Clamp(targetLevel, 1, 3);
#if UNITY_EDITOR
        string[] paths =
        {
            "Assets/Data/Levels/LVL_1_PremiereSemaine.asset",
            "Assets/Data/Levels/LVL_2_MiSemestre.asset",
            "Assets/Data/Levels/LVL_3_SemestreEnFolie.asset"
        };
        LevelData asset = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(paths[clamped - 1]);
        if (asset != null) return asset;
#endif
        LevelData[] loaded = Resources.FindObjectsOfTypeAll<LevelData>();
        foreach (LevelData ld in loaded)
        {
            if (ld != null && ld.LevelNumber == clamped && ld.SquareCount > 0) return ld;
        }
        return loaded.Length > 0 ? loaded[0] : null;
    }

    // ==================== RÉACTIONS AUX ÉVÉNEMENTS OBSERVER ====================

    private void HandlePropertyBought(int playerId, int tileIndex, int price)
    {
        UpdatePropertyVisual(tileIndex, playerId, 0);
        if (Application.isPlaying && tileIndex >= 0 && tileIndex < generatedTiles.Count)
        {
            SpawnFloatingText(generatedTiles[tileIndex].transform.position + Vector3.up * 1.15f, "ACHETÉ !", GetPlayerColor(playerId));
        }
    }

    private void HandlePropertyUpgraded(int playerId, int tileIndex, int newLevel)
    {
        int prevLevel = (tileIndex >= 0 && tileIndex < tileBuildingLevels.Count) ? tileBuildingLevels[tileIndex] : 0;
        UpdatePropertyVisual(tileIndex, playerId, newLevel);

        if (Application.isPlaying && tileIndex >= 0 && tileIndex < generatedTiles.Count)
        {
            if (newLevel > prevLevel)
            {
                SpawnFloatingText(generatedTiles[tileIndex].transform.position + Vector3.up * 1.55f, $"NIV. {newLevel} !", new Color(1f, 0.85f, 0.2f));
            }
            else if (newLevel < prevLevel)
            {
                SpawnFloatingText(generatedTiles[tileIndex].transform.position + Vector3.up * 1.35f, "VENDU -1 NIV.", new Color(0.95f, 0.45f, 0.25f));
            }
        }
    }

    private void HandlePlayerBankrupt(int playerId, int creditorId)
    {
        for (int i = 0; i < generatedTiles.Count; i++)
        {
            if (i < tileOwners.Count && tileOwners[i] == playerId)
            {
                UpdatePropertyVisual(i, -1, 0);
            }
        }

        // Masque le pion 3D de l'étudiant éliminé (fix P1-6)
        if (studentPawns != null && playerId >= 0 && playerId < studentPawns.Length && studentPawns[playerId] != null)
        {
            if (Application.isPlaying)
            {
                SpawnFloatingText(studentPawns[playerId].transform.position + Vector3.up * 1.2f, "FAILLITE !", new Color(0.95f, 0.22f, 0.22f));
            }
            studentPawns[playerId].gameObject.SetActive(false);
        }
    }

    private void HandleMoneyChanged(int playerId, int newMoney, int delta)
    {
        if (!Application.isPlaying || delta == 0) return;
        if (studentPawns != null && playerId >= 0 && playerId < studentPawns.Length &&
            studentPawns[playerId] != null && studentPawns[playerId].gameObject.activeInHierarchy)
        {
            string label = delta > 0 ? $"+{delta} €" : $"{delta} €";
            Color col = delta > 0 ? new Color(0.22f, 0.90f, 0.45f) : new Color(0.98f, 0.35f, 0.32f);
            SpawnFloatingText(studentPawns[playerId].transform.position + Vector3.up * 1.05f, label, col);
        }
    }

    private void HandleDetentionChanged(int playerId, bool inDetention)
    {
        if (!Application.isPlaying || !inDetention) return;
        if (studentPawns != null && playerId >= 0 && playerId < studentPawns.Length && studentPawns[playerId] != null)
        {
            SpawnFloatingText(studentPawns[playerId].transform.position + Vector3.up * 1.3f, "EN COLLE !", new Color(0.95f, 0.32f, 0.22f));
        }
    }

    private void HandleGlobalEventStarted(string title, string effect)
    {
        if (!Application.isPlaying || mainDirectionalLight == null) return;
        string low = (title ?? string.Empty).ToLowerInvariant();
        if (low.Contains("grève") || low.Contains("rer"))
        {
            targetLightColor = new Color(0.76f, 0.84f, 0.96f);
            targetLightIntensity = 0.85f;
        }
        else if (low.Contains("portes ouvertes") || low.Contains("forum"))
        {
            targetLightColor = new Color(1.0f, 0.92f, 0.72f);
            targetLightIntensity = 1.22f;
        }
        else if (low.Contains("soldes"))
        {
            targetLightColor = new Color(0.92f, 1.0f, 0.88f);
            targetLightIntensity = 1.15f;
        }
    }

    /// <summary>
    /// Fait tourner visuellement la roue 3D sur la case Roulette du BDE (Niveau 3).
    /// </summary>
    public void SpinRouletteWheelVisual(float duration = 1.3f)
    {
        if (!Application.isPlaying) return;
        for (int i = 0; i < generatedTiles.Count; i++)
        {
            if (generatedTiles[i] != null && generatedTiles[i].rouletteWheelTransform != null)
            {
                StartCoroutine(SpinWheelCoroutine(generatedTiles[i].rouletteWheelTransform, duration));
            }
        }
    }

    private IEnumerator SpinWheelCoroutine(Transform wheel, float duration)
    {
        float elapsed = 0f;
        float speed = 900f;
        while (elapsed < duration && wheel != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.1f, duration));
            float curSpeed = speed * (1f - t) * (1f - t);
            wheel.Rotate(Vector3.up, curSpeed * Time.deltaTime, Space.Self);
            yield return null;
        }
    }

    public Material EnsureValidMaterial()
    {
        if (sharedBoardMaterial != null) return sharedBoardMaterial;

#if UNITY_EDITOR
        Material projectMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Campus_URP.mat");
        if (projectMat != null)
        {
            sharedBoardMaterial = projectMat;
            return sharedBoardMaterial;
        }
#endif

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

    public static CampusSquareDefinition FromSquareData(SquareData sq)
    {
        if (sq == null)
        {
            return new CampusSquareDefinition("Case vide", ProceduralTileView.TileCategory.Corner, 0, ProceduralBuildingGenerator.CampusGroup.Marron, SquareType.Start);
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

        return new CampusSquareDefinition(sq.DisplayName, cat, displayedCost, grp, sq.Type);
    }

    // ==================== GÉNÉRATION DU PLATEAU (SOURCE UNIQUE : LevelData) ====================

    public void GenerateCampusBoard(LevelData data)
    {
        if (data == null)
        {
            data = levelData != null ? levelData : ResolveLevelDataAsset(campusLevel);
        }
        if (data == null || data.SquareCount == 0)
        {
            Debug.LogWarning("[ProceduralBoardGenerator] Aucun LevelData assigné ou trouvé dans Assets/Data/Levels.");
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
        campusLevel = Mathf.Clamp(level, 1, 3);
        if (levelData == null || levelData.LevelNumber != campusLevel)
        {
            LevelData resolved = ResolveLevelDataAsset(campusLevel);
            if (resolved != null) levelData = resolved;
        }
        GenerateCampusBoard(levelData);
    }

    private void BuildBoardFromLayout(CampusSquareDefinition[] layout)
    {
        int totalTiles = layout.Length;
        int tilesPerSide = totalTiles / 4;
        int regularCountPerSide = tilesPerSide - 1;

        ClearExistingBoard();

        float innerStripLength = regularCountPerSide * regularTileWidth + (regularCountPerSide + 1) * tileGap;
        float halfBoardSpan = (innerStripLength * 0.5f) + (cornerTileSize * 0.5f);
        float outerBoardSize = (halfBoardSpan + cornerTileSize * 0.5f) * 2f;

        // 0. Esplanade / Socle de table autour du plateau (supprime l'impression de plateau flottant dans le vide)
        BuildSurroundingCampusBase(outerBoardSize);

        // 1. Cour centrale du Campus (pelouse, allées, fontaine, bancs, lampadaires et bosquets)
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

        // 3. Recentrage et cadrage automatique de la caméra orbitale selon la taille du plateau
        OrbitalCamera cam = FindFirstObjectByType<OrbitalCamera>();
        if (cameraTargetToCenter == null && cam != null && cam.target != null)
        {
            cameraTargetToCenter = cam.target;
        }
        if (cameraTargetToCenter != null)
        {
            cameraTargetToCenter.position = transform.position;
        }
        if (cam != null)
        {
            cam.AdaptToBoardSize(totalTiles);
        }

        // 4. Repositionnement des 3 pions sur la case 0
        SnapAllPawnsToStart();
    }

    // ==================== GESTION DES 3 PIONS ÉTUDIANTS (COULEURS + ACCESSOIRES) ====================

    [ContextMenu("Créer / Replacer les 3 Pions Étudiants")]
    public void EnsureThreeStudentPawns()
    {
        EnsureValidMaterial();

        if (studentPawns == null || studentPawns.Length != 3)
        {
            studentPawns = new PawnSplineMover[3];
        }

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
            Color col = activePlayerColors[i];
            if (studentPawns[i] == null)
            {
                studentPawns[i] = CreateProceduralStudentPawn(i, pawnNames[i], col, activeCharacterIds[i]);
            }
            else
            {
                studentPawns[i].studentId = i;
                studentPawns[i].gameObject.name = pawnNames[i];
                ApplyPawnColor(studentPawns[i].gameObject, col);
                AttachCharacterAccessory(studentPawns[i].transform, activeCharacterIds[i], col);
            }

            if (studentPawns[i].gameObject.activeSelf)
            {
                studentPawns[i].SnapToTile(this, studentPawns[i].currentTileIndex, studentPawns[i].isLockedInColle);
            }
        }
    }

    public PawnSplineMover GetStudentPawn(int playerId)
    {
        int clamped = Mathf.Clamp(playerId, 0, 2);
        if (studentPawns == null || studentPawns.Length <= clamped || studentPawns[clamped] == null)
        {
            EnsureThreeStudentPawns();
        }
        return studentPawns[clamped];
    }

    public void SetAllPawnsSpeedMultiplier(float multiplier)
    {
        if (studentPawns == null) return;
        float clamped = Mathf.Clamp(multiplier, 0.5f, 10f);
        for (int i = 0; i < studentPawns.Length; i++)
        {
            if (studentPawns[i] != null)
            {
                studentPawns[i].speedMultiplier = clamped;
            }
        }
    }

    public void SnapAllPawnsToStart()
    {
        if (generatedTiles.Count == 0 || studentPawns == null) return;
        for (int i = 0; i < studentPawns.Length; i++)
        {
            if (studentPawns[i] != null)
            {
                studentPawns[i].gameObject.SetActive(true);
                studentPawns[i].studentId = i;
                studentPawns[i].SnapToTile(this, 0, false);
            }
        }
    }

    private PawnSplineMover CreateProceduralStudentPawn(int studentId, string objName, Color col, CharacterId charId)
    {
        GameObject pawnRoot = new GameObject(objName);
        pawnRoot.transform.SetParent(transform, true);

        CreateColoredPrimitive(PrimitiveType.Cylinder, "Pawn_Base", pawnRoot.transform,
            new Vector3(0f, 0.06f, 0f), new Vector3(0.38f, 0.06f, 0.38f), col);

        CreateColoredPrimitive(PrimitiveType.Cylinder, "Pawn_Body", pawnRoot.transform,
            new Vector3(0f, 0.25f, 0f), new Vector3(0.25f, 0.17f, 0.25f), col);

        CreateColoredPrimitive(PrimitiveType.Sphere, "Pawn_Head", pawnRoot.transform,
            new Vector3(0f, 0.50f, 0f), new Vector3(0.27f, 0.27f, 0.27f), col);

        AttachCharacterAccessory(pawnRoot.transform, charId, col);

        PawnSplineMover mover = pawnRoot.AddComponent<PawnSplineMover>();
        mover.studentId = studentId;
        return mover;
    }

    /// <summary>
    /// Ajoute un accessoire distinctif sur le pion selon l'archétype (Toque Major, Casquette Fêtard, Casque Techos).
    /// </summary>
    private void AttachCharacterAccessory(Transform pawnRoot, CharacterId charId, Color accentCol)
    {
        Transform existing = pawnRoot.Find("CharacterAccessory");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        GameObject acc = new GameObject("CharacterAccessory");
        acc.transform.SetParent(pawnRoot, false);

        if (charId == CharacterId.Major)
        {
            // Toque de diplômé (mortier carré noir + pompon doré)
            Transform board = CreateColoredPrimitive(PrimitiveType.Cube, "Mortarboard", acc.transform,
                new Vector3(0f, 0.65f, 0f), new Vector3(0.34f, 0.04f, 0.34f), new Color(0.14f, 0.15f, 0.20f));
            board.localRotation = Quaternion.Euler(0f, 45f, 0f);
            CreateColoredPrimitive(PrimitiveType.Sphere, "Tassel", acc.transform,
                new Vector3(0.16f, 0.62f, 0f), new Vector3(0.08f, 0.08f, 0.08f), new Color(0.98f, 0.80f, 0.16f));
        }
        else if (charId == CharacterId.Partygoer)
        {
            // Lunettes de soirée stylées + visière festive
            CreateColoredPrimitive(PrimitiveType.Cube, "PartyShades", acc.transform,
                new Vector3(0f, 0.52f, 0.12f), new Vector3(0.24f, 0.07f, 0.07f), new Color(0.12f, 0.12f, 0.16f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "PartyCap", acc.transform,
                new Vector3(0f, 0.63f, -0.02f), new Vector3(0.24f, 0.05f, 0.24f), new Color(0.98f, 0.78f, 0.18f));
        }
        else
        {
            // Casque audio du Techos (arceau + deux écouteurs)
            CreateColoredPrimitive(PrimitiveType.Cube, "Headband", acc.transform,
                new Vector3(0f, 0.64f, 0f), new Vector3(0.30f, 0.05f, 0.07f), new Color(0.16f, 0.18f, 0.22f));
            CreateColoredPrimitive(PrimitiveType.Sphere, "EarCup_L", acc.transform,
                new Vector3(-0.15f, 0.51f, 0f), new Vector3(0.08f, 0.12f, 0.12f), new Color(0.22f, 0.90f, 0.75f));
            CreateColoredPrimitive(PrimitiveType.Sphere, "EarCup_R", acc.transform,
                new Vector3(0.15f, 0.51f, 0f), new Vector3(0.08f, 0.12f, 0.12f), new Color(0.22f, 0.90f, 0.75f));
        }
    }

    private void ApplyPawnColor(GameObject pawnObj, Color col)
    {
        Transform acc = pawnObj.transform.Find("CharacterAccessory");
        MeshRenderer[] renderers = pawnObj.GetComponentsInChildren<MeshRenderer>(true);
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (MeshRenderer mr in renderers)
        {
            if (acc != null && mr.transform.IsChildOf(acc)) continue;
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
            bObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            MeshRenderer bRenderer = bObj.AddComponent<MeshRenderer>();
            bRenderer.sharedMaterial = sharedBoardMaterial;

            ProceduralBuildingGenerator building = bObj.AddComponent<ProceduralBuildingGenerator>();
            building.baseWidth = w * 0.76f;
            building.baseDepth = d * 0.36f;
            building.ApplyPlayerPalette(activePlayerColors);

            if (livelyCampusShowcase && previewBuildingLevel > 0)
            {
                displayedLevel = 1 + (index % 3);
            }

            building.SetCampusBuildingState(displayedLevel, displayedOwner, def.group);
            tileView.buildingInstance = building;
        }
        else
        {
            CreateSpecialTileLandmark(tileView, def, w, d);
            // Fix P0-4 : Ajoute un indicateur visuel de propriétaire sur les Gares et la Machine à café
            if (def.category == ProceduralTileView.TileCategory.Station ||
                def.category == ProceduralTileView.TileCategory.Company)
            {
                tileView.SetupSpecialOwnerMarker(w, d, sharedBoardMaterial);
            }
        }

        generatedTiles.Add(tileView);
        tileOwners.Add(displayedOwner);
        tileBuildingLevels.Add(displayedLevel);
    }

    private void CreateSpecialTileLandmark(ProceduralTileView tileView, CampusSquareDefinition def, float w, float d)
    {
        if (def.category == ProceduralTileView.TileCategory.Station)
        {
            // Abri de quai RER / Bus moderne
            Transform roof = CreateColoredPrimitive(PrimitiveType.Cube, "Station_Canopy", tileView.buildingAnchor,
                new Vector3(0f, 0.44f, 0f), new Vector3(w * 0.65f, 0.08f, d * 0.26f), new Color(0.18f, 0.25f, 0.38f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Pillar_L", roof.parent,
                new Vector3(-w * 0.24f, 0.22f, 0f), new Vector3(0.08f, 0.22f, 0.08f), new Color(0.75f, 0.78f, 0.82f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Pillar_R", roof.parent,
                new Vector3(w * 0.24f, 0.22f, 0f), new Vector3(0.08f, 0.22f, 0.08f), new Color(0.75f, 0.78f, 0.82f));
        }
        else if (def.category == ProceduralTileView.TileCategory.EventCard)
        {
            // Borne Événement violette + bandeau doré
            CreateColoredPrimitive(PrimitiveType.Cube, "Event_Totem", tileView.buildingAnchor,
                new Vector3(0f, 0.32f, 0f), new Vector3(0.38f, 0.56f, 0.20f), new Color(0.58f, 0.26f, 0.88f));
            CreateColoredPrimitive(PrimitiveType.Cube, "Event_Cap", tileView.buildingAnchor,
                new Vector3(0f, 0.62f, 0f), new Vector3(0.44f, 0.08f, 0.24f), new Color(0.95f, 0.78f, 0.20f));
        }
        else if (def.category == ProceduralTileView.TileCategory.Company)
        {
            // Kiosque Machine à Café (distributeur + tasse fumante)
            CreateColoredPrimitive(PrimitiveType.Cube, "Coffee_Machine", tileView.buildingAnchor,
                new Vector3(-0.18f, 0.34f, 0.02f), new Vector3(0.42f, 0.64f, 0.32f), new Color(0.32f, 0.22f, 0.16f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Coffee_Cup", tileView.buildingAnchor,
                new Vector3(0.24f, 0.20f, -0.02f), new Vector3(0.28f, 0.20f, 0.28f), new Color(0.92f, 0.88f, 0.82f));
        }
        else if (def.category == ProceduralTileView.TileCategory.Tax)
        {
            // Pupitre Frais de scolarité
            CreateColoredPrimitive(PrimitiveType.Cube, "Tax_Desk", tileView.buildingAnchor,
                new Vector3(0f, 0.26f, 0f), new Vector3(0.55f, 0.48f, 0.32f), new Color(0.74f, 0.18f, 0.22f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Tax_Coin", tileView.buildingAnchor,
                new Vector3(0f, 0.54f, 0f), new Vector3(0.30f, 0.05f, 0.30f), new Color(0.95f, 0.78f, 0.15f));
        }
        else if (def.category == ProceduralTileView.TileCategory.Special)
        {
            // Roulette du BDE : socle + roue tournante + pointeur
            GameObject wheelRoot = new GameObject("Roulette_Wheel_Root");
            wheelRoot.transform.SetParent(tileView.buildingAnchor, false);
            wheelRoot.transform.localPosition = new Vector3(0f, 0.16f, 0f);

            CreateColoredPrimitive(PrimitiveType.Cylinder, "Wheel_Rim", wheelRoot.transform,
                Vector3.zero, new Vector3(0.86f, 0.07f, 0.86f), new Color(0.95f, 0.75f, 0.12f));
            CreateColoredPrimitive(PrimitiveType.Cube, "Wheel_Bar_A", wheelRoot.transform,
                new Vector3(0f, 0.08f, 0f), new Vector3(0.78f, 0.04f, 0.10f), new Color(0.85f, 0.18f, 0.20f));
            CreateColoredPrimitive(PrimitiveType.Cube, "Wheel_Bar_B", wheelRoot.transform,
                new Vector3(0f, 0.08f, 0f), new Vector3(0.10f, 0.04f, 0.78f), new Color(0.18f, 0.72f, 0.40f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Wheel_Hub", wheelRoot.transform,
                new Vector3(0f, 0.13f, 0f), new Vector3(0.20f, 0.08f, 0.20f), new Color(0.98f, 0.94f, 0.82f));

            tileView.rouletteWheelTransform = wheelRoot.transform;
        }
        else if (def.category == ProceduralTileView.TileCategory.Corner)
        {
            BuildCornerLandmark(tileView, def.squareType, w, d);
        }
    }

    private void BuildCornerLandmark(ProceduralTileView tileView, SquareType sqType, float w, float d)
    {
        if (sqType == SquareType.Start)
        {
            // Arche d'entrée universitaire "La Rentrée"
            CreateColoredPrimitive(PrimitiveType.Cube, "Start_Pillar_L", tileView.buildingAnchor,
                new Vector3(-w * 0.28f, 0.36f, 0.18f), new Vector3(0.16f, 0.72f, 0.16f), new Color(0.90f, 0.88f, 0.82f));
            CreateColoredPrimitive(PrimitiveType.Cube, "Start_Pillar_R", tileView.buildingAnchor,
                new Vector3(w * 0.28f, 0.36f, 0.18f), new Vector3(0.16f, 0.72f, 0.16f), new Color(0.90f, 0.88f, 0.82f));
            CreateColoredPrimitive(PrimitiveType.Cube, "Start_Arch_Top", tileView.buildingAnchor,
                new Vector3(0f, 0.74f, 0.18f), new Vector3(w * 0.68f, 0.14f, 0.20f), new Color(0.16f, 0.56f, 0.48f));
        }
        else if (sqType == SquareType.Detention)
        {
            // Grille symbolique de la salle de Colle à l'arrière de la case
            for (int b = -2; b <= 2; b++)
            {
                CreateColoredPrimitive(PrimitiveType.Cylinder, $"Colle_Bar_{b}", tileView.buildingAnchor,
                    new Vector3(b * 0.22f, 0.32f, 0.32f), new Vector3(0.05f, 0.32f, 0.05f), new Color(0.28f, 0.30f, 0.35f));
            }
            CreateColoredPrimitive(PrimitiveType.Cube, "Colle_Header", tileView.buildingAnchor,
                new Vector3(0f, 0.64f, 0.32f), new Vector3(1.10f, 0.08f, 0.10f), new Color(0.28f, 0.30f, 0.35f));
        }
        else if (sqType == SquareType.StudentLounge)
        {
            // Parasol et table conviviale du Foyer étudiant
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Lounge_Table", tileView.buildingAnchor,
                new Vector3(0f, 0.16f, 0.18f), new Vector3(0.60f, 0.06f, 0.60f), new Color(0.84f, 0.68f, 0.46f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Lounge_Pole", tileView.buildingAnchor,
                new Vector3(0f, 0.38f, 0.18f), new Vector3(0.06f, 0.38f, 0.06f), new Color(0.75f, 0.75f, 0.78f));
            CreateColoredPrimitive(PrimitiveType.Cylinder, "Lounge_Umbrella", tileView.buildingAnchor,
                new Vector3(0f, 0.72f, 0.18f), new Vector3(0.85f, 0.08f, 0.85f), new Color(0.95f, 0.62f, 0.18f));
        }
        else if (sqType == SquareType.GoToDetention)
        {
            // Balise d'alerte Rattrapage
            CreateColoredPrimitive(PrimitiveType.Cube, "Alert_Base", tileView.buildingAnchor,
                new Vector3(0f, 0.18f, 0.20f), new Vector3(0.42f, 0.36f, 0.42f), new Color(0.22f, 0.24f, 0.28f));
            CreateColoredPrimitive(PrimitiveType.Sphere, "Alert_Beacon", tileView.buildingAnchor,
                new Vector3(0f, 0.46f, 0.20f), new Vector3(0.32f, 0.32f, 0.32f), new Color(0.92f, 0.18f, 0.18f));
        }
    }

    private void BuildSurroundingCampusBase(float outerBoardSize)
    {
        // Cadre de plateau élégant en bois/pierre sombre sous les cases
        CreateColoredPrimitive(PrimitiveType.Cube, "Board_Frame_Rim", tilesContainer,
            new Vector3(0f, -0.14f, 0f),
            new Vector3(outerBoardSize + 1.4f, 0.28f, outerBoardSize + 1.4f),
            new Color(0.22f, 0.25f, 0.31f));

        // Grande esplanade environnante pour habiller le fond de scène
        CreateColoredPrimitive(PrimitiveType.Cube, "Campus_Surrounding_Ground", tilesContainer,
            new Vector3(0f, -0.40f, 0f),
            new Vector3(outerBoardSize + 28f, 0.24f, outerBoardSize + 28f),
            new Color(0.18f, 0.34f, 0.26f));
    }

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

        // Place centrale (zone claire où atterrissent les dés : surface plane lisible)
        CreateColoredPrimitive(PrimitiveType.Cylinder, "Central_Plaza_Ring", tilesContainer,
            new Vector3(0f, pathY + 0.02f, 0f),
            new Vector3(plazaSize * 0.36f, 0.04f, plazaSize * 0.36f),
            new Color(0.78f, 0.76f, 0.72f));

        CreateColoredPrimitive(PrimitiveType.Cylinder, "Central_Water_Basin", tilesContainer,
            new Vector3(0f, pathY + 0.04f, 0f),
            new Vector3(plazaSize * 0.28f, 0.04f, plazaSize * 0.28f),
            new Color(0.24f, 0.54f, 0.76f));

        // 4 Bosquets d'arbres low-poly + 4 bancs de campus dans les quadrants
        float treeOffset = plazaSize * 0.31f;
        SpawnLowPolyTree(new Vector3(-treeOffset, pathY, -treeOffset), 1.0f);
        SpawnLowPolyTree(new Vector3(treeOffset, pathY, -treeOffset), 1.15f);
        SpawnLowPolyTree(new Vector3(-treeOffset, pathY, treeOffset), 1.1f);
        SpawnLowPolyTree(new Vector3(treeOffset, pathY, treeOffset), 0.95f);

        float benchOffset = plazaSize * 0.23f;
        SpawnCampusBench(new Vector3(-benchOffset, pathY, 0f), 90f);
        SpawnCampusBench(new Vector3(benchOffset, pathY, 0f), -90f);
        SpawnCampusBench(new Vector3(0f, pathY, -benchOffset), 0f);
        SpawnCampusBench(new Vector3(0f, pathY, benchOffset), 180f);
    }

    private void SpawnCampusBench(Vector3 basePos, float yawDegrees)
    {
        GameObject bench = new GameObject("Campus_Bench");
        bench.transform.SetParent(tilesContainer, false);
        bench.transform.localPosition = basePos;
        bench.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

        CreateColoredPrimitive(PrimitiveType.Cube, "Seat", bench.transform,
            new Vector3(0f, 0.14f, 0f), new Vector3(0.68f, 0.06f, 0.22f), new Color(0.54f, 0.34f, 0.18f));
        CreateColoredPrimitive(PrimitiveType.Cube, "Backrest", bench.transform,
            new Vector3(0f, 0.26f, -0.09f), new Vector3(0.68f, 0.16f, 0.05f), new Color(0.54f, 0.34f, 0.18f));
    }

    private void SpawnLowPolyTree(Vector3 basePos, float scale)
    {
        GameObject treeRoot = new GameObject("Campus_Tree");
        treeRoot.transform.SetParent(tilesContainer, false);
        treeRoot.transform.localPosition = basePos;
        treeRoot.transform.localScale = Vector3.one * scale;

        CreateColoredPrimitive(PrimitiveType.Cylinder, "Trunk", treeRoot.transform,
            new Vector3(0f, 0.25f, 0f), new Vector3(0.16f, 0.25f, 0.16f), new Color(0.45f, 0.28f, 0.15f));

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

    // ==================== TEXTES FLOTTANTS 3D (JUICE VISUEL) ====================

    public void SpawnFloatingText(Vector3 worldPos, string text, Color color)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(text)) return;
        GameObject go = new GameObject("FloatingFeedbackText");
        go.transform.position = worldPos;
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 44;
        tm.characterSize = 0.09f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontStyle = FontStyle.Bold;
        tm.color = color;
        StartCoroutine(AnimateFloatingText(go, tm));
    }

    private IEnumerator AnimateFloatingText(GameObject go, TextMesh tm)
    {
        float duration = 1.25f;
        float elapsed = 0f;
        Vector3 startPos = go.transform.position;
        Color baseColor = tm.color;

        while (elapsed < duration && go != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            go.transform.position = startPos + Vector3.up * (1.25f * t);

            Camera cam = Camera.main;
            if (cam != null)
            {
                go.transform.rotation = cam.transform.rotation;
            }

            Color c = baseColor;
            c.a = 1f - (t * t);
            tm.color = c;
            yield return null;
        }

        if (go != null) Destroy(go);
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
    /// Met à jour l'affichage d'un terrain, d'une Gare ou de la Machine à café (-1..2 et niveau 0..3).
    /// </summary>
    public void UpdatePropertyVisual(int tileIndex, int ownerId, int buildingLevel)
    {
        if (tileIndex < 0 || tileIndex >= generatedTiles.Count) return;

        int prevOwner = tileIndex < tileOwners.Count ? tileOwners[tileIndex] : -1;
        int prevLevel = tileIndex < tileBuildingLevels.Count ? tileBuildingLevels[tileIndex] : 0;

        if (tileIndex < tileOwners.Count) tileOwners[tileIndex] = ownerId;
        if (tileIndex < tileBuildingLevels.Count) tileBuildingLevels[tileIndex] = buildingLevel;

        ProceduralTileView tile = generatedTiles[tileIndex];
        if (tile == null) return;

        if (tile.buildingInstance != null)
        {
            bool shouldPop = (ownerId >= 0 && prevOwner < 0) || (buildingLevel > prevLevel);
            tile.buildingInstance.ApplyPlayerPalette(activePlayerColors);
            tile.buildingInstance.SetCampusBuildingState(buildingLevel, ownerId, tile.propertyGroup, shouldPop);
        }
        else if (tile.category == ProceduralTileView.TileCategory.Station ||
                 tile.category == ProceduralTileView.TileCategory.Company)
        {
            tile.SetSpecialOwnerVisual(ownerId, GetPlayerColor(ownerId));
        }
    }

    public void ResetAllPropertiesForNewGame()
    {
        previewBuildingLevel = 0;
        livelyCampusShowcase = false;
        characterVisualsSyncedFromState = false;
        targetLightColor = defaultLightColor;
        targetLightIntensity = defaultLightIntensity;

        for (int i = 0; i < generatedTiles.Count; i++)
        {
            UpdatePropertyVisual(i, -1, 0);
            if (generatedTiles[i] != null) generatedTiles[i].SetHighlighted(false);
        }
        SnapAllPawnsToStart();
    }

    public Vector3 GetPawnWorldPosition(int tileIndex, int playerId, bool inColle = false)
    {
        if (generatedTiles.Count == 0) return transform.position;
        int wrappedIndex = ((tileIndex % generatedTiles.Count) + generatedTiles.Count) % generatedTiles.Count;
        return generatedTiles[wrappedIndex].GetStudentPawnPosition(playerId, inColle);
    }
}
