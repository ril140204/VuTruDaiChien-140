using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace QATestBotDesktop
{
    public partial class MainForm
    {
        // ======================= ACCOUNTS =======================

        private void LoadAccountsToGrid()
        {
            var accounts = _svc.LoadAccounts();
            _gridAccounts.LoadAccounts(accounts);
            RenderAccountList();
        }

        private void SaveAccounts()
        {
            var accounts = _gridAccounts.CollectAccounts();
            if (!_svc.SaveAccounts(accounts))
            {
                MessageBox.Show("Không lưu được accounts.json.\nKiểm tra quyền ghi file.",
                    "QA TestBot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Toast("Đã lưu " + accounts.Count + " tài khoản.");
            RefreshStatus();
        }

        private void AddAccountRow()
        {
            _gridAccounts.Rows.Add(true, "", "", "", "", "");
            _gridAccounts.CurrentCell = _gridAccounts.Rows[_gridAccounts.Rows.Count - 1].Cells["Username"];
            _gridAccounts.Focus();
        }

        private void DeleteSelectedRow()
        {
            if (_gridAccounts.CurrentRow == null || _gridAccounts.CurrentRow.IsNewRow)
            {
                MessageBox.Show("Hãy chọn một tài khoản để xóa.", "QA TestBot",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string username = _gridAccounts.GetSelected()?.Username ?? "";
            var result = MessageBox.Show(
                "Xóa tài khoản \"" + username + "\" khỏi danh sách?\n" +
                "(Thư mục log của tài khoản vẫn được giữ lại.)",
                "Xác nhận xóa", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            _gridAccounts.Rows.Remove(_gridAccounts.CurrentRow);
            SaveAccounts();
        }

        private void OnAccountSelected()
        {
            if (_lstAccounts.SelectedItem is not AccountListItem item) return;
            _gridAccounts.SelectByUsername(item.Username);
        }

        private void RenderAccountList()
        {
            if (_lstAccounts == null || _gridAccounts == null) return;

            try { RenderAccountListCore(); }
            catch (Exception ex) { CrashLog.Write("RenderAccountList", ex); }
        }

        private void RenderAccountListCore()
        {
            string selected = (_lstAccounts.SelectedItem as AccountListItem)?.Username;
            string search = (_txtSearch?.Text ?? "").Trim().ToLowerInvariant();
            int filter = _cboFilter?.SelectedIndex ?? 0;

            List<AccountStatus> statuses;
            try { statuses = _svc.GetAccountStatuses(); } catch { statuses = new List<AccountStatus>(); }
            var statusMap = statuses.ToDictionary(s => s.Username, s => s, StringComparer.OrdinalIgnoreCase);

            _lstAccounts.BeginUpdate();
            try
            {
                _lstAccounts.Items.Clear();

                foreach (DataGridViewRow row in _gridAccounts.Rows)
                {
                    if (row.IsNewRow) continue;

                    string username = (row.Cells["Username"].Value ?? "").ToString().Trim();
                    if (username.Length == 0) continue;

                    bool enabled = true;
                    object ev = row.Cells["Enabled"].Value;
                    if (ev is bool bb) enabled = bb;
                    else bool.TryParse(ev?.ToString(), out enabled);
                    AccountStatus st;
                    if (!statusMap.TryGetValue(username, out st))
                    {
                        st = new AccountStatus { Username = username, Exists = false };
                    }

                    string statusText;
                    if (!enabled) statusText = "Tắt";
                    else if (!st.Exists) statusText = "Chưa chạy";
                    else if (st.Success) statusText = "Vào map";
                    else statusText = "Chưa vào map";

                    if (search.Length > 0 && !username.ToLowerInvariant().Contains(search)) continue;
                    if (filter == 1 && statusText != "Vào map") continue;
                    if (filter == 2 && statusText != "Chưa vào map") continue;
                    if (filter == 3 && statusText != "Chưa chạy") continue;

                    _lstAccounts.Items.Add(new AccountListItem
                    {
                        Username = username,
                        Success = st.Success,
                        Exists = st.Exists,
                        Duration = st.Duration,
                        StatusText = statusText
                    });
                }
            }
            finally { _lstAccounts.EndUpdate(); }

            // ListBox o che do OwnerDrawFixed khong tu ve lai khi Items thay doi
            // -> phai invalidate ro rang.
            _lstAccounts.Invalidate();

            if (_lstAccounts.Items.Count == 0 && _gridAccounts.Rows.Count > 0)
            {
                CrashLog.Write("RenderAccountList.Empty",
                    new Exception("Grid co " + _gridAccounts.Rows.Count + " dong nhung ListBox rong."));
            }

            // Khôi phục selection
            if (selected != null)
            {
                for (int i = 0; i < _lstAccounts.Items.Count; i++)
                {
                    if (_lstAccounts.Items[i] is AccountListItem it &&
                        string.Equals(it.Username, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        _lstAccounts.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        // ======================= CONFIG =======================

        private void LoadConfigToUi()
        {
            var cfg = _svc.LoadConfig();
            _chkEnvConfirmed.Checked = cfg.TestEnvConfirmed;
            _txtServerName.Text = cfg.ServerName ?? "";
            _txtAllowed.Text = string.Join(Environment.NewLine, cfg.AllowedServerNames ?? new List<string>());
            _numMaxConcurrent.Value = Clamp(_numMaxConcurrent, cfg.MaxConcurrent);
            _numStagger.Value = Clamp(_numStagger, (decimal)cfg.StaggerSeconds);
            _numTimeout.Value = Clamp(_numTimeout, (decimal)cfg.StepTimeout);
            _numSnapshot.Value = Clamp(_numSnapshot, (decimal)cfg.SnapshotInterval);
            _chkSuppressNre.Checked = cfg.SuppressUnityNullReference;
            _txtSuppressed.Text = string.Join(Environment.NewLine, cfg.SuppressedLogPatterns ?? new List<string>());
        }

        private static decimal Clamp(NumericUpDown num, decimal val)
        {
            if (val < num.Minimum) val = num.Minimum;
            if (val > num.Maximum) val = num.Maximum;
            return val;
        }

        private void SaveConfig()
        {
            var cfg = _svc.LoadConfig();
            cfg.TestEnvConfirmed = _chkEnvConfirmed.Checked;
            cfg.ServerName = _txtServerName.Text.Trim();
            cfg.AllowedServerNames = SplitLines(_txtAllowed.Text);
            cfg.MaxConcurrent = (int)_numMaxConcurrent.Value;
            cfg.StaggerSeconds = (double)_numStagger.Value;
            cfg.StepTimeout = (double)_numTimeout.Value;
            cfg.SnapshotInterval = (double)_numSnapshot.Value;
            cfg.SuppressUnityNullReference = _chkSuppressNre.Checked;
            cfg.SuppressedLogPatterns = SplitLines(_txtSuppressed.Text);

            if (!_svc.SaveConfig(cfg))
            {
                MessageBox.Show("Không lưu được bot_config.json.", "QA TestBot",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Cảnh báo an toàn nếu server không nằm trong whitelist.
            if (cfg.ServerName.Length > 0 && !cfg.AllowedServerNames.Contains(cfg.ServerName))
            {
                MessageBox.Show(
                    "Server đích \"" + cfg.ServerName + "\" không nằm trong whitelist.\n" +
                    "Launcher sẽ từ chối chạy cho tới khi bạn thêm server này vào whitelist.",
                    "Cảnh báo whitelist", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                Toast("Đã lưu cấu hình.");
            }
        }

        private static List<string> SplitLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(x => x.Trim())
                       .Where(x => x.Length > 0)
                       .ToList();
        }

        // ======================= LAUNCHER =======================

        private void DoStart()
        {
            if (_svc.IsLauncherRunning())
            {
                Toast("Launcher đang chạy rồi.");
                return;
            }

            // Lưu tài khoản + cấu hình trước khi chạy để không mất thay đổi.
            _svc.SaveAccounts(_gridAccounts.CollectAccounts());
            SaveConfig();

            var (ok, msg) = _svc.StartLauncher();
            Toast(msg, ok);
            RefreshStatus();
        }

        private void DoStop()
        {
            var (ok, msg) = _svc.StopAll();
            Toast(msg, ok);
            RefreshStatus();

            // Neu launcher khong phan hoi trong 3 giay -> tu dong escalate len force kill
            // de nguoi dung khong phai bam them lan nua.
            var escalate = new Timer { Interval = 3000 };
            escalate.Tick += (s, e) =>
            {
                escalate.Stop();
                escalate.Dispose();
                if (IsDisposed) return;

                if (_svc.IsLauncherRunning())
                {
                    var (kok, kmsg) = _svc.KillAll();
                    if (kok) Toast("Launcher khong phan hoi - da ep dung toan bo client. " + kmsg, true);
                }
            };
            escalate.Start();
        }

        private void DoKill()
        {
            var result = MessageBox.Show(
                "Ép dừng launcher và TẤT CẢ client game đang chạy?\n" +
                "Tiến trình sẽ bị kết thúc ngay lập tức.",
                "Xác nhận ép dừng", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;

            var (ok, msg) = _svc.KillAll();
            Toast(msg, ok);
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (IsDisposed || _lblStatus == null) return;

            try
            {
                bool running = _svc.IsLauncherRunning();
                var statuses = _svc.GetAccountStatuses();
                int total = _gridAccounts.Rows.Count;
                int success = statuses.Count(s => s.Success);
                int failed = statuses.Count(s => s.Exists && !s.Success);

                _lblStatus.Text = string.Format(
                    "{0} tài khoản   •   {1} vào map   •   {2} thất bại   •   Launcher: {3}",
                    total, success, failed, running ? "ĐANG CHẠY" : "đã dừng");

                _lblStatus.ForeColor = running ? Color.FromArgb(196, 43, 28) : Color.FromArgb(90, 98, 108);

                _btnStart.Enabled = !running;
                _btnStop.Enabled = running || File.Exists(AppPaths.StopFlag);

                RenderAccountList();
                PopulateLogAccounts();
            }
            catch (Exception ex)
            {
                CrashLog.Write("RefreshStatus", ex);
            }
        }

        private void PopulateLogAccounts()
        {
            if (_cboLogAccount == null) return;

            string current = _cboLogAccount.SelectedItem as string;
            _cboLogAccount.BeginUpdate();
            try
            {
                _cboLogAccount.Items.Clear();
                foreach (DataGridViewRow row in _gridAccounts.Rows)
                {
                    if (row.IsNewRow) continue;
                    string u = (row.Cells["Username"].Value ?? "").ToString().Trim();
                    if (u.Length > 0) _cboLogAccount.Items.Add(u);
                }
                if (current != null && _cboLogAccount.Items.Contains(current))
                    _cboLogAccount.SelectedItem = current;
                else if (_cboLogAccount.Items.Count > 0)
                    _cboLogAccount.SelectedIndex = 0;
            }
            finally { _cboLogAccount.EndUpdate(); }
        }

        // ======================= LOG =======================

        private void LoadLog()
        {
            if (_lvLog == null) return;
            string account = _cboLogAccount?.SelectedItem as string;
            if (string.IsNullOrEmpty(account)) { _lvLog.Items.Clear(); return; }

            var events = _svc.GetEvents(account, 500);
            _lvLog.BeginUpdate();
            try
            {
                _lvLog.Items.Clear();
                foreach (var ev in events)
                {
                    var item = new ListViewItem(ev.Timestamp.ToString("HH:mm:ss.fff"));
                    item.SubItems.Add(ev.State);
                    item.SubItems.Add(ev.EventType);
                    item.SubItems.Add(ev.Message);
                    _lvLog.Items.Add(item);
                }
                if (_lvLog.Items.Count > 0)
                    _lvLog.EnsureVisible(_lvLog.Items.Count - 1);
            }
            finally { _lvLog.EndUpdate(); }
        }

        private void ClearLog()
        {
            string account = _cboLogAccount?.SelectedItem as string;
            if (string.IsNullOrEmpty(account)) return;

            var result = MessageBox.Show(
                "Xóa toàn bộ log của tài khoản \"" + account + "\"?\n" +
                "Thư mục output\\" + account + " sẽ bị xóa.",
                "Xác nhận xóa log", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;

            if (_svc.ClearOutput(account))
            {
                Toast("Đã xóa log của " + account + ".");
                LoadLog();
                RefreshStatus();
            }
            else
            {
                Toast("Không xóa được log (file đang bị khóa?).", false);
            }
        }

        // ======================= REPORT =======================

        private void LoadReport()
        {
            if (_dgvReport == null) return;

            var statuses = _svc.GetAccountStatuses();
            _dgvReport.Rows.Clear();

            foreach (var st in statuses)
            {
                int idx = _dgvReport.Rows.Add(
                    st.Username,
                    st.Success ? "SUCCESS" : (st.Exists ? "FAILED" : "—"),
                    st.Duration.HasValue ? st.Duration.Value.ToString("0.00") + "s" : "—",
                    st.EventsCount.ToString(),
                    st.SnapshotsCount.ToString(),
                    st.LastState.Length > 0 ? st.LastState : "—");

                if (st.Success)
                {
                    _dgvReport.Rows[idx].DefaultCellStyle.ForeColor = _ok;
                }
                else if (st.Exists)
                {
                    _dgvReport.Rows[idx].DefaultCellStyle.ForeColor = _danger;
                }
                else
                {
                    _dgvReport.Rows[idx].DefaultCellStyle.ForeColor = Color.FromArgb(140, 146, 154);
                }
            }

            int success = statuses.Count(s => s.Success);
            int total = statuses.Count;
            double rate = total > 0 ? success * 100.0 / total : 0;
            _lblReportSummary.Text = string.Format(
                "Tổng: {0}   •   Vào map: {1}   •   Tỉ lệ: {2:0.0}%",
                total, success, rate);
        }

        private void GenerateReport()
        {
            _btnGenReport.Enabled = false;
            _btnGenReport.Text = "Đang xuất...";
            try
            {
                var (ok, output) = _svc.GenerateReport();
                if (ok)
                {
                    Toast("Đã xuất summary.md và summary.csv vào output\\.");
                    LoadReport();
                }
                else
                {
                    MessageBox.Show("Không xuất được báo cáo:\n\n" + output,
                        "QA TestBot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _btnGenReport.Enabled = true;
                _btnGenReport.Text = "Xuất báo cáo (summary.md / .csv)";
            }
        }

        // ======================= TOAST =======================

        private ToolTip _toast;
        private NotifyIcon _tray;

        private void Toast(string message, bool ok = true)
        {
            if (_tray == null)
            {
                _tray = new NotifyIcon
                {
                    Icon = SystemIcons.Application,
                    Visible = false,
                    Text = "QA TestBot"
                };
            }

            _tray.BalloonTipTitle = ok ? "QA TestBot" : "QA TestBot — Lỗi";
            _tray.BalloonTipText = message;
            _tray.BalloonTipIcon = ok ? ToolTipIcon.Info : ToolTipIcon.Error;
            _tray.ShowBalloonTip(4000);

            // Cũng hiển thị trong status bar để không bỏ lỡ khi không có tray.
            if (_lblPath != null && !IsDisposed)
            {
                string original = "Thư mục gốc: " + AppPaths.Root;
                _lblPath.Text = message;
                var timer = new Timer { Interval = 4000 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    if (!IsDisposed && _lblPath != null) _lblPath.Text = original;
                };
                timer.Start();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (_refreshTimer != null) { _refreshTimer.Stop(); _refreshTimer.Dispose(); }
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                if (_toast != null) { _toast.Dispose(); _toast = null; }
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}
