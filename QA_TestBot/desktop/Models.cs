using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QATestBotDesktop
{
    /// <summary>
    /// Mot tai khoan test. Tuong thich voi QLTK (accounts.json) va
    /// voi dinh dang hien tai cua QA_TestBot/config/accounts.json.
    /// </summary>
    public class AccountEntry
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("server")]
        public string Server { get; set; } = "";

        [JsonPropertyName("note")]
        public string Note { get; set; } = "";

        /// <summary>
        /// Chon nhân vật (1-based). 0 = khong chọn (dùng nhân vật đầu tiên).
        /// </summary>
        [JsonPropertyName("char_index")]
        public int CharIndex { get; set; } = 0;

        /// <summary>
        /// Độ phân giải cửa sổ game. Rỗng = để game tự quyết định.
        /// </summary>
        [JsonPropertyName("resolution")]
        public string Resolution { get; set; } = "";

        /// <summary>
        /// Cờ bật/tắt tài khoản (tương đương cột "Nhớ" của QLTK).
        /// null = chưa có trong file -> mặc định bật (tương thích accounts.json cũ).
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool? EnabledRaw { get; set; }

        [JsonIgnore]
        public bool Enabled
        {
            get { return EnabledRaw ?? true; }
            set { EnabledRaw = value; }
        }

        public AccountEntry Clone()
        {
            return new AccountEntry
            {
                Username = Username,
                Password = Password,
                Server = Server,
                Note = Note,
                CharIndex = CharIndex,
                Resolution = Resolution,
                Enabled = Enabled
            };
        }
    }

    /// <summary>
    /// Cấu hình bot đọc/ghi từ QA_TestBot/config/bot_config.json.
    /// </summary>
    public class BotConfig
    {
        [JsonPropertyName("test_env_confirmed")]
        public bool TestEnvConfirmed { get; set; } = true;

        [JsonPropertyName("server_name")]
        public string ServerName { get; set; } = "Vũ Trụ 1";

        [JsonPropertyName("allowed_server_names")]
        public List<string> AllowedServerNames { get; set; } = new List<string>();

        [JsonPropertyName("max_concurrent")]
        public int MaxConcurrent { get; set; } = 1;

        [JsonPropertyName("stagger_seconds")]
        public double StaggerSeconds { get; set; } = 6.0;

        [JsonPropertyName("step_timeout")]
        public double StepTimeout { get; set; } = 45.0;

        [JsonPropertyName("snapshot_interval")]
        public double SnapshotInterval { get; set; } = 5.0;

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

        [JsonPropertyName("suppress_unity_null_reference")]
        public bool SuppressUnityNullReference { get; set; } = true;

        [JsonPropertyName("suppressed_log_patterns")]
        public List<string> SuppressedLogPatterns { get; set; } = new List<string>();
    }

    /// <summary>
    /// Đọc / ghi JSON với UTF-8 và format đẹp (giống launcher.py).
    /// </summary>
    public static class JsonStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static T Load<T>(string path, Func<T> fallback) where T : class
        {
            try
            {
                if (!File.Exists(path)) return fallback();
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return fallback();
                T result = JsonSerializer.Deserialize<T>(json, ReadOptions);
                return result ?? fallback();
            }
            catch
            {
                return fallback();
            }
        }

        /// <summary>Ghi atomically: ghi file .tmp rồi thay thế, tránh hỏng file khi crash giữa chừng.</summary>
        public static bool Save<T>(string path, T data)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, WriteOptions), new System.Text.UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[QA.Desktop] Save failed: " + ex.Message);
                return false;
            }
        }
    }
}
