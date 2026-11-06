using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QLTK.Client140
{
    /// <summary>
    /// GameExplorer: tự động khám phá và dump thông tin game ra file log.
    ///
    /// Mục đích: giúp người dùng gửi thông tin game cho developer (Claude)
    /// để yêu cầu tạo tool mới mà không cần đọc console BepInEx.
    ///
    /// Khi vào MainGameScene, tự động tạo file:
    ///   BepInEx/GameExplorer.log
    /// chứa danh sách tất cả:
    ///   - Scene hiện tại, tất cả GameObjects quan trọng
    ///   - UI Panels, Buttons, Text
    ///   - Game managers (singleton classes)
    ///   - Player info
    ///   - Inventory panels, skill panels, v.v.
    ///
    /// Người dùng chỉ cần gửi file này cho Claude và nói:
    ///   "Đây là thông tin game, tôi muốn tool làm XYZ"
    /// </summary>
    internal sealed class GameExplorer : MonoBehaviour
    {
        public GameExplorer(IntPtr ptr) : base(ptr) { }

        private bool _explored;
        private float _waitTime;
        private string _logPath;
        private StringBuilder _sb;

        public static GameExplorer Install(BepInEx.Unity.IL2CPP.BasePlugin host)
        {
            try { ClassInjector.RegisterTypeInIl2Cpp<GameExplorer>(); }
            catch (Exception ex) { Log140.Error("[GameExplorer] Register loi: " + ex.Message); }

            var explorer = host.AddComponent<GameExplorer>();
            Log140.Info("[GameExplorer] Da cai dat. Se explore khi vao MainGameScene.");
            return explorer;
        }

        private void Update()
        {
            if (_explored) return;

            string scene = SafeSceneName();

            // Chờ vào MainGameScene rồi mới explore
            if (scene != "MainGameScene") return;

            // Chờ thêm 5 giây sau khi vào scene để UI load xong
            _waitTime += Time.deltaTime;
            if (_waitTime < 5f) return;

            _explored = true;
            RunExplore();
        }

        private void RunExplore()
        {
            try
            {
                string dir = Path.Combine(
                    Path.GetDirectoryName(typeof(GameExplorer).Assembly.Location) ?? ".",
                    "..");
                dir = Path.GetFullPath(dir);
                _logPath = Path.Combine(dir, "GameExplorer.log");
                _sb = new StringBuilder();

                Emit("=================================================================");
                Emit($"  GAME EXPLORER - Vu Tru Dai Chien (140)");
                Emit($"  Thoi gian: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Emit($"  Scene: {SafeSceneName()}");
                Emit("=================================================================");
                Emit("");

                // 1. Thông tin cơ bản
                ExploreBasicInfo();

                // 2. Tất cả UI Canvas
                ExploreCanvases();

                // 3. Tất cả Buttons
                ExploreButtons();

                // 4. Tất cả Text hiện trên màn hình
                ExploreVisibleText();

                // 5. Các Panel/Manager quan trọng
                ExploreGameManagers();

                // 6. Thông tin nhân vật
                ExplorePlayerInfo();

                // Ghi file
                File.WriteAllText(_logPath, _sb.ToString(), new UTF8Encoding(false));
                Log140.Info($"[GameExplorer] Da ghi {_logPath} ({_sb.Length} ky tu)");
                Log140.Info("[GameExplorer] Gui file nay cho Claude de yeu cau tool moi!");
            }
            catch (Exception ex)
            {
                Log140.Error("[GameExplorer] Loi: " + ex);
            }
        }

        // ------ Explore sections ------

        private void ExploreBasicInfo()
        {
            Section("THONG TIN CO BAN");
            Emit($"  Unity version: {Application.unityVersion}");
            Emit($"  Screen: {Screen.width}x{Screen.height}");
            Emit($"  FPS target: {Application.targetFrameRate}");
            Emit($"  Scene count: {SceneManager.sceneCount}");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                Emit($"  Scene[{i}]: name='{s.name}' loaded={s.isLoaded}");
            }
            Emit("");
        }

        private void ExploreCanvases()
        {
            Section("UI CANVASES");
            try
            {
                var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
                Emit($"  Tong so Canvas: {canvases.Length}");
                foreach (var c in canvases)
                {
                    if (c == null) continue;
                    string path = GetPath(c.gameObject);
                    Emit($"  Canvas: name='{c.gameObject.name}' active={c.gameObject.activeInHierarchy} " +
                         $"sortOrder={c.sortingOrder} renderMode={c.renderMode}");

                    // Liệt kê children panels (depth 1)
                    var t = c.transform;
                    for (int i = 0; i < t.childCount && i < 20; i++)
                    {
                        var child = t.GetChild(i);
                        if (child == null) continue;
                        Emit($"    Panel: name='{child.gameObject.name}' active={child.gameObject.activeInHierarchy}");
                    }
                }
            }
            catch (Exception ex) { Emit($"  LOI: {ex.Message}"); }
            Emit("");
        }

        private void ExploreButtons()
        {
            Section("BUTTONS (dang active)");
            try
            {
                var buttons = UnityEngine.Object.FindObjectsOfType<Button>(true);
                int active = 0;
                foreach (var btn in buttons)
                {
                    if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                    active++;
                    string text = GetButtonText(btn);
                    string path = GetPath(btn.gameObject);
                    Emit($"  BTN: name='{btn.gameObject.name}' text='{text ?? ""}' " +
                         $"interactable={btn.interactable} path={path}");
                }
                Emit($"  Tong: {active} active / {buttons.Length} total");
            }
            catch (Exception ex) { Emit($"  LOI: {ex.Message}"); }
            Emit("");
        }

        private void ExploreVisibleText()
        {
            Section("TEXT HIEN THI (TextMeshProUGUI, active, co noi dung)");
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true);
                int count = 0;
                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    string txt = t.text;
                    if (string.IsNullOrWhiteSpace(txt)) continue;
                    // Cắt text dài
                    if (txt.Length > 80) txt = txt.Substring(0, 80) + "...";
                    txt = txt.Replace("\n", "\\n").Replace("\r", "");

                    string parent = t.transform.parent != null ? t.transform.parent.gameObject.name : "(root)";
                    Emit($"  TXT: parent='{parent}' name='{t.gameObject.name}' text='{txt}'");
                    count++;
                    if (count > 200) { Emit("  ... (qua nhieu, cat bot)"); break; }
                }
                Emit($"  Tong: {count} text elements");
            }
            catch (Exception ex) { Emit($"  LOI: {ex.Message}"); }
            Emit("");
        }

        private void ExploreGameManagers()
        {
            Section("GAME MANAGERS (Singleton)");

            // Thử các class manager phổ biến
            TryFindManager<GameManager>("GameManager");
            TryFindManager<LoginUIManager>("LoginUIManager");

            // Tìm tất cả MonoBehaviour có tên chứa "Manager", "Controller", "Panel", "UI"
            try
            {
                var allMono = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
                var seen = new HashSet<string>();
                int count = 0;
                foreach (var m in allMono)
                {
                    if (m == null) continue;
                    string typeName;
                    try { typeName = m.GetType().Name; } catch { continue; }

                    if (seen.Contains(typeName)) continue;

                    bool interesting = typeName.Contains("Manager") || typeName.Contains("Controller")
                                    || typeName.Contains("Panel") || typeName.Contains("UI")
                                    || typeName.Contains("System") || typeName.Contains("Handler")
                                    || typeName.Contains("Service") || typeName.Contains("Data");

                    if (!interesting) continue;
                    seen.Add(typeName);
                    count++;

                    bool active = false;
                    try { active = m.gameObject.activeInHierarchy; } catch { }

                    Emit($"  {typeName}: obj='{m.gameObject.name}' active={active}");

                    if (count > 100) { Emit("  ... (qua nhieu, cat bot)"); break; }
                }
                Emit($"  Tong: {count} manager/panel/controller types");
            }
            catch (Exception ex) { Emit($"  LOI: {ex.Message}"); }
            Emit("");
        }

        private void ExplorePlayerInfo()
        {
            Section("THONG TIN NHAN VAT");
            try
            {
                var player = UnityEngine.Object.FindObjectOfType<MainPlayer>(true);
                if (player == null)
                {
                    Emit("  MainPlayer: KHONG TIM THAY");
                }
                else
                {
                    Emit($"  MainPlayer: name='{player.gameObject.name}' active={player.gameObject.activeInHierarchy}");
                    Emit($"  Position: {player.transform.position}");

                    // Liệt kê tất cả components trên MainPlayer
                    try
                    {
                        var comps = player.GetComponents<Component>();
                        foreach (var c in comps)
                        {
                            if (c == null) continue;
                            try { Emit($"    Component: {c.GetType().Name}"); } catch { }
                        }
                    }
                    catch { }

                    // Thử đọc level
                    int lv = PlayerInfo.TryGetLevel();
                    Emit($"  Level: {(lv >= 0 ? lv.ToString() : "khong doc duoc")}");
                }
            }
            catch (Exception ex) { Emit($"  LOI: {ex.Message}"); }
            Emit("");
        }

        // ------ Utilities ------

        private void TryFindManager<T>(string label) where T : UnityEngine.Object
        {
            try
            {
                var obj = UnityEngine.Object.FindObjectOfType<T>(true);
                if (obj != null)
                {
                    var go = (obj as Component)?.gameObject;
                    Emit($"  {label}: TIM THAY" + (go != null ? $" obj='{go.name}'" : ""));
                }
                else
                {
                    Emit($"  {label}: KHONG TIM THAY");
                }
            }
            catch (Exception ex) { Emit($"  {label}: LOI - {ex.Message}"); }
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
            while (t != null && depth < 5)
            {
                parts.Insert(0, t.gameObject.name);
                t = t.parent;
                depth++;
            }
            if (t != null) parts.Insert(0, "...");
            return string.Join("/", parts);
        }

        private static string SafeSceneName()
        {
            try { return SceneManager.GetActiveScene().name ?? ""; }
            catch { return ""; }
        }

        private void Section(string title)
        {
            Emit("─────────────────────────────────────────");
            Emit($"  {title}");
            Emit("─────────────────────────────────────────");
        }

        private void Emit(string line)
        {
            _sb?.AppendLine(line);
        }
    }
}
