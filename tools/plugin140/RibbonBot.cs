using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.UI;

namespace QLTK.Client140
{
    /// <summary>
    /// RibbonBot: tự động đi Ruy Băng đỏ (hoặc màu khác nếu cấu hình).
    ///
    /// LUỒNG HOÀN CHỈNH (theo mô tả người dùng + log thực tế):
    ///
    ///   1. Về Vùng Hoang Dã bằng CapsulePanel (teleport: chọn "Vùng Hoang Dã").
    ///   2. Đi tới NPC Đại tá Silver, bấm InteractButton để mở NpcInteractPanel.
    ///   3. Bấm "Tham gia bằng vé" -> chọn mốc level phù hợp với level nhân vật.
    ///   4. Trong ruy băng: bật auto đánh, dịch chuyển tới từng quái.
    ///   5. Hết quái map hiện tại -> đi tới cổng, Enter qua map kế tiếp.
    ///   6. Lặp tới tầng thượng; hút toàn bộ đồ rơi.
    ///   7. Gặp Tổng Tư Lệnh -> "Nhận thưởng" -> bấm hết các "Nhận ngay".
    ///   8. Nếu túi đầy: mở Túi đồ -> "Sắp xếp" -> đóng -> nhận tiếp.
    ///
    /// Mọi bước đều có timeout và ghi log để dễ chẩn đoán.
    /// </summary>
    internal sealed class RibbonBot : MonoBehaviour
    {
        public RibbonBot(IntPtr ptr) : base(ptr) { }

        // ---------------- cấu hình ----------------
        internal sealed class Config
        {
            public bool Enabled;
            public string MapHome = "Vùng Hoang Dã";     // map chứa NPC vào ruy băng
            public string NpcName = "Silver";            // Đại tá Silver
            // Bot thử lần lượt các nhãn (nhãn đúng phụ thuộc bản game).
            // Thứ tự từ cụ thể -> tổng quát.
            public string[] EntryOptions = { "Tham gia miễn phí", "Tham gia bằng vé", "Tham gia" };
            // Ruy băng có thời hạn 30 phút. Nếu đã mở 1 lần mà chưa đánh hết quái
            // thì NPC hiện nút "Tham gia tiếp" -> vào lại ngay, KHÔNG cần chọn level.
            public string[] ResumeOptions = { "Tham gia tiếp", "Tiếp tục tham gia", "Vào tiếp", "Tham gia lại" };
            public string RewardOption = "Nhận thưởng";
            public string SubmitText = "Nhận ngay";
            public string EnterGameText = "Vào game";
            public int MaxMaps = 12;                     // số map tối đa 1 lượt
            public float MoveTimeout = 12f;              // giây tối đa để tới 1 điểm
            public float StepTimeout = 40f;
            public int KillWaitSeconds = 90;              // chờ hết quái 1 map
            public float PickupRadius = 60f;
            public bool AutoPickup = true;
            public int MaxRewardLoops = 40;
        }

        private enum Phase
        {
            Idle,
            GoHome,
            GoNpc,
            TalkNpc,
            ChooseEntry,
            WaitEnter,
            ClearMap,
            GoGate,
            NextMap,
            GoRewardNpc,
            ClaimReward,
            SortInventory,
            Finish,
            Failed
        }

        private Config _cfg = new Config();

        /// <summary>Instance đang chạy. Tìm bằng FindObjectOfType không ổn với
        /// custom IL2CPP MonoBehaviour nên giữ tham chiếu trực tiếp.</summary>
        internal static RibbonBot Instance;

        private Phase _phase = Phase.Idle;
        private float _phaseTime;
        private float _logTime;
        private int _mapCount;
        private int _rewardLoops;
        private int _sortLoops;
        private int _rewardStall;    // số vòng liên tiếp không bấm được nút "Nhận ngay"
        private int _lastMapId = -1;

        private bool _entered;
        private bool _rewardPanelOpened;
        private bool _entryLogged;

        /// <summary>true = đang vào lại ruy băng đã mở (nút "Tham gia tiếp"),
        /// không cần chọn level.</summary>
        private bool _resumeMode;

        /// <summary>Quái đang đánh (chỉ để phát hiện đổi mục tiêu -> teleport 1 lần).</summary>
        private Mob _lastTargetMob;

        /// <summary>Thời gian đứng đánh cùng 1 con mà nó không chết (đánh không trúng).</summary>
        private float _stuckTime;

        /// <summary>
        /// Điểm đứng cách quái `gap` mét (về phía nhân vật đang đứng) để đánh thường trúng.
        /// Teleport TRÙNG vị trí quái sẽ bị lọt vào trong quái -> đấm gió không dame.
        /// </summary>
        private static Vector2 ClosePoint(Vector2? my, Vector2 mob, float gap)
        {
            if (my == null) return mob;
            Vector2 dir = my.Value - mob;
            if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(1f, 0f);
            dir.Normalize();
            return mob + dir * gap;
        }

        /// <summary>Chỉ số nhãn đã bấm trong EntryOptions (0 = miễn phí, 1 = bằng vé).</summary>
        private int _entryIndex = -1;

        /// <summary>Mốc thời gian sớm nhất được bấm InteractButton tiếp
        /// (nút này là toggle nên phải có cooldown).</summary>
        private float _interactCooldown;

        /// <summary>Mốc thời gian sớm nhất được bấm skill tiếp.</summary>
        private float _attackCooldown;

        /// <summary>Mốc phase-time sớm nhất được bấm cổng tiếp (chống spam toggle).</summary>
        private float _gateCooldown;

        private readonly List<string> _trace = new List<string>(64);

        // ---------------- cài đặt ----------------

        public static RibbonBot Install(BepInEx.Unity.IL2CPP.BasePlugin host)
        {
            try { ClassInjector.RegisterTypeInIl2Cpp<RibbonBot>(); }
            catch (Exception ex) { Log140.Error("[Ribbon] Register loi: " + ex.Message); }

            var b = host.AddComponent<RibbonBot>();
            Instance = b;
            b.Log("Da cai. F10 = bat dau, F11 = dung.");
            return b;
        }

        // ---------------- API điều khiển ----------------

