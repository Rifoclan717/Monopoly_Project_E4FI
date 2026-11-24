using System;
using System.Reflection;
using CampusTycoon;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Contexte persistant entre rechargements de scène (Rejouer / Niveau suivant / Menu Principal).
/// </summary>
public static class CampusLaunchContext
{
    public static bool AutoStartNextLoad = false;
    public static int SelectedLevelIndex = 1;     // 0 = Niveau 1, 1 = Niveau 2, 2 = Niveau 3
    public static int SelectedCharacterIndex = 0; // 0 = Major, 1 = Fêtard, 2 = Techos
}

/// <summary>
/// Contrôle le démarrage de Scene_Main (Menu Principal → Partie → Fin de partie / Redémarrage propre).
/// Référence explicitement les 3 LevelData, les 3 CharacterData et Mat_Campus_URP pour garantir
/// leur inclusion dans les Builds Standalone (.exe) sans toucher aux scripts Core/Model de Franck.
/// </summary>
[DefaultExecutionOrder(-100)]
public class CampusGameLauncher : MonoBehaviour
{
    [Header("Assets officiels (Référencés ici pour le Build .exe)")]
    public LevelData[] availableLevels = new LevelData[3];
    public CharacterData[] availableCharacters = new CharacterData[3];
    public Material defaultCampusMaterial;

    [Header("Références Scène (Trouvées automatiquement si vides)")]
    public TurnManager turnManager;
    public ProceduralBoardGenerator board;
    public ProceduralDiceRoller diceRoller;
    public CampusHUDController hud;
    public OrbitalCamera orbitalCamera;

    [Header("Comportement au démarrage & Rythme IA")]
    [Tooltip("Affiche le Menu Principal au lancement. Désactiver pour lancer directement la partie en test.")]
    public bool openMainMenuOnStart = true;
    [Tooltip("Temps de réflexion réaliste de l'IA en mode normal (vise 4 à 6 s par tour IA)")]
    [Range(0.4f, 3.0f)] public float normalAIPauseSeconds = 1.55f;

    public bool IsInMainMenu { get; private set; }
    public int SelectedLevelIndex { get; private set; } = 1;
    public int SelectedCharacterIndex { get; private set; } = 0;

    void Awake()
    {
        Time.timeScale = 1f;

        if (turnManager == null) turnManager = FindFirstObjectByType<TurnManager>();
        if (board == null) board = FindFirstObjectByType<ProceduralBoardGenerator>();
        if (diceRoller == null) diceRoller = FindFirstObjectByType<ProceduralDiceRoller>();
        if (hud == null) hud = FindFirstObjectByType<CampusHUDController>();
        if (orbitalCamera == null) orbitalCamera = FindFirstObjectByType<OrbitalCamera>();

        EnsureAssetsLoaded();
        EnsureMaterialsAssigned();

        SelectedLevelIndex = Mathf.Clamp(CampusLaunchContext.SelectedLevelIndex, 0, Mathf.Max(0, availableLevels.Length - 1));
        SelectedCharacterIndex = Mathf.Clamp(CampusLaunchContext.SelectedCharacterIndex, 0, Mathf.Max(0, availableCharacters.Length - 1));

        if (turnManager == null)
        {
            // Mode bac à sable (ex: Scene_3D avec MoverTester) : ne pas bloquer
            IsInMainMenu = false;
            return;
        }

        if (openMainMenuOnStart && !CampusLaunchContext.AutoStartNextLoad)
        {
            // Désactive TurnManager AVANT son Start() pour afficher le Menu Principal sur le plateau 3D vivant
            turnManager.enabled = false;
            IsInMainMenu = true;
        }
        else
        {
            CampusLaunchContext.AutoStartNextLoad = false;
            IsInMainMenu = false;
            ApplyConfigurationToTurnManagerAndBoard(SelectedLevelIndex, SelectedCharacterIndex);
            turnManager.enabled = true;
        }
    }

