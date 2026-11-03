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

    [Tooltip("Niveau d'amélioration initial affiché sur les bâtiments pour prévisualiser (0 en début de partie)")]
    [Range(0, 3)] public int previewBuildingLevel = 1;

    [Header("Dimensions Géométriques du Plateau")]
    public float regularTileWidth = 2.4f;
    public float tileDepth = 3.2f;
    public float cornerTileSize = 3.2f;
    public float tileHeight = 0.25f;
    public float tileGap = 0.08f;

    [Header("Références Prefabs & Caméra")]
    [Tooltip("Ton Prefab ProceduralBuilding (optionnel : créé par code s'il n'est pas assigné)")]
    public ProceduralBuildingGenerator buildingPrefab;
    [Tooltip("Matériau partagé (Mat_Building)")]
    public Material sharedBoardMaterial;
    [Tooltip("Glisse ici ton CameraTarget pour qu'il se centre automatiquement sur le plateau")]
    public Transform cameraTargetToCenter;

    [Header("Cases Générées (Lecture Seule)")]
    public List<ProceduralTileView> generatedTiles = new List<ProceduralTileView>();

    private Transform tilesContainer;
    private int lastBuiltLevel = -1;
    private int lastPreviewBuildingLevel = -1;

    void Start()
    {
        GenerateCampusBoard(campusLevel);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // En mode Éditeur, permet de changer de Niveau (1, 2 ou 3) en temps réel sans erreur SendMessage
        if (lastBuiltLevel != campusLevel || lastPreviewBuildingLevel != previewBuildingLevel)
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
    /// Génère intégralement le plateau de 16, 20 ou 24 cases selon le niveau choisi (1, 2 ou 3).
    /// </summary>
    [ContextMenu("Regénérer le Plateau Campus Tycoon")]
    public void GenerateCampusBoard(int level)
    {
        campusLevel = Mathf.Clamp(level, 1, 3);
        lastBuiltLevel = campusLevel;
        lastPreviewBuildingLevel = previewBuildingLevel;

        CampusSquareDefinition[] layout = GetLayoutForLevel(campusLevel);
        int totalTiles = layout.Length;         // 16, 20 ou 24
        int tilesPerSide = totalTiles / 4;      // 4, 5 ou 6 cases par côté
        int regularCountPerSide = tilesPerSide - 1;

        ClearExistingBoard();

        // Calcul du rayon entre le centre du plateau (0,0,0) et le milieu d'un côté
        float innerStripLength = regularCountPerSide * regularTileWidth + (regularCountPerSide + 1) * tileGap;
        float halfBoardSpan = (innerStripLength * 0.5f) + (cornerTileSize * 0.5f);

        // 1. Génération de la pelouse centrale du Campus (Cour intérieure)
        BuildCentralCampusPlaza(innerStripLength - tileGap);

        // 2. Génération en anneau des 16, 20 ou 24 cases avec orientation par Quaternions
        for (int i = 0; i < totalTiles; i++)
        {
            int sideIndex = i / tilesPerSide;       // 0 = Bas, 1 = Gauche, 2 = Haut, 3 = Droite
            int indexInSide = i % tilesPerSide;     // 0 = Coin, 1..N = Cases régulières
            bool isCorner = (indexInSide == 0);

            // Position locale sur le côté Sud (de Droite vers Gauche en sens horaire Monopoly)
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

            // Rotation pure par Quaternion autour de l'axe vertical (0°, 90°, 180°, 270°)
            Quaternion sideRotation = Quaternion.AngleAxis(sideIndex * 90f, Vector3.up);
            Vector3 worldPos = transform.position + (sideRotation * unrotatedPos);

            CreateTileAndBuilding(i, layout[i], isCorner, worldPos, sideRotation);
        }

        // 3. Recentrage automatique du CameraTarget au barycentre du plateau
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

        // Si c'est un terrain constructible, on y attache automatiquement ton générateur de bâtiment 3D
        if (def.category == ProceduralTileView.TileCategory.Property)
        {
            ProceduralBuildingGenerator building;
            if (buildingPrefab != null)
            {
                building = Instantiate(buildingPrefab, tileView.buildingAnchor);
                building.name = $"Building_{def.name}";
            }
            else
            {
                GameObject bObj = new GameObject($"Building_{def.name}");
                bObj.transform.SetParent(tileView.buildingAnchor, false);
                building = bObj.AddComponent<ProceduralBuildingGenerator>();
                if (sharedBoardMaterial != null)
                {
                    building.GetComponent<MeshRenderer>().sharedMaterial = sharedBoardMaterial;
                }
            }

            building.transform.localPosition = Vector3.zero;
            building.transform.localRotation = Quaternion.identity;
            building.baseWidth = w * 0.78f;
            building.baseDepth = d * 0.32f;
            building.SetCampusBuildingState(previewBuildingLevel, -1, def.group);

            tileView.buildingInstance = building;
        }

        generatedTiles.Add(tileView);
    }

    /// <summary>
    /// Crée le socle central du campus au milieu de l'anneau de cases.
    /// </summary>
    private void BuildCentralCampusPlaza(float plazaSize)
    {
        GameObject plaza = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plaza.name = "Central_Campus_Plaza";
        plaza.transform.SetParent(tilesContainer, false);
        plaza.transform.localPosition = new Vector3(0f, tileHeight * 0.35f, 0f);
        plaza.transform.localScale = new Vector3(plazaSize, tileHeight * 0.7f, plazaSize);

        MeshRenderer mr = plaza.GetComponent<MeshRenderer>();
        if (sharedBoardMaterial != null)
        {
            mr.sharedMaterial = sharedBoardMaterial;
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            Color lawnColor = new Color(0.24f, 0.52f, 0.32f); // Pelouse du campus
            block.SetColor("_BaseColor", lawnColor);
            block.SetColor("_Color", lawnColor);
            mr.SetPropertyBlock(block);
        }
    }

    private void ClearExistingBoard()
    {
        generatedTiles.Clear();

        Transform existing = transform.Find("Generated_Board_Container");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        GameObject containerObj = new GameObject("Generated_Board_Container");
        containerObj.transform.SetParent(transform, false);
        tilesContainer = containerObj.transform;
    }

    // ==================== API PUBLIQUE POUR LES ÉVÉNEMENTS OBSERVER ====================

    /// <summary>
    /// Met à jour le propriétaire et le niveau (0 à 3) d'un terrain lors de OnPropertyBought / OnPropertyUpgraded.
    /// </summary>
    public void UpdatePropertyVisual(int tileIndex, int ownerId, int buildingLevel)
    {
        if (tileIndex < 0 || tileIndex >= generatedTiles.Count) return;
        ProceduralTileView tile = generatedTiles[tileIndex];
        if (tile != null && tile.buildingInstance != null)
        {
            tile.buildingInstance.SetCampusBuildingState(buildingLevel, ownerId, tile.propertyGroup);
        }
    }

    /// <summary>
    /// Retourne la position 3D cible d'un pion étudiant sur une case donnée.
    /// </summary>
    public Vector3 GetPawnWorldPosition(int tileIndex, int playerId, bool inColle = false)
    {
        if (generatedTiles.Count == 0) return transform.position;
        int wrappedIndex = ((tileIndex % generatedTiles.Count) + generatedTiles.Count) % generatedTiles.Count;
        return generatedTiles[wrappedIndex].GetStudentPawnPosition(playerId, inColle);
    }

    // ==================== TABLEAUX OFFICIELS DES 3 NIVEAUX CAMPUS TYCOON ====================

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
            // NIVEAU 1 : Première semaine (16 cases : 4 coins, 8 terrains, 2 gares, 1 événement, 1 taxe)
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
            // NIVEAU 3 : Semestre en folie (24 cases : + groupe Or 300/320 + 3 événements + Roulette du BDE)
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
            // NIVEAU 2 (Référence) : Mi-semestre (20 cases officielles numérotées de 0 à 19)
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