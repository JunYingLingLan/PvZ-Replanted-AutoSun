using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PvZReplantedAutoSun
{
    /// <summary>
    /// 把游戏程序集中所有与阳光收集可能相关的类 / 方法签名导出为文本，
    /// 用于确定重植版真实的类名与方法名（IL2CPP 无法在编译期猜测）。
    /// </summary>
    internal static class ReflectionDumper
    {
        private static readonly string[] Keywords =
        {
            "sun", "coin", "collect", "money", "reward", "drop", "spawn", "pickup", "阳光"
        };

        private static readonly string[] SkipAssemblyPrefixes =
        {
            "System", "mscorlib", "netstandard", "Microsoft", "Mono.",
            "Il2CppInterop", "Il2CppSystem", "Il2Cppmscorlib",
            "BepInEx", "0Harmony", "HarmonyLib",
            "Cpp2IL", "AsmResolver", "Iced", "Newtonsoft"
        };

        public static void Dump(string directory)
        {
            var watch = Stopwatch.StartNew();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "reflection-dump.txt");

            int typeCount = 0;
            int methodCount = 0;

            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("# PvZ Replanted Auto Sun - 反射 dump");
                writer.WriteLine("# 生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                writer.WriteLine("# 关键字: " + string.Join(", ", Keywords));
                writer.WriteLine();

                Assembly[] assemblies;
                try
                {
                    assemblies = AppDomain.CurrentDomain.GetAssemblies();
                }
                catch (Exception ex)
                {
                    writer.WriteLine("获取程序集失败: " + ex.Message);
                    AutoSunPlugin.Logger.LogWarning("反射 dump 失败: " + ex.Message);
                    return;
                }

                foreach (var assembly in assemblies)
                {
                    string asmName;
                    try { asmName = assembly.GetName().Name; }
                    catch (Exception) { continue; }
                    if (ShouldSkip(asmName)) continue;

                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (Exception) { continue; }

                    foreach (var type in types)
                    {
                        string typeName;
                        try { typeName = type.FullName ?? type.Name; }
                        catch (Exception) { continue; }

                        MethodInfo[] methods;
                        try
                        {
                            methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                      BindingFlags.Instance | BindingFlags.Static |
                                                      BindingFlags.DeclaredOnly);
                        }
                        catch (Exception) { methods = new MethodInfo[0]; }

                        bool typeHit = Matches(typeName);
                        var interesting = new List<MethodInfo>();
                        foreach (var method in methods)
                        {
                            string methodName;
                            try { methodName = method.Name; }
                            catch (Exception) { continue; }
                            if (Matches(methodName)) interesting.Add(method);
                        }

                        if (!typeHit && interesting.Count == 0) continue;

                        typeCount++;
                        writer.WriteLine("== " + typeName + "   [程序集 " + asmName + "]");
                        if (interesting.Count == 0)
                        {
                            writer.WriteLine("    (类型名命中，但自身没有匹配的方法)");
                        }
                        foreach (var method in interesting)
                        {
                            methodCount++;
                            writer.WriteLine("    " + Describe(method));
                        }
                        writer.WriteLine();
                    }
                }
            }

            watch.Stop();
            AutoSunPlugin.Logger.LogInfo(string.Format(
                "反射 dump 完成: {0} 个类型 / {1} 个方法，耗时 {2} ms -> {3}",
                typeCount, methodCount, watch.ElapsedMilliseconds, path));
        }

        private static string Describe(MethodInfo method)
        {
            var sb = new StringBuilder();
            try
            {
                sb.Append(method.IsStatic ? "static " : "       ");
                sb.Append(method.ReturnType.Name).Append(' ').Append(method.Name).Append('(');

                var parameters = method.GetParameters();
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(parameters[i].ParameterType.Name).Append(' ')
                      .Append(parameters[i].Name);
                }
                sb.Append(')');
            }
            catch (Exception ex)
            {
                sb.Append("<无法解析: ").Append(ex.Message).Append('>');
            }
            return sb.ToString();
        }

        private static bool Matches(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var lower = text.ToLowerInvariant();
            for (int i = 0; i < Keywords.Length; i++)
            {
                if (lower.Contains(Keywords[i])) return true;
            }
            return false;
        }

        private static bool ShouldSkip(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return true;
            for (int i = 0; i < SkipAssemblyPrefixes.Length; i++)
            {
                if (assemblyName.StartsWith(SkipAssemblyPrefixes[i],
                                            StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 把当前场景中名字或组件名包含 sun/coin/collect 等关键字的对象导出，
    /// 用于确认阳光对象到底叫什么、挂了哪些组件。
    /// </summary>
    internal static class SceneDumper
    {
        private static readonly string[] Keywords =
        {
            "sun", "coin", "collect", "money", "reward", "drop", "spawn", "pickup", "阳光"
        };

        public static void Dump(string directory)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "scene-dump.txt");

            int visited = 0;
            int matched = 0;

            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("# 场景对象 dump（只列出名字或组件名匹配关键字的对象）");
                writer.WriteLine("# 生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                Scene scene;
                try { scene = SceneManager.GetActiveScene(); }
                catch (Exception ex)
                {
                    writer.WriteLine("获取当前场景失败: " + ex.Message);
                    return;
                }

                writer.WriteLine("场景: " + scene.name);
                writer.WriteLine();

                var stack = new Stack<Transform>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    Transform t = null;
                    try { t = root.transform; } catch (Exception) { }
                    if (t != null) stack.Push(t);
                }

                while (stack.Count > 0 && visited < 30000)
                {
                    var t = stack.Pop();
                    visited++;
                    if (t == null) continue;

                    GameObject go = null;
                    try { go = t.gameObject; } catch (Exception) { }

                    if (go != null)
                    {
                        List<string> components;
                        if (IsInteresting(go, out components))
                        {
                            matched++;
                            writer.WriteLine("对象: " + SafeName(go) +
                                             "   instanceID=" + SafeInstanceId(go));
                            writer.WriteLine("   路径: " + SafePath(t));
                            for (int i = 0; i < components.Count; i++)
                            {
                                writer.WriteLine("   组件: " + components[i]);
                            }
                            writer.WriteLine();
                        }
                    }

                    int childCount = 0;
                    try { childCount = t.childCount; } catch (Exception) { childCount = 0; }
                    for (int i = 0; i < childCount; i++)
                    {
                        Transform child = null;
                        try { child = t.GetChild(i); } catch (Exception) { }
                        if (child != null) stack.Push(child);
                    }
                }
            }

            AutoSunPlugin.Logger.LogInfo(string.Format(
                "场景 dump 完成: 遍历 {0} 个节点，命中 {1} 个候选对象 -> {2}",
                visited, matched, path));
        }

        private static bool IsInteresting(GameObject go, out List<string> componentNames)
        {
            componentNames = new List<string>();
            bool hit = false;

            var name = SafeName(go);
            if (!string.IsNullOrEmpty(name) && Matches(name)) hit = true;

            try
            {
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    string typeName;
                    try { typeName = comp.GetType().Name; }
                    catch (Exception) { continue; }
                    componentNames.Add(typeName);
                    if (Matches(typeName)) hit = true;
                }
            }
            catch (Exception) { }

            return hit;
        }

        private static bool Matches(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var lower = text.ToLowerInvariant();
            for (int i = 0; i < Keywords.Length; i++)
            {
                if (lower.Contains(Keywords[i])) return true;
            }
            return false;
        }

        private static string SafeName(GameObject go)
        {
            try { return go != null ? go.name : "<null>"; }
            catch (Exception) { return "<?>"; }
        }

        private static int SafeInstanceId(GameObject go)
        {
            try { return go.GetInstanceID(); }
            catch (Exception) { return -1; }
        }

        private static string SafePath(Transform t)
        {
            try
            {
                var parts = new List<string>();
                var current = t;
                int guard = 0;
                while (current != null && guard++ < 12)
                {
                    parts.Add(current.name);
                    current = current.parent;
                }
                parts.Reverse();
                return string.Join("/", parts);
            }
            catch (Exception) { return "<?>"; }
        }
    }
}
