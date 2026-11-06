using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QLTK.Client140
{
    /// <summary>
    /// GameApi: lớp bọc các API game 140 mà tool cần.
    ///
    /// Vì nhiều member bị obfuscate, mọi lời gọi đều qua reflection có kiểm tra
    /// null/khoảng giá trị, KHÔNG bao giờ ném exception ra ngoài. Nhờ vậy tool
    /// không bao giờ làm crash game dù game update đổi tên member.
    ///
    /// Các thứ đã dò được từ runtime (xem ApiDumper):
    ///   MainPlayer.moveTo(float x, float y) / .setPosition(Vector2) / .getPosition()
    ///   Character.isDied() / .getId()
    ///   ItemMapObject.interact() / .setPickByPlayer(Player) / .canPick() / .getCurrPosition()
    ///   MapGateway.getPosition() / .getToMapId() / .getId()
    ///   AutoAttackButton.NNKDCIFJNOH (bool: đang bật auto đánh)
    /// </summary>
    internal static class GameApi
    {
        // ---------- nhân vật ----------

        public static MainPlayer Me()
        {
            try { return UnityEngine.Object.FindObjectOfType<MainPlayer>(true); }
            catch { return null; }
        }

        public static Vector2? MyPos()
        {
            var me = Me();
            if (me == null) return null;
            try { return me.getPosition(); }
            catch
            {
                try { var p = me.transform.position; return new Vector2(p.x, p.y); }
                catch { return null; }
            }
        }

        /// <summary>
        /// Dịch chuyển nhân vật tới toạ độ.
        ///
        /// ĐÃ KIỂM CHỨNG trên client 140:
        ///   setPosition(Vector2) -> dịch chuyển TỨC THỜI, server chấp nhận,
        ///                           không bị kéo về vị trí cũ (test 4s giữ nguyên).
        ///   moveTo(x,y)          -> nhân vật BAY từ từ tới đích (chậm, mất thời gian).
        ///
        /// Vì vậy ưu tiên setPosition, chỉ dùng moveTo làm phương án cuối.
        /// </summary>
        public static bool MoveTo(float x, float y)
        {
            var me = Me();
            if (me == null) return false;

            // 1. setPosition: tức thời (ưu tiên số 1)
            try
            {
                me.setPosition(new Vector2(x, y));
                return true;
            }
            catch { }

            // 2. Gán thẳng transform (dự phòng)
            try
            {
                var p = me.transform.position;
                me.transform.position = new Vector3(x, y, p.z);
                return true;
            }
            catch { }

            // 3. moveTo: chỉ khi 2 cách trên lỗi (nhân vật sẽ bay từ từ)
            try
            {
                me.moveTo(x, y);
                return true;
            }
            catch { }

            return false;
        }

        public static float DistanceTo(float x, float y)
        {
            var p = MyPos();
            if (p == null) return float.MaxValue;
            return Vector2.Distance(p.Value, new Vector2(x, y));
        }

        public static int MyLevel()
        {
            // QUAN TRỌNG: level nhân vật nằm ở panel tên "Level" (text số).
            // "LevelText" (parent AppearanceInfo) là level KHÁC (không phải nhân vật),
            // đọc nó sẽ ra số lớn hơn và chọn sai mốc ruy băng.
            int lv = LevelFromPanel();
            if (lv >= 0) return lv;

            int fromUi = PlayerInfo.TryGetLevel();
            if (fromUi >= 0) return fromUi;

            string txt = ApiDumper.FindTextByObjectName("LevelText");
            if (!string.IsNullOrEmpty(txt) && int.TryParse(txt.Trim(), out int l2)) return l2;

            return -1;
        }

        /// <summary>
        /// Đọc level nhân vật từ panel "Level" (text số bên trong).
        /// Đây là nguồn đúng: khớp với log thật (level 81 -> chọn "Level 80 - 89").
        /// </summary>
        private static int LevelFromPanel()
        {
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshProUGUI>(true);
                int best = -1;

                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;

                    var par = t.transform.parent;
                    if (par == null) continue;
                    if (!string.Equals(par.gameObject.name, "Level", StringComparison.OrdinalIgnoreCase)) continue;

                    string raw = (t.text ?? "").Trim();
                    if (raw.Length == 0 || raw.Length > 6) continue;

                    if (int.TryParse(raw, out int v) && v >= 0)
                    {
                        // Ưu tiên số hợp lý (1..999)
                        if (v > 0 && v < 999) return v;
                        if (best < 0) best = v;
                    }
                }

                return best;
            }
            catch { }

            return -1;
        }

        public static bool IsDead()
        {
            var me = Me();
            if (me == null) return false;
            try { return me.isDied(); }
            catch { return false; }
        }

        // ---------- map / khu ----------

        public static string MapName()
        {
            string s = ApiDumper.FindTextByParentName("MapName");
            return string.IsNullOrEmpty(s) ? "" : s.Trim();
        }

        public static string ZoneName()
        {
            string s = ApiDumper.FindTextByParentName("ZoneObject");
            return string.IsNullOrEmpty(s) ? "" : s.Trim();
        }

        /// <summary>Map id + toạ độ server, đọc từ text dev dựng sẵn trong HUD.</summary>
        public static string DevInfo()
        {
            string s = ApiDumper.FindTextByParentName("AAAAAAAAAAA");
            return string.IsNullOrEmpty(s) ? "" : s.Trim();
        }

        /// <summary>Lấy map id hiện tại từ chuỗi "Map id: 14 | X: ... - Y: ...".</summary>
        public static int MapId()
        {
            string s = DevInfo();
            if (string.IsNullOrEmpty(s)) return -1;

            const string key = "Map id:";
            int i = s.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return -1;

            int j = i + key.Length;
            int end = j;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '-')) end++;

            string num = s.Substring(j, end - j).Trim();
            return int.TryParse(num, out int id) ? id : -1;
        }

        // ---------- quái ----------

        /// <summary>Lấy mobInfoId của quái (dùng cho MainGame.updateAutoMobValue).</summary>
        public static int MobInfoId(Mob m)
        {
            if (m == null) return -1;

            // Cách chính: Mob.getSelfInfo() -> LBLENEBILAB (giữ mobInfoId thật).
            // Từ runtime: autoAttackMobData key chính là mobInfoId này.
            try
            {
                object self = null;
                try { self = m.getSelfInfo(); } catch { }
                if (self == null) { try { self = m.LLIDGOILILA(); } catch { } }
                if (self == null) { try { self = m.HLJIDGGOLGK(); } catch { } }
                if (self != null)
                {
                    // Field public int BDEOJMLHHEK (0x14) — ứng viên mobInfoId
                    foreach (string fn in new[] { "BDEOJMLHHEK", "MODNMGEAPKB", "CMLLDGOHIBH", "AFGHNELMABP", "BEHJPLEEAFB" })
                    {
                        try
                        {
                            var fi = self.GetType().GetField(fn,
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (fi != null)
                            {
                                object v = fi.GetValue(self);
                                if (v != null)
                                {
                                    int id = Convert.ToInt32(v);
                                    if (id > 0) return id;
                                }
                            }
                        }
                        catch { }
                    }
                    // Method int không tham số (BIPIPMAJPJK, GHNLCJJGDGL, ELMFAIFMIPM...)
                    try
                    {
                        foreach (var mi in self.GetType().GetMethods(
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        {
                            try
                            {
                                if (mi.GetParameters().Length != 0 || mi.ReturnType != typeof(int)) continue;
                                if (mi.Name == "GetHashCode" || mi.Name == "GetInstanceID" || mi.Name.StartsWith("get_")) continue;
                                int id = (int)mi.Invoke(self, null);
                                if (id > 100 && id < 1000000) return id;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                // Thử property getMobInfoId / getInfoId / field mobInfoId
                foreach (string n in new[] { "getMobInfoId", "getInfoId", "getMonsterId", "getTemplateId", "getTypeId" })
                {
                    var mi = m.GetType().GetMethod(n,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (mi != null)
                    {
                        object v = mi.Invoke(m, null);
                        if (v != null) return Convert.ToInt32(v);
                    }
                }

                foreach (string n in new[] { "mobInfoId", "infoId", "monsterId", "templateId", "typeId" })
                {
                    var fi = m.GetType().GetField(n,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (fi != null)
                    {
                        object v = fi.GetValue(m);
                        if (v != null) return Convert.ToInt32(v);
                    }
                }
            }
            catch { }

            return -1;
        }

        public static List<Mob> AliveMobs(float maxDistance = 999f)
        {
            var result = new List<Mob>();
            Mob[] all;
            try { all = UnityEngine.Object.FindObjectsOfType<Mob>(true); }
            catch { return result; }

            var myPos = MyPos();

            foreach (var m in all)
            {
                if (m == null) continue;
                try
                {
                    if (!m.gameObject.activeInHierarchy) continue;
                    if (m.isDied()) continue;
                }
                catch { continue; }

                if (myPos != null)
                {
                    try
                    {
                        Vector3 p = m.transform.position;
                        if (Vector2.Distance(myPos.Value, new Vector2(p.x, p.y)) > maxDistance) continue;
                    }
                    catch { }
                }

                result.Add(m);
            }

            return result;
        }

        public static Vector2? MobPos(Mob m)
        {
            if (m == null) return null;
            try { return m.getPosition(); }
            catch
            {
                try { var p = m.transform.position; return new Vector2(p.x, p.y); }
                catch { return null; }
            }
        }

        public static string MobPosStr(Mob m)
        {
            if (m == null) return "null";
            try { return $"({m.transform.position.x:F2},{m.transform.position.y:F2})"; }
            catch { return "err"; }
        }

        /// <summary>Đọc HP hiện tại của quái (thử các getter khả dụng qua reflection).</summary>
        public static int MobHP(Mob m)
        {
            if (m == null) return -1;
            try
            {
                // Mob thừa kế ObjectMap -> scan property/field qua reflection
                var t = m.GetType();
                foreach (var prop in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    try
                    {
                        if (prop.PropertyType != typeof(int) && prop.PropertyType != typeof(float) && prop.PropertyType != typeof(long)) continue;
                        var nm = prop.Name.ToLower();
                        if (!nm.Contains("hp") && !nm.Contains("health") && !nm.Contains("life")) continue;
                        if (nm.Contains("max")) continue; // bỏ maxHP
                        var val = prop.GetValue(m);
                        if (val == null) continue;
                        if (val is int iv && iv > 0 && iv < 999999) return iv;
                        if (val is float fv && fv > 0 && fv < 999999) return (int)fv;
                        if (val is long lv && lv > 0 && lv < 999999) return (int)lv;
                    }
                    catch { }
                }
                // fallback field scan
                foreach (var fld in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    try
                    {
                        if (fld.FieldType != typeof(int) && fld.FieldType != typeof(float) && fld.FieldType != typeof(long)) continue;
                        var nm = fld.Name.ToLower();
                        if (!nm.Contains("hp") && !nm.Contains("health") && !nm.Contains("life")) continue;
                        if (nm.Contains("max")) continue;
                        var val = fld.GetValue(m);
                        if (val == null) continue;
                        if (val is int iv && iv > 0 && iv < 999999) return iv;
                        if (val is float fv && fv > 0 && fv < 999999) return (int)fv;
                        if (val is long lv && lv > 0 && lv < 999999) return (int)lv;
                    }
                    catch { }
                }
            }
            catch { }
            return -1;
        }

        /// <summary>Tìm quái gần nhất player (còn sống).</summary>
        public static Mob ClosestMob()
        {
            var me = Me();
            if (me == null) return null;
            Vector2 myPos;
            try { myPos = new Vector2(me.transform.position.x, me.transform.position.y); }
            catch { return null; }
            Mob best = null;
            float bestD = 999999f;
            foreach (var m in AliveMobs())
            {
                try
                {
                    var mp = new Vector2(m.transform.position.x, m.transform.position.y);
                    float d = Vector2.Distance(myPos, mp);
                    if (d < bestD) { bestD = d; best = m; }
                }
                catch { }
            }
            return best;
        }

        // ---------- cổng chuyển map ----------

        public static List<MapGateway> Gates()
        {
            var r = new List<MapGateway>();
            try
            {
                foreach (var g in UnityEngine.Object.FindObjectsOfType<MapGateway>(true))
                {
                    if (g == null) continue;
                    try { if (!g.gameObject.activeInHierarchy) continue; } catch { continue; }
                    r.Add(g);
                }
            }
            catch { }
            return r;
        }

        public static Vector2? GatePos(MapGateway g)
        {
            if (g == null) return null;
            try { return g.getPosition(); }
            catch
            {
                try { var p = g.transform.position; return new Vector2(p.x, p.y); }
                catch { return null; }
            }
        }

        public static int GateToMapId(MapGateway g)
        {
            if (g == null) return -1;
            try { return g.getToMapId(); }
            catch { return -1; }
        }

        // ---------- đồ rơi ----------

        public static List<ItemMapObject> GroundItems()
        {
            var r = new List<ItemMapObject>();
            try
            {
                foreach (var it in UnityEngine.Object.FindObjectsOfType<ItemMapObject>(true))
                {
                    if (it == null) continue;
                    try { if (!it.gameObject.activeInHierarchy) continue; } catch { continue; }
                    r.Add(it);
                }
            }
            catch { }
            return r;
        }

        public static Vector2? ItemPos(ItemMapObject it)
        {
            if (it == null) return null;
            try { return it.getCurrPosition(); }
            catch
            {
                try { var p = it.transform.position; return new Vector2(p.x, p.y); }
                catch { return null; }
            }
        }

        public static bool PickItem(ItemMapObject it)
        {
            if (it == null) return false;

            // Cách 1: interact() - đúng luồng game dùng khi người chơi bấm nhặt
            try { it.interact(); return true; } catch { }

            // Cách 2: gán người nhặt
            try
            {
                var me = Me();
                if (me != null) { it.setPickByPlayer(me); return true; }
            }
            catch { }

            return false;
        }

        // ---------- auto đánh ----------

        /// <summary>Trạng thái auto đánh (đọc từ cờ MainGame).</summary>
        public static bool AutoAttackOn()
        {
            return ReadAutoPlay();
        }

        private static float _lastAutoAttackClick = -99f;

        /// <summary>
        /// Đảm bảo auto đánh đang bật.
        /// QLTK 140: dùng MainGame.setPlayerOnAutoMode(true) — API chính thức.
        /// </summary>
        public static bool EnsureAutoAttack()
        {
            try
            {
                if (ReadAutoPlay()) return true;

                if (Time.realtimeSinceStartup - _lastAutoAttackClick < 3f) return false;
                _lastAutoAttackClick = Time.realtimeSinceStartup;

                return ApiDumper.SetAutoMode(true);
            }
            catch { }

            return false;
        }

        /// <summary>Đọc trạng thái auto đánh thật.</summary>
        public static bool ReadAutoPlay()
        {
            return ApiDumper.ReadAutoPlay();
        }

        // ---------- tiện ích UI ----------

        public static Button FindButtonByName(string name)
        {
            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    if (string.Equals(b.gameObject.name, name, StringComparison.OrdinalIgnoreCase)) return b;
                }
            }
            catch { }
            return null;
        }

        public static bool Click(Button b)
        {
            if (b == null) return false;
            try { b.onClick.Invoke(); return true; } catch { return false; }
        }

        public static bool ClickByName(string name)
        {
            return Click(FindButtonByName(name));
        }

        /// <summary>Bấm nút đầu tiên có text khớp (không dấu, không phân biệt hoa thường).</summary>
        public static Button FindButtonByText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string want = TextUtil.Strip(text);

            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;

                    string t = TextUtil.Strip(TextUtil.ButtonText(b));
                    if (t.Length == 0) continue;

                    if (t == want || t.Contains(want)) return b;
                }
            }
            catch { }

            return null;
        }

        public static string TextOf(Button b)
        {
            return TextUtil.ButtonText(b);
        }

        /// <summary>Đường dẫn đầy đủ của GameObject (tối đa 8 cấp cha).</summary>
        public static string PathOf(GameObject go)
        {
            if (go == null) return "";
            var parts = new List<string>();
            var t = go.transform;
            int depth = 0;
            while (t != null && depth < 8) { parts.Insert(0, t.gameObject.name); t = t.parent; depth++; }
            return string.Join("/", parts);
        }

        /// <summary>
        /// Kiểm tra panel theo tên có đang mở.
        /// Game dùng object-pool nên tên có hậu tố "(Clone)".
        /// Quét qua Transform là cách ổn định nhất trong IL2CPP.
        /// </summary>
        public static bool IsPanelOpen(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<RectTransform>(true);
                foreach (var rt in all)
                {
                    if (rt == null) continue;
                    if (!rt.gameObject.activeInHierarchy) continue;

                    string n = rt.gameObject.name;
                    if (n.Length < name.Length) continue;
                    if (n.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    return true;
                }
            }
            catch { }

            // fallback: GameObject
            try
            {
                var gos = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
                foreach (var go in gos)
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    if (go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>Tìm GameObject đang active theo tên (có hậu tố Clone).</summary>
        public static GameObject FindActive(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            try
            {
                var gos = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
                foreach (var go in gos)
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    if (string.Equals(go.name, name, StringComparison.OrdinalIgnoreCase)) return go;
                }
            }
            catch { }

            return null;
        }
    }

    /// <summary>Chuẩn hoá text tiếng Việt để so khớp (bỏ dấu, bỏ rich text).</summary>
    internal static class TextUtil
    {
        public static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            // bỏ thẻ rich text <color=...>
            try { s = System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty); } catch { }

            string formD;
            try { formD = s.Normalize(System.Text.NormalizationForm.FormD); }
            catch { return s.Trim().ToLowerInvariant(); }

            var sb = new System.Text.StringBuilder(formD.Length);
            foreach (char c in formD)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            string r = sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
            r = r.Replace('\u0111', 'd').Replace('\u0110', 'D');
            return r.Trim().ToLowerInvariant();
        }

        public static string ButtonText(Button b)
        {
            if (b == null) return "";

            try
            {
                var tmps = b.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (tmps != null)
                    foreach (var t in tmps)
                        if (t != null && !string.IsNullOrEmpty(t.text)) return t.text.Trim();
            }
            catch { }

            try
            {
                var texts = b.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                if (texts != null)
                    foreach (var t in texts)
                        if (t != null && !string.IsNullOrEmpty(t.text)) return t.text.Trim();
            }
            catch { }

            return "";
        }
    }
}
