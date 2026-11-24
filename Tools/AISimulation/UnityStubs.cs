// Stubs minimaux de UnityEngine, juste assez pour compiler les scripts du jeu hors Unity.
// Ils ne sont jamais compilés dans le projet Unity (dossier hors de Assets/).
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static T[] FindObjectsByType<T>(FindObjectsSortMode mode) where T : Object => new T[0];
        public static void DontDestroyOnLoad(Object target) { }
        public static void Destroy(Object target) { }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { this.name = name; }
        public T AddComponent<T>() where T : Component, new() => new T();
    }

    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }

    public struct Rect { public Rect(float x, float y, float w, float h) { } }
    public class RectOffset { public RectOffset(int l, int r, int t, int b) { } }
    public enum TextAnchor { MiddleRight, MiddleLeft, UpperLeft }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public int fontSize; public bool richText; public bool wordWrap;
        public RectOffset padding; public TextAnchor alignment;
        public GUIStyleState normal = new GUIStyleState();
    }
    public class GUISkin { public GUIStyle label = new GUIStyle(); public GUIStyle box = new GUIStyle(); }
    public static class GUI
    {
        public static GUISkin skin = new GUISkin();
        public static void Label(Rect r, string text, GUIStyle style) { }
    }
    public static class GUILayout
    {
        public static void BeginArea(Rect r) { }
        public static void EndArea() { }
        public static void BeginVertical(GUIStyle style) { }
        public static void EndVertical() { }
        public static void Label(string text, GUIStyle style) { }
        public static void Space(float pixels) { }
    }
    public static class Screen { public static int width = 1920; public static int height = 1080; }
    public static class Mathf
    {
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Abs(float v) => Math.Abs(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
    }

    public enum FindObjectsSortMode { None, InstanceID }

    public static class Time
    {
        /// <summary>Réglable par les tests.</summary>
        public static float unscaledTime { get; set; }
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    public class Component : Object { public GameObject gameObject => null; }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public class Sprite : Object { }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f);
    }

    public static class Debug
    {
        public static bool Enabled = false;
        public static void Log(object message) { if (Enabled) Console.WriteLine(message); }
        public static void Log(object message, Object context) => Log(message);
        public static void LogWarning(object message) { if (Enabled) Console.WriteLine("[warn] " + message); }
        public static void LogError(object message) { if (Enabled) Console.WriteLine("[error] " + message); }
        public static void LogError(object message, Object context) => LogError(message);
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute(int minLines, int maxLines) { } }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName { get; set; }
        public string menuName { get; set; }
        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute { public MinAttribute(float min) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ContextMenu : Attribute { public ContextMenu(string name) { } }
}

namespace UnityEngine.Events
{
    public class UnityEvent<T0, T1>
    {
        private System.Action<T0, T1> listeners;
        public void AddListener(System.Action<T0, T1> call) => listeners += call;
        public void RemoveListener(System.Action<T0, T1> call) => listeners -= call;
        public void Invoke(T0 a, T1 b) => listeners?.Invoke(a, b);
    }
}

namespace UnityEngine.InputSystem
{
    public class KeyControl { public bool wasPressedThisFrame; }
    public class Keyboard
    {
        public static Keyboard current;
        public KeyControl iKey = new KeyControl();
    }
}
