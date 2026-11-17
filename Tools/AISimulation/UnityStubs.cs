// Stubs minimaux de UnityEngine, juste assez pour compiler les scripts du jeu hors Unity.
// Ils ne sont jamais compilés dans le projet Unity (dossier hors de Assets/).
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static T[] FindObjectsByType<T>(FindObjectsSortMode mode) where T : Object => new T[0];
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

    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public class Sprite : Object { }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f);
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
