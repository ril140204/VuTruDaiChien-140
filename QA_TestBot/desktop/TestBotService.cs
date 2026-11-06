using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QATestBotDesktop
{
    /// <summary>
    /// Địa chỉ các file/thư mục của QA TestBot, tự phát hiện theo vị trí exe.
    /// </summary>
    public static class AppPaths
    {
        private static string _root;

        /// <summary>
        /// Ghi de thu muc goc. Dung khi chay app tu vi tri khac (vd: ban publish).
        /// </summary>
        public static string OverrideRoot { get; set; }

        /// <summary>
        /// Thư mục gốc project (chứa QA_TestBot/ và file game .exe).
        /// </summary>
        public static string Root
        {
            get
            {
                if (_root != null) return _root;

                // 0. Ghi de truc tiep trong code (dung cho test).
                if (!string.IsNullOrWhiteSpace(OverrideRoot) && Directory.Exists(OverrideRoot))
                {
                    _root = Path.GetFullPath(OverrideRoot);
                    return _root;
                }

                // 1. Ghi de tu moi truong (QATESTBOT_ROOT) - tien cho ban publish.
                string env = Environment.GetEnvironmentVariable("QATESTBOT_ROOT");
                if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
                {
                    _root = Path.GetFullPath(env);
                    return _root;
                }

                // 2. Dung file config canh goc dat ke thu muc exe.
                try
                {
                    string cfg = Path.Combine(AppContext.BaseDirectory, "qatestbot.path");
                    if (File.Exists(cfg))
                    {
                        string text = File.ReadAllText(cfg).Trim();
                        if (text.Length > 0 && Directory.Exists(text))
                        {
                            _root = Path.GetFullPath(text);
                            return _root;
                        }
                    }
                }
                catch { }

                string dir = AppContext.BaseDirectory;
                for (int i = 0; i < 10; i++)
                {
                    // Thu muc goc phai chua BOTH thu muc QA_TestBot/config va file game .exe.
                    // Neu chi kiem tra ton tai QA_TestBot thi se match nham o
                    // <root>/QA_TestBot/desktop/bin/... -> sai duong dan.
                    bool hasQa = Directory.Exists(Path.Combine(dir, "QA_TestBot", "config"));
                    bool hasGame = false;
                    try
                    {
                        string[] exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                        foreach (string f in exes)
                        {
                            string n = Path.GetFileNameWithoutExtension(f);
                            if (n.Contains("Tr") || n.Contains("Chi") || n.Contains("Vũ"))
                            {
                                hasGame = true;
                                break;
                            }
                        }
                    }
                    catch { }

                    if (hasQa && hasGame)
                    {
                        _root = dir;
                        return _root;
                    }

                    DirectoryInfo parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;
                }

                // Fallback: thu muc chua QA_TestBot (neu co).
                string qa = Path.Combine(AppContext.BaseDirectory, "QA_TestBot");
                _root = Directory.Exists(qa)
                    ? Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? AppContext.BaseDirectory
                    : AppContext.BaseDirectory;
                return _root;
            }
        }

        public static string QaDir => Path.Combine(Root, "QA_TestBot");
        public static string ConfigDir => Path.Combine(QaDir, "config");
        public static string OutputDir => Path.Combine(QaDir, "output");
        public static string AccountsFile => Path.Combine(ConfigDir, "accounts.json");
        public static string BotConfigFile => Path.Combine(ConfigDir, "bot_config.json");
        public static string StopFlag => Path.Combine(ConfigDir, "stop.flag");
        public static string LauncherScript => Path.Combine(QaDir, "launcher", "launcher.py");
        public static string ReportScript => Path.Combine(QaDir, "launcher", "report.py");

        public static string GameExe
        {
            get
            {
                string[] candidates = Directory.GetFiles(Root, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (string f in candidates)
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (n.Contains("Tr") || n.Contains("Chi") || n.Contains("Vũ"))
                        return f;
                }
                return candidates.Length > 0 ? candidates[0] : null;
            }
        }

        public static string PythonExe
        {
            get
            {
                foreach (string name in new[] { "python", "python3", "py" })
                {
                    string found = FindOnPath(name + ".exe");
                    if (found != null) return found;
                }
                return null;
            }
        }

        private static string FindOnPath(string exeName)
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string full = Path.Combine(dir.Trim(), exeName);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// Trạng thái của một tài khoản, đọc từ events.csv / snapshots.csv.
    /// </summary>
    public class AccountStatus
    {
        public string Username { get; set; } = "";
        public bool Success { get; set; }
        public bool Exists { get; set; }
        public int EventsCount { get; set; }
        public int SnapshotsCount { get; set; }
        public double? Duration { get; set; }
        public string LastState { get; set; } = "";

        public string StatusText
        {
            get
            {
                if (!Exists) return "Chưa chạy";
                if (Success) return "Vào map";
                return "Chưa vào map";
            }
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Account { get; set; } = "";
        public string State { get; set; } = "";
        public string EventType { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Lớp đọc/ghi dữ liệu và điều khiển launcher.
    /// </summary>
    public class TestBotService
    {
        private Process _launcher;
        private readonly object _lock = new object();

        // -- Accounts ---------------------------------------------------------

        public List<AccountEntry> LoadAccounts()
        {
            return JsonStore.Load(AppPaths.AccountsFile, () => new List<AccountEntry>()) ?? new List<AccountEntry>();
        }

        public bool SaveAccounts(List<AccountEntry> accounts)
        {
            return JsonStore.Save(AppPaths.AccountsFile, accounts);
        }

        public BotConfig LoadConfig()
        {
            return JsonStore.Load(AppPaths.BotConfigFile, () => new BotConfig()) ?? new BotConfig();
        }

        public bool SaveConfig(BotConfig config)
        {
            return JsonStore.Save(AppPaths.BotConfigFile, config);
        }

        // -- Status -----------------------------------------------------------

        public List<AccountStatus> GetAccountStatuses()
        {
            var result = new List<AccountStatus>();
            if (!Directory.Exists(AppPaths.OutputDir)) return result;

            foreach (string dir in Directory.GetDirectories(AppPaths.OutputDir))
            {
                string name = Path.GetFileName(dir);
                string eventsFile = Path.Combine(dir, "events.csv");
                string snapsFile = Path.Combine(dir, "snapshots.csv");

                var st = new AccountStatus
                {
                    Username = name,
                    Exists = File.Exists(eventsFile),
                    EventsCount = CountLines(eventsFile),
                    SnapshotsCount = CountLines(snapsFile)
                };

                if (st.Exists)
                {
                    try
                    {
                        var lines = ReadLastLines(eventsFile, 200);
                        foreach (string line in lines)
                        {
                            var parts = SplitCsvLine(line);
                            if (parts.Count < 4) continue;

                            string state = parts[2];
                            string type = parts[3];
                            string msg = parts.Count >= 5 ? parts[4] : "";

                            if (state == "InGame" || state == "EnterGame") st.Success = true;
                            if (msg.Contains("LOGIN_SUCCESS")) st.Success = true;
                            if (type == "METRIC" && msg.Contains("LOGIN_SUCCESS"))
                            {
                                st.Success = true;
                                double d = ParseDuration(msg);
                                if (d > 0) st.Duration = d;
                            }
                            st.LastState = state;
                        }
                    }
                    catch { }
                }

                result.Add(st);
            }
            return result;
        }

        private static double ParseDuration(string msg)
        {
            int idx = msg.IndexOf("in ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;
            string tail = msg.Substring(idx + 3);
            int sp = tail.IndexOf(' ');
            if (sp <= 0) return 0;
            double.TryParse(tail.Substring(0, sp).Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double val);
            return val;
        }

        private static int CountLines(string path)
        {
            if (!File.Exists(path)) return 0;
            try
            {
                int count = 0;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    while (sr.ReadLine() != null) count++;
                }
                return Math.Max(0, count - 1); // bỏ dòng header
            }
            catch { return 0; }
        }

        private static List<string> ReadLastLines(string path, int max)
        {
            var result = new List<string>();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    var buf = new Queue<string>(max + 1);
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (buf.Count == max + 1) buf.Dequeue();
                        buf.Enqueue(line);
                    }
                    result.AddRange(buf);
                }
            }
            catch { }
            return result;
        }

        /// <summary>Tách một dòng CSV, hỗ trợ dấu nháy kép và escaped quote.</summary>
        public static List<string> SplitCsvLine(string line)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(line)) return parts;

            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { parts.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            parts.Add(sb.ToString());
            return parts;
        }

        // -- Events -----------------------------------------------------------

        public List<LogEntry> GetEvents(string account, int limit)
        {
            var result = new List<LogEntry>();
            if (string.IsNullOrWhiteSpace(account)) return result;

            string eventsFile = Path.Combine(AppPaths.OutputDir, account, "events.csv");
            if (!File.Exists(eventsFile)) return result;

            try
            {
                var lines = ReadLastLines(eventsFile, limit + 1);
                foreach (string line in lines)
                {
                    var parts = SplitCsvLine(line);
                    if (parts.Count < 4) continue;
                    if (parts[0] == "timestamp") continue; // header

                    DateTime ts;
                    if (!DateTime.TryParse(parts[0], out ts)) continue;

                    result.Add(new LogEntry
                    {
                        Timestamp = ts,
                        Account = parts.Count > 1 ? parts[1] : "",
                        State = parts.Count > 2 ? parts[2] : "",
                        EventType = parts.Count > 3 ? parts[3] : "",
                        Message = parts.Count > 4 ? parts[4] : ""
                    });
                }
                result.Reverse();
            }
            catch { }
            return result;
        }

        // -- Launcher ---------------------------------------------------------

        public bool IsLauncherRunning()
        {
            lock (_lock)
            {
                if (_launcher != null && !_launcher.HasExited) return true;
            }
            return FindLauncherProcesses().Count > 0;
        }

        public List<int> FindLauncherProcesses()
        {
            var pids = new List<int>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -NonInteractive -Command " +
                                "\"Get-CimInstance Win32_Process -Filter \\\"Name='python.exe'\\\" | " +
                                "ForEach-Object { \\\"$($_.ProcessId)`t$($_.CommandLine)\\\" }\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return pids;
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(20000);

                    foreach (string line in output.Split('\n'))
                    {
                        if (line.IndexOf("launcher.py", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string head = line.Split('\t')[0].Trim();
                        int pid;
                        if (int.TryParse(head, out pid)) pids.Add(pid);
                    }
                }
            }
            catch { }
            return pids;
        }

        public event Action<string> LauncherOutput;

        /// <summary>Khởi chạy launcher.py (nếu chưa chạy).</summary>
        public (bool ok, string message) StartLauncher()
        {
            lock (_lock)
            {
                if (_launcher != null && !_launcher.HasExited)
                    return (false, "Launcher đang chạy rồi.");
            }

            if (IsLauncherRunning())
                return (false, "Launcher đang chạy rồi (phát hiện qua tiến trình).");

            string python = AppPaths.PythonExe;
            if (python == null)
                return (false, "Không tìm thấy Python trong PATH.");

            if (!File.Exists(AppPaths.LauncherScript))
                return (false, "Không tìm thấy launcher.py.");

            if (AppPaths.GameExe == null || !File.Exists(AppPaths.GameExe))
                return (false, "Không tìm thấy game executable trong thư mục gốc.");

            var cfg = LoadConfig();
            if (!cfg.TestEnvConfirmed)
                return (false, "test_env_confirmed = false — hệ thống sẽ từ chối chạy.");

            try
            {
                try
                {
                    if (File.Exists(AppPaths.StopFlag)) File.Delete(AppPaths.StopFlag);
                }
                catch { }

                var psi = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "\"" + AppPaths.LauncherScript + "\"",
                    WorkingDirectory = AppPaths.Root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _launcher = Process.Start(psi);
                if (_launcher == null) return (false, "Không khởi chạy được launcher.");

                _launcher.OutputDataReceived += (s, e) => { if (e.Data != null) LauncherOutput?.Invoke(e.Data); };
                _launcher.ErrorDataReceived += (s, e) => { if (e.Data != null) LauncherOutput?.Invoke("[ERR] " + e.Data); };
                _launcher.BeginOutputReadLine();
                _launcher.BeginErrorReadLine();

                return (true, "Đã khởi chạy launcher (PID: " + _launcher.Id + ").");
            }
            catch (Exception ex)
            {
                return (false, "Không khởi chạy được launcher: " + ex.Message);
            }
        }

        /// <summary>Dừng khẩn cấp: tạo stop.flag (launcher tự terminate client trong &lt;10s).</summary>
        public (bool ok, string message) StopAll()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDir);
                File.Create(AppPaths.StopFlag).Dispose();
                return (true, "Đã tạo stop.flag — launcher sẽ dừng toàn bộ client trong < 10 giây.");
            }
            catch (Exception ex)
            {
                return (false, "Không tạo được stop.flag: " + ex.Message);
            }
        }

        /// <summary>Ép dừng launcher và toàn bộ tiến trình game con.</summary>
        public (bool ok, string message) KillAll()
        {
            int killed = 0;
            lock (_lock)
            {
                if (_launcher != null && !_launcher.HasExited)
                {
                    try { _launcher.Kill(true); killed++; } catch { }
                    _launcher = null;
                }
            }

            foreach (int pid in FindLauncherProcesses())
            {
                try { Process.GetProcessById(pid).Kill(); killed++; } catch { }
            }

            foreach (int pid in FindGameProcesses())
            {
                try { Process.GetProcessById(pid).Kill(); killed++; } catch { }
            }

            try { if (File.Exists(AppPaths.StopFlag)) File.Delete(AppPaths.StopFlag); } catch { }

            return (true, killed > 0
                ? "Đã dừng " + killed + " tiến trình."
                : "Không có tiến trình nào đang chạy.");
        }

        public List<int> FindGameProcesses()
        {
            var pids = new List<int>();
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        string path = "";
                        try { path = p.MainModule.FileName; } catch { }
                        if (!string.IsNullOrEmpty(path) &&
                            path.StartsWith(AppPaths.Root, StringComparison.OrdinalIgnoreCase) &&
                            path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            !path.Contains("QA.TestBot.Desktop"))
                        {
                            pids.Add(p.Id);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return pids;
        }

        // -- Report -----------------------------------------------------------

        public (bool ok, string output) GenerateReport()
        {
            string python = AppPaths.PythonExe;
            if (python == null) return (false, "Không tìm thấy Python trong PATH.");
            if (!File.Exists(AppPaths.ReportScript)) return (false, "Không tìm thấy report.py.");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "\"" + AppPaths.ReportScript + "\"",
                    WorkingDirectory = AppPaths.Root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(120000);

                    if (proc.ExitCode == 0)
                        return (true, stdout);
                    return (false, stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>Xóa thư mục log của một tài khoản.</summary>
        public bool ClearOutput(string account)
        {
            if (string.IsNullOrWhiteSpace(account)) return false;
            try
            {
                string target = Path.GetFullPath(Path.Combine(AppPaths.OutputDir, account));
                string root = Path.GetFullPath(AppPaths.OutputDir);
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
                if (Directory.Exists(target)) Directory.Delete(target, true);
                return true;
            }
            catch { return false; }
        }
    }
}
