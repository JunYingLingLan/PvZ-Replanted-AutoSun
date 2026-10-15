using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Reloaded.Gameplay;
using Reloaded.TreeStateActivities;
using UnityEngine;

namespace PvZReplantedAutoSun
{
    /// <summary>
    /// 通过游戏原生 API 自动收集阳光 / 金币 / 银币 / 钻石。
    ///
    /// 关键事实（由 Mono.Cecil 反解 Assembly-CSharp.dll 得到）：
    ///   GameplayActivity : MonoBehaviour  —— 可用 FindObjectsOfType 找到
    ///       └─ Board m_board
    ///              └─ DataArray&lt;Coin&gt; m_coins   （元素是 DataArrayItem&lt;Coin&gt; 包装）
    ///                     └─ Coin.IsSun() / Coin.mType / Coin.GetClickRect()
    ///              └─ Board.MouseDown(x, y, clickCount, playerIndex)  ← 点击总入口
    ///
    /// 游戏用的是自研渲染系统（ReloadedObject / Widget，继承 Il2CppSystem.Object），
    /// 掉落物不是 Unity GameObject，必须走 Board / Coin 这套原生 API。
    /// 本实现不碰鼠标、不碰真实输入，只调用游戏自己的点击处理函数。
    /// </summary>
    public class AutoSunBehaviour : MonoBehaviour
    {
        private readonly Dictionary<IntPtr, float> _cooldown = new Dictionary<IntPtr, float>();
        private readonly List<IntPtr> _expiredKeys = new List<IntPtr>(64);

        private GameplayActivity _activity;
        private float _nextActivityLookup;
        private float _nextScanTime;
        private int _collectedCount;
        private int _lastCoinCount;
        private int _lastSunCount;

        // Il2CppInterop 注入类型必须提供这个构造函数
        public AutoSunBehaviour(IntPtr ptr) : base(ptr) { }

        private void Start()
        {
            AutoSunPlugin.Logger.LogInfo("AutoSunBehaviour 已启动（原生 Coin API 模式）。");
            try
            {
                if (AutoSunPlugin.DumpReflection.Value)
                {
                    ReflectionDumper.Dump(AutoSunPlugin.DumpDirectory.Value);
                }
            }
            catch (Exception ex)
            {
                AutoSunPlugin.Logger.LogWarning("反射 dump 失败: " + ex.Message);
            }
        }

        private void Update()
        {
            try
            {
                if (!AutoSunPlugin.Enabled.Value) return;

                float now = Time.time;
                if (now < _nextScanTime) return;

                int interval = Math.Max(50, AutoSunPlugin.ScanIntervalMs.Value);
                _nextScanTime = now + interval / 1000f;

                Scan(now);
            }
            catch (Exception ex)
            {
                AutoSunPlugin.Logger.LogError("扫描异常: " + ex);
                _nextScanTime = Time.time + 1f;
            }
        }

        // ------------------------------------------------------------ 核心扫描

        private void Scan(float now)
        {
            Board board = GetBoard();
            if (board == null) return;

            DataArray<Coin> coins = null;
            try { coins = board.m_coins; }
            catch (Exception) { }
            if (coins == null) return;

            int count = 0;
            try { count = coins.Count; }
            catch (Exception) { }
            if (count <= 0) return;
            _lastCoinCount = count;

            Il2CppReferenceArray<DataArray<Coin>.DataArrayItem<Coin>> list;
            try { list = coins.m_list; }
            catch (Exception) { return; }
            if (list == null) return;

            int limit = count;
            try { limit = Math.Min(count, list.Length); }
            catch (Exception) { }

            int handled = 0;
            int sunSeen = 0;
            int maxPerScan = Math.Max(1, AutoSunPlugin.MaxPerScan.Value);

            for (int i = 0; i < limit && handled < maxPerScan; i++)
            {
                Coin coin = null;
                try
                {
                    var item = list[i];
                    if (item != null) coin = item.mItem;
                }
                catch (Exception) { continue; }
                if (coin == null) continue;

                bool isSun = false;
                try { isSun = coin.IsSun(); }
                catch (Exception) { }

                CoinType coinType = CoinType.None;
                try { coinType = coin.mType; }
                catch (Exception) { }

                if (isSun) sunSeen++;

                string kind;
                if (!ShouldCollect(isSun, coinType, out kind))
                {
                    if (AutoSunPlugin.Verbose.Value && coinType != CoinType.None)
                    {
                        AutoSunPlugin.Logger.LogInfo("[跳过] 类型=" + coinType);
                    }
                    continue;
                }

                bool dead = false;
                try { dead = coin.mDead; }
                catch (Exception) { }
                if (dead) continue;

                IntPtr key = IntPtr.Zero;
                try { key = coin.Pointer; }
                catch (Exception) { continue; }
                if (key == IntPtr.Zero) continue;

                float last;
                if (_cooldown.TryGetValue(key, out last) &&
                    now - last < AutoSunPlugin.DedupeSeconds.Value)
                {
                    continue;
                }

                string via;
                bool ok = CollectCoin(board, coin, out via);
                _cooldown[key] = now;

                if (ok)
                {
                    handled++;
                    _collectedCount++;
                    if (AutoSunPlugin.Verbose.Value || _collectedCount <= 30 ||
                        _collectedCount % 50 == 0)
                    {
                        AutoSunPlugin.Logger.LogInfo(string.Format(
                            "收集{0} [{1}] 累计={2}  场上Coin={3} 其中阳光={4}",
                            kind, via, _collectedCount, count, sunSeen));
                    }
                }
            }

            _lastSunCount = sunSeen;
            CleanupCooldown(now);
        }

