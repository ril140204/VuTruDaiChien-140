using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.UI;

namespace QLTK.Client140
{
    /// <summary>
    /// CombatProbe v2: xác định CHÍNH XÁC điều gì làm đòn đánh không land.
    ///
    /// Phát hiện v1 (runtime):
    ///   - Click Skill1/Skill2 -> 4 quái CHẾT trong 2s  => skill CÓ gây dame thật.
    ///   - Test cũ bằng Thread.Sleep trong command handler -> block main thread Unity
    ///     => skill không bao giờ được execute => 0 dame (kết luận sai).
    ///
    /// Giả thuyết cần kiểm:
    ///   H9a - Teleport client-side (setPosition) KHÔNG cập nhật vị trí server
    ///         => server tính nhân vật ở xa quái => đòn đánh bị từ chối.
    ///   H9b - Teleport vào LỒN quái (0.4m) làm attack invalid.
    ///   H9c - moveTo (đi thật) giữ được tầm đánh server.
    ///
    /// Probe chạy trong Update() — KHÔNG Thread.Sleep.
    /// Lệnh: "probe start" / "probe stop"
    /// </summary>
    internal class CombatProbe : MonoBehaviour
    {
        internal static CombatProbe Instance;

        public bool Enabled;

        private int _step;
        private float _stepTime;
        private int _mobsAtStart;
        private int _prevMobs;
        private float _prevMobTime;
        private readonly List<string> _trace = new List<string>();
        private Vector2 _spawnPos;
        private Vector2 _mobPos;

        public static CombatProbe Install(BepInEx.Unity.IL2CPP.BasePlugin host)
        {
            try { ClassInjector.RegisterTypeInIl2Cpp<CombatProbe>(); }
            catch (Exception ex) { Log140.Error("[Probe] Register loi: " + ex.Message); }

            var p = host.AddComponent<CombatProbe>();
            Instance = p;
            Log140.Info("[Probe] Da cai v2. Lenh: probe start|stop");
            return p;
        }

        public void Start()
        {
            _step = 0;
            _stepTime = 0f;
            _trace.Clear();
            Enabled = true;
            Log("BAT DAU");
        }

        public void Stop()
        {
            Enabled = false;
            Log("DUNG");
            DumpTrace();
        }

        private void Update()
        {
            if (!Enabled) return;

            _stepTime += Time.deltaTime;
            float delay = StepDelay(_step);
            if (_stepTime < delay) return;
            _stepTime = 0f;

            try
            {
                switch (_step)
                {
                    case 0: B0_Baseline(); break;
                    case 1: B1_SkillNoTp(); break;
                    case 2: B2_Check(); break;
                    case 3: B3_SkillAgain(); break;
                    case 4: B4_Check(); break;
                    case 5: B5_TeleportNear(); break;
                    case 6: B6_Check(); break;
                    case 7: B7_SkillAfterTp(); break;
                    case 8: B8_Check(); break;
                    case 9: B9_TeleportFar(); break;
                    case 10: B10_Check(); break;
                    case 11: B11_SkillAfterFarTp(); break;
                    case 12: B12_Check(); break;
                    case 13: B13_InfoComponents(); break;
                    case 14: B14_Summary(); break;
                    default: Stop(); return;
                }
                _step++;
            }
            catch (Exception ex)
            {
                Log("ERR " + ex.Message);
                Enabled = false;
            }
        }

        /// <summary>Delay giữa các bước (giây) — bước bấm skill cần chờ skill land.</summary>
        private static float StepDelay(int step)
        {
            switch (step)
            {
                case 1: return 1.6f;   // sau khi bấm skill, chờ 1.6s
                case 3: return 1.6f;
                case 7: return 1.6f;
                case 11: return 1.6f;
                case 5: return 1.2f;   // sau teleport, chờ server ổn định
                case 9: return 1.2f;
                default: return 0.25f;
            }
        }

        // ---------- các bước ----------

        private void B0_Baseline()
        {
            _mobsAtStart = GameApi.AliveMobs().Count;
            _prevMobs = _mobsAtStart;
            _prevMobTime = Time.realtimeSinceStartup;

            var my = GameApi.MyPos();
            _spawnPos = my ?? new Vector2(0, 0);

            var mob = GameApi.ClosestMob();
            _mobPos = mob != null ? (GameApi.MobPos(mob) ?? _spawnPos) : _spawnPos;

            Log($"B0 quai={_mobsAtStart} player={Fmt(_spawnPos)} mob={Fmt(_mobPos)} " +
                $"d={(mob != null ? Vector2.Distance(_spawnPos, _mobPos).ToString("F2") : "-")}");
        }

        private void B1_SkillNoTp()
        {
            Log("B1 click Skill1 (KHONG teleport)");
            ClickSkill("Skill1");
        }

        private void B2_Check() { Check("B2"); }

        private void B3_SkillAgain()
        {
            Log("B3 click Skill1 + Skill2 (KHONG teleport)");
            ClickSkill("Skill1");
            ClickSkill("Skill2");
        }

        private void B4_Check() { Check("B4"); }

