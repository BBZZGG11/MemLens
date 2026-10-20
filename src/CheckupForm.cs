using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MemLens
{
    internal sealed class CheckupForm : Form
    {
        private readonly SystemSnapshot _snapshot;

        public CheckupForm(SystemSnapshot snapshot)
        {
            _snapshot = snapshot;
            Text = "MemLens 小白解读";
            ClientSize = new Size(780, 700);
            MinimumSize = new Size(700, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = Theme.Ui(9F);
            AutoScaleMode = AutoScaleMode.None;
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Theme.Navy;
            root.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.Text = "小白解读 · 这次体检说明什么";
            title.Font = Theme.UiBold(17F);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(24, 13);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "不用懂参数，先看结论和下一步。";
            sub.Font = Theme.Ui(9F);
            sub.ForeColor = Color.FromArgb(148, 163, 184);
            sub.AutoSize = true;
            sub.Location = new Point(26, 46);
            header.Controls.Add(sub);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.AutoScroll = true;
            body.Padding = new Padding(24, 16, 24, 12);
            body.BackColor = Color.White;
            root.Controls.Add(body, 0, 1);

            bool tight = (snapshot.CommitLimit > 0 && snapshot.CommitPercent >= 92) || snapshot.AvailablePercent < 12;
            bool watch = !tight && snapshot.AvailablePercent < 25;

            Panel status = new Panel();
            status.Dock = DockStyle.Top;
            status.Height = 104;
            status.Padding = new Padding(14, 10, 14, 8);
            status.BackColor = tight ? Theme.RedSoft : (watch ? Theme.AmberSoft : Theme.TealSoft);
            status.Paint += delegate(object sender, PaintEventArgs e)
            {
                Color border = tight ? Color.FromArgb(252, 165, 165) : (watch ? Color.FromArgb(253, 230, 138) : Color.FromArgb(153, 246, 228));
                using (Pen pen = new Pen(border))
                    e.Graphics.DrawRectangle(pen, 0, 0, status.Width - 1, status.Height - 1);
            };

            Label badge = new Label();
            badge.Text = tight ? "需要留意" : (watch ? "建议观察" : "总体健康");
            badge.Font = Theme.UiBold(8.2F);
            badge.ForeColor = tight ? Theme.Red : (watch ? Color.FromArgb(180, 83, 9) : Theme.Green);
            badge.AutoSize = true;
            badge.Location = new Point(14, 9);
            status.Controls.Add(badge);

            Label headline = new Label();
            headline.Text = tight ? "内存偏紧，先找增长，再考虑清理" : (watch ? "内存中等，暂时不用急着清" : "内存充足，暂不需要清理");
            headline.Font = Theme.UiBold(14F);
            headline.ForeColor = Theme.Ink;
            headline.AutoSize = false;
            headline.Location = new Point(14, 31);
            headline.Size = new Size(680, 28);
            status.Controls.Add(headline);

            Label statusDetail = new Label();
            statusDetail.Text = "可用 " + Fmt.Bytes(snapshot.AvailPhys) + " / " + Fmt.Bytes(snapshot.TotalPhys)
                + "（" + snapshot.AvailablePercent.ToString("0.0") + "%）  ·  可见进程 " + snapshot.Processes.Count + " 个";
            statusDetail.Font = Theme.Ui(9F);
            statusDetail.ForeColor = Theme.Muted;
            statusDetail.AutoSize = false;
            statusDetail.Location = new Point(14, 64);
            statusDetail.Size = new Size(680, 20);
            status.Controls.Add(statusDetail);

            TableLayoutPanel report = new TableLayoutPanel();
            report.Dock = DockStyle.Top;
            report.AutoSize = true;
            report.ColumnCount = 1;
            report.Width = 690;
            report.Margin = new Padding(0, 12, 0, 0);
            body.Controls.Add(report);

            List<ProcInfo> growth = new List<ProcInfo>();
            if (snapshot != null)
            {
                foreach (ProcInfo p in snapshot.Processes)
                    if (p.GrowthKnown && p.CanAct) growth.Add(p);
            }
            growth.Sort(delegate(ProcInfo a, ProcInfo b) { return b.GrowthBytes.CompareTo(a.GrowthBytes); });

            long maxPositive = 0;
            int over20 = 0;
            string topName = "";
            foreach (ProcInfo p in growth)
            {
                if (p.GrowthBytes > maxPositive)
                {
                    maxPositive = p.GrowthBytes;
                    topName = p.Name;
                }
                if (p.GrowthBytes >= 20L * 1024 * 1024) over20++;
            }

            string growthText;
            if (growth.Count == 0)
            {
                growthText = "这次还没做 10 秒增长采样。点主界面的“一键体检（10 秒）”，我会再看一次私有提交的变化。";
            }
            else if (over20 > 0)
            {
                growthText = "有 " + over20 + " 个进程在 10 秒内增长超过 20 MB（最大 " + topName + " " + Fmt.SignedBytes(maxPositive) + "）。这不等于泄漏，但建议再采样一次确认。";
            }
            else if (maxPositive >= 5L * 1024 * 1024)
            {
                growthText = "检测到轻微增长（最大 " + topName + " " + Fmt.SignedBytes(maxPositive) + "），但还不到值得处理的水平。";
            }
            else if (maxPositive > 0)
            {
                growthText = "10 秒内没有明显增长（最大 " + topName + " " + Fmt.SignedBytes(maxPositive) + "）。";
            }
            else
            {
                growthText = "10 秒内没有进程明显增长；不少进程还在下降，说明它们在释放缓存，不是泄漏。";
            }

            string conclusion;
            if (tight)
                conclusion = "先别急着清缓存。优先找“私有提交持续增长”的进程，再考虑限制它；清理只能短暂腾出物理内存。";
            else if (watch)
                conclusion = "现在不需要清理。可以偶尔看一次增长；如果某个后台程序长期占用高且 CPU 很低，再对它做温和限制。";
            else
                conclusion = "这台机器目前很健康。待机缓存和较大的工作集都是正常现象；为了数字好看去清理，通常只会让程序重新读盘。";

            AddHeading(report, "一句话结论");
            AddParagraph(report, conclusion);
            AddHeading(report, "我看到的数字");
            AddParagraph(report,
                "• 物理内存 " + Fmt.Bytes(snapshot.TotalPhys) + "，当前可用 " + Fmt.Bytes(snapshot.AvailPhys)
                + "（" + snapshot.AvailablePercent.ToString("0.0") + "%）。\r\n"
                + "• 待机缓存 " + Fmt.Bytes(snapshot.StandbyBytes) + "，已修改页 " + Fmt.Bytes(snapshot.ModifiedBytes) + "。\r\n"
                + "• 后台进程可回收估算约 " + Fmt.Bytes((ulong)Math.Max(0, snapshot.ProcessReclaimTotal))
                + "，来自 " + snapshot.ActionableCount + " 个可操作进程。\r\n"
                + "• 10 秒增长：" + growthText);
            AddHeading(report, "这些数字到底是什么意思");
            AddParagraph(report,
                "• 待机缓存：Windows 把暂时不用的文件页留在内存里，下次打开更快。内存紧张时自动回收，不是“无效内存”。\r\n"
                + "• 工作集 / 可回收：进程当前占用的物理页。可回收是估算值；回收后程序一访问，页可能很快又回来。\r\n"
                + "• 私有提交 / 10 秒增长：进程真正申请的内存。持续增长才更像泄漏；单次工作集大不等于异常。");
            AddHeading(report, "你现在可以做什么");
            if (tight)
            {
                AddParagraph(report,
                    "1. 回到主界面，点“10 秒增长”再采样一次。\r\n"
                    + "2. 同一进程连续两次都明显增长，再勾选它，用“限制选中 → 温和限制”。\r\n"
                    + "3. 只有确实卡顿或提交内存接近上限时，才考虑清理待机缓存。");
            }
            else
            {
                AddParagraph(report,
                    "1. 现在不需要清理，也不需要限制。\r\n"
                    + "2. 如果某个程序越用越卡：勾选它 → 限制选中 → 温和限制（内存优先级 1 级）。\r\n"
                    + "3. 只有明确需要腾物理内存时，才“回收选中”或清理待机缓存；不要定时做。");
            }
            AddHeading(report, "注意");
            AddParagraph(report,
                "本工具不会自动操作；“可回收”是估算，不是精确的泄漏值；没有提供工作集硬上限，避免频繁换页；内存优先级在进程重启后失效。");

            body.Controls.Add(status);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Theme.Header;
            root.Controls.Add(footer, 0, 2);
            footer.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            Button close = new Button();
            close.Text = "我知道了";
            close.DialogResult = DialogResult.OK;
            close.Size = new Size(104, 32);
            close.Location = new Point(ClientSize.Width - 128, 13);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderSize = 0;
            close.BackColor = Theme.Teal;
            close.ForeColor = Color.White;
            close.Font = Theme.UiBold(9F);
            close.Cursor = Cursors.Hand;
            footer.Controls.Add(close);
            AcceptButton = close;

            body.AutoScrollPosition = new Point(0, 0);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            DpiScale.Apply(this, new Size(780, 700), new Size(700, 600));
        }

        private static void AddHeading(TableLayoutPanel panel, string text)
        {
            int row = panel.RowCount;
            panel.RowCount = row + 1;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label label = new Label();
            label.Text = text;
            label.Font = Theme.UiBold(10.5F);
            label.ForeColor = Theme.Teal;
            label.AutoSize = true;
            label.Margin = new Padding(0, 10, 0, 4);
            panel.Controls.Add(label, 0, row);
        }

        private static void AddParagraph(TableLayoutPanel panel, string text)
        {
            int row = panel.RowCount;
            panel.RowCount = row + 1;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label label = new Label();
            label.Text = text;
            label.Font = Theme.Ui(8.9F);
            label.ForeColor = Theme.Ink;
            label.AutoSize = true;
            label.MaximumSize = new Size(680, 0);
            label.Margin = new Padding(0, 0, 0, 5);
            panel.Controls.Add(label, 0, row);
        }
    }
}
