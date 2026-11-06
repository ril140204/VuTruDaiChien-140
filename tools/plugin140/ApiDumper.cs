using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QLTK.Client140
{
    /// <summary>
    /// ApiDumper: công cụ khám phá API game 140 ngay lúc chạy.
    ///
    /// Dùng để developer tìm đúng method/field cần gọi mà không phải đoán tên
    /// (nhiều member bị obfuscate). Kết quả ghi vào GamePlay.log.
    ///
    /// Lệnh (gửi qua BepInEx/GameCmd.txt):
    ///   types &lt;tu khoa&gt;     -> liệt kê tên type chứa từ khoá
    ///   api &lt;TypeName&gt;      -> liệt kê toàn bộ member của type
    ///   pos                 -> map/zone/toạ độ/khu vực hiện tại
    ///   mobs                -> danh sách quái đang có (tên, toạ độ, hp)
    ///   gates               -> danh sách cổng chuyển map
    ///   items               -> danh sách đồ rơi trên đất
    ///   npcs                -> danh sách NPC gần đó
    /// </summary>
    internal static class ApiDumper
    {
        public static Action<string> Emit;

        // #region agent log
        private static void DbgLine(string hid, string run, string loc, string msg, string dataJson)
        {
            try
            {
                string line = "{\"sessionId\":\"2aec1d\",\"runId\":\"" + run + "\",\"hypothesisId\":\"" + hid + "\",\"location\":\"" + loc + "\",\"message\":\"" + msg + "\",\"data\":" + (string.IsNullOrEmpty(dataJson) ? "{}" : dataJson) + ",\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}";
                System.IO.File.AppendAllText("D:\\game\\VuTruDaiChien_140\\debug-2aec1d.log", line + "\n", System.Text.Encoding.UTF8);
            }
            catch { }
        }
        // #endregion

        private static readonly StringBuilder _sb = new StringBuilder(256);

        // ---------- tiện ích ----------

        private static Assembly GameAssembly()
        {
            try
            {
                var asm = Assembly.Load("Assembly-CSharp");
                if (asm != null) return asm;
            }
            catch { }

            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = "";
                    try { n = a.GetName().Name; } catch { }
                    if (n == "Assembly-CSharp") return a;
                }
            }
            catch { }

            return null;
        }

        private static Type[] SafeGetTypes(Assembly asm)
        {
            if (asm == null) return Array.Empty<Type>();
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                var list = new List<Type>();
                foreach (var t in ex.Types) if (t != null) list.Add(t);
                return list.ToArray();
            }
            catch { return Array.Empty<Type>(); }
        }

        // ---------- types ----------

        public static void DumpTypes(string keyword)
        {
            var asm = GameAssembly();
            if (asm == null) { Emit("APIERR | khong load duoc Assembly-CSharp"); return; }

            var types = SafeGetTypes(asm);
            Emit($"API    | Assembly-CSharp: {types.Length} types. Tim '{keyword}':");

            string kw = (keyword ?? "").ToLowerInvariant();
            int n = 0;

            foreach (var t in types)
            {
                if (t == null) continue;
                string full = "";
                try { full = t.FullName ?? t.Name; } catch { continue; }

                if (kw.Length > 0 && full.ToLowerInvariant().IndexOf(kw, StringComparison.Ordinal) < 0) continue;

                Emit("  TYPE " + full);
                if (++n >= 400) { Emit("  ... (cat bot)"); break; }
            }

            if (n == 0) Emit("  (khong co type nao khop)");
        }

        // ---------- api ----------

        public static void DumpTypeMembers(string typeName)
        {
            var asm = GameAssembly();
            if (asm == null) { Emit("APIERR | khong load duoc Assembly-CSharp"); return; }

            Type target = null;
            string want = (typeName ?? "").Trim();

            foreach (var t in SafeGetTypes(asm))
            {
                if (t == null) continue;
                string n = "", f = "";
                try { n = t.Name; f = t.FullName; } catch { continue; }

                if (string.Equals(n, want, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f, want, StringComparison.OrdinalIgnoreCase))
                {
                    target = t;
                    break;
                }
            }

            if (target == null) { Emit($"APIERR | khong thay type '{typeName}'"); return; }

            Emit($"===== API {target.FullName} =====");

            try { Emit($"  BASE: {target.BaseType?.FullName}"); } catch { }

            // Fields
            try
            {
                var fields = target.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Instance | BindingFlags.Static |
                                              BindingFlags.FlattenHierarchy);
                Emit($"  -- Fields ({fields.Length}):");
                foreach (var f in fields)
                {
                    string mod = f.IsStatic ? "static " : "";
                    string acc = f.IsPublic ? "public " : f.IsPrivate ? "private " : "protected ";
                    Emit($"     {acc}{mod}{f.FieldType.Name} {f.Name}");
                }
            }
            catch (Exception ex) { Emit("  FIELDERR " + ex.Message); }

            // Properties
            try
            {
                var props = target.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.Instance | BindingFlags.Static |
                                                 BindingFlags.FlattenHierarchy);
                Emit($"  -- Properties ({props.Length}):");
                foreach (var p in props)
                {
                    string g = p.GetGetMethod(true) != null ? "get" : "";
                    string s = p.GetSetMethod(true) != null ? "set" : "";
                    Emit($"     {p.PropertyType.Name} {p.Name} ({g}{s})");
                }
            }
            catch (Exception ex) { Emit("  PROPERR " + ex.Message); }

            // Methods
            try
            {
                var methods = target.GetMethods(BindingFlags.Public | BindingFlags.Instance |
                                                BindingFlags.Static | BindingFlags.DeclaredOnly);
                Emit($"  -- Methods ({methods.Length}):");
                foreach (var m in methods)
                {
                    _sb.Clear();
                    _sb.Append("     ");
                    if (m.IsStatic) _sb.Append("static ");
                    _sb.Append(m.ReturnType.Name).Append(' ').Append(m.Name).Append('(');

                    var ps = m.GetParameters();
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) _sb.Append(", ");
                        _sb.Append(ps[i].ParameterType.Name).Append(' ').Append(ps[i].Name);
                    }
                    _sb.Append(')');
                    Emit(_sb.ToString());
                }
            }
            catch (Exception ex) { Emit("  METHODERR " + ex.Message); }

            Emit("===== HET API =====");
        }

        // ---------- pos ----------

        /// <summary>
        /// Đọc thông tin map/nhân vật. Game có sẵn text dev "Map id: X | X: ... - Y: ..."
        /// nên đọc từ đó là chính xác nhất, không phải đoán API.
        /// </summary>
        public static void DumpPos()
        {
            Emit("===== POS =====");

            // toạ độ server (từ text dev)
            string dev = FindTextByParentName("AAAAAAAAAAA");
            if (!string.IsNullOrEmpty(dev)) Emit("  DEV    | " + dev);

            // tên map / khu
            string mapName = FindTextByParentName("MapName");
            if (!string.IsNullOrEmpty(mapName)) Emit("  MAP    | " + mapName);

            string zone = FindTextByParentName("ZoneObject");
            if (!string.IsNullOrEmpty(zone)) Emit("  ZONE   | " + zone);

            string levelText = FindTextByObjectName("LevelText");
            if (!string.IsNullOrEmpty(levelText)) Emit("  LEVEL  | " + levelText);

            // main player
            try
            {
                var p = UnityEngine.Object.FindObjectOfType<MainPlayer>(true);
                if (p != null)
                {
                    var pos = p.transform.position;
                    Emit($"  PLAYER | name='{p.gameObject.name}' pos=({pos.x:F2},{pos.y:F2},{pos.z:F2})");
                }
                else Emit("  PLAYER | khong tim thay MainPlayer");
            }
            catch (Exception ex) { Emit("  PLAYERERR " + ex.Message); }

            Emit("===== HET POS =====");
        }

        public static string FindTextByParentName(string parentName)
        {
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true);
                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    var par = t.transform.parent;
                    if (par == null) continue;
                    if (!string.Equals(par.gameObject.name, parentName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(t.text)) return t.text;
                }
            }
            catch { }
            return null;
        }

        public static string FindTextByObjectName(string objectName)
        {
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true);
                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    if (!string.Equals(t.gameObject.name, objectName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(t.text)) return t.text;
                }
            }
            catch { }
            return null;
        }

        // ---------- mobs ----------

        public static void DumpMobs()
        {
            Emit("===== MOBS =====");
            try
            {
                var mobs = UnityEngine.Object.FindObjectsOfType<Mob>(true);
                int alive = 0;

                foreach (var m in mobs)
                {
                    if (m == null) continue;

                    bool active = false;
                    try { active = m.gameObject.activeInHierarchy; } catch { }
                    if (!active) continue;

                    alive++;

                    Vector3 pos = m.transform.position;
                    string name = "";
                    try { name = m.gameObject.name; } catch { }

                    Emit($"  MOB  name='{name}' pos=({pos.x:F2},{pos.y:F2})");
                }

                Emit($"  TONG: {alive} mob active / {mobs.Length} total");
            }
            catch (Exception ex) { Emit("  MOBERR " + ex.Message); }
            Emit("===== HET MOBS =====");
        }

        // ---------- gates ----------

        public static void DumpGates()
        {
            Emit("===== GATES =====");
            try
            {
                var gates = UnityEngine.Object.FindObjectsOfType<MapGateway>(true);
                foreach (var g in gates)
                {
                    if (g == null) continue;

                    Vector2 gp;
                    try { gp = g.getPosition(); } catch { gp = Vector2.zero; }

                    Vector3 t = g.transform.position;
                    Emit($"  GATE name='{g.gameObject.name}' getPos=({gp.x:F2},{gp.y:F2}) " +
                         $"transform=({t.x:F2},{t.y:F2}) active={g.gameObject.activeInHierarchy}");
                }
                Emit($"  TONG: {gates.Length} gate");
            }
            catch (Exception ex) { Emit("  GATEERR " + ex.Message); }
            Emit("===== HET GATES =====");
        }

        // ---------- items ----------

        public static void DumpItems()
        {
            Emit("===== ITEMS TREN DAT =====");
            try
            {
                var items = UnityEngine.Object.FindObjectsOfType<ItemMapObject>(true);
                foreach (var it in items)
                {
                    if (it == null || !it.gameObject.activeInHierarchy) continue;
                    Vector3 p = it.transform.position;
                    string n = "";
                    try { n = it.gameObject.name; } catch { }
                    Emit($"  ITEM name='{n}' pos=({p.x:F2},{p.y:F2})");
                }
                Emit($"  TONG: {items.Length} item");
            }
            catch (Exception ex) { Emit("  ITEMERR " + ex.Message); }
            Emit("===== HET ITEMS =====");
        }

        /// <summary>Đếm số listener trên onClick của nút trong HUD (kiểm tra nút có tác dụng không).</summary>
        public static void CheckButtonListeners(string name)
        {
            Emit($"LISTEN | '{name}'");

            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    if (!string.Equals(b.gameObject.name, name, StringComparison.OrdinalIgnoreCase)) continue;

                    int persistent = 0;
                    try { persistent = b.onClick.GetPersistentEventCount(); } catch { }

                    // Đếm cả listener runtime qua reflection trên field m_Calls
                    int runtime = -1;
                    try
                    {
                        var f = typeof(UnityEngine.Events.UnityEventBase)
                            .GetField("m_Calls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (f != null)
                        {
                            var calls = f.GetValue(b.onClick);
                            var mf = calls?.GetType().GetField("m_RuntimeCalls",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var list = mf?.GetValue(calls) as System.Collections.ICollection;
                            if (list != null) runtime = list.Count;
                        }
                    }
                    catch { }

                    Emit($"LISTEN | persistent={persistent} runtime={runtime} interactable={b.interactable} path='{GameApi.PathOf(b.gameObject)}'");
                }
            }
            catch (Exception ex) { Emit("LISTENERR | " + ex.Message); }
        }

        /// <summary>
        /// Bấm nút bằng cách gọi thẳng IPointerDownHandler/IPointerUpHandler.
        ///
        /// RẤT QUAN TRỌNG: các nút trong HUD (Skill1..8, AutoAttackButton,
        /// ChangeTargetButton, InteractButton) KHÔNG dùng onClick — chúng cài
        /// IPointerDownHandler / IPointerUpHandler. Vì vậy onClick.Invoke()
        /// không có tác dụng gì.
        /// </summary>
        public static bool PointerClick(Button b)
        {
            if (b == null) return false;

            try
            {
                var go = b.gameObject;
                var handlers = go.GetComponents<UnityEngine.Component>();

                bool did = false;

                foreach (var h in handlers)
                {
                    if (h == null) continue;

                    var down = h.TryCast<UnityEngine.EventSystems.IPointerDownHandler>();
                    if (down != null)
                    {
                        try { down.OnPointerDown(MakeEventData()); did = true; } catch { }
                    }
                }

                foreach (var h in handlers)
                {
                    if (h == null) continue;

                    var up = h.TryCast<UnityEngine.EventSystems.IPointerUpHandler>();
                    if (up != null)
                    {
                        try { up.OnPointerUp(MakeEventData()); did = true; } catch { }
                    }
                }

                if (!did)
                {
                    foreach (var h in handlers)
                    {
                        if (h == null) continue;

                        var clk = h.TryCast<UnityEngine.EventSystems.IPointerClickHandler>();
                        if (clk != null)
                        {
                            try { clk.OnPointerClick(MakeEventData()); did = true; } catch { }
                        }
                    }
                }

                if (!did)
                {
                    try { b.onClick.Invoke(); return true; } catch { return false; }
                }

                return true;
            }
            catch { }

            return false;
        }

        private static UnityEngine.EventSystems.PointerEventData MakeEventData()
        {
            try
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es == null) return null;

                var data = new UnityEngine.EventSystems.PointerEventData(es);
                data.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
                data.clickCount = 1;
                data.pointerId = -1;
                data.pointerPress = null;
                data.rawPointerPress = null;
                data.position = new Vector2(Screen.width / 2f, Screen.height / 2f);
                data.pressPosition = data.position;
                return data;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Bấm nút qua EventSystem chuẩn của Unity (ExecuteEvents).
        /// Cách này xử lý đúng cả lớp obfuscated vì Unity tự dispatch tới handler.
        /// </summary>
        public static bool ExecuteClick(Button b)
        {
            if (b == null) return false;

            try
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es == null) return false;

                var go = b.gameObject;
                var data = MakeEventData();
                if (data == null) return false;

                data.pointerEnter = go;
                data.pointerPress = go;
                data.rawPointerPress = go;

                bool did = false;

                try
                {
                    UnityEngine.EventSystems.ExecuteEvents.Execute(
                        go,
                        data,
                        UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                    did = true;
                }
                catch { }

                try
                {
                    UnityEngine.EventSystems.ExecuteEvents.Execute(
                        go,
                        data,
                        UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                    did = true;
                }
                catch { }

                try
                {
                    UnityEngine.EventSystems.ExecuteEvents.Execute(
                        go,
                        data,
                        UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                    did = true;
                }
                catch { }

                // Gọi luôn Button.Press() nếu có
                try { b.Press(); did = true; } catch { }

                return did;
            }
            catch { }

            return false;
        }

        // ---------- key input test ----------

        /// <summary>Giả lập nhấn phím để test skill/nút (dùng Input API của Unity).</summary>
        public static void TestKey(string key)
        {
            Emit($"KEY    | '{key}'");

            try
            {
                var kc = Enum.Parse<UnityEngine.KeyCode>(key, true);
                Emit($"KEY    | KeyCode={kc}");

                // Không thể giả lập Input thật trong IL2CPP dễ dàng,
                // nhưng có thể bấm trực tiếp Button tương ứng phím tắt.
            }
            catch (Exception ex) { Emit("KEYERR | " + ex.Message); }
        }

        // ---------- auto attack data ----------

        /// <summary>Dump autoAttackMobData + thử bật tất cả quái vào danh sách auto.</summary>
        public static void DumpAutoMob(bool enableAll)
        {
            Emit("===== AUTO MOB DATA =====");
            try
            {
                var mg = FindMainGame();
                if (mg == null) { Emit("  khong thay MainGame"); return; }

                var dict = mg.autoAttackMobData;
                Emit($"  so entry = {(dict == null ? -1 : dict.Count)}");

                if (dict != null)
                {
                    int i = 0;
                    foreach (var kv in dict)
                    {
                        var d = kv.Value;
                        Emit($"   [{kv.Key}] mobInfoId={d.mobInfoId} selected={d.selected} specific={d.specificMobType}");
                        if (++i > 25) { Emit("   ..."); break; }
                    }
                }

                // Quái đang có trong map — đọc infoId qua getInfoInBar/getSelfInfo/getter int
                var mobs = GameApi.AliveMobs();
                Emit($"  quai trong map = {mobs.Count}");

                // #region agent log
                try
                {
                    string _js = "[]";
                    try
                    {
                        var _sb = new System.Text.StringBuilder("[");
                        int _n = 0;
                        foreach (var _m in mobs)
                        {
                            if (_n >= 6) break;
                            if (_m == null) continue;
                            var _cand = new System.Collections.Generic.List<string>();
                            try
                            {
                                var _t = _m.GetType();
                                var _mis = _t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                foreach (var _mi in _mis)
                                {
                                    try
                                    {
                                        if (_mi.GetParameters().Length != 0) continue;
                                        if (_mi.ReturnType != typeof(int)) continue;
                                        string _mn = _mi.Name;
                                        if (_mn == "getId" || _mn == "GetHashCode" || _mn == "GetInstanceID") continue;
                                        object _v = null;
                                        try { _v = _mi.Invoke(_m, null); } catch { continue; }
                                        _cand.Add(_mn + "=" + _v);
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                            _sb.Append("{\"pos\":\"\"+GameApi.MobPosStr(_m)+\"\",\"gets\":[" + string.Join(",", _cand.ConvertAll(_c => "\"" + _c.Replace("\"", "'") + "\"").ToArray()) + "},");
                            _n++;
                        }
                        if (_sb.Length > 1 && _sb[_sb.Length - 1] == ',') _sb.Length--;
                        _sb.Append("]");
                        _js = _sb.ToString();
                    }
                    catch { }
                    DbgLine("H5", "auto-tp2", "ApiDumper.cs:DumpAutoMob", "mob int getters", _js);
                }
                catch { }
                // #endregion

                var ids = new List<string>();
                foreach (var m in mobs)
                {
                    int id = GameApi.MobInfoId(m);
                    if (id > 0 && !ids.Contains(id.ToString())) ids.Add(id.ToString());
                }
                Emit("  mobInfoId trong map: " + string.Join(", ", ids));

                if (enableAll && ids.Count > 0)
                {
                    foreach (var s in ids)
                    {
                        int id = int.Parse(s);
                        try { mg.updateAutoMobValue(id, true); Emit($"   -> updateAutoMobValue({id}, true) OK"); }
                        catch (Exception ex) { Emit($"   -> updateAutoMobValue({id}) loi: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { Emit("  ERR " + ex.Message); }
            Emit("===== HET AUTO MOB =====");
        }

        // ---------- maingame methods ----------

        public static void DumpMainGameMethods()
        {
            Emit("===== MAINGAME METHODS =====");
            try
            {
                var mg = FindMainGame();
                if (mg == null) { Emit("  khong thay MainGame"); return; }
                Emit($"  type = {mg.GetType().FullName}");

                var flags = System.Reflection.BindingFlags.Public
                          | System.Reflection.BindingFlags.NonPublic
                          | System.Reflection.BindingFlags.Instance
                          | System.Reflection.BindingFlags.DeclaredOnly;

                foreach (var m in mg.GetType().GetMethods(flags))
                {
                    if (m.IsSpecialName) continue;
                    var ps = m.GetParameters();
                    var sb = new System.Text.StringBuilder();
                    sb.Append("  ").Append(m.ReturnType.Name).Append(' ').Append(m.Name).Append('(');
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(ps[i].ParameterType.Name);
                    }
                    sb.Append(')');
                    Emit(sb.ToString());
                }
            }
            catch (Exception ex) { Emit("  ERR " + ex.Message); }
            Emit("===== HET MAINGAME =====");
        }

        // ---------- skill test ----------

        /// <summary>Dò component trên nút skill HUD và thử kích hoạt.</summary>
        public static void TestSkill(string btnName)
        {
            Emit("===== SKILL TEST =====");

            string name = string.IsNullOrEmpty(btnName) ? "Skill1" : btnName.Trim();

            GameObject go = null;
            try
            {
                foreach (var t in UnityEngine.Object.FindObjectsOfType<RectTransform>(true))
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    if (string.Equals(t.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
                    { go = t.gameObject; break; }
                }
            }
            catch { }

            if (go == null) { Emit($"  khong thay '{name}'"); Emit("===== HET ====="); return; }

            Emit($"  GameObject '{go.name}' path='{GameApi.PathOf(go)}'");

            try
            {
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) continue;
                    Emit($"     comp: {c.GetType().FullName}");

                    // Liệt kê method public không tham số
                    try
                    {
                        var ms = c.GetType().GetMethods(
                            System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.DeclaredOnly);

                        foreach (var m in ms)
                        {
                            if (m.IsSpecialName) continue;
                            if (m.GetParameters().Length != 0) continue;
                            Emit($"        -> {m.ReturnType.Name} {m.Name}()");
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Emit("  ERR " + ex.Message); }

            Emit("===== HET SKILL TEST =====");
        }

        // ---------- player components ----------

        /// <summary>Liệt kê component trên GameObject MyPlayer + các method liên quan skill.</summary>
        public static void DumpPlayerComponents()
        {
            Emit("===== PLAYER COMPONENTS =====");
            try
            {
                var me = GameApi.Me();
                if (me == null) { Emit("  khong thay player"); return; }

                var go = me.gameObject;
                Emit($"  GameObject '{go.name}'");

                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) continue;
                    Emit($"  - {c.GetType().FullName}");
                }
            }
            catch (Exception ex) { Emit("  ERR " + ex.Message); }

            // Các manager chính trong scene
            Emit("  -- managers --");
            try
            {
                var mg = FindMainGame();
                if (mg != null)
                {
                    Emit("     MainGame: co");
                    var ui = mg.UIManager;
                    if (ui != null) Emit($"     UIManager: {ui.GetType().Name}");
                    var cm = mg.canvasManager;
                    if (cm != null) Emit($"     CanvasManager: {cm.GetType().Name}");
                    var foc = mg.focus;
                    if (foc != null) Emit($"     Focus: {foc.GetType().Name}");
                }
                else Emit("     MainGame: khong thay");
            }
            catch (Exception ex) { Emit("     managerERR " + ex.Message); }

            Emit("===== HET PLAYER COMPONENTS =====");
        }

        // ---------- combat test ----------

        /// <summary>
        /// Thử tấn công: chọn mục tiêu (ChangeTargetButton) rồi dùng skill.
        /// Trả về thông tin mục tiêu hiện tại + số quái.
        /// </summary>
        public static void TestCombat()
        {
            Emit("===== COMBAT TEST =====");

            var mg = FindMainGame();
            if (mg == null) { Emit("  khong thay MainGame"); return; }

            // Mục tiêu hiện tại
            try
            {
                var focus = mg.focus;
                if (focus != null)
                {
                    var tgt = focus.getTarget();
                    if (tgt == null) Emit("  target = null");
                    else
                    {
                        Emit($"  target = {tgt.GetType().Name}");

                        // Thử đọc vị trí/hp qua reflection (interop không sinh getter)
                        var tp = tgt.GetType().GetProperty("transform");
                        if (tp != null)
                        {
                            var tr = tp.GetValue(tgt) as UnityEngine.Transform;
                            if (tr != null) Emit($"     pos=({tr.position.x:F1},{tr.position.y:F1})");
                        }
                        Emit($"     id={TryGetter(tgt, "getId")}");
                    }
                }
                else Emit("  focus = null");
            }
            catch (Exception ex) { Emit("  targetERR " + ex.Message); }

            Emit($"  quai con: {GameApi.AliveMobs().Count}");
            Emit("===== HET COMBAT TEST =====");
        }

        private static string TryGetter(object obj, string name)
        {
            if (obj == null) return "";
            try
            {
                var m = obj.GetType().GetMethod(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (m != null) return m.Invoke(obj, null)?.ToString() ?? "";
            }
            catch { }
            return "";
        }

        // ---------- player methods ----------

        /// <summary>Liệt kê method của MainPlayer để tìm API tấn công.</summary>
        public static void DumpPlayerMethods()
        {
            Emit("===== PLAYER METHODS =====");
            try
            {
                var me = GameApi.Me();
                if (me == null) { Emit("  khong thay player"); return; }
                Emit($"  type = {me.GetType().FullName}");

                var flags = System.Reflection.BindingFlags.Public
                          | System.Reflection.BindingFlags.NonPublic
                          | System.Reflection.BindingFlags.Instance
                          | System.Reflection.BindingFlags.DeclaredOnly;

                var ms = me.GetType().GetMethods(flags);
                foreach (var m in ms)
                {
                    if (m.IsSpecialName) continue;
                    var ps = m.GetParameters();
                    var sb = new System.Text.StringBuilder();
                    sb.Append("  ").Append(m.ReturnType.Name).Append(' ').Append(m.Name).Append('(');
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(ps[i].ParameterType.Name);
                    }
                    sb.Append(')');
                    Emit(sb.ToString());
                }
            }
            catch (Exception ex) { Emit("  ERR " + ex.Message); }
            Emit("===== HET PLAYER METHODS =====");
        }

        // ---------- npc interact test ----------

        /// <summary>
        /// Thử gọi các method tương tác NPC để mở panel mà không cần nút.
        /// MCJDGKHGNJC (slot 140) nghi là "interact".
        /// </summary>
        public static void TestNpcInteract()
        {
            Emit("===== NPC INTERACT =====");

            var me = GameApi.Me();
            if (me == null) { Emit("  khong thay player"); return; }

            NPC best = null;
            float bestD = float.MaxValue;
            try
            {
                foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>(true))
                {
                    if (n == null || !n.gameObject.activeInHierarchy) continue;
                    Vector3 p = n.transform.position;
                    float d = Vector2.Distance(new Vector2(me.transform.position.x, me.transform.position.y),
                                               new Vector2(p.x, p.y));
                    Emit($"  NPC ({p.x:F1},{p.y:F1}) d={d:F2}");
                    if (d < bestD) { bestD = d; best = n; }
                }
            }
            catch (Exception ex) { Emit("  ERR " + ex.Message); return; }

            if (best == null) { Emit("  khong co NPC"); return; }

            Emit($"  -> chon NPC gan nhat d={bestD:F2}");

            // Thử MCJDGKHGNJC (slot 140)
            try { best.MCJDGKHGNJC(); Emit("  MCJDGKHGNJC() OK"); }
            catch (Exception ex) { Emit("  MCJDGKHGNJC loi: " + ex.Message); }

            Emit("===== HET NPC INTERACT =====");
        }

        // ---------- teleport test ----------

        /// <summary>
        /// Thử các cách dịch chuyển khác nhau để tìm cách nhanh nhất mà
        /// server vẫn chấp nhận (không bị kéo về).
        ///   tp set &lt;x&gt; &lt;y&gt;    -> dùng setPosition(Vector2)
        ///   tp tf  &lt;x&gt; &lt;y&gt;    -> gán transform.position
        ///   tp move &lt;x&gt; &lt;y&gt;   -> dùng moveTo (bay từ từ)
        ///   tp info             -> in vị trí hiện tại
        /// </summary>
        public static void Teleport(string mode, float x, float y)
        {
            var me = GameApi.Me();
            if (me == null) { Emit("TPERR | khong thay MainPlayer"); return; }

            var before = GameApi.MyPos();
            Emit($"TP     | mode={mode} target=({x:F1},{y:F1}) before=({before?.x:F1},{before?.y:F1})");

            string m = (mode ?? "").Trim().ToLowerInvariant();

            switch (m)
            {
                case "set":
                    try { me.setPosition(new Vector2(x, y)); Emit("TP     | -> setPosition OK"); }
                    catch (Exception ex) { Emit("TPERR  | setPosition: " + ex.Message); }
                    break;

                case "tf":
                    try
                    {
                        var p = me.transform.position;
                        me.transform.position = new Vector3(x, y, p.z);
                        Emit("TP     | -> transform.position OK");
                    }
                    catch (Exception ex) { Emit("TPERR  | transform: " + ex.Message); }
                    break;

                case "move":
                    try { me.moveTo(x, y); Emit("TP     | -> moveTo OK"); }
                    catch (Exception ex) { Emit("TPERR  | moveTo: " + ex.Message); }
                    break;

                case "warp":
                    // Kết hợp: setPosition + transform để cả client và logic đều nhận
                    try
                    {
                        me.setPosition(new Vector2(x, y));
                        var p2 = me.transform.position;
                        me.transform.position = new Vector3(x, y, p2.z);
                        Emit("TP     | -> warp (setPosition + transform) OK");
                    }
                    catch (Exception ex) { Emit("TPERR  | warp: " + ex.Message); }
                    break;

                default:
                    Emit("TPERR  | dung: tp set|tf|move|warp|info <x> <y>");
                    return;
            }
        }

        /// <summary>In vị trí hiện tại + danh sách quái gần nhất (đo khoảng cách).</summary>
        public static void TeleportInfo()
        {
            Emit("===== TP INFO =====");
            var me = GameApi.Me();
            if (me == null) { Emit("  khong thay MainPlayer"); Emit("===== HET ====="); return; }

            var pos = GameApi.MyPos();
            Vector3 tr = me.transform.position;
            Emit($"  MyPos()            = ({pos?.x:F2},{pos?.y:F2})");
            Emit($"  transform.position = ({tr.x:F2},{tr.y:F2},{tr.z:F2})");

            var mobs = GameApi.AliveMobs();
            int n = 0;
            foreach (var m in mobs)
            {
                var mp = GameApi.MobPos(m);
                if (mp == null) continue;
                Emit($"  MOB ({mp.Value.x:F2},{mp.Value.y:F2}) d={(pos != null ? Vector2.Distance(pos.Value, mp.Value) : -1):F2}");
                if (++n >= 5) break;
            }
            Emit($"  TONG {mobs.Count} mob");
            Emit("===== HET TP INFO =====");
        }

        // ---------- auto attack ----------

        /// <summary>
        /// Dò trạng thái auto đánh.
        /// QLTK 140: AutoAttackButton chỉ mở menu (Tab).
        /// Cờ thật: MainGame.isAutoAttackOn / isOnAutoMode
        /// + method setPlayerOnAutoMode(bool).
        ///
        /// Lưu ý: một số getter KHÔNG được Il2CppInterop sinh ra ở compile-time,
        /// nên phải gọi qua reflection.
        /// </summary>
        public static void DumpAutoAttack()
        {
            Emit("===== AUTO ATTACK =====");

            var mg = FindMainGame();
            if (mg == null) { Emit("  khong thay MainGame"); }
            else
            {
                Emit($"  MainGame name='{mg.gameObject.name}'");

                Emit($"     isAutoAttackOn = {GetBoolProp(mg, "isAutoAttackOn")}");
                Emit($"     isOnAutoMode   = {GetBoolProp(mg, "isOnAutoMode")}");
                Emit($"     isInitialized  = {GetBoolProp(mg, "isInitialized")}");

                try { Emit($"     autoAttackMobData.Count = {mg.autoAttackMobData?.Count}"); } catch { }

                Emit($"     BLJEMOOGPGD = {SafeBool(() => mg.BLJEMOOGPGD)}");
                Emit($"     NCAOKLGBPOI = {SafeBool(() => mg.NCAOKLGBPOI)}");
                Emit($"     BKJINDIJANL = {SafeBool(() => mg.BKJINDIJANL)}");
            }

            Emit("===== HET AUTO ATTACK =====");
        }

        private static string SafeBool(Func<bool> f)
        {
            try { return f().ToString(); } catch (Exception ex) { return "ERR(" + ex.GetType().Name + ")"; }
        }

        /// <summary>Đọc 1 property bool trên object qua reflection.</summary>
        public static bool GetBoolProp(object obj, string name)
        {
            if (obj == null) return false;
            try
            {
                var t = obj.GetType();
                var p = t.GetProperty(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(bool))
                    return (bool)p.GetValue(obj);

                var m = t.GetMethod("get_" + name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (m != null && m.ReturnType == typeof(bool))
                    return (bool)m.Invoke(obj, null);
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Bật/tắt chế độ tự động đánh bằng API chính thức của game.
        /// setPlayerOnAutoMode(bool) là method thật dùng cho nút auto.
        /// </summary>
        public static bool SetAutoMode(bool on)
        {
            var mg = FindMainGame();
            if (mg == null) { LastAutoError = "khong thay MainGame"; return false; }

            bool ok = false;

            // Method chính thức
            try { mg.setPlayerOnAutoMode(on); ok = true; }
            catch (Exception ex) { LastAutoError = "setPlayerOnAutoMode: " + ex.Message; }

            // Các cờ bổ trợ (nếu có)
            try { mg.BLJEMOOGPGD = on; } catch { }
            try { mg.NCAOKLGBPOI = on; } catch { }
            try { mg.BKJINDIJANL = on; } catch { }

            return ok;
        }

        public static string LastAutoError = "";
        public static string LastSkillError = "";

        /// <summary>
        /// Bật 1 mobInfoId vào danh sách auto: thêm entry mới vào
        /// MainGame.autoAttackMobData (Dictionary readonly nhưng MUTABLE —
        /// chỉ readonly reference, vẫn Add/Remove được).
        /// Sau đó gọi updateAutoAttackMobData() để game refresh UI/logic.
        /// Cách này bypass JKEBDKKFDDE (tên obfuscated, có thể đã đổi).
        /// </summary>
        public static bool SetAutoMobSingle(int id, bool on)
        {
            try
            {
                var mg = FindMainGame();
                if (mg == null) { LastAutoError = "khong thay MainGame"; return false; }
                var dict = mg.autoAttackMobData;
                if (dict == null) { LastAutoError = "autoAttackMobData null"; return false; }
                if (on)
                {
                    if (!dict.ContainsKey(id))
                    {
                        try
                        {
                            // ctor(int, bool, Nullable<int>) — bắt buộc 3 tham số
                            var empty = new Il2CppSystem.Nullable<int>();
                            var data = new AutoAttackMobData(id, true, empty);
                            dict.Add(id, data);
                        }
                        catch (Exception ex) { LastAutoError = "dict.Add: " + ex.Message; return false; }
                    }
                    else
                    {
                        try { dict[id].selected = true; } catch (Exception ex) { LastAutoError = "selected: " + ex.Message; return false; }
                    }
                }
                else
                {
                    try { if (dict.ContainsKey(id)) dict[id].selected = false; } catch { }
                }
                try { mg.updateAutoAttackMobData(); } catch { }
                try
                {
                    // sync qua JKEBDKKFDDE nếu còn tồn tại (không bắt buộc)
                    mg.JKEBDKKFDDE(id, on);
                }
                catch { }
                return true;
            }
            catch (Exception ex) { LastAutoError = ex.Message; return false; }
        }

        /// <summary>
        /// Bật TẤT CẢ mobInfoId đang có trong map vào auto list.
        /// Trả về số id đã thêm.
        /// </summary>
        public static int EnableAllMapMobs()
        {
            int added = 0;
            try
            {
                var mobs = GameApi.AliveMobs();
                var seen = new System.Collections.Generic.HashSet<int>();
                foreach (var m in mobs)
                {
                    int id = GameApi.MobInfoId(m);
                    if (id <= 0 || seen.Contains(id)) continue;
                    seen.Add(id);
                    if (SetAutoMobSingle(id, true)) added++;
                }
            }
            catch { }
            return added;
        }

        /// <summary>
        /// Tìm SkillButton trong HUD (slot 1-8) theo index (1-based).
        /// Return null nếu không thấy hoặc button bị disable.
        /// </summary>
        public static UnityEngine.UI.Button FindSkillButton(int slot)
        {
            LastSkillError = "";
            if (slot < 1 || slot > 8) { LastSkillError = "slot phai 1-8"; return null; }
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>(true);
                foreach (var b in all)
                {
                    try
                    {
                        if (b == null || b.gameObject == null) continue;
                        var nm = b.gameObject.name;
                        // HUDCanvas/SkillLayout/Skill1, Skill2, ..., SkillButton, SkillButton1...
                        if (!nm.Contains("Skill")) continue;
                        // check path chứa SkillLayout (không cần full path)
                        bool ok = false;
                        try
                        {
                            var tr = b.transform;
                            while (tr != null)
                            {
                                if (tr.gameObject.name.Contains("SkillLayout")) { ok = true; break; }
                                tr = tr.parent;
                            }
                        }
                        catch { }
                        if (!ok) continue;
                        // nếu tên là Skill1..8 hoặc SkillButton1..8 -> check số
                        if (nm.Length >= 6)
                        {
                            var last = nm.Substring(nm.Length - 1, 1);
                            if (int.TryParse(last, out int idx) && idx == slot)
                            {
                                if (!b.interactable) { LastSkillError = $"skill{slot} not interactable"; return null; }
                                return b;
                            }
                        }
                        // fallback: đếm thứ tự trong SkillLayout
                        var par = b.transform.parent;
                        if (par != null && par.gameObject.name.Contains("SkillLayout"))
                        {
                            int idx = b.transform.GetSiblingIndex();
                            if (idx == slot - 1)
                            {
                                if (!b.interactable) { LastSkillError = $"skill{slot} not interactable"; return null; }
                                return b;
                            }
                        }
                    }
                    catch { }
                }
                LastSkillError = $"khong tim skill{slot}";
                return null;
            }
            catch (Exception ex) { LastSkillError = ex.Message; return null; }
        }

        /// <summary>
        /// Bấm skill slot (1-8) vào quái gần nhất. Trả về true nếu click ok.
        /// Không bật auto game — chỉ click skill 1 lần.
        /// </summary>
        public static bool ClickSkill(int slot)
        {
            LastSkillError = "";
            var btn = FindSkillButton(slot);
            if (btn == null) return false;
            try
            {
                var target = GameApi.ClosestMob();
                if (target == null) { LastSkillError = "khong co quai"; return false; }
                // bấm skill thẳng (không set focus — game tự nhận target gần nhất)
                btn.onClick?.Invoke();
                return true;
            }
            catch (Exception ex) { LastSkillError = ex.Message; return false; }
        }

        /// <summary>
        /// Gọi updateAutoMobValue(int, bool) qua reflection (dò overload đúng).
        /// </summary>
        public static bool SetAutoMobReflect(int id, bool on)
        {
            try
            {
                var mg = FindMainGame();
                if (mg == null) { LastAutoError = "khong thay MainGame"; return false; }
                foreach (var mi in mg.GetType().GetMethods(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    try
                    {
                        if (mi.Name != "updateAutoMobValue" && mi.Name != "JKEBDKKFDDE") continue;
                        var ps = mi.GetParameters();
                        if (ps.Length != 2 || ps[0].ParameterType != typeof(int) || ps[1].ParameterType != typeof(bool)) continue;
                        mi.Invoke(mg, new object[] { id, on });
                        return true;
                    }
                    catch { }
                }
                LastAutoError = "khong tim overload updateAutoMobValue(int,bool)";
                return false;
            }
            catch (Exception ex) { LastAutoError = ex.Message; return false; }
        }

        /// <summary>Đọc trạng thái auto đánh thật (qua reflection cho getter).</summary>
        public static bool ReadAutoPlay()
        {
            var mg = FindMainGame();
            if (mg == null) return false;

            if (GetBoolProp(mg, "isAutoAttackOn")) return true;
            if (GetBoolProp(mg, "isOnAutoMode")) return true;

            try { if (mg.BLJEMOOGPGD) return true; } catch { }
            try { if (mg.NCAOKLGBPOI) return true; } catch { }

            return false;
        }

        /// <summary>Tìm MainGame (singleton GCCMPOOGKDM hoặc quét scene).</summary>
        public static MainGame FindMainGame()
        {
            try
            {
                var mg = UnityEngine.Object.FindObjectOfType<MainGame>(true);
                if (mg != null) return mg;
            }
            catch { }

            try
            {
                var mg = MainGame.GCCMPOOGKDM;
                if (mg != null) return mg;
            }
            catch { }

            return null;
        }

        // ---------- level ----------

        /// <summary>
        /// Dò mọi nơi có thể chứa level nhân vật.
        /// Bản 140 hiển thị level ở vài chỗ khác nhau (HUD, AppearanceInfo,
        /// CharacterMenu), và tên panel đã bị obfuscate nên phải quét tự động.
        /// </summary>
        public static void DumpLevel()
        {
            Emit("===== LEVEL =====");

            // 1. Text tên "LevelText"
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true);
                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    string n = t.gameObject.name;
                    if (n.IndexOf("Level", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string par = t.transform.parent != null ? t.transform.parent.gameObject.name : "";
                    string raw = (t.text ?? "").Trim();
                    if (raw.Length == 0) continue;

                    Emit($"  T name='{n}' parent='{par}' text='{raw}'");
                }
            }
            catch (Exception ex) { Emit("  LEVELERR " + ex.Message); }

            // 2. Panel AppearanceInfo
            Emit("  -- AppearanceInfo children:");
            try
            {
                var gos = UnityEngine.Object.FindObjectsOfType<Transform>(true);
                foreach (var tr in gos)
                {
                    if (tr == null || !tr.gameObject.activeInHierarchy) continue;
                    if (tr.gameObject.name != "AppearanceInfo") continue;

                    var children = tr.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var c in children)
                    {
                        if (c == null) continue;
                        Emit($"     {c.gameObject.name} = '{c.text}'");
                    }
                }
            }
            catch (Exception ex) { Emit("  APPEERR " + ex.Message); }

            // 3. Kết quả từ các hàm đọc sẵn có
            Emit($"  PlayerInfo.TryGetLevel() = {PlayerInfo.TryGetLevel()}");
            Emit($"  GameApi.MyLevel()        = {GameApi.MyLevel()}");

            // 4. Mọi text dạng số trên HUD (để đối chiếu thủ công)
            Emit("  -- Text so tren man hinh:");
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true);
                int n = 0;
                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    string raw = (t.text ?? "").Trim();
                    if (raw.Length == 0 || raw.Length > 12) continue;

                    bool allDigits = true;
                    foreach (char c in raw) if (!char.IsDigit(c) && c != '%' && c != '.') { allDigits = false; break; }
                    if (!allDigits) continue;

                    string par = t.transform.parent != null ? t.transform.parent.gameObject.name : "";
                    Emit($"     '{raw}' (name='{t.gameObject.name}' parent='{par}')");
                    if (++n >= 40) break;
                }
            }
            catch { }

            Emit("===== HET LEVEL =====");
        }

        // ---------- npcs ----------
        public static void DumpNpcs()
        {
            Emit("===== NPC =====");
            try
            {
                var npcs = UnityEngine.Object.FindObjectsOfType<NPC>(true);
                foreach (var n in npcs)
                {
                    if (n == null || !n.gameObject.activeInHierarchy) continue;
                    Vector3 p = n.transform.position;
                    string name = "";
                    try { name = n.gameObject.name; } catch { }
                    Emit($"  NPC  name='{name}' pos=({p.x:F2},{p.y:F2}) text='{NearbyName(n)}'");
                }
                Emit($"  TONG: {npcs.Length} npc");
            }
            catch (Exception ex) { Emit("  NPCERR " + ex.Message); }
            Emit("===== HET NPC =====");
        }

        /// <summary>Đọc text world-space gần object (tên NPC hiện trên đầu).</summary>
        public static string NearbyName(NPC npc)
        {
            try
            {
                Vector3 pos = npc.transform.position;
                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshPro>(true);

                string best = "";
                float bestD = 8f;

                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    string s = t.text;
                    if (string.IsNullOrEmpty(s)) continue;

                    float d = Vector3.Distance(t.transform.position, pos);
                    if (d < bestD) { bestD = d; best = s.Trim(); }
                }

                return best;
            }
            catch { }
            return "";
        }

        // ---------- interact panel buttons ----------

        /// <summary>
        /// Liệt kê các nút đang active trong NpcInteractPanel / InteractPanel.
        /// Đây là nơi game đặt các lựa chọn (Tham gia bằng vé, mốc level, Nhận thưởng).
        /// </summary>
        public static void DumpInteractButtons()
        {
            Emit("===== INTERACT BUTTONS =====");

            try
            {
                var btns = UnityEngine.Object.FindObjectsOfType<Button>(true);
                int n = 0;

                foreach (var b in btns)
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;

                    string path = PathOf(b.gameObject);
                    bool inPanel = path.IndexOf("InteractPanel", StringComparison.OrdinalIgnoreCase) >= 0
                                || path.IndexOf("NpcInteract", StringComparison.OrdinalIgnoreCase) >= 0
                                || path.IndexOf("ObjectPoolHandler", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!inPanel) continue;

                    Emit($"  BTN path='{path}' text='{ButtonText(b)}' interactable={b.interactable}");
                    n++;
                }

                Emit($"  TONG: {n} nut trong panel");

                // Trạng thái panel
                Emit("  Panel NpcInteractPanel dang mo? " + GameApi.IsPanelOpen("NpcInteractPanel"));
                Emit("  Panel MilestoneRewardPanel dang mo? " + GameApi.IsPanelOpen("MilestoneRewardPanel"));
                Emit("  Panel CharacterMenu dang mo? " + GameApi.IsPanelOpen("CharacterMenu"));
                Emit("  Panel CapsulePanel dang mo? " + GameApi.IsPanelOpen("CapsulePanel"));
            }
            catch (Exception ex) { Emit("  ERR " + ex.Message); }

            Emit("===== HET INTERACT BUTTONS =====");
        }

        /// <summary>Liệt kê toàn bộ nút active kèm text (dùng để tìm nút theo tên).</summary>
        public static void DumpAllButtons()
        {
            Emit("===== TAT CA BUTTON =====");
            try
            {
                var btns = UnityEngine.Object.FindObjectsOfType<Button>(true);
                int n = 0;
                foreach (var b in btns)
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    Emit($"  BTN '{b.gameObject.name}' text='{ButtonText(b)}' path='{PathOf(b.gameObject)}'");
                    if (++n >= 250) { Emit("  ..."); break; }
                }
                Emit($"  TONG: {n}");
            }
            catch (Exception ex) { Emit("  ERR " + ex.Message); }
            Emit("===== HET BUTTONS =====");
        }

        public static string PathOf(GameObject go)
        {
            if (go == null) return "";
            var parts = new List<string>();
            var t = go.transform;
            int depth = 0;
            while (t != null && depth < 8) { parts.Insert(0, t.gameObject.name); t = t.parent; depth++; }
            return string.Join("/", parts);
        }

        public static string ButtonText(Button b)
        {
            return TextUtil.ButtonText(b);
        }
    }
}
