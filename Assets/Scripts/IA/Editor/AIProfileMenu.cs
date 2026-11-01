#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CampusTycoon.AI.EditorTools
{
    /// <summary>Crée les trois profils d'IA avec les valeurs du document de règles.</summary>
    public static class AIProfileMenu
    {
        private const string Folder = "Assets/Data/AI";

        [MenuItem("Campus Tycoon/IA/Créer les profils par défaut")]
        public static void CreateDefaultProfiles()
        {
            Directory.CreateDirectory(Folder);

            foreach (CharacterId id in System.Enum.GetValues(typeof(CharacterId)))
            {
                string path = $"{Folder}/AI_{id}.asset";
                var profile = AssetDatabase.LoadAssetAtPath<AIProfile>(path);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<AIProfile>();
                    profile.LoadDefaults(id);
                    AssetDatabase.CreateAsset(profile, path);
                }
                else
                {
                    profile.LoadDefaults(id);
                    EditorUtility.SetDirty(profile);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Profils d'IA créés dans {Folder}");
        }
    }
}
#endif