        public void StartBot()
        {
            if (_cfg.Enabled) { Log("Dang chay roi."); return; }

            _cfg.Enabled = true;
            _mapCount = 0;
            _rewardLoops = 0;
            _sortLoops = 0;
            _rewardStall = 0;
            _lastMapId = -1;
            _entered = false;
            _lastTargetMob = null;
            _stuckTime = 0f;
            _rewardPanelOpened = false;
            _resumeMode = false;
            _entryIndex = -1;
            _entryLogged = false;
            _interactCooldown = 0f;
            _attackCooldown = 0f;
            _gateCooldown = 0f;
            _trace.Clear();

            Log("===== BAT DAU RUY BANG =====");
            Log($"Level nhan vat: {GameApi.MyLevel()} | Map: '{GameApi.MapName()}' | {GameApi.DevInfo()}");

            Transition(Phase.GoHome);
        }

        public void StopBot(string reason = "nguoi dung")
        {
            if (!_cfg.Enabled) return;

            _cfg.Enabled = false;
            Log("===== DUNG (" + reason + ") =====");
            DumpTrace();
            Transition(Phase.Idle);
        }

        public bool IsRunning => _cfg.Enabled;

        // ---------------- vòng lặp ----------------

        private void Update()
        {
            // Phím tắt
            try
            {
                if (Input.GetKeyDown(KeyCode.F10)) StartBot();
                if (Input.GetKeyDown(KeyCode.F11)) StopBot("F11");
            }
            catch { }

            if (!_cfg.Enabled) return;

            _phaseTime += Time.deltaTime;
            _logTime += Time.deltaTime;

            try
            {
                switch (_phase)
                {
                    case Phase.GoHome: TickGoHome(); break;
                    case Phase.GoNpc: TickGoNpc(); break;
                    case Phase.TalkNpc: TickTalkNpc(); break;
                    case Phase.ChooseEntry: TickChooseEntry(); break;
                    case Phase.WaitEnter: TickWaitEnter(); break;
                    case Phase.ClearMap: TickClearMap(); break;
                    case Phase.GoGate: TickGoGate(); break;
                    case Phase.NextMap: TickNextMap(); break;
                    case Phase.GoRewardNpc: TickGoRewardNpc(); break;
                    case Phase.ClaimReward: TickClaimReward(); break;
                    case Phase.SortInventory: TickSortInventory(); break;
                }
            }
            catch (Exception ex)
            {
                Log("LOI Update (" + _phase + "): " + ex.Message);
                Transition(Phase.Failed);
            }
        }

        // ---------------- 1. về Vùng Hoang Dã ----------------

        private void TickGoHome()
        {
            string mapNow = GameApi.MapName();

            if (TextUtil.Strip(mapNow).Contains(TextUtil.Strip(_cfg.MapHome)))
            {
                Log("Da o " + _cfg.MapHome);
                Transition(Phase.GoNpc);
                return;
            }

            // Dùng CapsulePanel để dịch chuyển (đúng như log: bấm teleport rồi chọn map)
            if (_phaseTime < 1f) return;

            if (!OpenCapsulePanel())
            {
                if (_phaseTime > 8f) Fail("khong mo duoc CapsulePanel");
                return;
            }

            // Chọn mục map home trong danh sách
            if (ClickCapsuleEntry(_cfg.MapHome))
            {
                Log("Teleport ve " + _cfg.MapHome);
                Transition(Phase.GoNpc);
                return;
            }

            if (_phaseTime > 12f) Fail("khong thay muc '" + _cfg.MapHome + "' trong CapsulePanel");
        }

        private bool OpenCapsulePanel()
        {
            // Nếu panel đã mở thì thôi
            if (GameApi.IsPanelOpen("CapsulePanel")) return true;

            // Mở bằng nút teleport trên HUD (SlotUseItemButton / nút phi thuyền)
            foreach (string n in new[] { "SlotUseItemButton", "SlotUseItemButton1", "FlyButton" })
            {
                if (GameApi.ClickByName(n)) return true;
            }

            return false;
        }

