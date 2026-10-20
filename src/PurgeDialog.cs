using System;
using System.Drawing;
using System.Windows.Forms;

namespace MemLens
{
    internal sealed class PurgeDialog : Form
    {
        private RadioButton _rbLow;
        private RadioButton _rbAll;

        public int Command { get; private set; }

        public PurgeDialog(bool isAdmin)
        {
            Command = Native.MemoryPurgeLowPriorityStandbyList;
            Text = "清理系统待机缓存";
            ClientSize = new Size(550, 390);
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
            title.Text = "清理系统待机缓存";
            title.Font = Theme.UiBold(16F);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(22, 13);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "待机缓存是 Windows 的正常文件缓存，不是故障。";
            sub.Font = Theme.Ui(8.8F);
            sub.ForeColor = Color.FromArgb(148, 163, 184);
            sub.AutoSize = true;
            sub.Location = new Point(24, 43);
            header.Controls.Add(sub);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(24, 18, 24, 8);
            root.Controls.Add(body, 0, 1);

            _rbLow = new RadioButton();
            _rbLow.Text = "仅清理低优先级待机页（推荐）";
            _rbLow.AutoSize = true;
            _rbLow.Checked = true;
            _rbLow.Font = Theme.UiBold(9.2F);
            _rbLow.ForeColor = Theme.Ink;
            _rbLow.Location = new Point(24, 18);
            body.Controls.Add(_rbLow);

            Label lowHelp = new Label();
            lowHelp.Text = "优先丢弃不常访问的缓存，对常用程序影响较小。";
            lowHelp.AutoSize = true;
            lowHelp.MaximumSize = new Size(480, 0);
            lowHelp.Location = new Point(46, 46);
            lowHelp.ForeColor = Theme.Muted;
            lowHelp.Font = Theme.Ui(8.5F);
            body.Controls.Add(lowHelp);

            _rbAll = new RadioButton();
            _rbAll.Text = "清理全部待机页（释放更多，可能变慢）";
            _rbAll.AutoSize = true;
            _rbAll.Font = Theme.UiBold(9.2F);
            _rbAll.ForeColor = Theme.Ink;
            _rbAll.Location = new Point(24, 88);
            body.Controls.Add(_rbAll);

            Label allHelp = new Label();
            allHelp.Text = "包括常用文件缓存。之后打开程序可能重新读盘，出现短暂卡顿。";
            allHelp.AutoSize = true;
            allHelp.MaximumSize = new Size(480, 0);
            allHelp.Location = new Point(46, 116);
            allHelp.ForeColor = Theme.Muted;
            allHelp.Font = Theme.Ui(8.5F);
            body.Controls.Add(allHelp);

            Panel note = new Panel();
            note.Location = new Point(24, 158);
            note.Size = new Size(502, 96);
            note.BackColor = Theme.AmberSoft;
            note.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(253, 230, 138)))
                    e.Graphics.DrawRectangle(pen, 0, 0, note.Width - 1, note.Height - 1);
            };
            body.Controls.Add(note);

            Label noteText = new Label();
            noteText.Text = "这不会减少程序的提交内存，也不能修复内存泄漏；只是把缓存页变为空闲页。\r\nWindows 本身会在需要时自动回收待机页，通常不需要手动清理。\r\n" + (isAdmin ? "当前已是管理员，可以执行。" : "当前不是管理员，点击“应用”后会提示以管理员身份重启。");
            noteText.Font = Theme.Ui(8.5F);
            noteText.ForeColor = Color.FromArgb(146, 64, 14);
            noteText.AutoSize = false;
            noteText.Location = new Point(12, 9);
            noteText.Size = new Size(476, 78);
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

            Button apply = new Button();
            apply.Text = "应用";
            apply.DialogResult = DialogResult.OK;
            apply.Size = new Size(92, 32);
            apply.Location = new Point(ClientSize.Width - 116, 13);
            apply.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            apply.FlatStyle = FlatStyle.Flat;
            apply.FlatAppearance.BorderSize = 0;
            apply.BackColor = Theme.Teal;
            apply.ForeColor = Color.White;
            apply.Font = Theme.UiBold(9F);
            apply.Cursor = Cursors.Hand;
            apply.Click += delegate
            {
                Command = _rbAll.Checked ? Native.MemoryPurgeStandbyList : Native.MemoryPurgeLowPriorityStandbyList;
            };
            footer.Controls.Add(apply);

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

            AcceptButton = apply;
            CancelButton = cancel;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            DpiScale.Apply(this, new Size(550, 390), Size.Empty);
        }
    }
}
