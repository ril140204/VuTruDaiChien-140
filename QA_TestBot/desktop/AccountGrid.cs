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
    /// <summary>
    /// Bảng tài khoản dùng chung cho cả tab Tài khoản và tab Chi tiết.
    /// </summary>
    public class AccountGrid : DataGridView
    {
        public DataGridViewCheckBoxColumn EnabledCol;
        public DataGridViewTextBoxColumn UsernameCol;
        public DataGridViewTextBoxColumn PasswordCol;
        public DataGridViewTextBoxColumn ServerCol;
        public DataGridViewTextBoxColumn CharIndexCol;
        public DataGridViewTextBoxColumn NoteCol;

        public AccountGrid()
        {
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            BackgroundColor = Color.White;
            BorderStyle = BorderStyle.None;
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ColumnHeadersHeight = 34;
            EnableHeadersVisualStyles = false;
            GridColor = Color.FromArgb(232, 236, 240);
            RowHeadersVisible = false;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            MultiSelect = false;
            RowTemplate.Height = 30;
            EditMode = DataGridViewEditMode.EditOnEnter;
            StandardTab = true;

            ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 248);
            ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 66, 75);
            ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(244, 246, 248);
            ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 0, 0);

            DefaultCellStyle.BackColor = Color.White;
            DefaultCellStyle.ForeColor = Color.FromArgb(35, 40, 47);
            DefaultCellStyle.Font = new Font("Segoe UI", 9.5F);
            DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 234, 250);
            DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 26, 33);
            DefaultCellStyle.Padding = new Padding(4, 0, 0, 0);

            EnabledCol = new DataGridViewCheckBoxColumn
            {
                Name = "Enabled",
                HeaderText = "✓",
                Width = 34,
                FillWeight = 6,
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            UsernameCol = new DataGridViewTextBoxColumn
            {
                Name = "Username",
                HeaderText = "Tài khoản",
                FillWeight = 20
            };
            PasswordCol = new DataGridViewTextBoxColumn
            {
                Name = "Password",
                HeaderText = "Mật khẩu",
                FillWeight = 16
            };
            ServerCol = new DataGridViewTextBoxColumn
            {
                Name = "Server",
                HeaderText = "Server",
                FillWeight = 13
            };
            CharIndexCol = new DataGridViewTextBoxColumn
            {
                Name = "CharIndex",
                HeaderText = "NV",
                FillWeight = 7,
                MaxInputLength = 3
            };
            NoteCol = new DataGridViewTextBoxColumn
            {
                Name = "Note",
                HeaderText = "Ghi chú",
                FillWeight = 38
            };

            Columns.Add(EnabledCol);
            Columns.Add(UsernameCol);
            Columns.Add(PasswordCol);
            Columns.Add(ServerCol);
            Columns.Add(CharIndexCol);
            Columns.Add(NoteCol);
        }

        /// <summary>Nạp danh sách tài khoản vào lưới.</summary>
        public void LoadAccounts(List<AccountEntry> accounts)
        {
            Rows.Clear();
            if (accounts == null) return;

            foreach (var acc in accounts)
            {
                int idx = Rows.Add(
                    acc.Enabled,
                    acc.Username,
                    acc.Password,
                    acc.Server,
                    acc.CharIndex > 0 ? acc.CharIndex.ToString() : "",
                    acc.Note);
                Rows[idx].Tag = acc.Clone();
            }
        }

        /// <summary>Thu thập dữ liệu từ lưới về danh sách (đã loại bỏ dòng trống).</summary>
        public List<AccountEntry> CollectAccounts()
        {
            var result = new List<AccountEntry>();
            foreach (DataGridViewRow row in Rows)
            {
                if (row.IsNewRow) continue;

                string username = Cell(row, "Username").Trim();
                if (username.Length == 0) continue;

                int charIndex = 0;
                int.TryParse(Cell(row, "CharIndex").Trim(), out charIndex);

                result.Add(new AccountEntry
                {
                    Enabled = GetBool(row, "Enabled"),
                    Username = username,
                    Password = Cell(row, "Password"),
                    Server = Cell(row, "Server").Trim(),
                    CharIndex = charIndex,
                    Note = Cell(row, "Note")
                });
            }
            return result;
        }

        private static string Cell(DataGridViewRow row, string col)
        {
            if (row == null) return "";
            DataGridViewCell cell = null;
            try { cell = row.Cells[col]; } catch { }
            if (cell == null) return "";
            object val = cell.Value;
            return val == null ? "" : val.ToString();
        }

        private static bool GetBool(DataGridViewRow row, string col)
        {
            if (row == null) return true;
            DataGridViewCell cell = null;
            try { cell = row.Cells[col]; } catch { }
            if (cell == null) return true;
            object val = cell.Value;
            if (val == null) return true;
            if (val is bool b) return b;
            bool parsed;
            return bool.TryParse(val.ToString(), out parsed) ? parsed : true;
        }

        /// <summary>Lấy tài khoản đang được chọn (theo Username).</summary>
        public AccountEntry GetSelected()
        {
            if (CurrentRow == null || CurrentRow.IsNewRow) return null;

            string username = Cell(CurrentRow, "Username").Trim();
            if (username.Length == 0) return null;

            int charIndex = 0;
            int.TryParse(Cell(CurrentRow, "CharIndex").Trim(), out charIndex);

            return new AccountEntry
            {
                Enabled = GetBool(CurrentRow, "Enabled"),
                Username = username,
                Password = Cell(CurrentRow, "Password"),
                Server = Cell(CurrentRow, "Server").Trim(),
                CharIndex = charIndex,
                Note = Cell(CurrentRow, "Note")
            };
        }

        public void SelectByUsername(string username)
        {
            if (string.IsNullOrEmpty(username)) return;
            foreach (DataGridViewRow row in Rows)
            {
                if (row.IsNewRow) continue;
                if (string.Equals(Cell(row, "Username").Trim(), username, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    CurrentCell = row.Cells["Username"];
                    return;
                }
            }
        }
    }
}