        private bool ClickCapsuleEntry(string wanted)
        {
            string want = TextUtil.Strip(wanted);

            try
            {
                // CapsuleObject chứa text "Vùng Hoang Dã (Lv15)"
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;

                    string path = PathOf(b.gameObject);
                    if (path.IndexOf("CapsulePanel", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string t = TextUtil.Strip(GameApi.TextOf(b));
                    if (t.Length == 0) continue;
                    if (!t.Contains(want)) continue;

                    Log("Chon capsule: " + GameApi.TextOf(b));
                    return GameApi.Click(b);
                }
            }
            catch { }

            return false;
        }

        // ---------------- 2. tới NPC ----------------

        private void TickGoNpc()
        {
            if (_phaseTime > _cfg.StepTimeout) { Fail("khong toi duoc NPC " + _cfg.NpcName); return; }

            if (GameApi.IsPanelOpen("NpcInteractPanel"))
            {
                Log("NpcInteractPanel da mo");
                Transition(Phase.TalkNpc);
                return;
            }

            var npc = FindNpc(_cfg.NpcName);
            if (npc == null)
            {
                // Không tìm ra NPC theo tên -> dùng NPC gần nhất trong map
                npc = FindNearestNpc();
            }

            if (npc == null)
            {
                if (_phaseTime > 3f && GameApi.ClickByName("InteractButton"))
                {
                    Log("Bam InteractButton (da dung gan NPC)");
                    Transition(Phase.TalkNpc);
                }
                return;
            }

            Vector3 np = npc.transform.position;
            float dist = GameApi.DistanceTo(np.x, np.y);

            if (dist > 1.5f)
            {
                if (_logTime > 3f)
                {
                    _logTime = 0f;
                    Log($"Di chuyen toi NPC ({np.x:F1},{np.y:F1}) d={dist:F1}m");
                }
                MoveToward(np.x, np.y);
                return;
            }

            // Đủ gần -> bấm InteractButton
            if (GameApi.ClickByName("InteractButton"))
            {
                Log($"Bam InteractButton voi NPC (d={dist:F1}m)");
                Transition(Phase.TalkNpc);
            }
        }

        private NPC FindNpc(string keyword)
        {
            string want = TextUtil.Strip(keyword);

            try
            {
                NPC[] npcs = UnityEngine.Object.FindObjectsOfType<NPC>(true);

                var alive = new List<NPC>();
                foreach (var n in npcs)
                    if (n != null && n.gameObject.activeInHierarchy) alive.Add(n);

                if (alive.Count == 0) return null;

                // 1. Khớp theo tên đọc được
                foreach (var n in alive)
                {
                    string name = NpcNameOf(n);
                    if (name.Length > 0 && name.ToLowerInvariant().Contains(want)) return n;
                }

                // 2. NPC mục tiêu (Đại tá Silver) thường KHÔNG có text tên world
                //    -> ưu tiên NPC không đọc được tên.
                var unnamed = new List<NPC>();
                foreach (var n in alive)
                {
                    if (NpcNameOf(n).Length == 0) unnamed.Add(n);
                }

                if (unnamed.Count > 0)
                {
                    // Nhiều NPC không tên -> lấy cái gần nhân vật nhất
                    Vector2? my = GameApi.MyPos();
                    if (my == null) return unnamed[0];

                    NPC best = null;
                    float bestD = float.MaxValue;
                    foreach (var n in unnamed)
                    {
                        Vector3 p = n.transform.position;
                        float d = Vector2.Distance(my.Value, new Vector2(p.x, p.y));
                        if (d < bestD) { bestD = d; best = n; }
                    }
                    return best;
                }

                // 3. Chỉ có 1 NPC -> dùng luôn
                if (alive.Count == 1) return alive[0];
            }
            catch { }

            return null;
        }

        /// <summary>Lấy NPC gần nhân vật nhất (dùng khi không đọc được tên).</summary>
        private NPC FindNearestNpc()
        {
            try
            {
                Vector2? my = GameApi.MyPos();
                if (my == null) return null;

                NPC best = null;
                float bestD = float.MaxValue;

                foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>(true))
                {
                    if (n == null || !n.gameObject.activeInHierarchy) continue;
                    Vector3 p = n.transform.position;
                    float d = Vector2.Distance(my.Value, new Vector2(p.x, p.y));
                    if (d < bestD) { bestD = d; best = n; }
                }
                return best;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Đọc tên NPC từ text world-space gần nhất.
        /// Game đặt tên trong Canvas "NameController" (TextMeshPro world) có
        /// Tittle + Name; ta lấy tất cả text world gần NPC.
        /// </summary>
        private static string NpcNameOf(NPC npc)
        {
            try
            {
                Vector3 pos = npc.transform.position;

                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshPro>(true);
                string best = "";
                float bestD = 6f;

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

        // ---------------- 3. nói chuyện + chọn mốc level ----------------

        private void TickTalkNpc()
        {
            if (_phaseTime > _cfg.StepTimeout) { Fail("ket o NpcInteractPanel"); return; }

            // Panel NPC thật (popup trên scene) — kiểm tra trực tiếp, không qua FindInteractButton
            bool panelReallyOpen = GameApi.IsPanelOpen("NpcInteractPanel");
            bool panelOpen = panelReallyOpen
                          || FindInteractButton("Tham gia miễn phí", exactOnly: true) != null
                          || FindInteractButton("Tham gia bằng vé", exactOnly: true) != null
                          || FindInteractButton("Tiếp tục tham gia", exactOnly: true) != null
                          || FindInteractButton("Tham gia", exactOnly: true) != null
                          || FindInteractButton("Tham gia tiếp", exactOnly: true) != null;

            if (!panelOpen)
            {
                // Chờ đủ gần NPC rồi mới bấm; cooldown 3s chống toggle đóng panel
                var npc = FindNpc(_cfg.NpcName) ?? FindNearestNpc();
                if (npc != null)
                {
                    Vector3 np = npc.transform.position;
                    if (GameApi.DistanceTo(np.x, np.y) > 2.5f)
                    {
                        GameApi.MoveTo(np.x, np.y);
                        return;
                    }
                }

                if (_phaseTime > _interactCooldown && PressInteract())
                {
                    Log("Mo panel NPC (bam InteractButton)");
                    _interactCooldown = _phaseTime + 3f;
                }
                return;
            }

            // Panel vừa mở nhưng nút bên trong chưa kịp render -> chờ 1s
            if (!panelReallyOpen && _phaseTime < 1f) return;

            // Chỉ dùng nút "Tham gia tiếp" ở lần đầu (chưa từng thử cách khác)
            if (_entryIndex <= 0)
            {
                foreach (string opt in _cfg.ResumeOptions)
                {
                    Button resume = FindInteractButton(opt, exactOnly: true);
                    if (resume == null) continue;
                    if (!SafeInteractable(resume)) continue;

                    string rpath = GameApi.PathOf(resume.gameObject);
                    bool rok = ApiDumper.ExecuteClick(resume);
                    if (!rok) rok = GameApi.Click(resume);
                    Log($"Bam '{opt}' (vao lai ruy bang) path='{rpath}' ok={rok}");

                    if (rok)
                    {
                        _resumeMode = true;
                        Transition(Phase.WaitEnter);
                        return;
                    }
                }
            }

            // Nút vào ruy băng, theo thứ tự:
            //   0 = "Tham gia miễn phí"  (1 lần/ngày)
            //   1 = "Tham gia bằng vé"   (hết lượt miễn phí thì dùng vé)
            //   2 = "Tham gia"           (nhãn chung)
            for (int i = Math.Max(0, _entryIndex); i < _cfg.EntryOptions.Length; i++)
            {
                string opt = _cfg.EntryOptions[i];

                Button entry = FindInteractButton(opt, exactOnly: true);
                if (entry == null) continue;

                // Nút bị khoá = hết lượt / không đủ điều kiện -> thử nhãn kế tiếp
                if (!SafeInteractable(entry))
                {
                    Log($"Nut '{opt}' bi khoa (het luot) -> thu cach khac");
                    _entryIndex = i + 1;
                    continue;
                }

                string path = GameApi.PathOf(entry.gameObject);
                // Dùng ExecuteClick (EventSystem) cho chắc thay vì onClick.Invoke
                bool ok = ApiDumper.ExecuteClick(entry);
                if (!ok) ok = GameApi.Click(entry);
                Log($"Bam '{opt}' path='{path}' ok={ok}");

                if (ok)
                {
                    _entryIndex = i;
                    Transition(Phase.ChooseEntry);
                    return;
                }
            }

            // Không tìm thấy nhãn nào khớp -> log các nút đang có để chẩn đoán
            if (_phaseTime > 4f)
            {
                Log("Nut trong panel NPC: " + DumpNpcPanelButtons());
                Fail("khong tim thay nut vao ruy bang");
            }
            return;
        }

        /// <summary>Đọc cờ interactable của nút (false = nút bị khoá).</summary>
        private static bool SafeInteractable(Button b)
        {
            try { return b != null && b.interactable; } catch { }
            return false;
        }

        /// <summary>
        /// Tìm nút trong panel tương tác của NPC.
        ///
        /// QUAN TRỌNG: game dùng object pool. Mọi nút trong NpcInteractPanel đều
        /// dùng chung class InteractPanelButton, và trong pool
        /// (DontDestroyObject/ObjectPoolHandler) luôn tồn tại bản template.
        /// Bấm vào template không có tác dụng hoặc bấm sai nút.
        /// -> Phải loại bỏ hoàn toàn nút nằm trong ObjectPoolHandler.
        /// </summary>
        private Button FindInteractButton(string text, bool exactOnly = false)
        {
            string want = TextUtil.Strip(text);
            if (want.Length == 0) return null;

            Button exactLoose = null;
            Button partial = null;

            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;

                    string t = TextUtil.Strip(GameApi.TextOf(b));
                    if (t.Length == 0) continue;

                    string path = GameApi.PathOf(b.gameObject);

                    // Bỏ template trong pool — đây chính là nguyên nhân bấm sai nút
                    if (path.IndexOf("ObjectPoolHandler", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    // Chỉ nhận nút trong panel tương tác / popup đang hiển thị
                    bool validPath = path.IndexOf("InteractPanel", StringComparison.OrdinalIgnoreCase) >= 0
                                  || path.IndexOf("InteractButtonHolder", StringComparison.OrdinalIgnoreCase) >= 0
                                  || path.IndexOf("NpcInteract", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!validPath) continue;

                    // Khớp chính xác -> trả ngay
                    if (t == want) return b;

                    // Khớp chứa -> giữ làm dự phòng
                    if (!exactOnly && t.Contains(want))
                    {
                        if (exactLoose == null) exactLoose = b;
                    }
                    else if (partial == null && t.Contains(want))
                    {
                        partial = b;
                    }
                }
            }
            catch { }

            return exactLoose ?? partial;
        }

        /// <summary>Liệt kê text + trạng thái khoá các nút trong panel NPC (chẩn đoán).</summary>
        private string DumpNpcPanelButtons()
        {
            var list = new List<string>();
            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;

                    string path = GameApi.PathOf(b.gameObject);
                    if (path.IndexOf("ObjectPoolHandler", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (path.IndexOf("NpcInteract", StringComparison.OrdinalIgnoreCase) < 0 &&
                        path.IndexOf("InteractPanel", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string t = GameApi.TextOf(b);
                    if (string.IsNullOrEmpty(t)) continue;

                    list.Add(t + (SafeInteractable(b) ? "" : " [KHOA]"));
                }
            }
            catch { }
            return string.Join(" | ", list);
        }

        private void TickChooseEntry()
        {
            if (_phaseTime > _cfg.StepTimeout) { Fail("khong chon duoc moc level"); return; }

            int level = GameApi.MyLevel();
            if (level < 0) { if (_phaseTime > 5f) Fail("khong doc duoc level nhan vat"); return; }

            // Gom TẤT CẢ nút mốc level, sắp xếp theo mức độ phù hợp.
            // Mốc chứa level nhân vật được ưu tiên tuyệt đối.
            var options = new List<(Button btn, int lo, int hi, string text, string path)>();

            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;

                    string raw = GameApi.TextOf(b);
                    if (string.IsNullOrEmpty(raw)) continue;
                    if (!raw.StartsWith("Level", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!TryParseLevelRange(raw, out int lo, out int hi)) continue;

                    string path = GameApi.PathOf(b.gameObject);

                    // Bỏ template trong pool (bị recycle text -> bấm sẽ sai mốc)
                    if (path.IndexOf("ObjectPoolHandler", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    options.Add((b, lo, hi, raw.Trim(), path));
                }
            }
            catch { }

            if (options.Count == 0)
            {
                // Danh sách level chưa hiện. Hai khả năng:
                //   a) Panel đang chuyển động -> chờ thêm
                //   b) Đã hết lượt miễn phí -> phải dùng VÉ
                if (_phaseTime > 3f && _entryIndex >= 0 && _entryIndex < _cfg.EntryOptions.Length - 1)
                {
                    Log($"Khong hien danh sach level sau khi bam '{_cfg.EntryOptions[_entryIndex]}' -> chuyen sang cach tiep theo");
                    _entryIndex++;
                    _entryLogged = false;
                    Transition(Phase.TalkNpc);
                    return;
                }

                if (_phaseTime > 4f && !_entryLogged)
                {
                    _entryLogged = true;
                    Log("Chua thay nut moc level. Nut dang co: " + DumpNpcPanelButtons());
                }
                return;
            }

            _entryLogged = false;

            // Điểm phù hợp: 0 = chứa level, 1 = sát nhất, 2 = xa nhất
            var scored = new List<(int score, Button btn, string text, string path)>();

            foreach (var o in options)
            {
                int score;
                if (level >= o.lo && level <= o.hi) score = 0;
                else score = 1 + Math.Min(Math.Abs(level - o.lo), Math.Abs(level - o.hi));

                scored.Add((score, o.btn, o.text, o.path));
            }

            scored.Sort((a, b) => a.score.CompareTo(b.score));

            // Log toàn bộ mốc tìm thấy để dễ chẩn đoán
            Log($"Level nhan vat = {level}. Cac moc tim thay: " +
                string.Join(", ", scored.ConvertAll(x => $"{x.text}{(x.score == 0 ? "*" : "")}")));

            var pick = scored[0];
            bool ok = ApiDumper.ExecuteClick(pick.btn);
            if (!ok) ok = GameApi.Click(pick.btn);

            Log($"Chon moc '{pick.text}' cho level {level} path='{pick.path}' ok={ok}");

            if (!ok)
            {
                try { pick.btn.Select(); } catch { }
                if (_phaseTime > 10f) Fail("khong click duoc nut moc level");
                return;
            }

            Transition(Phase.WaitEnter);
        }

        private static bool TryParseLevelRange(string s, out int lo, out int hi)
        {
            lo = hi = -1;

            var nums = new List<int>();
            int i = 0;
            while (i < s.Length)
            {
                if (char.IsDigit(s[i]))
                {
                    int j = i;
                    while (j < s.Length && char.IsDigit(s[j])) j++;
                    if (int.TryParse(s.Substring(i, j - i), out int v)) nums.Add(v);
                    i = j;
                }
                else i++;
            }

            if (nums.Count >= 2) { lo = nums[0]; hi = nums[1]; return true; }
            if (nums.Count == 1) { lo = hi = nums[0]; return true; }
            return false;
        }

        // ---------------- 4. chờ vào ruy băng ----------------

        private void TickWaitEnter()
        {
            // Hết thời gian chờ mà vẫn ở map home -> lần vào này thất bại.
            // Nguyên nhân thường gặp: ĐÃ HẾT LƯỢT MIỄN PHÍ (1 lần/ngày).
            // Danh sách level vẫn hiện và bấm được, nhưng game không cho vào.
            // -> quay lại NPC và thử cách kế tiếp ("Tham gia bằng vé").
            if (_phaseTime > _cfg.StepTimeout)
            {
                if (_entryIndex >= 0 && _entryIndex < _cfg.EntryOptions.Length - 1)
                {
                    _entryIndex++;
                    _resumeMode = false;
                    Log($"Vao bang '{_cfg.EntryOptions[_entryIndex - 1]}' that bai "
                        + $"(co the het luot) -> thu '{_cfg.EntryOptions[_entryIndex]}'");
                    _entryLogged = false;
                    Transition(Phase.TalkNpc);
                    return;
                }

                Fail("khong vao duoc ruy bang");
                return;
            }

            // Đang load
            if (GameApi.IsPanelOpen("MainInGameLoadingPanel")) { _phaseTime = 0f; return; }

            int mid = GameApi.MapId();
            string mapNow = GameApi.MapName();
            bool home = TextUtil.Strip(mapNow).Contains(TextUtil.Strip(_cfg.MapHome));

            // Đã vào map mới (map id đổi)
            if (_lastMapId < 0) _lastMapId = mid;

            if (mid > 0 && mid != _lastMapId)
            {
                Log($"Da vao ruy bang, map id = {mid} (map '{mapNow}')");
                _lastMapId = mid;
                _mapCount = 1;
                _entered = true;

                GameApi.EnsureAutoAttack();
                Transition(Phase.ClearMap);
                return;
            }

            // Không đổi map id nhưng tên map khác map home -> cũng coi là đã vào
            if (!home && _phaseTime > 3f && mapNow.Length > 0)
            {
                Log($"Da vao ruy bang (map '{mapNow}', id={mid})");
                _lastMapId = mid;
                _mapCount = 1;
                _entered = true;
                GameApi.EnsureAutoAttack();
                Transition(Phase.ClearMap);
                return;
            }

            // Trong chế độ resume, panel NPC có thể đóng lại -> không cần bấm gì thêm
            if (_resumeMode)
            {
                // Vẫn còn ở map home -> chưa vào được, thử lại nút "Tham gia tiếp"
                if (_phaseTime > 5f)
                {
                    _resumeMode = false;
                    Log("Vao lai that bai, thu lai tu dau");
                    Transition(Phase.GoNpc);
                    return;
                }
            }

            // Bấm nút "Vào game" nếu có (một số bản cần xác nhận thêm)
            if (_phaseTime > 2f && !home)
            {
                Button enter = GameApi.FindButtonByText(_cfg.EnterGameText);
                if (enter != null && GameApi.Click(enter))
                    Log($"Bam '{_cfg.EnterGameText}'");
            }
        }

        // ---------------- 5. dọn quái 1 map ----------------
        // LUỒNG MỚI (theo yêu cầu): vào map -> bật auto 1 lần -> game tự đánh,
        // bot chỉ dịch chuyển tới quái gần nhất khi quái cũ chết.
        // KHÔNG bấm skill, KHÔNG bấm ChangeTarget — bỏ hết.
        // Auto BẬT XUYÊN SUỐT từ lúc vào ruy băng tới lúc xong, không bao giờ tắt.

        private void TickClearMap()
        {
            if (!_entered) { Transition(Phase.GoHome); return; }

            // Chết thì dừng
            if (GameApi.IsDead()) { Fail("nhan vat da chet"); return; }

            // Auto bật xuyên suốt: gọi mỗi frame nhưng EnsureAutoAttack chỉ bấm khi thật sự tắt.
            // (Không bao giờ gọi SetAutoMode(false) ở bất kỳ đâu trong bot.)
            GameApi.EnsureAutoAttack();

            // Hút đồ rơi gần (không teleport đi hút khi đang đánh — chỉ nhặt gần để khỏi giật)
            if (_cfg.AutoPickup) PickupAll();

            var mobs = GameApi.AliveMobs();
            if (mobs.Count > 0)
            {
                _phaseTime = 0f;   // reset timeout khi vẫn còn quái

                // Chọn con gần nhất
                Mob nearest = null;
                Vector2? nearestPos = null;
                float best = float.MaxValue;
                Vector2? my = GameApi.MyPos();
                foreach (var m in mobs)
                {
                    var mp = GameApi.MobPos(m);
                    if (mp == null) continue;
                    float d = my != null ? Vector2.Distance(my.Value, mp.Value) : 0f;
                    if (d < best) { best = d; nearest = m; nearestPos = mp; }
                }

                if (nearest != null && nearestPos != null)
                {
                    // Teleport tới SÁT quái (cách 1m) để đánh thường trúng dame.
                    // Quái ngoài tầm đánh -> đấm gió giật giật, server không nhận dame.
                    bool isNewTarget = _lastTargetMob == null || !SameMob(_lastTargetMob, nearest);
                    // #region agent log
                    try
                    {
                        var _mg = ApiDumper.FindMainGame();
                        bool _aa = false;
                        try { _aa = ApiDumper.ReadAutoPlay(); } catch { }
                        object _t = null;
                        try
                        {
                            object _f = _mg != null ? _mg.focus : null;
                            var _fm = _f != null ? _f.GetType().GetMethod("JACHHEFJHHK") : null;
                            if (_fm != null) _t = _fm.Invoke(_f, null);
                        }
                        catch { }
                        HLog("H1", "auto-tp", "RibbonBot.cs:TickClearMap", "auto+target snapshot", "{\"auto\":" + (_aa ? "true" : "false") + ",\"mobD\":" + best.ToString("F1") + ",\"isNew\":" + (isNewTarget ? "true" : "false") + ",\"hasFocusTarget\":" + (_t != null ? "true" : "false") + "}");
                    }
                    catch { }
                    // #endregion
                    if (isNewTarget)
                    {
                        // Mục tiêu mới -> teleport thẳng tới sát (cách 1m theo hướng hiện tại)
                        Vector2 tp = ClosePoint(my, nearestPos.Value, 1f);
                        GameApi.MoveTo(tp.x, tp.y);
                        _lastTargetMob = nearest;
                        _stuckTime = 0f;
                    }
                    else
                    {
                        // Cùng mục tiêu: theo dõi xem có giết được không.
                        // Nếu sau 5s vẫn con đó (đánh không trúng) -> teleport lại sát hơn (0.5m).
                        _stuckTime += Time.deltaTime;
                        if (best > 1.5f || _stuckTime > 5f)
                        {
                            float gap = _stuckTime > 5f ? 0.5f : 1f;
                            Vector2 tp = ClosePoint(my, nearestPos.Value, gap);
                            GameApi.MoveTo(tp.x, tp.y);
                            if (_stuckTime > 5f)
                            {
                                Log($"Danh khong trung (>5s, d={best:F1}m) -> tp lai sat {gap}m");
                                _stuckTime = 0f;
                            }
                        }
                    }
                    // (không bấm skill gì cả — auto của game tự đánh)
                }

                if (_logTime > 5f)
                {
                    _logTime = 0f;
                    Log($"Map {_mapCount}: con {mobs.Count} quai (auto danh, tp sat 1m)");
                }
                return;
            }

            // Hết quái -> reset target
            _lastTargetMob = null;
            _stuckTime = 0f;

            // Hút sạch đồ rơi trong map trước khi qua cổng (lúc này mới teleport đi hút)
            VacuumAll();
            if (GameApi.GroundItems().Count > 0)
            {
                if (_phaseTime < 5f) return;   // cho thêm 5s hút đồ
            }

            // Không còn quái -> quét hết lượt (boss có thể spawn muộn) rồi mới qua map kế
            if (_phaseTime < 3f) return;

            Log($"Map {_mapCount} da sach quai");
            _mapCount++;

            if (_mapCount > _cfg.MaxMaps) { Transition(Phase.GoRewardNpc); return; }
            Transition(Phase.GoGate);
        }

        /// <summary>So sánh 2 Mob có phải cùng 1 instance không (tránh crash IL2CPP).</summary>
        private static bool SameMob(Mob a, Mob b)
        {
            try
            {
                if (a == null || b == null) return false;
                return a.GetInstanceID() == b.GetInstanceID();
            }
            catch { return false; }
        }

        /// <summary>
        /// Vòng đánh: đổi mục tiêu (Tab) + bấm liên tục các skill sẵn sàng.
        ///
        /// Cơ chế thật của client 140:
        ///   - Các nút HUD (Skill1..8, ChangeTargetButton) KHÔNG chạy qua
        ///     Button.onClick (persistent=0) mà qua IPointerDown/IPointerUp.
        ///     Vì vậy dùng ApiDumper.ExecuteClick (EventSystem.ExecuteEvents).
        ///   - Lỗi cũ: bấm xong onClick vào template ObjectPoolHandler ->
        ///     trúng mốc level khác (vd bấm 'Level 80-89' mà game nhận 'Level 40-49').
        /// </summary>
        private void AttackLoop()
        {
            // Chỉ spam mỗi 0.8s: đổi mục tiêu 1 lần rồi bấm hết dàn skill
            if (_attackCooldown > 0f && Time.realtimeSinceStartup < _attackCooldown) return;
            _attackCooldown = Time.realtimeSinceStartup + 0.8f;

            ChangeTarget();
            FireAllSkills();
        }

        /// <summary>Bấm nút đổi mục tiêu 2 lần để chắc có target mới nhất.</summary>
        private void ChangeTarget()
        {
            ClickHudOnce("ChangeTargetButton");
            ClickHudOnce("ChangeTargetButton");
        }

        /// <summary>Bấm hết dàn skill còn cooldown, bỏ qua nút đang hồi chiêu.</summary>
        private void FireAllSkills()
        {
            for (int i = 1; i <= 8; i++)
                ClickHudOnce("Skill" + i);
        }

        /// <summary>Bấm 1 nút HUD 1 lần duy nhất bằng EventSystem chuẩn.
        /// Bỏ qua nút đang cooldown (click xong mà vẫn hồi chiêu là đúng, không sao).</summary>
        private bool ClickHudOnce(string goName)
        {
            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    if (!string.Equals(b.gameObject.name, goName, StringComparison.OrdinalIgnoreCase)) continue;

                    // Bỏ nút template trong pool
                    string path = PathOf(b.gameObject);
                    if (path.IndexOf("ObjectPoolHandler", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    try { if (!b.interactable) continue; } catch { }

                    return ApiDumper.ExecuteClick(b);
                }
            }
            catch { }

            return false;
        }

        /// <summary>Bấm nút InteractButton 1 lần bằng EventSystem (mở panel NPC / qua cổng).
        /// Nút này là TOGGLE nên gọi đúng 1 lần, không gọi onClick spam.</summary>
        private static bool PressInteract()
        {
            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    if (!string.Equals(b.gameObject.name, "InteractButton", StringComparison.OrdinalIgnoreCase)) continue;

                    if (ApiDumper.ExecuteClick(b)) return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>Kiểm tra đã có mục tiêu chưa (Focus.getTarget).</summary>
        private bool HasTarget()
        {
            try
            {
                var mg = ApiDumper.FindMainGame();
                if (mg == null) return false;

                var focus = mg.focus;
                if (focus == null) return false;

                return focus.getTarget() != null;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Hút toàn bộ đồ rơi trên mặt đất vào rương: teleport tới từng món rồi interact,
        /// không chỉ nhặt món trong bán kính nhỏ.
        /// </summary>
        private void VacuumAll()
        {
            try
            {
                var items = GameApi.GroundItems();
                if (items.Count == 0) return;

                int done = 0;
                foreach (var it in items)
                {
                    var p = GameApi.ItemPos(it);
                    if (p == null) continue;

                    // Tới thẳng vị trí món đồ rồi nhặt
                    if (GameApi.DistanceTo(p.Value.x, p.Value.y) > 1.2f)
                        GameApi.MoveTo(p.Value.x, p.Value.y);

                    if (GameApi.PickItem(it)) done++;
                    if (done >= 5) break;   // mỗi frame hút tối đa 5 món, frame sau hút tiếp
                }

                if (done > 0 && _logTime > 5f)
                {
                    _logTime = 0f;
                    Log($"Hut {done}/{items.Count} do roi");
                }
            }
            catch { }
        }

        private void PickupAll()
        {
            var items = GameApi.GroundItems();
            if (items.Count == 0) return;

            int picked = 0;
            foreach (var it in items)
            {
                var p = GameApi.ItemPos(it);
                if (p == null) continue;

                if (GameApi.DistanceTo(p.Value.x, p.Value.y) <= _cfg.PickupRadius)
                {
                    if (GameApi.PickItem(it)) picked++;
                }
            }

            if (picked > 0 && _logTime > 3f)
            {
                _logTime = 0f;
                Log($"Nhat {picked} do roi");
            }
        }

        // ---------------- 6. đi tới cổng ----------------

        private void TickGoGate()
        {
            if (_phaseTime > _cfg.StepTimeout) { Fail("khong toi duoc cong chuyen map"); return; }

            var gates = GameApi.Gates();
            if (gates.Count == 0)
            {
                // Không có cổng -> có thể đã ở tầng cuối
                Log("Khong con cong -> coi nhu het map");
                Transition(Phase.GoRewardNpc);
                return;
            }

            // Chọn cổng: ưu tiên cổng dẫn tới map khác map hiện tại.
            // Nếu không phân biệt được thì lấy cổng gần nhất.
            MapGateway nearest = null;
            float best = float.MaxValue;
            Vector2? my = GameApi.MyPos();
            int curMap = GameApi.MapId();

            foreach (var g in gates)
            {
                var gp = GameApi.GatePos(g);
                if (gp == null) continue;

                int toMap = GameApi.GateToMapId(g);
                bool isNext = (toMap > 0 && curMap > 0 && toMap != curMap);

                float d = my != null ? Vector2.Distance(my.Value, gp.Value) : 0f;

                // Cổng sang map khác được ưu tiên mạnh
                float score = isNext ? d - 1000f : d;

                if (score < best) { best = score; nearest = g; }
            }

            if (nearest == null) { Fail("khong doc duoc toa do cong"); return; }

            var target = GameApi.GatePos(nearest).Value;
            int toMapId = GameApi.GateToMapId(nearest);

            // Nếu cổng không ở gần và đang đi sai hướng -> bỏ qua
            if (best > 1.5f)
            {
                GameApi.MoveTo(target.x, target.y);
                return;
            }

            // Đứng ở cổng -> dịch chuyển thẳng tới tâm cổng rồi bấm Enter để qua map.
            // (Không đứng xa bấm, server không nhận)
            Log($"Toi cong ({target.x:F1},{target.y:F1}) -> sang map {toMapId}");
            GameApi.MoveTo(target.x, target.y);
            PressInteract();
            Transition(Phase.NextMap);
        }

        // ---------------- 7. qua map kế ----------------

        private void TickNextMap()
        {
            if (_phaseTime > _cfg.StepTimeout) { Fail("map khong chuyen"); return; }

            if (GameApi.IsPanelOpen("MainInGameLoadingPanel")) { _phaseTime = 0f; return; }

            int mid = GameApi.MapId();
            if (mid > 0 && mid != _lastMapId)
            {
                Log($"Da sang map id {mid}");
                _lastMapId = mid;
                _entered = true;
                GameApi.EnsureAutoAttack();
                Transition(Phase.ClearMap);
                return;
            }

            // Bấm InteractButton (nút "Enter") để qua cổng — đúng 1 lần / 1.5s
            // (nút là toggle, spam sẽ đóng panel ngay)
            if (_phaseTime > _gateCooldown)
            {
                _gateCooldown = _phaseTime + 1.5f;
                if (PressInteract()) return;
            }

            // Có thể phải bấm nút trong panel (vd xác nhận)
            Button enter = GameApi.FindButtonByText(_cfg.EnterGameText);
            if (enter != null) ApiDumper.ExecuteClick(enter);
        }

        // ---------------- 8. nhận thưởng ----------------

        private void TickGoRewardNpc()
        {
            if (_phaseTime > _cfg.StepTimeout * 2) { Fail("khong toi duoc NPC nhan thuong"); return; }

            // Hút hết đồ trước khi nhận thưởng
            if (_cfg.AutoPickup) PickupAll();

            var npc = FindNpc("Tổng Tư Lệnh");
            if (npc == null)
            {
                if (_phaseTime > 3f && GameApi.ClickByName("InteractButton"))
                {
                    Log("Bam InteractButton (NPC thuong)");
                    Transition(Phase.ClaimReward);
                }
                return;
            }

            Vector3 np = npc.transform.position;
            if (GameApi.DistanceTo(np.x, np.y) > 1.5f) { MoveToward(np.x, np.y); return; }

            if (GameApi.ClickByName("InteractButton"))
            {
                Log("Bam InteractButton voi NPC thuong");
                Transition(Phase.ClaimReward);
            }
        }

        private void TickClaimReward()
        {
            if (_phaseTime > _cfg.StepTimeout) { Fail("khong nhan duoc thuong"); return; }

            // Lần đầu: bấm "Nhận thưởng" (nút trong NpcInteractPanel)
            if (!_rewardPanelOpened)
            {
                Button claim = FindInteractButton(_cfg.RewardOption);
                if (claim != null)
                {
                    string path = GameApi.PathOf(claim.gameObject);
                    bool ok = GameApi.Click(claim);
                    Log($"Bam '{_cfg.RewardOption}' path='{path}' ok={ok}");
                    _rewardPanelOpened = true;
                    _phaseTime = 0f;
                    return;
                }

                // Nút chưa xuất hiện -> thử bấm InteractButton lại (panel NPC có thể đã đóng)
                if (_phaseTime > 3f)
                {
                    if (GameApi.ClickByName("InteractButton"))
                    {
                        Log("Bam InteractButton de mo lai panel NPC");
                        _phaseTime = 0f;
                    }
                }
                return;
            }

            // Bấm tất cả "Nhận ngay" đang hiện trong panel thưởng.
            // DÙNG ExecuteClick (EventSystem) — nút milestone không chạy qua onClick.
            int clicked = 0;
            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>(true))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;

                    string t = TextUtil.Strip(GameApi.TextOf(b));
                    if (t.Length == 0) continue;
                    if (!t.Contains(TextUtil.Strip(_cfg.SubmitText))) continue;

                    string path = GameApi.PathOf(b.gameObject);

                    // Chỉ bấm nút trong MilestoneRewardPanel để khỏi bấm nhầm.
                    // Bỏ nút template trong pool.
                    if (path.IndexOf("Milestone", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (path.IndexOf("ObjectPoolHandler", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    if (ApiDumper.ExecuteClick(b)) { clicked++; _rewardLoops++; }
                }
            }
            catch { }

            if (clicked > 0)
            {
                Log($"Bam {clicked} nut '{_cfg.SubmitText}' (tong {_rewardLoops})");
                _phaseTime = 0f;

                // Nếu túi đầy (không nhận thêm được dù còn mốc) -> sắp xếp rồi nhận tiếp.
                // Dấu hiệu: vẫn còn nút "Nhận ngay" nhưng 3 vòng liên tiếp không tăng tổng.
                if (_rewardStall >= 3)
                {
                    _rewardStall = 0;
                    Log("Nhan cham (co the tui day) -> sap xep tui do");
                    Transition(Phase.SortInventory);
                    return;
                }

                if (_rewardLoops > _cfg.MaxRewardLoops) { Finish(); }
                return;
            }

            // Không bấm được nút nào.
            _rewardStall++;

            // 1) Túi đầy -> game chặn nhận. Mở túi -> Sắp xếp -> Quay lại nhận tiếp.
            if (_rewardStall >= 2 && _sortLoops < 5)
            {
                Log("Khong nhan duoc (co the tui day) -> sap xep tui do");
                Transition(Phase.SortInventory);
                return;
            }

            // 2) Panel thưởng chưa mở (đã bị đóng) -> mở lại bằng "Nhận thưởng" ở panel NPC
            if (!GameApi.IsPanelOpen("MilestoneRewardPanel"))
            {
                Button claim = FindInteractButton(_cfg.RewardOption);
                if (claim != null && ApiDumper.ExecuteClick(claim))
                {
                    Log("Mo lai panel thuong");
                    _phaseTime = 0f;
                    _rewardStall = 0;
                    return;
                }

                if (_phaseTime > 10f)
                {
                    Log("Da nhan het thuong co the nhan duoc");
                    Finish();
                }
                return;
            }

            // Panel thưởng đang mở nhưng không có nút nhận -> hết mốc
            if (_phaseTime > 5f)
            {
                Log("Da nhan het thuong co the nhan duoc");
                Finish();
            }
        }

        // ---------------- 9. sắp xếp túi ----------------

        private void TickSortInventory()
        {
            if (_phaseTime > 25f)
            {
                Log("Sap xep tui qua lau -> quay lai nhan thuong");
                _rewardPanelOpened = false;
                Transition(Phase.ClaimReward);
                return;
            }

            // Mở túi (InventoryButton) bằng ExecuteClick
            if (!GameApi.IsPanelOpen("CharacterMenu") &&
                !GameApi.IsPanelOpen("InventoryPanel") &&
                !GameApi.IsPanelOpen("BagPanel"))
            {
                Button inv = GameApi.FindButtonByName("InventoryButton");
                if (inv == null) inv = GameApi.FindButtonByName("BagButton");
                if (inv != null && ApiDumper.ExecuteClick(inv)) { _phaseTime = 0f; }
                return;
            }

            // Bấm "Sắp xếp" (SortButton) — nút sắp xếp nằm trong tab túi
            if (_phaseTime < 2f) return;

            Button sort = GameApi.FindButtonByName("SortButton");
            if (sort == null) sort = GameApi.FindButtonByText("Sắp xếp");
            if (sort == null) sort = GameApi.FindButtonByText("Sap xep");

            if (sort != null)
            {
                Log("Bam 'Sap xep' tui do path='" + GameApi.PathOf(sort.gameObject) + "'");
                ApiDumper.ExecuteClick(sort);
                _phaseTime = 0f;
                _sortLoops++;
                _rewardStall = 0;
            }

            // Đóng túi rồi quay lại nhận tiếp (không nhận nữa = túi vẫn đầy thì bot Finish)
            if (_phaseTime > 2f)
            {
                Button close = GameApi.FindButtonByName("CloseButton");
                if (close == null) close = GameApi.FindButtonByName("CloseBtn");
                if (close != null) ApiDumper.ExecuteClick(close);

                _rewardPanelOpened = false;   // buộc mở lại panel thưởng
                Transition(Phase.ClaimReward);
            }
        }

        // ---------------- kết thúc ----------------

        private void Finish()
        {
            Log($"===== XONG RUY BANG =====");
            Log($"Tong ket: {_mapCount} map, {_rewardLoops} luot nhan thuong");
            _cfg.Enabled = false;
            Transition(Phase.Idle);
            DumpTrace();
        }

        private void Fail(string reason)
        {
            Log("THAT BAI: " + reason);
            _cfg.Enabled = false;
            Transition(Phase.Failed);
            DumpTrace();
        }

        // ---------------- tiện ích ----------------

        private void Transition(Phase p)
        {
            if (_phase != p) Log($"{_phase} -> {p}");
            _phase = p;
            _phaseTime = 0f;

            if (_phase == Phase.Idle || _phase == Phase.Failed) { /* giữ trạng thái */ }
        }

        private void MoveToward(float x, float y)
        {
            if (GameApi.DistanceTo(x, y) > 2f) GameApi.MoveTo(x, y);
        }

        // #region agent log
        private static void HLog(string hid, string run, string loc, string msg, string dataJson)
        {
            try
            {
                string line = "{\"sessionId\":\"2aec1d\",\"runId\":\"" + run + "\",\"hypothesisId\":\"" + hid + "\",\"location\":\"" + loc + "\",\"message\":\"" + msg + "\",\"data\":" + (string.IsNullOrEmpty(dataJson) ? "{}" : dataJson) + ",\"timestamp\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}";
                System.IO.File.AppendAllText("D:\\game\\VuTruDaiChien_140\\debug-2aec1d.log", line + "\n", System.Text.Encoding.UTF8);
            }
            catch { }
        }
        // #endregion

        private void Log(string msg)
        {
            string line = "[Ribbon] " + msg;
            Log140.Info(line);
            _trace.Add(DateTime.Now.ToString("HH:mm:ss") + " " + msg);
            if (_trace.Count > 200) _trace.RemoveAt(0);
        }

        private void DumpTrace()
        {
            try
            {
                if (_trace.Count == 0) return;
                Log140.Info("[Ribbon] ----- TRACE -----");
                foreach (var t in _trace) Log140.Info("[Ribbon]   " + t);
                Log140.Info("[Ribbon] ----- HET TRACE -----");
            }
            catch { }
        }

        private static string PathOf(GameObject go)
        {
            if (go == null) return "";
            var parts = new List<string>();
            var t = go.transform;
            int depth = 0;
            while (t != null && depth < 6) { parts.Insert(0, t.gameObject.name); t = t.parent; depth++; }
            return string.Join("/", parts);
        }
    }
}