    void Start()
    {
        if (IsInMainMenu)
        {
            PreviewSelectionOnBoard(SelectedLevelIndex, SelectedCharacterIndex);
            if (orbitalCamera != null)
            {
                orbitalCamera.autoOrbitMenuMode = true;
            }
        }
        else
        {
            if (orbitalCamera != null)
            {
                orbitalCamera.autoOrbitMenuMode = false;
            }
        }
    }

    public void PreviewSelectionOnBoard(int levelIndex, int characterIndex)
    {
        SelectedLevelIndex = Mathf.Clamp(levelIndex, 0, Mathf.Max(0, availableLevels.Length - 1));
        SelectedCharacterIndex = Mathf.Clamp(characterIndex, 0, Mathf.Max(0, availableCharacters.Length - 1));
        CampusLaunchContext.SelectedLevelIndex = SelectedLevelIndex;
        CampusLaunchContext.SelectedCharacterIndex = SelectedCharacterIndex;

        LevelData lvl = GetLevel(SelectedLevelIndex);
        CharacterData[] orderedChars = BuildOrderedCharacters(SelectedCharacterIndex);

        if (board != null)
        {
            if (lvl != null)
            {
                board.levelData = lvl;
                board.GenerateCampusBoard(lvl);
            }
            if (orderedChars != null)
            {
                board.ApplyCharacterVisuals(orderedChars);
            }
            board.ResetAllPropertiesForNewGame();
        }
    }

    /// <summary>
    /// Lance la partie depuis le Menu Principal avec le niveau et le personnage sélectionnés.
    /// </summary>
    public void StartGameFromMenu(int levelIndex, int characterIndex)
    {
        SelectedLevelIndex = Mathf.Clamp(levelIndex, 0, Mathf.Max(0, availableLevels.Length - 1));
        SelectedCharacterIndex = Mathf.Clamp(characterIndex, 0, Mathf.Max(0, availableCharacters.Length - 1));
        CampusLaunchContext.SelectedLevelIndex = SelectedLevelIndex;
        CampusLaunchContext.SelectedCharacterIndex = SelectedCharacterIndex;

        ApplyConfigurationToTurnManagerAndBoard(SelectedLevelIndex, SelectedCharacterIndex);

        IsInMainMenu = false;
        Time.timeScale = 1f;

        if (orbitalCamera != null)
        {
            orbitalCamera.autoOrbitMenuMode = false;
            orbitalCamera.ResetView();
        }

        if (turnManager != null)
        {
            turnManager.enabled = true;
        }

        if (hud != null)
        {
            hud.NotifyGameStartedFromLauncher();
        }
    }

