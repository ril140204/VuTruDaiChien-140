using System;
using System.Text;

namespace QLTK.Client140
{
    /// <summary>
    /// Đọc tham số dòng lệnh mà QLTK.exe truyền cho game khi mở tài khoản.
    ///
    /// Format THẬT (đối chiếu runtime IL của QLTK.MainForm.LaunchSingleGame):
    ///
    ///   -autologin &lt;base64(user)&gt; &lt;base64(pass)&gt; &lt;charIndex&gt; &lt;w&gt; &lt;h&gt;
    ///              &lt;nhat(0|1)&gt; &lt;server&gt; &lt;stt&gt;
    ///              [-proxy &lt;host&gt; &lt;port&gt; &lt;user&gt; &lt;pass&gt;]
    ///
    /// ĐIỂM QUAN TRỌNG: QLTK nối chuỗi arguments KHÔNG có dấu nháy, nên server
    /// có dấu cách ("Vũ Trụ 1") sẽ bị tách thành nhiều token. Vì vậy KHÔNG được
    /// đọc server theo một index cố định.
    ///
    /// Cách đọc chắc chắn:
    ///   - 6 token đầu là cố định: user, pass, charIndex, w, h, nhat
    ///   - token CUỐI của phần autologin là stt (số nguyên)
    ///   - phần nằm giữa (có thể nhiều token) chính là tên server
    /// </summary>
    internal sealed class GameArgs
    {
        public string Username = "";
        public string Password = "";
        public string Server = "";
        public int Width = 800;
        public int Height = 600;
        public int CharIndex = 1;
        public int Stt = -1;
        public bool Nhat;

        public string ProxyHost = "";
        public int ProxyPort;
        public string ProxyUser = "";
        public string ProxyPass = "";

        public bool HasAutoLogin { get; private set; }

        public static GameArgs Parse()
        {
            var g = new GameArgs();

            try
            {
                string raw = GetRawArguments();
                if (string.IsNullOrEmpty(raw)) return g;

                int idx = raw.IndexOf("-autologin", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return g;

                string rest = raw.Substring(idx + "-autologin".Length).Trim();

                // Tách phần proxy ra trước (nếu có)
                string proxyPart = "";
                int p = rest.IndexOf("-proxy", StringComparison.OrdinalIgnoreCase);
                if (p >= 0)
                {
                    proxyPart = rest.Substring(p + "-proxy".Length).Trim();
                    rest = rest.Substring(0, p).Trim();
                }

                string[] tok = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length < 8) return g;

                g.HasAutoLogin = true;

                g.Username = DecodeB64(tok[0]);
                g.Password = DecodeB64(tok[1]);
                int.TryParse(tok[2], out g.CharIndex);
                int.TryParse(tok[3], out g.Width);
                int.TryParse(tok[4], out g.Height);
                g.Nhat = tok[5] == "1";

                // Token cuối = STT; phần giữa = tên server (có thể nhiều token)
                int sttIndex = tok.Length - 1;
                if (!int.TryParse(tok[sttIndex], out g.Stt))
                {
                    // Không có STT (hiếm) -> coi như phần còn lại đều là server
                    sttIndex = tok.Length;
                    g.Stt = -1;
                }

                int serverStart = 6;
                int serverCount = sttIndex - serverStart;
                if (serverCount > 0)
                {
                    var sb = new StringBuilder();
                    for (int i = serverStart; i < sttIndex; i++)
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(tok[i]);
                    }
                    g.Server = sb.ToString();
                }

                // Proxy
                string[] pt = proxyPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (pt.Length > 0) g.ProxyHost = pt[0];
                if (pt.Length > 1) int.TryParse(pt[1], out g.ProxyPort);
                if (pt.Length > 2) g.ProxyUser = pt[2];
                if (pt.Length > 3) g.ProxyPass = string.Join(" ", pt, 3, pt.Length - 3);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[QLTK140] Parse args loi: " + ex.Message);
            }

            return g;
        }

        private static string DecodeB64(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch { return s; }
        }

        private static string GetRawArguments()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                if (args != null && args.Length > 1)
                {
                    string joined = string.Join(" ", args, 1, args.Length - 1);
                    if (joined.IndexOf("-autologin", StringComparison.OrdinalIgnoreCase) >= 0)
                        return joined;
                }
            }
            catch { }

            try
            {
                var psi = System.Diagnostics.Process.GetCurrentProcess().StartInfo;
                if (!string.IsNullOrEmpty(psi.Arguments)) return psi.Arguments;
            }
            catch { }

            return "";
        }
    }
}
