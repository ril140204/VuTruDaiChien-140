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
    public partial class MainForm : Form
    {
        private readonly TestBotService _svc = new TestBotService();
        private Timer _refreshTimer;

        // --- controls: top bar ---
        private Button _btnStart;
        private Button _btnStop;
        private Button _btnKill;
        private Button _btnRefresh;
        private Button _btnAdd;
        private Button _btnSave;
        private Button _btnDelete;
        private Label _lblStatus;
        private Label _lblTitle;

        // --- controls: left panel ---
        private TextBox _txtSearch;
        private ComboBox _cboFilter;
        private Panel _pnlAccountList;
        private ListBox _lstAccounts;

        // --- controls: tabs ---
        private TabControl _tabs;

        // --- tab: tài khoản ---
        private AccountGrid _gridAccounts;

        // --- tab: cấu hình ---
        private TextBox _txtServerName;
        private TextBox _txtAllowed;
        private NumericUpDown _numMaxConcurrent;
        private NumericUpDown _numStagger;
        private NumericUpDown _numTimeout;
        private NumericUpDown _numSnapshot;
        private CheckBox _chkSuppressNre;
        private TextBox _txtSuppressed;
        private CheckBox _chkEnvConfirmed;
        private Button _btnSaveConfig;

        // --- tab: nhật ký ---
        private ComboBox _cboLogAccount;
        private ListView _lvLog;
        private Button _btnClearLog;

        // --- tab: báo cáo ---
        private DataGridView _dgvReport;
        private Label _lblReportSummary;
        private Button _btnGenReport;

        private readonly Color _accent = Color.FromArgb(0, 120, 212);
        private readonly Color _danger = Color.FromArgb(196, 43, 28);
        private readonly Color _ok = Color.FromArgb(16, 124, 65);

        public MainForm()
        {
            Text = "QA TestBot - Quản Lý Tài Khoản - Vũ Trụ Đại Chiến";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1024, 640);
            Size = new Size(1440, 860);
            BackColor = Color.FromArgb(240, 242, 245);
            Font = new Font("Segoe UI", 9F);

            try
            {
                BuildUi();

                LoadAccountsToGrid();
                LoadConfigToUi();
                RefreshStatus();
                LoadReport();
            }
            catch (Exception ex)
            {
                CrashLog.Write("MainForm.Init", ex);
                MessageBox.Show("Lỗi khi khởi tạo giao diện:\n\n" + ex.Message +
                    "\n\nChi tiết đã ghi vào:\n" + CrashLog.LastPath,
                    "QA TestBot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _refreshTimer = new Timer { Interval = 2500 };
            _refreshTimer.Tick += (s, e) => RefreshStatus();
            _refreshTimer.Start();

            _svc.LauncherOutput += line =>
            {
                if (IsDisposed) return;
                BeginInvoke(new Action(() => { }));
            };
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(root);

            root.Controls.Add(BuildTopBar(), 0, 0);
            root.Controls.Add(BuildBody(), 0, 1);
            root.Controls.Add(BuildStatusBar(), 0, 2);
        }

        // ======================= TOP BAR =======================
        private Control BuildTopBar()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            panel.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(Color.FromArgb(225, 229, 234)), 0, panel.Height - 1, panel.Width, panel.Height - 1);

            // FlowLayoutPanel de cac nut tu dung chieu rong, khong bi chen ep.
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(14, 10, 14, 0),
                BackColor = Color.White
            };

            _lblTitle = new Label
            {
                Text = "QA TestBot",
                Font = new Font("Segoe UI Semibold", 13F),
                ForeColor = Color.FromArgb(28, 34, 41),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(124, 32),
                Margin = new Padding(0, 0, 10, 0)
            };
            bar.Controls.Add(_lblTitle);

            _btnStart = MakeButton("Bắt đầu", _accent, Color.White);
            _btnStart.Click += (s, e) => DoStart();
            _btnStart.Size = new Size(104, 32);
            bar.Controls.Add(_btnStart);

            _btnStop = MakeButton("Dừng", Color.FromArgb(232, 76, 61), Color.White);
            _btnStop.Click += (s, e) => DoStop();
            _btnStop.Size = new Size(88, 32);
            bar.Controls.Add(_btnStop);

            _btnKill = MakeButton("Ép dừng", Color.White, Color.FromArgb(196, 43, 28));
            _btnKill.FlatAppearance.BorderColor = Color.FromArgb(196, 43, 28);
            _btnKill.Click += (s, e) => DoKill();
            _btnKill.Size = new Size(104, 32);
            bar.Controls.Add(_btnKill);

            bar.Controls.Add(new Panel { Width = 14, Height = 1, Margin = new Padding(0) });

            _btnAdd = MakeButton("Thêm", Color.White, Color.FromArgb(70, 76, 85));
            _btnAdd.FlatAppearance.BorderColor = Color.FromArgb(210, 214, 220);
            _btnAdd.Click += (s, e) => AddAccountRow();
            _btnAdd.Size = new Size(84, 32);
            bar.Controls.Add(_btnAdd);

            _btnSave = MakeButton("Lưu", Color.White, Color.FromArgb(70, 76, 85));
            _btnSave.FlatAppearance.BorderColor = Color.FromArgb(210, 214, 220);
            _btnSave.Click += (s, e) => SaveAccounts();
            _btnSave.Size = new Size(84, 32);
            bar.Controls.Add(_btnSave);

            _btnDelete = MakeButton("Xóa", Color.White, Color.FromArgb(70, 76, 85));
            _btnDelete.FlatAppearance.BorderColor = Color.FromArgb(210, 214, 220);
            _btnDelete.Click += (s, e) => DeleteSelectedRow();
            _btnDelete.Size = new Size(84, 32);
            bar.Controls.Add(_btnDelete);

            _btnRefresh = MakeButton("↻", Color.White, Color.FromArgb(70, 76, 85));
            _btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(210, 214, 220);
            _btnRefresh.Font = new Font("Segoe UI", 13F);
            _btnRefresh.Click += (s, e) => { RefreshStatus(); LoadReport(); };
            _btnRefresh.Size = new Size(40, 32);
            bar.Controls.Add(_btnRefresh);

            panel.Controls.Add(bar);
            return panel;
        }

        // ======================= BODY =======================
        private Control BuildBody()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(225, 229, 234),
                FixedPanel = FixedPanel.Panel1
            };

            split.Panel1.Controls.Add(BuildLeftPanel());
            split.Panel2.Controls.Add(BuildTabs());

            // SplitContainer validate MinSize/SplitterDistance ngay khi gan.
            // -> Gan sau khi form da co kich thuoc that, va luon kiem tra gioi han.
            Shown += (s, e) =>
            {
                try
                {
                    int total = split.Width - split.SplitterWidth;
                    if (total <= 0) return;

                    int min1 = Math.Min(220, Math.Max(0, total / 3));
                    int min2 = Math.Min(420, Math.Max(0, total - min1));

                    split.Panel1MinSize = min1;
                    split.Panel2MinSize = min2;

                    int desired = 330;
                    if (desired < min1) desired = min1;
                    if (desired > total - min2) desired = total - min2;
                    if (desired > 0) split.SplitterDistance = desired;
                }
                catch (Exception ex)
                {
                    CrashLog.Write("SplitContainer.Init", ex);
                }
            };

            return split;
        }

        private Control BuildLeftPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.White,
                Padding = new Padding(12, 12, 12, 12)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // tieu de
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // filter
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // danh sach
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // thong ke

            var lbl = new Label
            {
                Text = "DANH SÁCH TÀI KHOẢN",
                Font = new Font("Segoe UI Semibold", 9F),
                ForeColor = Color.FromArgb(120, 128, 138),
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(lbl, 0, 0);

            var filterRow = new Panel { Dock = DockStyle.Fill };

            _txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = "Tìm tài khoản...",
                Font = new Font("Segoe UI", 9.5F),
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtSearch.TextChanged += (s, e) => RenderAccountList();
            filterRow.Controls.Add(_txtSearch);

            _cboFilter = new ComboBox
            {
                Dock = DockStyle.Right,
                Width = 122,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F),
                Margin = new Padding(6, 0, 0, 0)
            };
            _cboFilter.Items.AddRange(new object[]
            {
                "Tất cả trạng thái", "Vào map", "Chưa vào map", "Chưa chạy"
            });
            _cboFilter.SelectedIndex = 0;
            _cboFilter.SelectedIndexChanged += (s, e) => RenderAccountList();
            filterRow.Controls.Add(_cboFilter);

            panel.Controls.Add(filterRow, 0, 1);

            // Stats strip - phai them TRUOC panel Dock=Fill, vi WinForms xu ly
            // Dock theo thu tu them: control them sau se bi panel Fill che mat.
            var stats = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 249, 251) };
            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(90, 98, 108)
            };
            stats.Controls.Add(_lblStatus);
            panel.Controls.Add(stats, 0, 3);

            _pnlAccountList = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
            _lstAccounts = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5F),
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 44
            };
            _lstAccounts.DrawItem += DrawAccountItem;
            _lstAccounts.SelectedIndexChanged += (s, e) => OnAccountSelected();
            _pnlAccountList.Controls.Add(_lstAccounts);
            panel.Controls.Add(_pnlAccountList, 0, 2);

            return panel;
        }

        private void DrawAccountItem(object sender, DrawItemEventArgs e)
        {
            try { DrawAccountItemCore(sender, e); }
            catch (Exception ex) { CrashLog.Write("DrawAccountItem", ex); }
        }

        private void DrawAccountItemCore(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lstAccounts.Items.Count) return;

            var item = _lstAccounts.Items[e.Index] as AccountListItem;
            if (item == null) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var g = e.Graphics;

            using (var bg = new SolidBrush(selected ? Color.FromArgb(222, 238, 252) : Color.White))
                g.FillRectangle(bg, e.Bounds);

            if (selected)
                using (var accent = new SolidBrush(_accent))
                    g.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);

            using (var nameFont = new Font("Segoe UI Semibold", 9.5F))
            using (var smallFont = new Font("Segoe UI", 8F))
            using (var nameBrush = new SolidBrush(Color.FromArgb(20, 26, 33)))
            using (var dimBrush = new SolidBrush(Color.FromArgb(130, 138, 148)))
            {
                g.DrawString(item.Username, nameFont, nameBrush, e.Bounds.Left + 10, e.Bounds.Top + 5);

                string detail = item.StatusText;
                if (item.Duration.HasValue)
                    detail += "  •  " + item.Duration.Value.ToString("0.00") + "s";

                g.DrawString(detail, smallFont, dimBrush, e.Bounds.Left + 10, e.Bounds.Top + 23);

                // Status badge on the right
                Color badge = item.Success ? _ok
                    : (item.Exists ? Color.FromArgb(190, 120, 20) : Color.FromArgb(150, 156, 164));
                var size = g.MeasureString(item.StatusText, smallFont);
                var badgeRect = new Rectangle(
                    e.Bounds.Right - (int)size.Width - 18,
                    e.Bounds.Top + 22, (int)size.Width + 10, 16);

                using (var fill = new SolidBrush(Color.FromArgb(28, badge)))
                    g.FillRectangle(fill, badgeRect);

                using (var text = new SolidBrush(badge))
                    g.DrawString(item.StatusText, smallFont, text, badgeRect.Left + 5, e.Bounds.Top + 22);
            }

            using (var pen = new Pen(Color.FromArgb(240, 242, 245)))
                g.DrawLine(pen, e.Bounds.Left + 10, e.Bounds.Bottom - 1, e.Bounds.Right - 8, e.Bounds.Bottom - 1);
        }

        // ======================= TABS =======================
        private Control BuildTabs()
        {
            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(16, 6),
                Font = new Font("Segoe UI", 9.5F)
            };

            _tabs.TabPages.Add(BuildTab("Tài khoản", BuildAccountsTab()));
            _tabs.TabPages.Add(BuildTab("Cấu hình", BuildConfigTab()));
            _tabs.TabPages.Add(BuildTab("Nhật ký", BuildLogTab()));
            _tabs.TabPages.Add(BuildTab("Báo cáo", BuildReportTab()));

            _tabs.SelectedIndexChanged += (s, e) =>
            {
                if (_tabs.SelectedTab?.Tag?.ToString() == "log") LoadLog();
                if (_tabs.SelectedTab?.Tag?.ToString() == "report") LoadReport();
            };

            return _tabs;
        }

        private TabPage BuildTab(string title, Control content)
        {
            var page = new TabPage(title)
            {
                BackColor = Color.White,
                Padding = new Padding(14),
                Tag = title.ToLower() == "nhật ký" ? "log" : (title.ToLower() == "báo cáo" ? "report" : "")
            };
            page.Controls.Add(content);
            return page;
        }

        // ======================= ACCOUNTS TAB =======================
        private Control BuildAccountsTab()
        {
            var panel = new Panel { Dock = DockStyle.Fill };

            _gridAccounts = new AccountGrid { Dock = DockStyle.Fill };
            _gridAccounts.SelectionChanged += (s, e) =>
            {
                var acc = _gridAccounts.GetSelected();
                if (acc != null)
                {
                    int idx = _lstAccounts.Items.IndexOf(
                        _lstAccounts.Items.Cast<AccountListItem>()
                            .FirstOrDefault(x => string.Equals(x.Username, acc.Username, StringComparison.OrdinalIgnoreCase)));
                    if (idx >= 0) _lstAccounts.SelectedIndex = idx;
                }
            };

            var scroll = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 4) };
            scroll.Controls.Add(_gridAccounts);
            panel.Controls.Add(scroll);

            return panel;
        }

        // ======================= CONFIG TAB =======================
        private Control BuildConfigTab()
        {
            var outer = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoScroll = true,
                Padding = new Padding(4, 4, 24, 4)
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            _chkEnvConfirmed = new CheckBox
            {
                Text = "Xác nhận môi trường kiểm thử (test_env_confirmed)",
                AutoSize = true,
                Checked = true,
                ForeColor = Color.FromArgb(35, 40, 47),
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(0, 9, 0, 0)
            };
            inner.Controls.Add(_chkEnvConfirmed, 0, row++);
            inner.Controls.Add(new Label(), 1, row - 1);

            AddLabel(inner, ref row, "Server đích");
            _txtServerName = AddTextBox(inner, ref row, "Vũ Trụ 1");

            AddLabel(inner, ref row, "Whitelist server (mỗi dòng 1 tên)");
            _txtAllowed = AddTextBox(inner, ref row, "", 92);

            AddLabel(inner, ref row, "Số client đồng thời tối đa");
            _numMaxConcurrent = AddNumber(inner, ref row, 1, 1, 32);

            AddLabel(inner, ref row, "Giãn cách đăng nhập (giây)");
            _numStagger = AddNumber(inner, ref row, 6, 0, 600);

            AddLabel(inner, ref row, "Timeout mỗi bước (giây)");
            _numTimeout = AddNumber(inner, ref row, 45, 5, 600);

            AddLabel(inner, ref row, "Chu kỳ snapshot (giây)");
            _numSnapshot = AddNumber(inner, ref row, 5, 1, 300);

            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));

            AddLabel(inner, ref row, "Chuỗi log cần ẩn (mỗi dòng 1 chuỗi)");
            _txtSuppressed = AddTextBox(inner, ref row, "", 92);

            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            _chkSuppressNre = new CheckBox
            {
                Text = "Ẩn lỗi NullReferenceException từ Unity",
                AutoSize = true,
                ForeColor = Color.FromArgb(35, 40, 47),
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(0, 6, 0, 0)
            };
            inner.Controls.Add(_chkSuppressNre, 0, row);
            inner.Controls.Add(new Label
            {
                Text = "Chỉ áp dụng cho log nguồn \"Unity\". Log của BepInEx và plugin QA TestBot luôn được giữ nguyên.",
                AutoSize = true,
                ForeColor = Color.FromArgb(130, 138, 148),
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 10, 0, 0)
            }, 1, row);
            row++;

            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            _btnSaveConfig = MakeButton("Lưu cấu hình", _accent, Color.White);
            _btnSaveConfig.Click += (s, e) => SaveConfig();
            _btnSaveConfig.Anchor = AnchorStyles.Left;
            inner.Controls.Add(_btnSaveConfig, 0, row);
            inner.Controls.Add(new Label(), 1, row);

            outer.Controls.Add(inner);
            return outer;
        }

        private void AddLabel(TableLayoutPanel t, ref int row, string text)
        {
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            t.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.FromArgb(70, 76, 85),
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(0, 7, 0, 0)
            }, 0, row);
            t.Controls.Add(new Label(), 1, row);
            row++;
        }

        private TextBox AddTextBox(TableLayoutPanel t, ref int row, string value, int height = 26)
        {
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, height + 4));
            var tb = new TextBox
            {
                Text = value,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Width = 460,
                Font = new Font("Segoe UI", 9.5F),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 4, 0, 0)
            };
            tb.Multiline = height > 40;
            tb.ScrollBars = height > 40 ? ScrollBars.Vertical : ScrollBars.None;
            t.Controls.Add(tb, 1, row);
            row++;
            return tb;
        }

        private NumericUpDown AddNumber(TableLayoutPanel t, ref int row, decimal val, decimal min, decimal max)
        {
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var num = new NumericUpDown
            {
                Value = val,
                Minimum = min,
                Maximum = max,
                DecimalPlaces = 0,
                Width = 110,
                Anchor = AnchorStyles.Left,
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(0, 4, 0, 0)
            };
            t.Controls.Add(num, 1, row);
            row++;
            return num;
        }

        // ======================= LOG TAB =======================
        private Control BuildLogTab()
        {
            var panel = new Panel { Dock = DockStyle.Fill };

            var top = new Panel { Dock = DockStyle.Top, Height = 44 };

            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 7, 0, 0),
                BackColor = Color.White
            };

            var lbl = new Label
            {
                Text = "Tài khoản:",
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(70, 76, 85),
                Margin = new Padding(0, 6, 8, 0)
            };
            topBar.Controls.Add(lbl);

            _cboLogAccount = new ComboBox
            {
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(0, 3, 0, 0)
            };
            _cboLogAccount.SelectedIndexChanged += (s, e) => LoadLog();
            topBar.Controls.Add(_cboLogAccount);

            topBar.Controls.Add(new Panel { Width = 12, Height = 1, Margin = new Padding(0) });

            _btnClearLog = MakeButton("Xóa log tài khoản", Color.White, Color.FromArgb(196, 43, 28));
            _btnClearLog.FlatAppearance.BorderColor = Color.FromArgb(230, 180, 175);
            _btnClearLog.Size = new Size(158, 30);
            _btnClearLog.Margin = new Padding(0, 2, 0, 0);
            _btnClearLog.Click += (s, e) => ClearLog();
            topBar.Controls.Add(_btnClearLog);

            top.Controls.Add(topBar);

            _lvLog = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(252, 252, 253)
            };
            _lvLog.Columns.Add("Thời gian", 165);
            _lvLog.Columns.Add("Trạng thái", 120);
            _lvLog.Columns.Add("Loại", 130);
            _lvLog.Columns.Add("Nội dung", 700);

            panel.Controls.Add(_lvLog);
            return panel;
        }

        // ======================= REPORT TAB =======================
        private Control BuildReportTab()
        {
            var panel = new Panel { Dock = DockStyle.Fill };

            var top = new Panel { Dock = DockStyle.Top, Height = 52 };

            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 10, 0, 0),
                BackColor = Color.White
            };

            _lblReportSummary = new Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.FromArgb(45, 52, 60),
                Margin = new Padding(0, 6, 16, 0)
            };
            topBar.Controls.Add(_lblReportSummary);

            topBar.Controls.Add(new Panel { Width = 12, Height = 1, Margin = new Padding(0) });

            _btnGenReport = MakeButton("Xuất báo cáo (summary.md / .csv)", Color.White, Color.FromArgb(70, 76, 85));
            _btnGenReport.FlatAppearance.BorderColor = Color.FromArgb(210, 214, 220);
            _btnGenReport.Size = new Size(246, 30);
            _btnGenReport.Margin = new Padding(0, 1, 0, 0);
            _btnGenReport.Click += (s, e) => GenerateReport();
            topBar.Controls.Add(_btnGenReport);

            top.Controls.Add(topBar);

            _dgvReport = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(232, 236, 240),
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight = 34,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            _dgvReport.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 248);
            _dgvReport.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 66, 75);
            _dgvReport.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _dgvReport.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 0, 0);
            _dgvReport.DefaultCellStyle.Font = new Font("Segoe UI", 9.5F);
            _dgvReport.DefaultCellStyle.Padding = new Padding(4, 0, 0, 0);
            _dgvReport.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 234, 250);
            _dgvReport.RowTemplate.Height = 30;

            _dgvReport.Columns.Add("Account", "Tài khoản");
            _dgvReport.Columns.Add("Status", "Trạng thái");
            _dgvReport.Columns.Add("Duration", "Thời gian vào");
            _dgvReport.Columns.Add("Events", "Sự kiện");
            _dgvReport.Columns.Add("Snapshots", "Snapshot");
            _dgvReport.Columns.Add("LastState", "Trạng thái cuối");

            panel.Controls.Add(_dgvReport);
            return panel;
        }

        // ======================= STATUS BAR =======================
        private Label _lblPath;

        private Control BuildStatusBar()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(228, 232, 237) };
            _lblPath = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(90, 98, 108)
            };
            _lblPath.Text = "Thư mục gốc: " + AppPaths.Root;
            panel.Controls.Add(_lblPath);
            return panel;
        }

        private Button MakeButton(string text, Color bg, Color fg)
        {
            var btn = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Font = new Font("Segoe UI", 9.5F),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.12f);
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.08f);
            return btn;
        }

        // ======================= MODEL BINDING =======================
        private class AccountListItem
        {
            public string Username;
            public bool Success;
            public bool Exists;
            public double? Duration;
            public string StatusText;
        }
    }
}
