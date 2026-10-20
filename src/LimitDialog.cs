using System;
using System.Drawing;
using System.Windows.Forms;

namespace MemLens
{
    internal sealed class LimitDialog : Form
    {
        private RadioButton _rbLow;
        private RadioButton _rbTrim;
        private RadioButton _rbReset;
        private CheckBox _chkTrim;
        private Button _btnApply;

        public string Mode { get; private set; }
        public bool TrimAfter { get; private set; }

        public LimitDialog(int processCount)
        {
            Mode = "low";
            TrimAfter = true;
            Text = "限制选中进程";
            ClientSize = new Size(590, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = Theme.Ui(9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Theme.Navy;
            root.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.Text = "限制选中进程";
            title.Font = Theme.UiBold(16F);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(22, 13);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "将作用于 " + processCount + " 个进程。选择一种方式，MemLens 不会自动执行。";
            sub.Font = Theme.Ui(8.8F);
            sub.ForeColor = Color.FromArgb(148, 163, 184);
            sub.AutoSize = true;
            sub.Location = new Point(24, 43);
            header.Controls.Add(sub);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(24, 16, 24, 8);
            root.Controls.Add(body, 0, 1);

            int y = 14;
            _rbLow = MakeRadio("温和限制：把内存优先级降到最低（1 级）", 24, y, true);
            body.Controls.Add(_rbLow);
            y += 28;

            Label lowHelp = MakeHelp("系统内存紧张时优先回收它的页；内存充足时仍可正常使用。推荐给浏览器、Steam、聊天工具等后台程序。", 46, y);
            body.Controls.Add(lowHelp);
            y += 42;

            _chkTrim = new CheckBox();
            _chkTrim.Text = "同时立即回收一次，让现有页也按低优先级重新进入（推荐）";
            _chkTrim.AutoSize = true;
            _chkTrim.ForeColor = Theme.Ink;
            _chkTrim.Location = new Point(48, y);
            _chkTrim.Checked = true;
            body.Controls.Add(_chkTrim);
            y += 38;

            _rbTrim = MakeRadio("只回收一次：立即把工作集移出物理内存", 24, y, false);
            body.Controls.Add(_rbTrim);
            y += 28;
            Label trimHelp = MakeHelp("一次性操作，不改变优先级。程序下次访问会重新读盘，可能短暂卡顿。", 46, y);
            body.Controls.Add(trimHelp);
            y += 40;

            _rbReset = MakeRadio("恢复默认：把内存优先级改回正常（5 级）", 24, y, false);
            body.Controls.Add(_rbReset);
            y += 28;
            Label resetHelp = MakeHelp("用于撤销之前的温和限制；不会自动回收内存。", 46, y);
            body.Controls.Add(resetHelp);
            y += 36;

            Panel note = new Panel();
            note.Location = new Point(24, y);
            note.Size = new Size(542, 72);
            note.BackColor = Theme.AmberSoft;
            note.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(253, 230, 138)))
                    e.Graphics.DrawRectangle(pen, 0, 0, note.Width - 1, note.Height - 1);
            };
            body.Controls.Add(note);

            Label noteText = new Label();
            noteText.Text = "这是软限制，不是硬性内存上限。MemLens 不设置工作集硬上限，避免程序频繁换页、卡顿。\r\n内存优先级在进程退出或重启后失效。";
            noteText.Font = Theme.Ui(8.6F);
            noteText.ForeColor = Color.FromArgb(146, 64, 14);
            noteText.AutoSize = false;
            noteText.Location = new Point(12, 9);
            noteText.Size = new Size(516, 54);
            note.Controls.Add(noteText);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Theme.Header;
            footer.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            root.Controls.Add(footer, 0, 2);

            _btnApply = new Button();
            _btnApply.Text = "应用";
            _btnApply.DialogResult = DialogResult.OK;
            _btnApply.Size = new Size(92, 32);
            _btnApply.Location = new Point(ClientSize.Width - 116, 13);
            _btnApply.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnApply.FlatStyle = FlatStyle.Flat;
            _btnApply.FlatAppearance.BorderSize = 0;
            _btnApply.BackColor = Theme.Teal;
            _btnApply.ForeColor = Color.White;
            _btnApply.Font = Theme.UiBold(9F);
            _btnApply.Cursor = Cursors.Hand;
            footer.Controls.Add(_btnApply);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Size = new Size(82, 32);
            cancel.Location = new Point(ClientSize.Width - 208, 13);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderSize = 1;
            cancel.FlatAppearance.BorderColor = Theme.Border;
            cancel.BackColor = Color.White;
            cancel.ForeColor = Theme.Ink;
            cancel.Cursor = Cursors.Hand;
            footer.Controls.Add(cancel);

            AcceptButton = _btnApply;
            CancelButton = cancel;

            _rbLow.CheckedChanged += delegate { UpdateEnabled(); };
            _rbTrim.CheckedChanged += delegate { UpdateEnabled(); };
            _rbReset.CheckedChanged += delegate { UpdateEnabled(); };
            _btnApply.Click += delegate { CaptureMode(); };
        }

        private void UpdateEnabled()
        {
            _chkTrim.Enabled = _rbLow.Checked;
        }

        private void CaptureMode()
        {
            if (_rbTrim.Checked) Mode = "trim";
            else if (_rbReset.Checked) Mode = "reset";
            else Mode = "low";
            TrimAfter = _chkTrim.Checked && _rbLow.Checked;
        }

        private static RadioButton MakeRadio(string text, int x, int y, bool selected)
        {
            RadioButton rb = new RadioButton();
            rb.Text = text;
            rb.AutoSize = true;
            rb.Location = new Point(x, y);
            rb.ForeColor = Theme.Ink;
            rb.Font = Theme.UiBold(9.2F);
            rb.Checked = selected;
            return rb;
        }

        private static Label MakeHelp(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.MaximumSize = new Size(510, 0);
            label.Location = new Point(x, y);
            label.ForeColor = Theme.Muted;
            label.Font = Theme.Ui(8.5F);
            return label;
        }
    }
}
