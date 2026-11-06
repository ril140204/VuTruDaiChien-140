using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QA.TestBot
{
    [BepInPlugin("com.qa.testbot", "QA TestBot", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public static ManualLogSource LogSource;

        public override void Load()
        {
            LogSource = Log;

            string accountFile = Environment.GetEnvironmentVariable("TESTBOT_ACCOUNT_FILE");
            if (string.IsNullOrEmpty(accountFile) || !File.Exists(accountFile))
            {
                Log.LogInfo("[QA_TestBot] TESTBOT_ACCOUNT_FILE is not set. Standby mode.");
                return;
            }

            Log.LogInfo($"[QA_TestBot] Active! Loading config: {accountFile}");
            ClassInjector.RegisterTypeInIl2Cpp<TestBotController>();
            AddComponent<TestBotController>();
        }
    }

    public static class ServerHelper
    {
        public static bool IsServerInWhitelist(string serverName, List<string> whitelist)
        {
            if (string.IsNullOrEmpty(serverName) || whitelist == null) return false;
            foreach (var item in whitelist)
            {
                if (IsServerMatch(serverName, item))
                    return true;
            }
            return false;
        }

        public static bool IsServerMatch(string displayName, string targetName)
        {
            if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(targetName))
                return false;

            displayName = displayName.Trim();
            targetName = targetName.Trim();

            if (string.Equals(displayName, targetName, StringComparison.OrdinalIgnoreCase))
                return true;

            // Khớp số thứ tự server (1, 2, 3...)
            if ((targetName.Contains("1") || targetName.Equals("1")) && (displayName.Contains("1") || displayName.Equals("1")))
                return true;
            if ((targetName.Contains("2") || targetName.Equals("2")) && (displayName.Contains("2") || displayName.Equals("2")))
                return true;
            if ((targetName.Contains("3") || targetName.Equals("3")) && (displayName.Contains("3") || displayName.Equals("3")))
                return true;

            return false;
        }
    }

    /// <summary>
    /// Bo boc ConsoleLogListener cua BepInEx de loai bo cac dong log nhieu den tu Unity
    /// (vi du NullReferenceException phat sinh do thieu thu vien Facebook.Unity.Windows).
    /// CHI chan log co nguon "Unity". Log cua BepInEx va cua plugin QA TestBot luon duoc giu nguyen.
    /// </summary>
    public class FilteringLogListener : ILogListener
    {
        private readonly ILogListener _inner;
        private readonly List<string> _patterns;

        public FilteringLogListener(ILogListener inner, List<string> patterns)
        {
            _inner = inner;
            _patterns = patterns ?? new List<string>();
        }

        public LogLevel LogLevelFilter => _inner != null ? _inner.LogLevelFilter : LogLevel.All;

        public void LogEvent(object sender, LogEventArgs args)
        {
            if (_inner == null) return;
            if (args != null && ShouldSuppress(args)) return;
            _inner.LogEvent(sender, args);
        }

        public void Dispose()
        {
            // ConsoleLogListener.Dispose() la rong nen chuyen tiep khong gay anh huong.
            (_inner as IDisposable)?.Dispose();
        }

        private bool ShouldSuppress(LogEventArgs args)
        {
            // Chi loc log den tu Unity, khong dung vao log cua BepInEx/plugin.
            string sourceName = null;
            try { sourceName = args.Source != null ? args.Source.SourceName : null; }
            catch { }
            if (!string.Equals(sourceName, "Unity", StringComparison.OrdinalIgnoreCase))
                return false;

            string message = args.Data as string;
            if (string.IsNullOrEmpty(message)) return false;

            for (int i = 0; i < _patterns.Count; i++)
            {
                string pattern = _patterns[i];
                if (string.IsNullOrEmpty(pattern)) continue;
                if (message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Quan ly viec cai dat/-go bo bo loc log nhieu tren console cua BepInEx.
    /// </summary>
    public static class LogFilterManager
    {
        private static FilteringLogListener _installedFilter;
        private static ILogListener _originalConsoleListener;

        public static void Apply(List<string> patterns)
        {
            try
            {
                var listeners = BepInEx.Logging.Logger.Listeners;
                if (listeners == null) return;

                // Go bo filter cu (neu co) va tra ve listener goc.
                if (_installedFilter != null)
                {
                    listeners.Remove(_installedFilter);
                    _installedFilter = null;
                }

                if (_originalConsoleListener == null)
                {
                    foreach (var listener in listeners)
                    {
                        if (listener is ConsoleLogListener console)
                        {
                            _originalConsoleListener = console;
                            break;
                        }
                    }
                }

                if (_originalConsoleListener == null)
                {
                    Plugin.LogSource?.LogWarning("[QA_TestBot] Khong tim thay ConsoleLogListener, bo loc log se khong hoat dong.");
                    return;
                }

                // Phai REMOVE listener goc khoi danh sach, neu giu lai no van se in ra log bi chan.
                if (listeners.Contains(_originalConsoleListener))
                    listeners.Remove(_originalConsoleListener);

                if (patterns == null || patterns.Count == 0)
                {
                    // Tat bo loc -> tra listener goc ve lai.
                    if (!listeners.Contains(_originalConsoleListener))
                        listeners.Add(_originalConsoleListener);
                    Plugin.LogSource?.LogInfo("[QA_TestBot] Bo loc log nhieu: TAT.");
                    return;
                }

                // Wrapper thay the listener goc: nhan log -> loc -> chuyen tiep cho listener goc.
                _installedFilter = new FilteringLogListener(_originalConsoleListener, patterns);
                listeners.Add(_installedFilter);
                Plugin.LogSource?.LogInfo($"[QA_TestBot] Bo loc log nhieu: DA BAT. Che do che: {string.Join(" | ", patterns)}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[QA_TestBot] Loi khi cai dat bo loc log: {ex.Message}");
            }
        }

        public static List<string> BuildPatterns(BotConfig config)
        {
            var patterns = new List<string>();
            if (config == null) return patterns;

            if (config.SuppressUnityNullReference)
                patterns.Add("NullReferenceException");

            if (config.SuppressedLogPatterns != null)
            {
                foreach (var p in config.SuppressedLogPatterns)
                {
                    if (!string.IsNullOrWhiteSpace(p) && !patterns.Contains(p))
                        patterns.Add(p.Trim());
                }
            }
            return patterns;
        }
    }

    public class BotConfig
    {
        [JsonPropertyName("test_env_confirmed")]
        public bool TestEnvConfirmed { get; set; } = false;

        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("server_name")]
        public string ServerName { get; set; } = "";

        [JsonPropertyName("allowed_server_names")]
        public List<string> AllowedServerNames { get; set; } = new List<string>();

        [JsonPropertyName("step_timeout")]
        public float StepTimeout { get; set; } = 45.0f;

        [JsonPropertyName("snapshot_interval")]
        public float SnapshotInterval { get; set; } = 5.0f;

        [JsonPropertyName("log_dir")]
        public string LogDir { get; set; } = "QA_TestBot/output";

        [JsonPropertyName("account_input_name")]
        public string AccountInputName { get; set; } = "Account";

        [JsonPropertyName("password_input_name")]
        public string PasswordInputName { get; set; } = "Password";

        [JsonPropertyName("login_button_name")]
        public string LoginButtonName { get; set; } = "LoginButton";

        [JsonPropertyName("server_expand_button_name")]
        public string ServerExpandButtonName { get; set; } = "ExpandButton";

        /// <summary>
        /// An cac dong log loi NullReferenceException phat sinh tu Unity (do thieu Facebook.Unity.Windows).
        /// Chi ap dung cho log nguon "Unity", khong chan log cua BepInEx hay cua plugin.
        /// </summary>
        [JsonPropertyName("suppress_unity_null_reference")]
        public bool SuppressUnityNullReference { get; set; } = true;

        /// <summary>
        /// Danh sach chuoi mau can chan them (khop khong phan biet hoa/thuong).
        /// Vi du: "FileNotFoundException", "Could not load the file"
        /// </summary>
        [JsonPropertyName("suppressed_log_patterns")]
        public List<string> SuppressedLogPatterns { get; set; } = new List<string>();
    }

    public enum BotState
    {
        WaitScene,
        WaitLogin,
        WaitServer,
        ChooseCharacter,
        EnterGame,
        InGame,
        Failed
    }

    public class TestBotController : MonoBehaviour
    {
        public TestBotController(IntPtr ptr) : base(ptr) { }

        private BotConfig _config;
        private BotState _currentState = BotState.WaitScene;
        private float _stateTimer = 0f;
        private float _sceneLogTimer = 0f;
        private float _serverLogTimer = 0f;
        private float _retryTimer = 0f;
        private float _snapshotTimer = 0f;
        private Stopwatch _loginStopwatch;

        private string _eventsFilePath;
        private string _snapshotsFilePath;
        private bool _isInitialized = false;
        private bool _hasInputCredentials = false;
        private bool _hasClickedLogin = false;
        private bool _hasClickedExpand = false;
        private bool _hasClickedEnterGame = false;
        private string _lastServerListSignature = null;

        private readonly object _csvLock = new object();

        private void Awake()
        {
            _loginStopwatch = Stopwatch.StartNew();
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                string accountFilePath = Environment.GetEnvironmentVariable("TESTBOT_ACCOUNT_FILE");
                if (string.IsNullOrEmpty(accountFilePath) || !File.Exists(accountFilePath))
                {
                    Plugin.LogSource.LogError("[TestBot] Account config file missing.");
                    _currentState = BotState.Failed;
                    return;
                }

                string jsonContent = File.ReadAllText(accountFilePath);
                _config = JsonSerializer.Deserialize<BotConfig>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (_config == null)
                {
                    Plugin.LogSource.LogError("[TestBot] Failed to parse config JSON.");
                    _currentState = BotState.Failed;
                    return;
                }

                if (!_config.TestEnvConfirmed)
                {
                    Plugin.LogSource.LogError("[TestBot] REFUSING TO RUN: test_env_confirmed is false!");
                    _currentState = BotState.Failed;
                    return;
                }

                if (string.IsNullOrEmpty(_config.ServerName) ||
                    _config.AllowedServerNames == null ||
                    !ServerHelper.IsServerInWhitelist(_config.ServerName, _config.AllowedServerNames))
                {
                    Plugin.LogSource.LogError($"[TestBot] REFUSING TO RUN: server '{_config.ServerName}' not in allowed_server_names!");
                    _currentState = BotState.Failed;
                    return;
                }

                string accountSafe = string.IsNullOrEmpty(_config.Username) ? "unknown" : _config.Username;
                string accountLogDir = Path.Combine(_config.LogDir, accountSafe);
                if (!Directory.Exists(accountLogDir))
                {
                    Directory.CreateDirectory(accountLogDir);
                }

                _eventsFilePath = Path.Combine(accountLogDir, "events.csv");
                _snapshotsFilePath = Path.Combine(accountLogDir, "snapshots.csv");

                InitCsvFiles();

                // Cai dat bo loc log nhieu Unity (chi ap dung khi config da duoc xac nhan).
                LogFilterManager.Apply(LogFilterManager.BuildPatterns(_config));

                LogEvent("INIT", $"Initialized for user '{_config.Username}', target server: '{_config.ServerName}'");
                _isInitialized = true;
                TransitionTo(BotState.WaitScene);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TestBot] Init exception: {ex}");
                _currentState = BotState.Failed;
            }
        }

        private void InitCsvFiles()
        {
            lock (_csvLock)
            {
                if (!File.Exists(_eventsFilePath))
                {
                    File.WriteAllText(_eventsFilePath, "timestamp,account,state,event_type,message\n");
                }
                if (!File.Exists(_snapshotsFilePath))
                {
                    File.WriteAllText(_snapshotsFilePath, "time,account,scene,type,name,x,y,z\n");
                }
            }
        }

        private void LogEvent(string eventType, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},{_config?.Username ?? "N/A"},{_currentState},{eventType},\"{message.Replace("\"", "\"\"")}\"\n";
            Plugin.LogSource?.LogInfo($"[TestBot] {line.TrimEnd()}");

            if (!string.IsNullOrEmpty(_eventsFilePath))
            {
                try
                {
                    lock (_csvLock)
                    {
                        File.AppendAllText(_eventsFilePath, line);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource?.LogError($"[TestBot] Error writing CSV: {ex.Message}");
                }
            }
        }

        private void TransitionTo(BotState newState)
        {
            LogEvent("STATE_CHANGE", $"State transition: {_currentState} -> {newState}");
            _currentState = newState;
            _stateTimer = 0f;
            _retryTimer = 0f;
            _lastServerListSignature = null;
        }

        private void Update()
        {
            if (!_isInitialized || _currentState == BotState.Failed)
                return;

            _stateTimer += Time.deltaTime;
            _sceneLogTimer += Time.deltaTime;
            _serverLogTimer += Time.deltaTime;

            try
            {
                string activeScene = SceneManager.GetActiveScene().name;

                // 1. Nếu đang ở MainGameScene bất kỳ lúc nào -> Chuyển sang InGame
                if (activeScene == "MainGameScene" && _currentState != BotState.InGame)
                {
                    _loginStopwatch.Stop();
                    double durationSec = _loginStopwatch.Elapsed.TotalSeconds;
                    LogEvent("METRIC", $"LOGIN_SUCCESS: Entered MainGameScene in {durationSec:F2} seconds.");
                    TransitionTo(BotState.InGame);
                    return;
                }

                // 2. Kiểm tra ưu tiên: Nếu màn hình chọn nhân vật đã xuất hiện trong khi đang ở WaitLogin hoặc WaitServer
                if ((_currentState == BotState.WaitLogin || _currentState == BotState.WaitServer) && IsCharacterSelectVisible())
                {
                    LogEvent("ACTION", "Character selection screen detected! Transitioning to ChooseCharacter.");
                    TransitionTo(BotState.ChooseCharacter);
                    return;
                }

                switch (_currentState)
                {
                    case BotState.WaitScene:
                        HandleWaitScene(activeScene);
                        break;

                    case BotState.WaitLogin:
                        HandleWaitLogin(activeScene);
                        break;

                    case BotState.WaitServer:
                        HandleWaitServer();
                        break;

                    case BotState.ChooseCharacter:
                        HandleChooseCharacter();
                        break;

                    case BotState.EnterGame:
                        HandleEnterGame(activeScene);
                        break;

                    case BotState.InGame:
                        HandleInGame(activeScene);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogEvent("ERROR", $"Exception in state {_currentState}: {ex.Message}");
            }
        }

        private void HandleWaitScene(string activeScene)
        {
            if (_sceneLogTimer >= 3.0f)
            {
                _sceneLogTimer = 0f;
                LogEvent("SCENE_WAIT", $"Current active scene: '{activeScene}'. Waiting for LoginScene...");
            }

            if (activeScene == "LoginScene")
            {
                LogEvent("SCENE_LOADED", "LoginScene detected! Starting login sequence.");
                TransitionTo(BotState.WaitLogin);
            }
            else if (_stateTimer > 90.0f)
            {
                LogEvent("TIMEOUT", $"Timeout 90s waiting for LoginScene. Stuck in '{activeScene}'.");
                TransitionTo(BotState.Failed);
            }
        }

        private void HandleWaitLogin(string activeScene)
        {
            if (_stateTimer > _config.StepTimeout)
            {
                LogEvent("TIMEOUT", $"Timeout {_config.StepTimeout}s waiting for login inputs in {activeScene}.");
                TransitionTo(BotState.Failed);
                return;
            }

            LoginUIManager uiMgr = null;
            try { uiMgr = LoginUIManager.gI() ?? UnityEngine.Object.FindObjectOfType<LoginUIManager>(true); } catch { }

            LoginPanel loginPanel = null;
            try { loginPanel = uiMgr?.loginPanel ?? UnityEngine.Object.FindObjectOfType<LoginPanel>(true); } catch { }

            if (uiMgr != null && (loginPanel == null || !loginPanel.gameObject.activeInHierarchy))
            {
                try { uiMgr.showLoginPanel(); } catch { }
            }

            if (loginPanel != null && !loginPanel.gameObject.activeInHierarchy)
            {
                try { loginPanel.gameObject.SetActive(true); } catch { }
            }

            TMP_InputField accInput = null;
            TMP_InputField passInput = null;
            Button loginBtn = null;

            try
            {
                accInput = loginPanel?.accountField?.inputField ?? FindInputField(_config.AccountInputName);
                passInput = loginPanel?.passwordField?.inputField ?? FindInputField(_config.PasswordInputName);
                loginBtn = loginPanel?.loginButton?.button ?? FindButton(_config.LoginButtonName);
            }
            catch { }

            if (accInput != null && passInput != null && loginBtn != null)
            {
                if (!_hasInputCredentials)
                {
                    try
                    {
                        if (loginPanel?.accountField != null)
                            loginPanel.accountField.setText(_config.Username);
                        else
                            accInput.text = _config.Username;

                        if (loginPanel?.passwordField != null)
                            loginPanel.passwordField.setText(_config.Password);
                        else
                            passInput.text = _config.Password;

                        _hasInputCredentials = true;
                        LogEvent("ACTION", $"Filled credentials for '{_config.Username}'.");
                    }
                    catch (Exception ex)
                    {
                        LogEvent("ERROR", $"Error setting credentials: {ex.Message}");
                    }
                }

                if (!_hasClickedLogin && _stateTimer > 0.5f)
                {
                    LogEvent("ACTION", "Clicking login button.");
                    loginBtn.onClick.Invoke();
                    _hasClickedLogin = true;
                    TransitionTo(BotState.WaitServer);
                }
            }
        }

        private void HandleWaitServer()
        {
            if (_stateTimer > _config.StepTimeout)
            {
                LogEvent("TIMEOUT", $"Timeout {_config.StepTimeout}s waiting for server list.");
                TransitionTo(BotState.Failed);
                return;
            }

            // 1. Kiểm tra nếu màn hình chọn nhân vật đã xuất hiện
            if (IsCharacterSelectVisible())
            {
                LogEvent("ACTION", "Character selection screen detected in WaitServer. Transitioning to ChooseCharacter.");
                TransitionTo(BotState.ChooseCharacter);
                return;
            }

            // 2. Mở dropdown server 1 lần duy nhất nếu đang ở màn chọn server
            if (!_hasClickedExpand && _stateTimer > 1.0f)
            {
                _hasClickedExpand = true;
                try
                {
                    ChooseServerPanel chooseServer = UnityEngine.Object.FindObjectOfType<ChooseServerPanel>(true);
                    if (chooseServer != null && chooseServer.gameObject.activeInHierarchy)
                    {
                        Button expandBtn = chooseServer.expandButton?.button ?? FindButton(_config.ServerExpandButtonName);
                        if (expandBtn != null && expandBtn.gameObject.activeInHierarchy)
                        {
                            LogEvent("ACTION", "Clicking expandButton to show server list.");
                            expandBtn.onClick.Invoke();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogEvent("WARN", $"ExpandButton exception: {ex.Message}");
                }
            }

            // 3. Quét danh sách ServerObject
            ServerObject[] serverObjects = null;
            try
            {
                serverObjects = UnityEngine.Object.FindObjectsOfType<ServerObject>(true);
            }
            catch { }

            if (serverObjects != null && serverObjects.Length > 0)
            {
                List<string> foundNames = new List<string>();
                foreach (ServerObject sObj in serverObjects)
                {
                    if (sObj == null || !sObj.gameObject.activeInHierarchy) continue;

                    string displayName = null;
                    try
                    {
                        if (sObj.serverName != null)
                            displayName = sObj.serverName.text;
                    }
                    catch { }

                    if (string.IsNullOrEmpty(displayName))
                    {
                        try
                        {
                            var tmp = sObj.GetComponentInChildren<TextMeshProUGUI>(true);
                            if (tmp != null) displayName = tmp.text;
                        }
                        catch { }
                    }

                    if (string.IsNullOrEmpty(displayName)) continue;
                    displayName = displayName.Trim();
                    foundNames.Add(displayName);

                    if (ServerHelper.IsServerMatch(displayName, _config.ServerName) &&
                        ServerHelper.IsServerInWhitelist(displayName, _config.AllowedServerNames))
                    {
                        Button btn = null;
                        try
                        {
                            if (sObj.myButton != null && sObj.myButton.button != null)
                                btn = sObj.myButton.button;
                        }
                        catch { }

                        if (btn == null)
                        {
                            try { btn = sObj.GetComponent<Button>() ?? sObj.GetComponentInChildren<Button>(true); } catch { }
                        }

                        if (btn != null)
                        {
                            LogEvent("ACTION", $"Found matching server: '{displayName}'. Clicking to enter.");
                            btn.onClick.Invoke();
                            TransitionTo(BotState.ChooseCharacter);
                            return;
                        }
                    }
                }

                // Chi ghi SERVER_LIST khi danh sach server thay doi thuc su.
                // Neu ghi moi 2 giay se lam ngan output CSV va spam console (hàng nghìn dòng).
                string serverSignature = string.Join("|", foundNames);
                if (!string.Equals(serverSignature, _lastServerListSignature, StringComparison.Ordinal))
                {
                    _lastServerListSignature = serverSignature;
                    _serverLogTimer = 0f;
                    LogEvent("SERVER_LIST", $"Scanned servers: [{string.Join(", ", foundNames)}]. Target: '{_config.ServerName}'");
                }
            }
        }

        private void HandleChooseCharacter()
        {
            if (_stateTimer > _config.StepTimeout)
            {
                LogEvent("TIMEOUT", $"Timeout {_config.StepTimeout}s in ChooseCharacter.");
                TransitionTo(BotState.Failed);
                return;
            }

            if (_hasClickedEnterGame)
            {
                TransitionTo(BotState.EnterGame);
                return;
            }

            // Đợi 0.5s để UI nhân vật render ổn định
            if (_stateTimer < 0.5f) return;

            // Cách 1: Tìm button có text "Vào game"
            Button vaoGameBtn = FindButtonByText("Vào game");
            if (vaoGameBtn != null && vaoGameBtn.gameObject.activeInHierarchy)
            {
                LogEvent("ACTION", "Found 'Vào game' button by text. Clicking to enter game.");
                vaoGameBtn.onClick.Invoke();
                _hasClickedEnterGame = true;
                TransitionTo(BotState.EnterGame);
                return;
            }

            // Cách 2: Tìm qua CharacterChoosingObject (slot nhân vật đầu tiên có sẵn)
            try
            {
                CharacterChoosingObject[] charObjs = UnityEngine.Object.FindObjectsOfType<CharacterChoosingObject>(true);
                if (charObjs != null && charObjs.Length > 0)
                {
                    foreach (var cObj in charObjs)
                    {
                        if (cObj == null || !cObj.gameObject.activeInHierarchy) continue;

                        Button btn = cObj.GetComponentInChildren<Button>(true);
                        if (btn != null && btn.gameObject.activeInHierarchy)
                        {
                            string btnText = GetButtonText(btn);
                            // Bỏ qua nếu là nút "Tạo mới"
                            if (!string.IsNullOrEmpty(btnText) && btnText.ToLowerInvariant().Contains("tạo mới"))
                                continue;

                            LogEvent("ACTION", $"Found CharacterChoosingObject '{cObj.gameObject.name}' with button text '{btnText}'. Clicking to enter game.");
                            btn.onClick.Invoke();
                            _hasClickedEnterGame = true;
                            TransitionTo(BotState.EnterGame);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent("WARN", $"CharacterChoosingObject exception: {ex.Message}");
            }

            // Cách 3: Nút fallback
            Button fallbackBtn = FindButton("EnterGameButton") ?? FindButton("StartButton") ?? FindButton("PlayButton");
            if (fallbackBtn != null && fallbackBtn.gameObject.activeInHierarchy)
            {
                LogEvent("ACTION", $"Clicking fallback enter button '{fallbackBtn.gameObject.name}'.");
                fallbackBtn.onClick.Invoke();
                _hasClickedEnterGame = true;
                TransitionTo(BotState.EnterGame);
            }
        }

        private void HandleEnterGame(string activeScene)
        {
            if (_stateTimer > _config.StepTimeout)
            {
                LogEvent("TIMEOUT", $"Timeout {_config.StepTimeout}s waiting for MainGameScene (Current: {activeScene}).");
                TransitionTo(BotState.Failed);
                return;
            }

            if (activeScene == "MainGameScene")
            {
                _loginStopwatch.Stop();
                double durationSec = _loginStopwatch.Elapsed.TotalSeconds;
                LogEvent("METRIC", $"LOGIN_SUCCESS: Entered MainGameScene in {durationSec:F2} seconds.");
                TransitionTo(BotState.InGame);
                return;
            }

            // Nếu sau 3 giây mà vẫn ở màn hình chọn nhân vật (do click bị lag hoặc server chưa phản hồi)
            if (_stateTimer > 3.0f && _retryTimer >= 3.0f)
            {
                _retryTimer = 0f;
                Button vaoGameBtn = FindButtonByText("Vào game");
                if (vaoGameBtn != null && vaoGameBtn.gameObject.activeInHierarchy)
                {
                    LogEvent("ACTION", "Still in character screen after 3s. Retrying 'Vào game' click.");
                    vaoGameBtn.onClick.Invoke();
                }
            }
            else
            {
                _retryTimer += Time.deltaTime;
            }
        }

        private void HandleInGame(string activeScene)
        {
            _snapshotTimer += Time.deltaTime;
            if (_snapshotTimer >= _config.SnapshotInterval)
            {
                _snapshotTimer = 0f;
                RecordSnapshot(activeScene);
            }
        }

        private void RecordSnapshot(string activeScene)
        {
            try
            {
                string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string account = _config.Username;
                List<string> lines = new List<string>();

                // 1. Snapshot MainPlayer (tọa độ nhân vật của bot)
                try
                {
                    MainPlayer[] mainPlayers = UnityEngine.Object.FindObjectsOfType<MainPlayer>(true);
                    if (mainPlayers != null)
                    {
                        foreach (var player in mainPlayers)
                        {
                            if (player == null || player.gameObject == null) continue;
                            Vector3 pos = player.transform.position;
                            lines.Add($"{timeStr},{account},{activeScene},MainPlayer,\"{player.gameObject.name}\",{pos.x:F2},{pos.y:F2},{pos.z:F2}");
                        }
                    }
                }
                catch { }

                // 2. Snapshot Quái (Mob)
                try
                {
                    Mob[] mobs = UnityEngine.Object.FindObjectsOfType<Mob>(true);
                    if (mobs != null)
                    {
                        foreach (var mob in mobs)
                        {
                            if (mob == null || mob.gameObject == null) continue;
                            Vector3 pos = mob.transform.position;
                            lines.Add($"{timeStr},{account},{activeScene},Mob,\"{mob.gameObject.name}\",{pos.x:F2},{pos.y:F2},{pos.z:F2}");
                        }
                    }
                }
                catch { }

                // 3. Snapshot NPC
                try
                {
                    NPC[] npcs = UnityEngine.Object.FindObjectsOfType<NPC>(true);
                    if (npcs != null)
                    {
                        foreach (var npc in npcs)
                        {
                            if (npc == null || npc.gameObject == null) continue;
                            Vector3 pos = npc.transform.position;
                            lines.Add($"{timeStr},{account},{activeScene},NPC,\"{npc.gameObject.name}\",{pos.x:F2},{pos.y:F2},{pos.z:F2}");
                        }
                    }
                }
                catch { }

                // 4. Snapshot Người chơi khác (OtherPlayer)
                try
                {
                    OtherPlayer[] players = UnityEngine.Object.FindObjectsOfType<OtherPlayer>(true);
                    if (players != null)
                    {
                        foreach (var player in players)
                        {
                            if (player == null || player.gameObject == null) continue;
                            Vector3 pos = player.transform.position;
                            lines.Add($"{timeStr},{account},{activeScene},OtherPlayer,\"{player.gameObject.name}\",{pos.x:F2},{pos.y:F2},{pos.z:F2}");
                        }
                    }
                }
                catch { }

                if (lines.Count > 0)
                {
                    lock (_csvLock)
                    {
                        File.AppendAllLines(_snapshotsFilePath, lines);
                    }
                    LogEvent("SNAPSHOT", $"Snapshot: {lines.Count} objects recorded.");
                }
                else
                {
                    LogEvent("SNAPSHOT", "Snapshot tick: No objects currently rendered.");
                }
            }
            catch (Exception ex)
            {
                LogEvent("ERROR", $"Error in RecordSnapshot: {ex.Message}");
            }
        }

        private bool IsCharacterSelectVisible()
        {
            try
            {
                // 1. Kiểm tra panel chọn nhân vật
                CharacterChoosingPanel charPanel = UnityEngine.Object.FindObjectOfType<CharacterChoosingPanel>(true);
                if (charPanel != null && charPanel.gameObject.activeInHierarchy)
                    return true;

                // 2. Kiểm tra đối tượng nhân vật (CharacterChoosingObject)
                CharacterChoosingObject[] charObjs = UnityEngine.Object.FindObjectsOfType<CharacterChoosingObject>(true);
                if (charObjs != null)
                {
                    foreach (var c in charObjs)
                    {
                        if (c != null && c.gameObject.activeInHierarchy)
                            return true;
                    }
                }

                // 3. Kiểm tra nút "Vào game"
                Button vaoGameBtn = FindButtonByText("Vào game");
                if (vaoGameBtn != null && vaoGameBtn.gameObject.activeInHierarchy)
                    return true;
            }
            catch { }

            return false;
        }

        private string GetButtonText(Button btn)
        {
            if (btn == null) return null;
            try
            {
                var tmps = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (tmps != null)
                {
                    foreach (var t in tmps)
                    {
                        if (t != null && !string.IsNullOrEmpty(t.text))
                            return t.text.Trim();
                    }
                }

                var texts = btn.GetComponentsInChildren<Text>(true);
                if (texts != null)
                {
                    foreach (var t in texts)
                    {
                        if (t != null && !string.IsNullOrEmpty(t.text))
                            return t.text.Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        private Button FindButtonByText(string targetText)
        {
            if (string.IsNullOrEmpty(targetText)) return null;
            string targetLower = targetText.Trim().ToLowerInvariant();

            try
            {
                Button[] allBtns = UnityEngine.Object.FindObjectsOfType<Button>(false);
                foreach (var btn in allBtns)
                {
                    if (btn == null || !btn.gameObject.activeInHierarchy) continue;

                    string btnText = GetButtonText(btn);
                    if (string.IsNullOrEmpty(btnText)) continue;

                    // Loại bỏ thẻ HTML/rich text ví dụ <color=red>...</color>
                    string clean = Regex.Replace(btnText, "<.*?>", string.Empty).Trim().ToLowerInvariant();
                    if (clean.Equals(targetLower) || clean.Contains(targetLower))
                    {
                        return btn;
                    }
                }
            }
            catch { }
            return null;
        }

        private TMP_InputField FindInputField(string name)
        {
            TMP_InputField[] allInputs = UnityEngine.Object.FindObjectsOfType<TMP_InputField>(true);
            foreach (var inp in allInputs)
            {
                if (inp != null && inp.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return inp;
                }
            }
            return null;
        }

        private Button FindButton(string name)
        {
            Button[] allBtns = UnityEngine.Object.FindObjectsOfType<Button>(true);
            foreach (var b in allBtns)
            {
                if (b != null && b.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return b;
                }
            }
            return null;
        }
    }
}
