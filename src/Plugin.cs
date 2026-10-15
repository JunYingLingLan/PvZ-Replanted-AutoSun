using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace PvZReplantedAutoSun
{
    /// <summary>
    /// 植物大战僵尸：重植版 自动收集插件（BepInEx 6 / IL2CPP）。
    ///
    /// 设计要点：
    ///   1. 不模拟任何真实鼠标输入：没有 SendInput / SetCursorPos / 光标移动，
    ///      因此完全不占用、不干扰你的物理鼠标。
    ///   2. 不做图像识别：直接遍历游戏的 Board.m_coins，用游戏自己的
    ///      Coin.IsSun() 与 CoinType 判断类型，再调用 Board.MouseDown 触发收集。
    ///   3. 可收集阳光、金币、银币、钻石；奖杯 / 脑子等特殊掉落默认不收。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class AutoSunPlugin : BasePlugin
    {
        public const string PluginGuid = "com.dsh.pvzreplanted.autosun";
        public const string PluginName = "PvZ Replanted Auto Sun";
        public const string PluginVersion = "0.2.0";

        internal static ManualLogSource Logger;

        // 1-总开关
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> ScanIntervalMs;
        internal static ConfigEntry<int> MaxPerScan;
        internal static ConfigEntry<float> DedupeSeconds;
        internal static ConfigEntry<bool> RunInBackground;

        // 2-收集范围
        internal static ConfigEntry<bool> CollectSun;
        internal static ConfigEntry<bool> CollectMoney;
        internal static ConfigEntry<bool> CollectAwards;

        // 3-收集方式
        internal static ConfigEntry<string> CollectMode;
        internal static ConfigEntry<bool> DryRun;

        // 4-调试
        internal static ConfigEntry<bool> DumpReflection;
        internal static ConfigEntry<string> DumpDirectory;
        internal static ConfigEntry<bool> Verbose;

        public override void Load()
        {
            Logger = Log;

            Enabled = Config.Bind("1-总开关", "Enabled", true,
                "总开关。关闭后插件不做任何收集动作。");
            ScanIntervalMs = Config.Bind("1-总开关", "ScanIntervalMs", 250,
                "扫描间隔（毫秒）。建议 150~500，太小会掉帧。");
            MaxPerScan = Config.Bind("1-总开关", "MaxPerScan", 3,
                "每次扫描最多收集几个掉落物，防止一瞬间全部收完。");
            DedupeSeconds = Config.Bind("1-总开关", "DedupeSeconds", 4.0f,
                "同一个掉落物在此秒数内只处理一次，避免重复收集导致数值翻倍。");
            RunInBackground = Config.Bind("1-总开关", "RunInBackground", true,
                "让游戏在窗口失去焦点时继续运行（Unity 默认失焦会暂停主循环）。");

            CollectSun = Config.Bind("2-收集范围", "CollectSun", true,
                "自动收集阳光（Coin.IsSun() 判定，涵盖 Sun / SmallSun / LargeSun / DoubleSun）。");
            CollectMoney = Config.Bind("2-收集范围", "CollectMoney", true,
                "自动收集金币 / 银币 / 钻石（CoinType 为 Gold / Silver / Diamond）。");
            CollectAwards = Config.Bind("2-收集范围", "CollectAwards", false,
                "自动收集关卡奖励类掉落物（钱袋、钻石袋、巧克力、礼物等）。"
                + "默认关闭，以免打乱正常奖励流程。");

            CollectMode = Config.Bind("3-收集方式", "CollectMode", "MouseDown",
                "MouseDown = 调用 Board.MouseDown(x, y, 1, 0)，走游戏自己的点击判定（推荐）；"
                + "Collect = 直接调用 Coin.Collect(0, false)。若前者无效请改成 Collect。");
            DryRun = Config.Bind("3-收集方式", "DryRun", false,
                "只记录将要执行的动作，不真正收集。排查问题时可以先打开。");

            DumpReflection = Config.Bind("4-调试", "DumpReflection", false,
                "启动时导出程序集中含 sun/coin/collect 关键字的类与方法，用于排错。");
            DumpDirectory = Config.Bind("4-调试", "DumpDirectory",
                Path.Combine(Paths.BepInExRootPath, "AutoSun"),
                "dump 文件输出目录。");
            Verbose = Config.Bind("4-调试", "Verbose", false,
                "输出每个被跳过的 CoinType 等详细日志。");

            try
            {
                Directory.CreateDirectory(DumpDirectory.Value);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("创建 dump 目录失败: " + ex.Message);
            }

            if (RunInBackground.Value)
            {
                try
                {
                    Application.runInBackground = true;
                    Logger.LogInfo("已设置 Application.runInBackground = true（失焦不暂停）。");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("设置 runInBackground 失败: " + ex.Message);
                }
            }

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<AutoSunBehaviour>();
                var host = new GameObject("PvZReplantedAutoSun");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.hideFlags = HideFlags.HideAndDontSave;
                host.AddComponent<AutoSunBehaviour>();
                Logger.LogInfo("AutoSunBehaviour 组件已挂载。");
            }
            catch (Exception ex)
            {
                Logger.LogError("挂载 AutoSunBehaviour 失败: " + ex);
            }

            Logger.LogInfo(string.Format("{0} {1} 加载完成。", PluginName, PluginVersion));
        }
    }
}
