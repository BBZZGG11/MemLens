using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MemLens
{
    internal sealed class HelpForm : Form
    {
        private RichTextBox _text;

        public HelpForm()
        {
            Text = "MemLens 使用说明";
            ClientSize = new Size(760, 650);
            MinimumSize = new Size(680, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = Theme.Ui(9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowIcon = false;
            MaximizeBox = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Theme.Navy;
            root.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.Text = "MemLens 使用说明";
            title.Font = Theme.UiBold(17F);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(24, 14);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "先理解数字，再决定是否操作。";
            sub.Font = Theme.Ui(9F);
            sub.ForeColor = Color.FromArgb(148, 163, 184);
            sub.AutoSize = true;
            sub.Location = new Point(26, 45);
            header.Controls.Add(sub);

            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(24, 16, 20, 8);
            host.BackColor = Color.White;
            root.Controls.Add(host, 0, 1);

            _text = new RichTextBox();
            _text.Dock = DockStyle.Fill;
            _text.BorderStyle = BorderStyle.None;
            _text.ReadOnly = true;
            _text.BackColor = Color.White;
            _text.ForeColor = Theme.Ink;
            _text.Font = Theme.Ui(9.3F);
            _text.DetectUrls = true;
            _text.ScrollBars = RichTextBoxScrollBars.Vertical;
            _text.LinkClicked += delegate(object sender, LinkClickedEventArgs e)
            {
                try { Process.Start(new ProcessStartInfo(e.LinkText) { UseShellExecute = true }); }
                catch { }
            };
            host.Controls.Add(_text);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Theme.Header;
            footer.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            root.Controls.Add(footer, 0, 2);

            Button close = new Button();
            close.Text = "知道了";
            close.DialogResult = DialogResult.OK;
            close.Size = new Size(96, 32);
            close.Location = new Point(ClientSize.Width - 120, 12);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderSize = 0;
            close.BackColor = Theme.Teal;
            close.ForeColor = Color.White;
            close.Font = Theme.UiBold(9F);
            close.Cursor = Cursors.Hand;
            footer.Controls.Add(close);
            AcceptButton = close;

            BuildText();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            DpiScale.Apply(this, new Size(760, 650), new Size(680, 560));
        }

        private void BuildText()
        {
            Append("一句话：这个工具把“可回收的缓存页”和“可能泄漏的私有提交”分开看，不把 Windows 的正常缓存当成故障。\r\n\r\n", Theme.Ui(9.6F), Theme.Ink);

            Heading("1. 它到底统计什么");
            Append("• 系统待机缓存：Windows 把暂时不用的文件页放在待机列表里，内存紧张时会自动回收。它不是“无效内存”，而是加速下一次读取的缓存。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("• 已修改页：已经改动、还没写回磁盘的页，数量通常很小。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("• 进程工作集：进程当前驻留在物理内存里的页，包含共享 DLL/文件页和私有页。工作集大，不等于泄漏。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("• 可回收估算：共享页按 90% 计，私有常驻按冷页比例计（空闲 35%，活跃 8%）。这是启发式估算，不是 Windows 官方指标。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("• 私有提交：进程已经提交的私有虚拟内存。它持续上涨，才更像泄漏。\r\n\r\n", Theme.Ui(9.3F), Theme.Ink);

            Heading("2. 为什么内存越大，同样几个程序占用也越多");
            Append("这是正常现象，主要有三个原因：\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("① Windows 会把空闲内存拿去做待机缓存，内存越大，缓存越多；\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("② 浏览器、Electron、.NET/Java 运行时会根据可用内存扩大堆或缓存；\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("③ 内存不紧张时，系统不会急着把进程的工作集修剪掉，页就留在物理内存里。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("所以“占用数字变大”本身不是问题。真正值得关注的是：可用内存长期很低、提交内存接近上限、某个进程的私有提交持续增长。\r\n\r\n", Theme.Ui(9.3F), Theme.Ink);

            Heading("3. 建议的使用顺序");
            Append("第一步：看“10 秒增长”。连续采样两次，某进程私有提交持续上涨，才值得怀疑。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("第二步：对高占用、低 CPU 的后台程序使用“温和限制”（内存优先级 1 级）。这不会硬性禁止它用内存，只在系统紧张时优先回收它。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("第三步：确实需要立刻腾出物理内存时，再对选中的进程“回收一次”。回收后程序访问会重新读盘，可能短暂卡顿。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("第四步：待机缓存只在确实需要时清理，而且优先选“仅低优先级”。频繁清理只会让程序反复读盘。\r\n\r\n", Theme.Ui(9.3F), Theme.Ink);

            Heading("4. 安全边界");
            Append("• 不会自动清理；所有写操作都要先勾选、再确认。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("• 不安装驱动、不注册服务、不修改注册表。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("• 不碰系统关键进程、其他会话进程和本工具自身。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("• 不提供工作集硬上限。硬上限会造成频繁换页和卡顿，而且不能修复泄漏。\r\n", Theme.Ui(9.3F), Theme.Ink);
            Append("• 内存优先级在进程退出/重启后失效，不会永久改坏程序。\r\n\r\n", Theme.Ui(9.3F), Theme.Ink);

            Heading("5. 参考");
            Append("Microsoft SetProcessInformation（内存优先级）\r\nhttps://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation\r\n\r\n", Theme.Ui(8.8F), Theme.Muted);
            Append("Microsoft EmptyWorkingSet（工作集回收）\r\nhttps://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset\r\n\r\n", Theme.Ui(8.8F), Theme.Muted);
            Append("Microsoft SetProcessWorkingSetSizeEx（硬上限的风险）\r\nhttps://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-setprocessworkingsetsizeex\r\n\r\n", Theme.Ui(8.8F), Theme.Muted);
            Append("Sysinternals RAMMap（待机列表可视化）\r\nhttps://learn.microsoft.com/en-us/sysinternals/downloads/rammap\r\n\r\n", Theme.Ui(8.8F), Theme.Muted);
            Append("Microsoft Q&A：待机内存为什么很大\r\nhttps://learn.microsoft.com/en-us/answers/questions/3150421/windows-takes-26-5-gb-ram-in-standby\r\n", Theme.Ui(8.8F), Theme.Muted);
            _text.SelectionStart = 0;
            _text.SelectionLength = 0;
            _text.ScrollToCaret();
        }

        private void Heading(string text)
        {
            Append(text + "\r\n", Theme.UiBold(11F), Theme.Teal);
        }

        private void Append(string text, Font font, Color color)
        {
            _text.SelectionStart = _text.TextLength;
            _text.SelectionLength = 0;
            _text.SelectionFont = font;
            _text.SelectionColor = color;
            _text.AppendText(text);
        }
    }
}
