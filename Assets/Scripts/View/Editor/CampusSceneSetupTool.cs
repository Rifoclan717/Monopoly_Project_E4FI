#if UNITY_EDITOR
using System.Collections.Generic;
using CampusTycoon;
using CampusTycoon.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CampusTycoon.View.EditorTools
{
    /// <summary>
    /// Outil d'installation et de validation rapide pour la Vue 3D / Scene_Main (Scope Mohamed).
    /// Garantit le branchement de tous les assets pour le Build Standalone (.exe).
    /// </summary>
    public static class CampusSceneSetupTool
    {
        [MenuItem("Campus Tycoon/Vue 3D/Configurer Scene_Main (Rendu Final)")]
        public static void ConfigureMainScene()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Campus_URP.mat");
            if (mat == null)
            {
                Debug.LogError("[CampusSceneSetup] Mat_Campus_URP.mat introuvable dans Assets/Materials/ !");
                return;
            }

            LevelData lvl1 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Data/Levels/LVL_1_PremiereSemaine.asset");
            LevelData lvl2 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Data/Levels/LVL_2_MiSemestre.asset");
            LevelData lvl3 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Data/Levels/LVL_3_SemestreEnFolie.asset");

            CharacterData major = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/Data/Characters/CHAR_Major.asset");
            CharacterData fetard = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/Data/Characters/CHAR_Fetard.asset");
            CharacterData techos = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/Data/Characters/CHAR_Techos.asset");

            ProceduralBoardGenerator board = Object.FindFirstObjectByType<ProceduralBoardGenerator>();
            ProceduralDiceRoller dice = Object.FindFirstObjectByType<ProceduralDiceRoller>();
            CampusHUDController hud = Object.FindFirstObjectByType<CampusHUDController>();
            TurnManager tm = Object.FindFirstObjectByType<TurnManager>();
            OrbitalCamera cam = Object.FindFirstObjectByType<OrbitalCamera>();

            if (board != null)
            {
                board.sharedBoardMaterial = mat;
                if (board.levelData == null && lvl2 != null) board.levelData = lvl2;
                EditorUtility.SetDirty(board);
            }

            if (dice != null)
            {
                dice.sharedDiceMaterial = mat;
                EditorUtility.SetDirty(dice);
            }

            GameObject host = hud != null ? hud.gameObject : new GameObject("CampusViewManager");

            CampusGameLauncher launcher = Object.FindFirstObjectByType<CampusGameLauncher>();
            if (launcher == null) launcher = host.AddComponent<CampusGameLauncher>();
            launcher.availableLevels = new[] { lvl1, lvl2, lvl3 };
            launcher.availableCharacters = new[] { major, fetard, techos };
            launcher.defaultCampusMaterial = mat;
            launcher.turnManager = tm;
            launcher.board = board;
            launcher.diceRoller = dice;
            launcher.hud = hud;
            launcher.orbitalCamera = cam;
            launcher.openMainMenuOnStart = true;
            EditorUtility.SetDirty(launcher);

            CampusAudioManager audio = Object.FindFirstObjectByType<CampusAudioManager>();
            if (audio == null) audio = host.AddComponent<CampusAudioManager>();
            EditorUtility.SetDirty(audio);

            CampusTileInspector inspector = Object.FindFirstObjectByType<CampusTileInspector>();
            if (inspector == null) inspector = host.AddComponent<CampusTileInspector>();
            inspector.board = board;
            inspector.turnManager = tm;
            inspector.launcher = launcher;
            EditorUtility.SetDirty(inspector);

            if (hud != null)
            {
                hud.turnManager = tm;
                hud.board = board;
                hud.diceRoller = dice;
                hud.launcher = launcher;
                hud.tileInspector = inspector;
                hud.audioManager = audio;
                hud.orbitalCamera = cam;
                if (hud.barkController == null) hud.barkController = Object.FindFirstObjectByType<AIBarkController>();
                EditorUtility.SetDirty(hud);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[CampusSceneSetup] Configuration de Scene_Main terminée (Mat_Campus_URP, Launcher 3 niveaux, Audio, Inspecteur). Pensez à sauvegarder (Ctrl+S) !");
        }

        [MenuItem("Campus Tycoon/Vue 3D/Vérifier la configuration de la Scène")]
        public static void ValidateCurrentScene()
        {
            List<string> issues = new List<string>();

            ProceduralBoardGenerator board = Object.FindFirstObjectByType<ProceduralBoardGenerator>();
            ProceduralDiceRoller dice = Object.FindFirstObjectByType<ProceduralDiceRoller>();
            CampusHUDController hud = Object.FindFirstObjectByType<CampusHUDController>();
            CampusGameLauncher launcher = Object.FindFirstObjectByType<CampusGameLauncher>();
            TurnManager tm = Object.FindFirstObjectByType<TurnManager>();
            OrbitalCamera cam = Object.FindFirstObjectByType<OrbitalCamera>();

            if (board == null) issues.Add("ProceduralBoardGenerator absent de la scène.");
            else if (board.sharedBoardMaterial == null) issues.Add("ProceduralBoardGenerator.sharedBoardMaterial vide (risque rose fluo en Build .exe).");

            if (dice == null) issues.Add("ProceduralDiceRoller absent de la scène.");
            else if (dice.sharedDiceMaterial == null) issues.Add("ProceduralDiceRoller.sharedDiceMaterial vide (risque dés rose fluo en Build .exe).");

            if (hud == null) issues.Add("CampusHUDController absent de la scène.");
            if (cam == null) issues.Add("OrbitalCamera absente de la scène.");

            if (tm != null && launcher == null)
                issues.Add("CampusGameLauncher absent (le Menu Principal et l'inclusion des 3 LevelData en Build ne seront pas actifs).");

            if (launcher != null)
            {
                if (launcher.availableLevels == null || launcher.availableLevels.Length < 3)
                    issues.Add("CampusGameLauncher.availableLevels doit contenir les 3 niveaux.");
                else
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (launcher.availableLevels[i] == null) issues.Add($"CampusGameLauncher.availableLevels[{i}] manquant.");
                        else if (!launcher.availableLevels[i].Validate()) issues.Add($"Niveau {i + 1} invalide.");
                    }
                }

                if (launcher.availableCharacters == null || launcher.availableCharacters.Length < 3)
                    issues.Add("CampusGameLauncher.availableCharacters doit contenir les 3 personnages.");
                if (launcher.defaultCampusMaterial == null)
                    issues.Add("CampusGameLauncher.defaultCampusMaterial vide.");
            }

            if (issues.Count == 0)
            {
                Debug.Log("[CampusSceneSetup] VALIDATION OK : la scène est 100 % prête pour Play Mode et Build Standalone (.exe) !");
            }
            else
            {
                Debug.LogError("[CampusSceneSetup] Problèmes détectés :\n- " + string.Join("\n- ", issues));
            }
        }
    }
}
#endif