        // ------------------------------------------------------------ 收集范围

        /// <summary>
        /// 决定某个 Coin 要不要收。
        /// 用游戏自己的 IsSun() 判断阳光，用 CoinType 精确判断金币 / 银币 / 钻石，
        /// 避免误收奖杯（Trophy / RIPTrophy / VersusTrophy*）、脑子（Brain）等。
        /// </summary>
        private static bool ShouldCollect(bool isSun, CoinType type, out string kind)
        {
            kind = null;

            if (isSun)
            {
                if (!AutoSunPlugin.CollectSun.Value) return false;
                kind = "阳光";
                return true;
            }

            switch (type)
            {
                case CoinType.Silver:
                    if (!AutoSunPlugin.CollectMoney.Value) return false;
                    kind = "银币";
                    return true;

                case CoinType.Gold:
                    if (!AutoSunPlugin.CollectMoney.Value) return false;
                    kind = "金币";
                    return true;

                case CoinType.Diamond:
                    if (!AutoSunPlugin.CollectMoney.Value) return false;
                    kind = "钻石";
                    return true;
            }

            if (AutoSunPlugin.CollectAwards.Value && IsAward(type))
            {
                kind = "奖励(" + type + ")";
                return true;
            }

            return false;
        }

        /// <summary>关卡奖励类掉落物，默认不收（避免打乱正常奖励流程）。</summary>
        private static bool IsAward(CoinType type)
        {
            switch (type)
            {
                case CoinType.AwardMoneyBag:
                case CoinType.AwardPresent:
                case CoinType.AwardBagDiamond:
                case CoinType.AwardSilverSunflower:
                case CoinType.AwardGoldSunflower:
                case CoinType.Chocolate:
                case CoinType.AwardChocolate:
                case CoinType.PresentPlant:
                case CoinType.PresentMinigames:
                case CoinType.PresentPuzzleMode:
                case CoinType.PresentSurvivalMode:
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------ 收集动作

        private bool CollectCoin(Board board, Coin coin, out string via)
        {
            via = null;

            string mode = (AutoSunPlugin.CollectMode != null)
                ? AutoSunPlugin.CollectMode.Value
                : "MouseDown";
            bool preferCollect =
                !string.IsNullOrEmpty(mode) &&
                mode.Equals("Collect", StringComparison.OrdinalIgnoreCase);

            if (!preferCollect)
            {
                // 方案 A：走游戏自己的点击判定流程，等价于真人点了一下
                int cx = 0;
                int cy = 0;
                try
                {
                    Rect rect = coin.GetClickRect();
                    cx = (int)(rect.x + rect.width * 0.5f);
                    cy = (int)(rect.y + rect.height * 0.5f);
                }
                catch (Exception) { }

                via = string.Format("Board.MouseDown({0},{1},1,0)", cx, cy);
                if (DryRunHit(via)) return true;

                try
                {
                    board.MouseDown(cx, cy, 1, 0);
                    return true;
                }
                catch (Exception ex)
                {
                    AutoSunPlugin.Logger.LogWarning("Board.MouseDown 调用失败: " + ex.Message);
                }
            }

            // 方案 B：直接调用收集方法
            via = "Coin.Collect(0,false)";
            if (DryRunHit(via)) return true;

            try
            {
                coin.Collect(0, false);
                return true;
            }
            catch (Exception ex)
            {
                AutoSunPlugin.Logger.LogWarning("Coin.Collect 调用失败: " + ex.Message);
            }

            return false;
        }

        private static bool DryRunHit(string via)
        {
            if (!AutoSunPlugin.DryRun.Value) return false;
            AutoSunPlugin.Logger.LogInfo("[DryRun] 命中 " + via + "，未实际执行。");
            return true;
        }

        // ------------------------------------------------------------ 定位 Board

        private Board GetBoard()
        {
            float now = Time.time;
            bool needLookup = false;

            if (_activity == null) needLookup = true;
            else if (now >= _nextActivityLookup) needLookup = true;

            if (needLookup)
            {
                _nextActivityLookup = now + 3f;
                _activity = FindActivity();
            }

            if (_activity == null) return null;

            try { return _activity.Board; }
            catch (Exception) { return null; }
        }

        private static GameplayActivity FindActivity()
        {
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType(
                    Il2CppType.Of<GameplayActivity>());
                if (found != null)
                {
                    for (int i = 0; i < found.Length; i++)
                    {
                        var obj = found[i];
                        if (obj == null) continue;
                        var activity = obj.TryCast<GameplayActivity>();
                        if (activity != null) return activity;
                    }
                }
            }
            catch (Exception ex)
            {
                AutoSunPlugin.Logger.LogWarning("查找 GameplayActivity 失败: " + ex.Message);
            }
            return null;
        }

        // ------------------------------------------------------------ 辅助

        private void CleanupCooldown(float now)
        {
            if (_cooldown.Count < 128) return;

            _expiredKeys.Clear();
            float limit = AutoSunPlugin.DedupeSeconds.Value * 4f;
            foreach (var kv in _cooldown)
            {
                if (now - kv.Value > limit) _expiredKeys.Add(kv.Key);
            }
            for (int i = 0; i < _expiredKeys.Count; i++) _cooldown.Remove(_expiredKeys[i]);
        }
    }
}
