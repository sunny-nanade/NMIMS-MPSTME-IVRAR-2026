using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    public static class MixerProbe
    {
        public static string Probe()
        {
            var sb = new StringBuilder();
            foreach (var tn in new[] { "AudioMixerController", "AudioMixerGroupController", "AudioMixerEffectController", "AudioMixerSnapshotController" })
            {
                var t = Type.GetType("UnityEditor.Audio." + tn + ", UnityEditor");
                sb.AppendLine("== " + tn + " " + t);
                if (t == null) continue;
                foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m is MethodInfo mi)
                        sb.AppendLine($"M {mi.ReturnType.Name} {mi.Name}({string.Join(",", Array.ConvertAll(mi.GetParameters(), p => p.ParameterType.Name + " " + p.Name))})");
                    else if (m is PropertyInfo pi) sb.AppendLine($"P {pi.PropertyType.Name} {pi.Name}");
                    else if (m is FieldInfo fi) sb.AppendLine($"F {fi.FieldType.Name} {fi.Name}");
                }
            }
            return sb.ToString();
        }
    }
}
