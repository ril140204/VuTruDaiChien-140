using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QLTK.Client140
{
    /// <summary>
    /// LiveWatcher: ghi lai MOI thao tac nguoi choi trong game theo thoi gian thuc.
    ///
    /// Muc dich: de nguoi dung "choi tay" mot lan roi gui file log cho developer,
    /// developer doc log la biet chinh xac:
    ///   - bam nut nao (ten nut, text tren nut, duong dan trong hierarchy)
    ///   - panel nao vua bat/tat (suy ra chuc nang)
    ///   - chi so nhan vat thay doi the nao (level, hp, mp, map, toa do)
    ///   - go phim gi (hotkey, phim tat)
    ///   - text tren man hinh doi ra sao
    ///
    /// File: BepInEx/GamePlay.log  (ghi ngay, khong can thoat game)
    ///
    /// Hotkey F9 = dump toan bo UI hien tai (de xem co gi tren man hinh).
    /// </summary>
    internal sealed class LiveWatcher : MonoBehaviour
    {
        public LiveWatcher(IntPtr ptr) : base(ptr) { }

        // ---------- cau hinh ----------
        private const float ScanInterval = 0.5f;    // quet Button/InputField moi 0.5s
        private const float StateInterval = 0.5f;   // quet trang thai panel/nhan vat
        private const float PosInterval = 2.0f;     // ghi toa do moi 2s
        private const int MaxLinesPerSession = 200000;

        private string _logPath;
        private readonly object _lock = new object();
        private bool _broken;

        private float _scanTimer;
        private float _stateTimer;
        private float _posTimer;
        private float _cmdTimer;
        private int _lineCount;

        private string _lastScene = "";

        // Button da hook: key = instance pointer
        private readonly HashSet<IntPtr> _hookedButtons = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, string> _buttonLabels = new Dictionary<IntPtr, string>();

        // Trang thai panel de phat hien bat/tat
        private readonly Dictionary<IntPtr, bool> _panelActive = new Dictionary<IntPtr, bool>();
        private readonly Dictionary<IntPtr, string> _panelNames = new Dictionary<IntPtr, string>();

        // Gia tri input field truoc do
        private readonly Dictionary<IntPtr, string> _inputValues = new Dictionary<IntPtr, string>();

        // Trang thai nhan vat truoc do
        private int _lastLevel = int.MinValue;
        private string _lastHp = null;
        private string _lastMp = null;
        private Vector3 _lastPos = new Vector3(float.MaxValue, 0, 0);

        // ---------- cai dat ----------

        public static LiveWatcher Install(BepInEx.Unity.IL2CPP.BasePlugin host)
        {
            try { ClassInjector.RegisterTypeInIl2Cpp<LiveWatcher>(); }
            catch (Exception ex) { Log140.Error("[Live] Register loi: " + ex.Message); }

            var w = host.AddComponent<LiveWatcher>();
            w.Init();
            return w;
        }

        private void Init()
        {
            try
            {
                string dir = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(typeof(LiveWatcher).Assembly.Location) ?? ".", ".."));
                _logPath = Path.Combine(dir, "GamePlay.log");

                lock (_lock)
                {
                    File.WriteAllText(_logPath,
                        "=== GAMEPLAY LOG " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n" +
                        "=== Ban hay thao tac trong game, moi hanh dong se duoc ghi lai tai day ===\n" +
                        "=== F9 = dump toan bo UI hien tai ===\n\n",
                        new UTF8Encoding(false));
                }

                Log140.Info("[Live] Ghi log thao tac vao: " + _logPath);
            }
            catch (Exception ex)
            {
                _broken = true;
                Log140.Error("[Live] Khong mo duoc file log: " + ex.Message);
            }
        }

        // ---------- vong lap ----------

        private void Update()
        {
            if (_broken) return;
            if (_lineCount > MaxLinesPerSession) return;

            try
            {
                float dt = Time.deltaTime;

                // 1. Scene doi
                string scene = SafeSceneName();
                if (scene != _lastScene)
                {
                    Write($"SCENE  | {_lastScene} -> {scene}");
                    _lastScene = scene;
                    // Reset cache khi doi scene de hook lai tu dau
                    _hookedButtons.Clear();
                    _buttonLabels.Clear();
                    _panelActive.Clear();
                    _inputValues.Clear();
                }

                // 2. Hook moi Button chua duoc hook
                _scanTimer += dt;
                if (_scanTimer >= ScanInterval)
                {
                    _scanTimer = 0f;
                    HookButtons();
                    WatchInputFields();
                }

                // 3. Panel bat/tat + chi so nhan vat
                _stateTimer += dt;
                if (_stateTimer >= StateInterval)
                {
                    _stateTimer = 0f;
                    WatchPanels();
                    WatchPlayerStats();
                }

                // 4. Toa do
                _posTimer += dt;
                if (_posTimer >= PosInterval)
                {
                    _posTimer = 0f;
                    WatchPosition();
                }

                // 5. Hotkey: F9 dump UI
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    Write("HOTKEY | F9 -> dump UI");
                    DumpUi("F9 dump");
                }

                // 6. Ghi lai phim tat nguoi dung bam (chi phim chu cai / mui ten / space)
                WatchKeys();

                // 7. Lenh tu file (developer dieu khien game tu ben ngoai)
                _cmdTimer += dt;
                if (_cmdTimer >= 0.3f)
                {
                    _cmdTimer = 0f;
                    ProcessCommandFile();
                }
            }
            catch (Exception ex)
            {
                Write("ERROR  | Update: " + ex.Message);
            }
        }

        // ---------- hook button ----------

        private void HookButtons()
        {
            Button[] buttons;
            try { buttons = UnityEngine.Object.FindObjectsOfType<Button>(true); }
            catch { return; }

            foreach (var btn in buttons)
            {
                if (btn == null) continue;

                IntPtr ptr;
                try { ptr = btn.Pointer; }
                catch { continue; }

                if (ptr == IntPtr.Zero) continue;

                string label = SafeName(btn.gameObject) + " | text='" + (GetButtonText(btn) ?? "") + "'";
                _buttonLabels[ptr] = label;

                if (_hookedButtons.Contains(ptr)) continue;
                _hookedButtons.Add(ptr);

                // AddListener co the that bai voi mot so nut - khi do bo qua,
                // van con WatchPanels() phat hien panel vua mo.
                try
                {
                    var captured = _buttonLabels[ptr];
                    var go = btn.gameObject;
                    UnityAction action = (UnityAction)(() =>
                    {
                        string path = GetPath(go);
                        Write($"CLICK  | {captured} | path={path} | scene={SafeSceneName()}");
                    });
                    btn.onClick.AddListener(action);
                }
                catch (Exception ex)
                {
                    Write("WARN   | Khong hook duoc nut '" + _buttonLabels[ptr] + "': " + ex.Message);
                }
            }
        }

        private void WatchInputFields()
        {
            TMP_InputField[] fields;
            try { fields = UnityEngine.Object.FindObjectsOfType<TMP_InputField>(true); }
            catch { return; }

            foreach (var f in fields)
            {
                if (f == null) continue;

                IntPtr ptr;
                try { ptr = f.Pointer; }
                catch { continue; }
                if (ptr == IntPtr.Zero) continue;

                string val;
                try { val = f.text ?? ""; }
                catch { continue; }

                bool had;
                string old = null;
                had = _inputValues.TryGetValue(ptr, out old);

                if (!had)
                {
                    _inputValues[ptr] = val;
                    continue;
                }

                if (val != old)
                {
                    _inputValues[ptr] = val;
                    Write($"INPUT  | {SafeName(f.gameObject)} : '{old}' -> '{val}'");
                }
            }
        }

        // ---------- panel bat/tat ----------

        private void WatchPanels()
        {
            // Chỉ quan tâm panel cấp cao (con trực tiếp của Canvas) để log gọn
            Canvas[] canvases;
            try { canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true); }
            catch { return; }

            foreach (var c in canvases)
            {
                if (c == null) continue;

                var tr = c.transform;
                int n = tr.childCount;
                for (int i = 0; i < n; i++)
                {
                    Transform child;
                    try { child = tr.GetChild(i); }
                    catch { continue; }
                    if (child == null) continue;

                    var go = child.gameObject;
                    if (go == null) continue;

                    IntPtr ptr;
                    try { ptr = go.Pointer; }
                    catch { continue; }
                    if (ptr == IntPtr.Zero) continue;

                    bool active;
                    try { active = go.activeSelf; }
                    catch { continue; }

                    bool had = _panelActive.TryGetValue(ptr, out bool prev);
                    _panelActive[ptr] = active;
                    _panelNames[ptr] = go.name;

                    if (!had) continue;
                    if (prev == active) continue;

                    Write($"PANEL  | {go.name} {(active ? "MO" : "DONG")}");
                }
            }
        }

        // ---------- chi so nhan vat ----------

        private void WatchPlayerStats()
        {
            MainPlayer player = null;
            try { player = UnityEngine.Object.FindObjectOfType<MainPlayer>(true); }
            catch { }
            if (player == null) return;

            // Level
            try
            {
                int lv = PlayerInfo.TryGetLevel();
                if (lv >= 0 && lv != _lastLevel)
                {
                    Write($"PLAYER | level: {(_lastLevel == int.MinValue ? "?" : _lastLevel.ToString())} -> {lv}");
                    _lastLevel = lv;
                }
            }
            catch { }

            // HP / MP: quet text dang "123/456" tren man hinh
            try
            {
                var stats = UnityEngine.Object.FindObjectOfType<MyPlayerStats>(true);
                if (stats != null)
                {
                    string hp = ReadStatText(stats, "hp");
                    string mp = ReadStatText(stats, "mp");

                    if (hp != null && hp != _lastHp)
                    {
                        Write($"PLAYER | HP: {(_lastHp ?? "?")} -> {hp}");
                        _lastHp = hp;
                    }
                    if (mp != null && mp != _lastMp)
                    {
                        Write($"PLAYER | MP: {(_lastMp ?? "?")} -> {mp}");
                        _lastMp = mp;
                    }
                }
            }
            catch { }
        }

        private static string ReadStatText(MyPlayerStats stats, string kind)
        {
            try
            {
                var t = stats.GetType();
                var f = t.GetField(kind + "Text",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (f == null) return null;

                object val = f.GetValue(stats);
                if (val == null) return null;

                // MyUIText -> .text (TextMeshProUGUI)
                var vt = val.GetType();
                var textProp = vt.GetProperty("text");
                if (textProp != null)
                {
                    object s = textProp.GetValue(val);
                    return s as string;
                }
            }
            catch { }
            return null;
        }

        private void WatchPosition()
        {
            try
            {
                var p = UnityEngine.Object.FindObjectOfType<MainPlayer>(true);
                if (p == null) return;

                Vector3 pos = p.transform.position;
                if (_lastPos.x == float.MaxValue)
                {
                    _lastPos = pos;
                    return;
                }

                float d = Vector3.Distance(pos, _lastPos);
                if (d >= 1f)
                {
                    Write($"MOVE   | ({_lastPos.x:F1},{_lastPos.y:F1}) -> ({pos.x:F1},{pos.y:F1}) d={d:F1}");
                    _lastPos = pos;
                }
            }
            catch { }
        }

        // ---------- phim ----------

        private void WatchKeys()
        {
            // Chi log cac phim nghiep vu hay dung, tranh spam W/A/S/D
            if (Input.GetKeyDown(KeyCode.Space)) Write("KEY    | Space");
            if (Input.GetKeyDown(KeyCode.Return)) Write("KEY    | Enter");
            if (Input.GetKeyDown(KeyCode.Escape)) Write("KEY    | Escape");
            if (Input.GetKeyDown(KeyCode.Tab)) Write("KEY    | Tab");

            for (int k = (int)KeyCode.Alpha1; k <= (int)KeyCode.Alpha9; k++)
            {
                if (Input.GetKeyDown((KeyCode)k)) Write($"KEY    | {(k - (int)KeyCode.Alpha1) + 1}");
            }
        }

        // ---------- dump UI ----------

        public void DumpUi(string reason)
        {
            Write("===== DUMP UI (" + reason + ") scene=" + SafeSceneName() + " =====");

            try
            {
                var btns = UnityEngine.Object.FindObjectsOfType<Button>(true);
                Write("-- Buttons: " + btns.Length);
                foreach (var b in btns)
                {
                    if (b == null) continue;
                    Write($"   BTN  name='{b.gameObject.name}' active={b.gameObject.activeInHierarchy} " +
                          $"text='{GetButtonText(b) ?? ""}' path={GetPath(b.gameObject)}");
                }
            }
            catch (Exception ex) { Write("   LOI Button: " + ex.Message); }

            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true);
                Write("-- Texts co noi dung: ");
                int n = 0;
                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    string s = t.text;
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (s.Length > 100) s = s.Substring(0, 100) + "...";
                    Write($"   TXT  parent='{(t.transform.parent != null ? t.transform.parent.gameObject.name : "root")}' " +
                          $"name='{t.gameObject.name}' text='{s.Replace("\n", "\\n")}'");
                    if (++n > 300) { Write("   ... cat bot"); break; }
                }
            }
            catch (Exception ex) { Write("   LOI Text: " + ex.Message); }

            Write("===== HET DUMP =====");
        }

        // ---------- lenh tu file (developer dieu khien tu ben ngoai) ----------

        private string _cmdPath;

        /// <summary>
        /// Doc file lenh BepInEx/GameCmd.txt. Moi dong la 1 lenh:
        ///   dump                        -> dump toan bo UI
        ///   click &lt;ten nut hoac text&gt;   -> bam nut dau tien khop
        ///   clickpath &lt;duong dan&gt;       -> bam nut theo path hierarchy
        ///   text &lt;ten field&gt; &lt;gia tri&gt;  -> dien text vao o input
        ///   key &lt;ten phim&gt;              -> gui 1 phim
        /// File bi xoa sau khi thuc thi xong tat ca lenh.
        /// </summary>
        private void ProcessCommandFile()
        {
            try
            {
                if (string.IsNullOrEmpty(_cmdPath))
                {
                    string dir = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(typeof(LiveWatcher).Assembly.Location) ?? ".", ".."));
                    _cmdPath = Path.Combine(dir, "GameCmd.txt");
                }

                if (!File.Exists(_cmdPath)) return;

                string[] lines;
                try { lines = File.ReadAllLines(_cmdPath, Encoding.UTF8); }
                catch { return; }

                try { File.Delete(_cmdPath); } catch { }

                foreach (string raw in lines)
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    Write("CMD    | " + line);
                    try { ExecuteCommand(line); }
                    catch (Exception ex) { Write("CMDERR | " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Write("CMDERR | ProcessCommandFile: " + ex.Message);
            }
        }

        private void ExecuteCommand(string line)
        {
            string[] parts = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string cmd = parts[0].ToLowerInvariant();

            switch (cmd)
            {
                case "dump":
                    DumpUi("lenh file");
                    break;

                case "click":
                    {
                        if (parts.Length < 2) { Write("CMDERR | thieu ten nut"); return; }
                        string target = line.Substring(parts[0].Length).Trim().Trim('"');
                        ClickButtonByText(target);
                        break;
                    }

                case "clickpath":
                    {
                        if (parts.Length < 2) { Write("CMDERR | thieu path"); return; }
                        string target = line.Substring(parts[0].Length).Trim().Trim('"');
                        ClickButtonByPath(target);
                        break;
                    }

                case "text":
                    {
                        if (parts.Length < 3) { Write("CMDERR | dung: text <field> <value>"); return; }
                        SetInputField(parts[1], parts[2]);
                        break;
                    }

                case "key":
                    {
                        if (parts.Length < 2) { Write("CMDERR | thieu ten phim"); return; }
                        if (Enum.TryParse(parts[1], true, out KeyCode kc))
                        {
                            Write("CMD    | gui phim " + kc + " (khong the mo phong Input, chi ghi log)");
                        }
                        break;
                    }

                case "getlevel":
                    Write("CMD    | level=" + PlayerInfo.TryGetLevel());
                    break;

                // ----- khám phá API (ApiDumper) -----
                case "types":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpTypes(parts.Length > 1 ? parts[1] : "");
                    break;

                case "api":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpTypeMembers(line.Substring(3).Trim());
                    break;

                case "pos":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpPos();
                    break;

                case "mobs":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpMobs();
                    break;

                case "gates":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpGates();
                    break;

                case "items":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpItems();
                    break;

                case "npcs":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpNpcs();
                    break;

                case "ibtn":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpInteractButtons();
                    break;

                case "abtn":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpAllButtons();
                    break;

                case "level":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpLevel();
                    break;

                case "auto":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpAutoAttack();
                    break;

                case "autoon":
                    {
                        bool ok = ApiDumper.SetAutoMode(true);
                        Write($"CMD    | autoon ok={ok} read={ApiDumper.ReadAutoPlay()} err='{ApiDumper.LastAutoError}'");
                        break;
                    }

                case "autooff":
                    {
                        bool ok = ApiDumper.SetAutoMode(false);
                        Write($"CMD    | autooff ok={ok} read={ApiDumper.ReadAutoPlay()}");
                        break;
                    }

                case "pms":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpPlayerMethods();
                    break;

                case "combat":
                    ApiDumper.Emit = Write;
                    ApiDumper.TestCombat();
                    break;

                case "pcomp":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpPlayerComponents();
                    break;

                case "skill":
                    ApiDumper.Emit = Write;
                    ApiDumper.TestSkill(parts.Length > 1 ? parts[1].Trim() : "Skill1");
                    break;

                case "listen":
                    ApiDumper.Emit = Write;
                    ApiDumper.CheckButtonListeners(parts.Length > 1 ? parts[1].Trim() : "Skill1");
                    break;

                case "pclick":
                    {
                        // pclick <ten nut>  -> bấm bằng IPointerDown/Up
                        string nm = parts.Length > 1 ? parts[1].Trim() : "Skill1";

                        Button target = null;
                        try
                        {
                            foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                            {
                                if (b == null || !b.gameObject.activeInHierarchy) continue;
                                if (string.Equals(b.gameObject.name, nm, StringComparison.OrdinalIgnoreCase))
                                { target = b; break; }
                            }
                        }
                        catch { }

                        if (target == null) { Write("CMDERR | khong thay nut " + nm); break; }

                        bool ok = ApiDumper.ExecuteClick(target);
                        Write($"CMD    | pclick '{nm}' ok={ok}");
                        break;
                    }

                case "mgm":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpMainGameMethods();
                    break;

                case "automob":
                    ApiDumper.Emit = Write;
                    ApiDumper.DumpAutoMob(parts.Length > 1 && parts[1].Trim() == "on");
                    break;

                case "automobadd":
                    {
                        // automobadd <mobInfoId> — thêm entry vào autoAttackMobData dict.
                        // Không id = bật hết quái trong map.
                        int _aid = -1;
                        if (parts.Length > 1) int.TryParse(parts[1].Trim(), out _aid);
                        bool _ok1; string _e1;
                        if (_aid > 0)
                        {
                            _ok1 = ApiDumper.SetAutoMobSingle(_aid, true);
                            _e1 = ApiDumper.LastAutoError;
                        }
                        else
                        {
                            int _n = ApiDumper.EnableAllMapMobs();
                            _ok1 = _n > 0; _e1 = _n + " ids (LastErr='" + ApiDumper.LastAutoError + "')";
                        }
                        ApiDumper.Emit = Write;
                        ApiDumper.DumpAutoMob(false);
                        Write($"CMD    | automobadd id={_aid} ok={_ok1} err='{_e1}'");
                        break;
                    }

                case "probe":
                    {
                        // probe start|stop|status
                        string _psub = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "status";
                        var _probe = CombatProbe.Instance;
                        if (_probe == null) { Write("CMDERR | chua cai CombatProbe"); break; }
                        if (_psub == "start") { _probe.Start(); Write("CMD    | probe start"); }
                        else if (_psub == "stop") { _probe.Stop(); Write("CMD    | probe stop"); }
                        else { Write($"CMD    | probe enabled={_probe.Enabled}"); }
                        break;
                    }

                case "npcgo":
                    {
                        // Thử tương tác NPC trực tiếp bằng API (không qua nút)
                        ApiDumper.Emit = Write;
                        ApiDumper.TestNpcInteract();
                        break;
                    }

                case "tp":
                    {
                        // tp <set|tf|move|warp|info> <x> <y>
                        // Lưu ý: line được Split(..., 3) nên phần thứ 3 có thể
                        // chứa cả "x y" -> tách lại ở đây.
                        string mode = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "info";

                        if (mode == "info") { ApiDumper.Emit = Write; ApiDumper.TeleportInfo(); break; }

                        string rest = parts.Length > 2 ? parts[2].Trim() : "";
                        string[] nums = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        if (nums.Length < 2)
                        {
                            Write("CMDERR | dung: tp <set|tf|move|warp> <x> <y>");
                            break;
                        }

                        float px = 0, py = 0;
                        float.TryParse(nums[0].Replace(',', '.'), out px);
                        float.TryParse(nums[1].Replace(',', '.'), out py);

                        ApiDumper.Emit = Write;
                        ApiDumper.Teleport(mode, px, py);
                        break;
                    }

                // ----- điều khiển RibbonBot -----
                case "ribbon":
                    {
                        if (parts.Length < 2) { Write("CMDERR | dung: ribbon start|stop|status"); return; }
                        HandleRibbonCommand(parts[1]);
                        break;
                    }

                default:
                    Write("CMDERR | lenh khong ho tro: " + cmd);
                    break;
            }
        }

        private void ClickButtonByText(string target)
        {
            if (string.IsNullOrEmpty(target)) return;

            Button[] buttons;
            try { buttons = UnityEngine.Object.FindObjectsOfType<Button>(true); }
            catch { return; }

            string want = Strip(target);

            // QUAN TRỌNG: game dùng object pool. Cùng một text có thể tồn tại ở:
            //   - nút THẬT đang hiển thị:  PopupCanvas/...Panel(Clone)/...
            //   - nút TEMPLATE trong pool:  DontDestroyObject/ObjectPoolHandler/...
            // Bấm vào template KHÔNG có tác dụng (hoặc bấm sai nút).
            // Vì vậy phải ưu tiên nút nằm ngoài ObjectPoolHandler.
            Button best = null;
            int bestScore = int.MinValue;

            foreach (var b in buttons)
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;

                string txt = Strip(GetButtonText(b) ?? "");
                string name = Strip(b.gameObject.name);

                bool textExact = (txt == want);
                bool nameExact = (name == want);
                bool textLoose = txt.Length > 0 && txt.Contains(want);
                bool nameLoose = name.Length > 0 && name.Contains(want);

                if (!textExact && !nameExact && !textLoose && !nameLoose) continue;

                string path = GetPath(b.gameObject);

                int score = 0;
                if (textExact) score += 1000;
                else if (nameExact) score += 900;
                else if (textLoose) score += 500;
                else score += 300;

                // Phạt nặng nút template trong pool
                if (path.IndexOf("ObjectPoolHandler", StringComparison.OrdinalIgnoreCase) >= 0)
                    score -= 5000;

                // Ưu tiên nút trong panel đang hiển thị
                if (path.IndexOf("PopupCanvas", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 200;

                if (score > bestScore) { bestScore = score; best = b; }
            }

            if (best == null)
            {
                Write("CMDERR | khong tim thay nut '" + target + "'");
                return;
            }

            Write("CLICK  | (lenh) " + SafeName(best.gameObject)
                  + " text='" + (GetButtonText(best) ?? "") + "'"
                  + " path='" + GetPath(best.gameObject) + "'");
            try { best.onClick.Invoke(); }
            catch (Exception ex) { Write("CMDERR | invoke loi: " + ex.Message); }
        }

        private void ClickButtonByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            Button[] buttons;
            try { buttons = UnityEngine.Object.FindObjectsOfType<Button>(true); }
            catch { return; }

            string want = path.Replace("\\", "/");

            foreach (var b in buttons)
            {
                if (b == null) continue;
                string p = GetPath(b.gameObject);
                if (p.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Write("CLICK  | (path) " + p);
                    try { b.onClick.Invoke(); } catch (Exception ex) { Write("CMDERR | " + ex.Message); }
                    return;
                }
            }

            Write("CMDERR | khong tim thay path '" + path + "'");
        }

        private void SetInputField(string fieldName, string value)
        {
            TMP_InputField[] fields;
            try { fields = UnityEngine.Object.FindObjectsOfType<TMP_InputField>(true); }
            catch { return; }

            string want = Strip(fieldName);

            foreach (var f in fields)
            {
                if (f == null) continue;
                if (Strip(f.gameObject.name).Contains(want))
                {
                    try
                    {
                        f.text = value;
                        Write("TEXT   | " + f.gameObject.name + " = '" + value + "'");
                    }
                    catch (Exception ex) { Write("CMDERR | " + ex.Message); }
                    return;
                }
            }

            Write("CMDERR | khong tim thay input '" + fieldName + "'");
        }

        private static string Strip(string s)
        {
            return (s ?? "").Trim().ToLowerInvariant();
        }

        private void HandleRibbonCommand(string sub)
        {
            try
            {
                var bot = RibbonBot.Instance;
                if (bot == null) { Write("CMDERR | chua cai RibbonBot"); return; }

                switch ((sub ?? "").Trim().ToLowerInvariant())
                {
                    case "start":
                        bot.StartBot();
                        Write("CMD    | ribbon start");
                        break;
                    case "stop":
                        bot.StopBot("lenh file");
                        Write("CMD    | ribbon stop");
                        break;
                    case "status":
                        Write($"CMD    | ribbon running={bot.IsRunning}");
                        break;
                    default:
                        Write("CMDERR | dung: ribbon start|stop|status");
                        break;
                }
            }
            catch (Exception ex) { Write("CMDERR | ribbon: " + ex.Message); }
        }

        // ---------- ghi file ----------

        private void Write(string line)
        {
            if (_broken || string.IsNullOrEmpty(_logPath)) return;

            _lineCount++;
            string s = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + "\n";

            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_logPath, s, new UTF8Encoding(false));
                }
            }
            catch { _broken = true; }

            // Hien song song tren cua so BepInEx de nguoi dung thay ngay khi thao tac.
            // Bo qua MOVE/INPUT de khong lam nghen console.
            if (line.StartsWith("MOVE") || line.StartsWith("INPUT")) return;

            try
            {
                if (line.StartsWith("ERROR") || line.StartsWith("CMDERR")) Log140.Error("[Live] " + line);
                else if (line.StartsWith("WARN")) Log140.Warn("[Live] " + line);
                else Log140.Info("[Live] " + line);
            }
            catch { }
        }

        // ---------- tien ich ----------

        private static string SafeSceneName()
        {
            try { return SceneManager.GetActiveScene().name ?? ""; }
            catch { return ""; }
        }

        private static string SafeName(GameObject go)
        {
            try { return go != null ? go.name : "(null)"; }
            catch { return "(?)"; }
        }

        private static string GetButtonText(Button btn)
        {
            if (btn == null) return null;

            try
            {
                var tmps = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (tmps != null)
                    foreach (var t in tmps)
                        if (t != null && !string.IsNullOrEmpty(t.text)) return t.text.Trim();
            }
            catch { }

            try
            {
                var texts = btn.GetComponentsInChildren<Text>(true);
                if (texts != null)
                    foreach (var t in texts)
                        if (t != null && !string.IsNullOrEmpty(t.text)) return t.text.Trim();
            }
            catch { }

            return null;
        }

        private static string GetPath(GameObject go)
        {
            if (go == null) return "(null)";

            var parts = new List<string>();
            var t = go.transform;
            int depth = 0;

            while (t != null && depth < 6)
            {
                parts.Insert(0, t.gameObject.name);
                t = t.parent;
                depth++;
            }

            if (t != null) parts.Insert(0, "...");
            return string.Join("/", parts);
        }
    }
}
