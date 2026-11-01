// Stubs minimaux de UnityEngine, juste assez pour compiler les scripts du jeu hors Unity.
// Ils ne sont jamais compilés dans le projet Unity (dossier hors de Assets/).
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public static class Debug
    {
        public static bool Enabled = false;
        public static void Log(object message) { if (Enabled) Console.WriteLine(message); }
        public static void LogWarning(object message) { if (Enabled) Console.WriteLine("[warn] " + message); }
    }

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
