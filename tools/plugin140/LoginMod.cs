using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QLTK.Client140
{
    /// <summary>
    /// Auto-login cho báº£n game 140, viáº¿t láº¡i theo Ä‘Ăºng flow Ä‘Ă£ cháº¡y thĂ nh cĂ´ng
    /// trong QA_TestBot/plugin/Plugin.cs (TestBotController).
    ///
    /// Ba Ä‘iá»ƒm khĂ¡c biá»‡t quyáº¿t Ä‘á»‹nh so vá»›i báº£n trÆ°á»›c (báº£n bá»‹
    /// TypeInitializationException / NullReferenceException):
    ///
    ///   1. MonoBehaviour tá»± Ä‘á»‹nh nghÄ©a Báº®T BUá»˜C cĂ³ constructor
    ///      (IntPtr ptr) : base(ptr). Constructor khĂ´ng tham sá»‘ khiáº¿n
    ///      Il2CppInterop khĂ´ng dá»±ng Ä‘Æ°á»£c MethodInfoStore cho
    ///      GameObject.AddComponent&lt;T&gt;() -> NullReferenceException.
    ///
    ///   2. KhĂ´ng táº¡o GameObject riĂªng: dĂ¹ng AddComponent trĂªn chĂ­nh plugin
    ///      (BasePlugin lĂ  MonoBehaviour), Ä‘Ăºng nhÆ° QA_TestBot Ä‘Ă£ lĂ m.
    ///
    ///   3. Click nĂºt báº±ng Button.onClick.Invoke() (ButtonClickedEvent).
    ///      Báº£n trÆ°á»›c Ă©p Button.onClick sang UnityAction nĂªn luĂ´n null,
    ///      khiáº¿n má»i cĂº click (Ä‘Äƒng nháº­p, chá»n server, vĂ o game) Ä‘á»u
    ///      khĂ´ng bao giá» thá»±c thi.
    ///
    /// API game 140 dĂ¹ng á»Ÿ Ä‘Ă¢y (Ä‘Ă£ Ä‘á»‘i chiáº¿u dump.cs + interop DLL):
    ///   LoginUIManager.gI() / .loginPanel / .showLoginPanel()
    ///   LoginPanel.accountField / .passwordField (MyInputField)
    ///             .loginButton (MyButton) / .invokeLoginBtn()
    ///   MyInputField.setText(string) / .getText()
    ///   MyButton.button (UnityEngine.UI.Button)
    ///   ChooseServerPanel.expandButton / .JABPOJJPIHE -> List&lt;ServerObject&gt;
    ///   ServerObject.serverName (MyUIText) / .myButton (MyButton)
    ///   CharacterChoosingObject.actionButton (MyButton)
    ///   Scene vĂ o game: "MainGameScene", scene Ä‘Äƒng nháº­p: "LoginScene"
    /// </summary>
    internal sealed class LoginMod : MonoBehaviour
    {
        // IL2CPP: constructor báº¯t buá»™c nháº­n IntPtr vĂ  chuyá»ƒn cho base.
        public LoginMod(IntPtr ptr) : base(ptr) { }

        private enum Phase
        {
            WaitScene,
            WaitLogin,
            ClickedLogin,
            WaitServer,
            ChooseCharacter,
            EnterGame,
            Done,
            Failed
        }

        private GameArgs _args;
        private SocketClient _socket;

        private Phase _phase = Phase.WaitScene;
        private float _phaseTime;
        private float _sceneLogTime;

        private bool _filled;
        private bool _clickedLogin;
        private bool _clickedExpand;
        private bool _clickedEnter;
        private bool _sttSent;
        private bool _retrySlotSelected;
        private bool _dumpedCharUI;
        private float _retryEnterTimer;
        private string _lastServerSignature;

        private const float StepTimeout = 45f;

        /// <summary>CĂ i component lĂªn chĂ­nh GameObject cá»§a plugin (main thread).</summary>
        public static LoginMod Install(BasePlugin host, GameArgs args, SocketClient socket)
        {
            try { ClassInjector.RegisterTypeInIl2Cpp<LoginMod>(); }
            catch (Exception ex) { Log140.Error("[QLTK140] Register LoginMod loi: " + ex.Message); }

            var mod = host.AddComponent<LoginMod>();
            mod._args = args;
            mod._socket = socket;

            Log140.Info($"[QLTK140] LoginMod da cai. user='{args.Username}' server='{args.Server}' " +
                      $"char={args.CharIndex} stt={args.Stt} res={args.Width}x{args.Height}");
            return mod;
        }

        private void Update()
        {
            if (_phase == Phase.Done || _phase == Phase.Failed) return;
            if (_args == null) return;

            _phaseTime += Time.deltaTime;
            _sceneLogTime += Time.deltaTime;

            try
            {
                string scene = SafeSceneName();

                // ÄĂ£ vĂ o map -> bĂ¡o STT rá»“i káº¿t thĂºc
                if (scene == "MainGameScene")
                {
                    if (_phase != Phase.Done)
                    {
                        Log140.Info($"[QLTK140] LOGIN_SUCCESS: vao MainGameScene sau {_phaseTime:F2}s");
                        Transition(Phase.Done);
                        SendStt();
                    }
                    return;
                }

                // MĂ n chá»n nhĂ¢n váº­t cĂ³ thá»ƒ xuáº¥t hiá»‡n á»Ÿ báº¥t ká»³ bÆ°á»›c nĂ o
                if ((_phase == Phase.WaitLogin || _phase == Phase.ClickedLogin || _phase == Phase.WaitServer)
                    && IsCharacterSelectVisible())
                {
                    Log140.Info("[QLTK140] Phat hien man chon nhan vat -> ChooseCharacter");
                    Transition(Phase.ChooseCharacter);
                    return;
                }

                switch (_phase)
                {
                    case Phase.WaitScene: HandleWaitScene(scene); break;
                    case Phase.WaitLogin: HandleWaitLogin(); break;
                    case Phase.ClickedLogin: HandleClickedLogin(); break;
                    case Phase.WaitServer: HandleWaitServer(); break;
                    case Phase.ChooseCharacter: HandleChooseCharacter(); break;
                    case Phase.EnterGame: HandleEnterGame(scene); break;
                }
            }
            catch (Exception ex)
            {
                Log140.Error($"[QLTK140] Update loi ({_phase}): {ex.Message}");
            }
        }

        private void Transition(Phase p)
        {
            Log140.Info($"[QLTK140] {_phase} -> {p}");
            _phase = p;
            _phaseTime = 0f;
            _lastServerSignature = null;
        }

        private void Timeout(string what)
        {
            Log140.Error($"[QLTK140] TIMEOUT: {what} (phase {_phase}, {_phaseTime:F1}s)");
            Transition(Phase.Failed);
        }

        // ---------- bÆ°á»›c 0: chá» LoginScene ----------
        private void HandleWaitScene(string scene)
        {
            if (_sceneLogTime >= 3f)
            {
                _sceneLogTime = 0f;
                Log140.Info($"[QLTK140] Dang cho LoginScene, hien tai: '{scene}'");
            }

            if (scene == "LoginScene") Transition(Phase.WaitLogin);
            else if (_phaseTime > 90f) Timeout("khong thay LoginScene");
        }

        // ---------- bÆ°á»›c 1: Ä‘iá»n tĂ i khoáº£n + báº¥m Ä‘Äƒng nháº­p ----------
        private void HandleWaitLogin()
        {
            if (_phaseTime > StepTimeout) { Timeout("khong thay o nhap tai khoan"); return; }

            var ui = GetLoginUIManager();
            var panel = GetLoginPanel(ui);

            // Äáº£m báº£o panel Ä‘ang hiá»ƒn thá»‹
            if (ui != null && (panel == null || !panel.gameObject.activeInHierarchy))
            {
                try { ui.showLoginPanel(); } catch { }
            }
            if (panel != null && !panel.gameObject.activeInHierarchy)
            {
                try { panel.gameObject.SetActive(true); } catch { }
            }

            if (panel == null) return;

            MyInputField acc = null, pwd = null;
            Button loginBtn = null;
            try
            {
                acc = panel.accountField;
                pwd = panel.passwordField;
                if (panel.loginButton != null) loginBtn = panel.loginButton.button;
            }
            catch { }

            if (acc == null || pwd == null) return;

            if (!_filled)
            {
                try
                {
                    // QA_TestBot dĂ¹ng setText() vĂ  Ä‘Ă£ cháº¡y Ä‘Ăºng -> giá»¯ nguyĂªn.
                    acc.setText(_args.Username ?? "");
                    pwd.setText(_args.Password ?? "");
                    _filled = true;
                    Log140.Info($"[QLTK140] Da dien tai khoan '{_args.Username}'");
                }
                catch (Exception ex)
                {
                    Log140.Error("[QLTK140] Dien tai khoan loi: " + ex.Message);
                    return;
                }
            }

            if (!_filled || _clickedLogin || _phaseTime < 0.5f) return;

            try
            {
                if (loginBtn != null)
                {
                    loginBtn.onClick.Invoke();
                    Log140.Info("[QLTK140] Da bam nut Dang Nhap (Button.onClick)");
                }
                else
                {
                    panel.invokeLoginBtn();
                    Log140.Info("[QLTK140] Da bam nut Dang Nhap (invokeLoginBtn)");
                }

                _clickedLogin = true;
                Transition(Phase.ClickedLogin);
            }
            catch (Exception ex)
            {
                Log140.Error("[QLTK140] Bam Dang Nhap loi: " + ex.Message);
            }
        }

        // ---------- bÆ°á»›c 2: chá» chuyá»ƒn sang mĂ n chá»n server ----------
        private void HandleClickedLogin()
        {
            if (_phaseTime > StepTimeout) { Timeout("khong thay danh sach server sau khi dang nhap"); return; }

            // Chá»‰ chá» ngáº¯n: náº¿u server list / mĂ n nhĂ¢n váº­t chÆ°a lĂªn thĂ¬ chuyá»ƒn sang quĂ©t.
            if (_phaseTime > 0.6f) Transition(Phase.WaitServer);
        }

        // ---------- bÆ°á»›c 3: má»Ÿ dropdown + chá»n server ----------
        private void HandleWaitServer()
        {
            if (_phaseTime > StepTimeout) { Timeout("khong chon duoc server"); return; }

            if (!_clickedExpand && _phaseTime > 0.8f)
            {
                _clickedExpand = true;
                try
                {
                    var chooseServer = UnityEngine.Object.FindObjectOfType<ChooseServerPanel>(true);
                    if (chooseServer != null && chooseServer.gameObject.activeInHierarchy)
                    {
                        Button expand = null;
                        try { if (chooseServer.expandButton != null) expand = chooseServer.expandButton.button; } catch { }
                        if (expand == null) expand = FindButton("ExpandButton");

                        if (expand != null && expand.gameObject.activeInHierarchy)
                        {
                            expand.onClick.Invoke();
                            Log140.Info("[QLTK140] Da mo dropdown server");
                        }
                    }
                }
                catch (Exception ex) { Log140.Warn("[QLTK140] Mo dropdown loi: " + ex.Message); }
            }

            ServerObject[] servers = null;
            try { servers = UnityEngine.Object.FindObjectsOfType<ServerObject>(true); } catch { }
            if (servers == null || servers.Length == 0) return;

            var names = new List<string>();

            foreach (var so in servers)
            {
                if (so == null || !so.gameObject.activeInHierarchy) continue;

                string name = SafeServerName(so);
                if (string.IsNullOrEmpty(name)) continue;
                names.Add(name);

                if (!ServerMatch(name, _args.Server)) continue;

                Button btn = null;
                try { if (so.myButton != null) btn = so.myButton.button; } catch { }
                if (btn == null) { try { btn = so.GetComponentInChildren<Button>(true); } catch { } }
                if (btn == null || !btn.gameObject.activeInHierarchy) continue;

                Log140.Info($"[QLTK140] Chon server '{name}' (khop '{_args.Server}')");
                btn.onClick.Invoke();
                Transition(Phase.ChooseCharacter);
                return;
            }

            string sig = string.Join("|", names);
            if (sig != _lastServerSignature)
            {
                _lastServerSignature = sig;
                Log140.Info($"[QLTK140] Danh sach server: [{string.Join(", ", names)}] - can '{_args.Server}'");
            }
        }

        // ---------- bÆ°á»›c 4: chá»n nhĂ¢n váº­t + báº¥m vĂ o game ----------
        private void HandleChooseCharacter()
        {
            if (_phaseTime > StepTimeout) { Timeout("khong chon duoc nhan vat"); return; }
            if (_clickedEnter) { Transition(Phase.EnterGame); return; }

            // Wait for UI to stabilize
            if (_phaseTime < 0.6f) return;

            // Dump UI once for diagnostics (written to log file)
            if (!_dumpedCharUI)
            {
                _dumpedCharUI = true;
                DumpCharacterScreen();
            }

            // Select character slot by charIndex if multiple slots exist
            if (!_retrySlotSelected)
            {
                _retrySlotSelected = true;
                TrySelectCharacterSlot();
            }

            // Strategy 1: Find button with text "Vao game"
            Button enter = FindButtonByText("Vào game");
            if (enter == null) enter = FindButtonByText("vao game");
            if (enter != null && enter.gameObject.activeInHierarchy)
            {
                enter.onClick.Invoke();
                Log140.Info("[QLTK140] Bam 'Vao game'");
                _clickedEnter = true;
                Transition(Phase.EnterGame);
                return;
            }

            // Strategy 2: Call GameManager.gI().onStartGame() directly (native game API)
            try
            {
                var gm = GameManager.gI();
                if (gm != null)
                {
                    gm.onStartGame();
                    Log140.Info("[QLTK140] Goi GameManager.onStartGame() thanh cong");
                    _clickedEnter = true;
                    Transition(Phase.EnterGame);
                    return;
                }
            }
            catch (Exception ex) { Log140.Warn("[QLTK140] GameManager.onStartGame() loi: " + ex.Message); }

            // Strategy 3: Find CharacterChoosingObject and click its actionButton
            try
            {
                var charObjs = UnityEngine.Object.FindObjectsOfType<CharacterChoosingObject>(true);
                if (charObjs != null && charObjs.Length > 0)
                {
                    foreach (var cObj in charObjs)
                    {
                        if (cObj == null || !cObj.gameObject.activeInHierarchy) continue;

                        Button btn = null;
                        try { if (cObj.actionButton != null) btn = cObj.actionButton.button; } catch { }
                        if (btn == null) { try { btn = cObj.GetComponentInChildren<Button>(true); } catch { } }
                        if (btn == null || !btn.gameObject.activeInHierarchy) continue;

                        string btnText = GetButtonText(btn);
                        if (!string.IsNullOrEmpty(btnText) && StripDiacritics(btnText).ToLowerInvariant().Contains("tao moi"))
                            continue;   // skip "Tao moi" (create new) slots

                        btn.onClick.Invoke();
                        Log140.Info($"[QLTK140] Bam actionButton CCO '{cObj.gameObject.name}' text='{btnText}'");
                        _clickedEnter = true;
                        Transition(Phase.EnterGame);
                        return;
                    }
                }
            }
            catch (Exception ex) { Log140.Warn("[QLTK140] Tim CCO actionButton loi: " + ex.Message); }

            // Strategy 4: Fallback button names
            Button fallback = FindButton("EnterGameButton")
                           ?? FindButton("StartButton")
                           ?? FindButton("PlayButton");
            if (fallback != null && fallback.gameObject.activeInHierarchy)
            {
                fallback.onClick.Invoke();
                Log140.Info($"[QLTK140] Bam nut vao game du phong '{fallback.gameObject.name}'");
                _clickedEnter = true;
                Transition(Phase.EnterGame);
            }
        }
        /// <summary>Chan doan: liet ke toan bo UI man chon nhan vat de tim nut dung.</summary>
        private void DumpCharacterScreen()
        {
            try
            {
                var btns = UnityEngine.Object.FindObjectsOfType<Button>(true);
                Log140.Info("DUMP: " + btns.Length + " Button trong scene");
                foreach (var b in btns)
                {
                    if (b == null) continue;
                    string txt = GetButtonText(b);
                    Log140.Info("  BTN name='" + b.gameObject.name + "' active=" + b.gameObject.activeInHierarchy +
                                " interactable=" + b.interactable + " text='" + (txt ?? "") + "'");
                }
            }
            catch (Exception ex) { Log140.Error("Dump Button loi: " + ex.Message); }

            try
            {
                var objs = UnityEngine.Object.FindObjectsOfType<CharacterChoosingObject>(true);
                Log140.Info("DUMP: " + objs.Length + " CharacterChoosingObject");
                foreach (var o in objs)
                {
                    if (o == null) continue;
                    string btnName = "null";
                    try { if (o.actionButton != null && o.actionButton.button != null) btnName = o.actionButton.button.gameObject.name; } catch { }
                    Log140.Info("  CCO name='" + o.gameObject.name + "' active=" + o.gameObject.activeInHierarchy +
                                " actionBtn='" + btnName + "'");
                }
            }
            catch (Exception ex) { Log140.Error("Dump CCO loi: " + ex.Message); }

            try
            {
                var panels = UnityEngine.Object.FindObjectsOfType<CharacterChoosingPanel>(true);
                Log140.Info("DUMP: " + panels.Length + " CharacterChoosingPanel");
                foreach (var p in panels)
                    if (p != null)
                        Log140.Info("  CCP name='" + p.gameObject.name + "' active=" + p.gameObject.activeInHierarchy);
            }
            catch { }
        }
        private void TrySelectCharacterSlot()
        {
            int index = _args.CharIndex > 0 ? _args.CharIndex - 1 : 0;

            CharacterChoosingObject[] slots = null;
            try { slots = UnityEngine.Object.FindObjectsOfType<CharacterChoosingObject>(true); } catch { }
            if (slots == null || slots.Length <= 1) return;   // 1 nhĂ¢n váº­t: khĂ´ng cáº§n chá»n slot

            var list = new List<CharacterChoosingObject>();
            foreach (var s in slots)
                if (s != null && s.gameObject.activeInHierarchy) list.Add(s);
            if (list.Count <= 1) return;

            if (index >= list.Count) index = 0;

            try
            {
                var slot = list[index];
                Button btn = null;
                try { if (slot.actionButton != null) btn = slot.actionButton.button; } catch { }
                if (btn == null) { try { btn = slot.GetComponentInChildren<Button>(true); } catch { } }
                if (btn == null || !btn.gameObject.activeInHierarchy) return;

                string txt = GetButtonText(btn);
                if (!string.IsNullOrEmpty(txt) && StripDiacritics(txt).ToLowerInvariant().Contains("tao moi"))
                    return;   // slot trá»‘ng "Táº¡o má»›i" -> bá» qua

                btn.onClick.Invoke();
                Log140.Info($"[QLTK140] Chon slot nhan vat #{index} ('{slot.gameObject.name}')");
            }
            catch (Exception ex) { Log140.Warn("[QLTK140] Chon slot nhan vat loi: " + ex.Message); }
        }

        // ---------- bÆ°á»›c 5: chá» vĂ o map ----------
        private void HandleEnterGame(string scene)
        {
            if (scene == "MainGameScene")
            {
                Log140.Info("[QLTK140] LOGIN_SUCCESS: da vao MainGameScene");
                Transition(Phase.Done);
                SendStt();
                return;
            }

            if (_phaseTime > StepTimeout) { Timeout($"cho MainGameScene that bai (dang o '{scene}')"); return; }

            // Retry "Vao game" click every 3 seconds using a proper timer (not every frame)
            _retryEnterTimer += Time.deltaTime;
            if (_phaseTime > 3f && _retryEnterTimer >= 3f)
            {
                _retryEnterTimer = 0f;

                // Try button text first
                var btn = FindButtonByText("Vào game");
                if (btn == null) btn = FindButtonByText("vao game");
                if (btn != null && btn.gameObject.activeInHierarchy)
                {
                    try { btn.onClick.Invoke(); } catch { }
                    Log140.Info("[QLTK140] Retry bam 'Vao game'");
                    return;
                }

                // Try GameManager.onStartGame() as fallback
                try
                {
                    var gm = GameManager.gI();
                    if (gm != null)
                    {
                        gm.onStartGame();
                        Log140.Info("[QLTK140] Retry goi GameManager.onStartGame()");
                    }
                }
                catch { }
            }
        }
        // ---------- gá»­i STT lĂªn QLTK ----------
        private void SendStt()
        {
            if (_sttSent) return;
            if (_socket == null || !_socket.IsConnected)
            {
                Log140.Warn("[QLTK140] Socket chua san sang, khong gui duoc STT");
                return;
            }

            try
            {
                _socket.Send($"STT:{_args.Stt}");
                _sttSent = true;
                Log140.Info($"[QLTK140] Da gui STT={_args.Stt} len QLTK");
            }
            catch (Exception ex)
            {
                Log140.Error("[QLTK140] Gui STT loi: " + ex.Message);
            }
        }

        // ---------- tiá»‡n Ă­ch ----------

        private static string SafeSceneName()
        {
            try { return SceneManager.GetActiveScene().name ?? ""; }
            catch { return ""; }
        }

        private static LoginUIManager GetLoginUIManager()
        {
            try
            {
                var ui = LoginUIManager.gI();
                if (ui != null) return ui;
            }
            catch { }

            try { return UnityEngine.Object.FindObjectOfType<LoginUIManager>(true); }
            catch { return null; }
        }

        private static LoginPanel GetLoginPanel(LoginUIManager ui)
        {
            try { if (ui != null && ui.loginPanel != null) return ui.loginPanel; }
            catch { }

            try { return UnityEngine.Object.FindObjectOfType<LoginPanel>(true); }
            catch { return null; }
        }

        private static bool IsCharacterSelectVisible()
        {
            try
            {
                var panel = UnityEngine.Object.FindObjectOfType<CharacterChoosingPanel>(true);
                if (panel != null && panel.gameObject.activeInHierarchy) return true;
            }
            catch { }

            try
            {
                var objs = UnityEngine.Object.FindObjectsOfType<CharacterChoosingObject>(true);
                if (objs != null)
                    foreach (var o in objs)
                        if (o != null && o.gameObject.activeInHierarchy) return true;
            }
            catch { }

            try
            {
                var btn = FindButtonByText("VĂ o game");
                if (btn != null && btn.gameObject.activeInHierarchy) return true;
            }
            catch { }

            return false;
        }

        private static string SafeServerName(ServerObject so)
        {
            try
            {
                if (so.serverName != null && !string.IsNullOrEmpty(so.serverName.text))
                    return so.serverName.text.Trim();
            }
            catch { }

            try
            {
                var tmp = so.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text.Trim();
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Khá»›p tĂªn server. QLTK truyá»n tĂªn Ä‘áº§y Ä‘á»§ ("VÅ© Trá»¥ 1"); náº¿u parse bá»‹
        /// máº¥t dáº¥u cĂ¡ch thĂ¬ váº«n khá»›p Ä‘Æ°á»£c theo sá»‘ hiá»‡u server.
        /// </summary>
        private static bool ServerMatch(string display, string target)
        {
            if (string.IsNullOrEmpty(display)) return false;
            if (string.IsNullOrEmpty(target)) return true;

            display = display.Trim();
            target = target.Trim();

            if (string.Equals(display, target, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(StripDiacritics(display), StripDiacritics(target), StringComparison.OrdinalIgnoreCase))
                return true;

            int a = TrailingNumber(display), b = TrailingNumber(target);
            if (a >= 0 && b >= 0 && a == b) return true;

            return false;
        }

        private static int TrailingNumber(string s)
        {
            var m = Regex.Match(s ?? "", @"(\d+)\s*$");
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : -1;
        }

        private static string StripDiacritics(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";

            string formD = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (char c in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC)
                     .Replace('\u0111', 'd').Replace('\u0110', 'D');
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

        private static Button FindButtonByText(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            string want = StripDiacritics(target).Trim().ToLowerInvariant();

            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Button>(true);
                foreach (var btn in all)
                {
                    if (btn == null || !btn.gameObject.activeInHierarchy) continue;

                    string txt = GetButtonText(btn);
                    if (string.IsNullOrEmpty(txt)) continue;

                    string clean = Regex.Replace(txt, "<.*?>", string.Empty);
                    clean = StripDiacritics(clean).Trim().ToLowerInvariant();

                    if (clean.Equals(want) || clean.Contains(want)) return btn;
                }
            }
            catch { }

            return null;
        }

        private static Button FindButton(string name)
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Button>(true);
                foreach (var b in all)
                    if (b != null && b.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return b;
            }
            catch { }
            return null;
        }
    }
}