    public void RestartCurrentGame()
    {
        Time.timeScale = 1f;
        CampusLaunchContext.SelectedLevelIndex = SelectedLevelIndex;
        CampusLaunchContext.SelectedCharacterIndex = SelectedCharacterIndex;
        CampusLaunchContext.AutoStartNextLoad = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void LaunchNextLevel()
    {
        Time.timeScale = 1f;
        int next = Mathf.Clamp(SelectedLevelIndex + 1, 0, Mathf.Max(0, availableLevels.Length - 1));
        CampusLaunchContext.SelectedLevelIndex = next;
        CampusLaunchContext.SelectedCharacterIndex = SelectedCharacterIndex;
        CampusLaunchContext.AutoStartNextLoad = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        CampusLaunchContext.AutoStartNextLoad = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitApplication()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public bool HasNextLevel()
    {
        return SelectedLevelIndex + 1 < availableLevels.Length && availableLevels[SelectedLevelIndex + 1] != null;
    }

    public LevelData GetLevel(int index)
    {
        if (availableLevels == null || index < 0 || index >= availableLevels.Length) return null;
        return availableLevels[index];
    }

    public CharacterData GetCharacter(int index)
    {
        if (availableCharacters == null || index < 0 || index >= availableCharacters.Length) return null;
        return availableCharacters[index];
    }

    public CharacterData[] BuildOrderedCharacters(int humanIndex)
    {
        if (availableCharacters == null || availableCharacters.Length < 3) return null;
        for (int i = 0; i < 3; i++)
        {
            if (availableCharacters[i] == null) return null;
        }

        int h = Mathf.Clamp(humanIndex, 0, 2);
        CharacterData[] ordered = new CharacterData[3];
        ordered[0] = availableCharacters[h];
        int slot = 1;
        for (int i = 0; i < 3; i++)
        {
            if (i == h) continue;
            ordered[slot++] = availableCharacters[i];
        }
        return ordered;
    }

    private void ApplyConfigurationToTurnManagerAndBoard(int levelIndex, int characterIndex)
    {
        LevelData lvl = GetLevel(levelIndex);
        CharacterData[] orderedChars = BuildOrderedCharacters(characterIndex);

        if (board != null)
        {
            if (lvl != null)
            {
                board.levelData = lvl;
                board.GenerateCampusBoard(lvl);
            }
            if (orderedChars != null)
            {
                board.ApplyCharacterVisuals(orderedChars);
            }
            board.ResetAllPropertiesForNewGame();
        }

        if (turnManager != null)
        {
            // Configure les champs [SerializeField] de TurnManager avant son Start() sans modifier Core/TurnManager.cs.
            // Quand Franck ajoutera TurnManager.Configure(LevelData, CharacterData[]), on pourra l'appeler directement.
            if (lvl != null) SetPrivateField(turnManager, "level", lvl);
            if (orderedChars != null) SetPrivateField(turnManager, "characters", orderedChars);
            if (board != null) SetPrivateField(turnManager, "board", board);
            if (diceRoller != null) SetPrivateField(turnManager, "dice", diceRoller);
            if (hud != null) SetPrivateField(turnManager, "hud", hud);
            SetPrivateField(turnManager, "aiPause", normalAIPauseSeconds);
        }
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null) return;
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(target, value);
        }
        else
        {
            Debug.LogWarning($"[CampusGameLauncher] Champ '{fieldName}' introuvable sur {target.GetType().Name}.");
        }
    }

    private void EnsureMaterialsAssigned()
    {
#if UNITY_EDITOR
        if (defaultCampusMaterial == null)
        {
            defaultCampusMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Campus_URP.mat");
        }
#endif
        if (defaultCampusMaterial != null)
        {
            if (board != null && board.sharedBoardMaterial == null)
            {
                board.sharedBoardMaterial = defaultCampusMaterial;
            }
            if (diceRoller != null && diceRoller.sharedDiceMaterial == null)
            {
                diceRoller.sharedDiceMaterial = defaultCampusMaterial;
            }
        }
    }

    private void EnsureAssetsLoaded()
    {
#if UNITY_EDITOR
        string[] levelPaths =
        {
            "Assets/Data/Levels/LVL_1_PremiereSemaine.asset",
            "Assets/Data/Levels/LVL_2_MiSemestre.asset",
            "Assets/Data/Levels/LVL_3_SemestreEnFolie.asset"
        };
        if (availableLevels == null || availableLevels.Length != 3) availableLevels = new LevelData[3];
        for (int i = 0; i < 3; i++)
        {
            if (availableLevels[i] == null)
            {
                availableLevels[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(levelPaths[i]);
            }
        }

        string[] charPaths =
        {
            "Assets/Data/Characters/CHAR_Major.asset",
            "Assets/Data/Characters/CHAR_Fetard.asset",
            "Assets/Data/Characters/CHAR_Techos.asset"
        };
        if (availableCharacters == null || availableCharacters.Length != 3) availableCharacters = new CharacterData[3];
        for (int i = 0; i < 3; i++)
        {
            if (availableCharacters[i] == null)
            {
                availableCharacters[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterData>(charPaths[i]);
            }
        }
#endif
    }
}