        private void B5_TeleportNear()
        {
            // Teleport tới sát quái (0.4m) — giống hệt RibbonBot đang làm
            var mob = GameApi.ClosestMob();
            if (mob == null) { Log("B5 khong co quai"); return; }
            var mp = GameApi.MobPos(mob);
            if (mp == null) return;

            Vector2 dir = _spawnPos - mp.Value;
            if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(1, 0);
            dir.Normalize();
            Vector2 tp = mp.Value + dir * 0.4f;

            bool ok = GameApi.MoveTo(tp.x, tp.y);
            Log($"B5 TELEPORT sat quái {Fmt(tp)} ok={ok} (dau={Fmt(_spawnPos)})");
        }

        private void B6_Check() { Check("B6"); }

        private void B7_SkillAfterTp()
        {
            var my = GameApi.MyPos();
            Log($"B7 click Skill1 SAU teleport (player={Fmt(my ?? new Vector2())})");
            ClickSkill("Skill1");
        }

        private void B8_Check() { Check("B8"); }

        private void B9_TeleportFar()
        {
            // Teleport đi xa 25m
            Vector2 tp = _spawnPos + new Vector2(25f, 0f);
            bool ok = GameApi.MoveTo(tp.x, tp.y);
            Log($"B9 TELEPORT xa {Fmt(tp)} ok={ok}");
        }

        private void B10_Check() { Check("B10"); }

        private void B11_SkillAfterFarTp()
        {
            Log("B11 click Skill1 SAU teleport xa");
            ClickSkill("Skill1");
        }

        private void B12_Check() { Check("B12"); }

        private void B13_InfoComponents()
        {
            // Liệt kê component trên GameObject Skill1 để tìm API cooldown/cast
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Button>(true);
                foreach (var b in all)
                {
                    try
                    {
                        if (b == null || b.gameObject.name != "Skill1") continue;
                        Log($"B13 Skill1 path={Path(b.transform)} active={b.gameObject.activeInHierarchy} interactable={b.interactable}");
                        foreach (var c in b.GetComponents<Component>())
                        {
                            try { Log($"B13   comp={c?.GetType().FullName ?? "null"}"); } catch { }
                        }
                        // Thử đọc cooldown qua các component
                        foreach (var c in b.GetComponents<Component>())
                        {
                            try
                            {
                                if (c == null) continue;
                                foreach (var mi in c.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    try
                                    {
                                        if (mi.GetParameters().Length != 0) continue;
                                        if (mi.ReturnType != typeof(long) && mi.ReturnType != typeof(int)) continue;
                                        var nm = mi.Name.ToUpperInvariant();
                                        if (!nm.Contains("COOLDOWN") && !nm.Contains("LEFT")) continue;
                                        object v = mi.Invoke(c, null);
                                        Log($"B13   cooldownMethod {c.GetType().Name}.{mi.Name}()={v}");
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                        break;
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log("B13 ERR " + ex.Message); }
        }

        private void B14_Summary()
        {
            Log($"SUMMARY quai dau={_mobsAtStart} cuoi={GameApi.AliveMobs().Count} " +
                $"=> chet tong={_mobsAtStart - GameApi.AliveMobs().Count}");
            Log("Ket qua: so sanh chet sau B2 (khong tp) vs B8 (sau tp sat) vs B12 (sau tp xa)");
        }

        // ---------- helpers ----------

        private void Check(string tag)
        {
            int n = GameApi.AliveMobs().Count;
            int delta = _prevMobs - n;
            float dt = Time.realtimeSinceStartup - _prevMobTime;
            Log($"{tag} quai={n} chetLuon={_mobsAtStart - n} vuaChet={delta} trong {dt:F2}s");
            _prevMobs = n;
            _prevMobTime = Time.realtimeSinceStartup;
        }

        private static string Fmt(Vector2 v) => $"({v.x:F2},{v.y:F2})";

        private static string Path(Transform t)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                while (t != null) { sb.Insert(0, t.gameObject.name + "/"); t = t.parent; }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        /// <summary>Bấm skill button theo tên GameObject.</summary>
        private static void ClickSkill(string name)
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Button>(true);
                foreach (var b in all)
                {
                    try
                    {
                        if (b == null) continue;
                        if (b.gameObject.name != name) continue;
                        if (!b.gameObject.activeInHierarchy) { Log($"  {name} KHONG active"); return; }
                        if (!b.interactable) { Log($"  {name} KHONG interactable"); return; }
                        b.onClick?.Invoke();
                        Log($"  click {name} OK");
                        return;
                    }
                    catch { }
                }
                Log($"  khong tim {name}");
            }
            catch (Exception ex) { Log($"  click {name} loi {ex.Message}"); }
        }

        private static void Log(string msg)
        {
            string line = "[Probe] " + msg;
            Log140.Info(line);
            var inst = Instance;
            if (inst != null)
            {
                inst._trace.Add(DateTime.Now.ToString("HH:mm:ss") + " " + msg);
                if (inst._trace.Count > 300) inst._trace.RemoveAt(0);
            }
        }

        private void DumpTrace()
        {
            try
            {
                if (_trace.Count == 0) return;
                Log140.Info("[Probe] ----- TRACE -----");
                foreach (var t in _trace) Log140.Info("[Probe]   " + t);
                Log140.Info("[Probe] ----- HET TRACE -----");
            }
            catch { }
        }
    }
}
