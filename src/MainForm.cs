using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MemLens
{
    internal sealed class MainForm : Form
    {
        private readonly MemScanner _scanner = new MemScanner();
        private readonly HashSet<int> _checked = new HashSet<int>();
        private readonly ToolTip _tip = new ToolTip();

        private SystemSnapshot _snapshot;
        private DataGridView _grid;
        private SplitContainer _split;
        private MemoryBar _bar;
        private CheckBox _chkOnlyReclaim;
        private Label _valTotal;
        private Label _valUsed;
        private Label _valAvail;
        private Label _valStandby;
        private Label _valModified;
        private Label _valReclaim;
        private Label _lblBasis;
        private Label _lblSelectedTitle;
        private Label _lblSelectedBody;
        private Label _lblAdvice;
        private Label _lblLastScan;
        private Label _lblAdmin;
        private Label _lblAdminHint;
        private Panel _sideHost;
        private Label _lblCheckupTitle;
        private Label _lblCheckupDetail;
        private Panel _checkupBanner;
        private Button _btnCheckup;
        private Button _btnExplain;
        private bool _checkupAfterScan;
        private Button _btnScan;
        private Button _btnGrowth;
        private Button _btnTrim;
        private Button _btnLimit;
        private Button _btnRestore;
        private Button _btnPurge;
        private Button _btnExport;
        private ToolStripStatusLabel _status;
        private ToolStripStatusLabel _statusRight;
        private ToolStripProgressBar _progress;

        private bool _busy;
        private bool _updating;
        private string _sortKey = "reclaim";
        private bool _sortDesc = true;
        private int _selectedPid = -1;
        private int _scanGeneration;

        public MainForm()
        {
            Text = "MemLens · 内存透视";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(1280, 870);
            MinimumSize = new Size(1200, 790);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Font = Theme.Ui(9F);
            DoubleBuffered = true;
            KeyPreview = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            _tip.AutoPopDelay = 12000;
            _tip.InitialDelay = 350;
            _tip.ReshowDelay = 100;
            _tip.ShowAlways = true;

            BuildUi();
            Shown += delegate
            {
                UpdateAdminBadge();
                try
                {
                    _split.Panel2MinSize = 330;
                    _split.SplitterDistance = Math.Max(720, ClientSize.Width - 380);
                    if (_sideHost != null) _sideHost.AutoScrollPosition = new Point(0, 0);
                }
                catch { }
                StartScan();
            };
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.BackColor = Theme.Bg;
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildSummary(), 0, 1);
            root.Controls.Add(BuildCheckupBanner(), 0, 2);
            root.Controls.Add(BuildToolbar(), 0, 3);
            root.Controls.Add(BuildContent(), 0, 4);
            root.Controls.Add(BuildStatus(), 0, 5);
        }

        private Control BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Theme.Navy;
            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Teal, 2F))
                    e.Graphics.DrawLine(pen, 0, header.Height - 2, header.Width, header.Height - 2);
            };

            Label title = new Label();
            title.Text = "MemLens · 内存透视";
            title.Font = Theme.UiBold(22F);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(24, 14);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "看清 Windows 的“可回收占用”，再由你决定是否限制它";
            sub.Font = Theme.Ui(9.2F);
            sub.ForeColor = Color.FromArgb(148, 163, 184);
            sub.AutoSize = true;
            sub.Location = new Point(26, 64);
            header.Controls.Add(sub);

            _lblAdmin = new Label();
            _lblAdmin.Text = "普通权限";
            _lblAdmin.Font = Theme.UiBold(8.8F);
            _lblAdmin.TextAlign = ContentAlignment.MiddleCenter;
            _lblAdmin.Size = new Size(104, 28);
            _lblAdmin.BackColor = Color.FromArgb(51, 65, 85);
            _lblAdmin.ForeColor = Color.FromArgb(226, 232, 240);
            _lblAdmin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(_lblAdmin);

            Label win = new Label();
            win.Text = "Windows 10 · 本机数据";
            win.Font = Theme.Ui(8.2F);
            win.ForeColor = Color.FromArgb(100, 116, 139);
            win.AutoSize = true;
            win.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(win);

            header.Resize += delegate
            {
                _lblAdmin.Left = Math.Max(24, header.ClientSize.Width - _lblAdmin.Width - 24);
                _lblAdmin.Top = 25;
                win.Left = Math.Max(24, header.ClientSize.Width - win.Width - 26);
                win.Top = 65;
            };

            return header;
        }

        private Control BuildSummary()
        {
            Panel summary = new Panel();
            summary.Dock = DockStyle.Fill;
            summary.BackColor = Theme.Bg;
            summary.Padding = new Padding(18, 10, 18, 4);

            TableLayoutPanel outer = new TableLayoutPanel();
            outer.Dock = DockStyle.Fill;
            outer.ColumnCount = 1;
            outer.RowCount = 2;
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            summary.Controls.Add(outer);

            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Fill;
            cards.ColumnCount = 6;
            cards.RowCount = 1;
            for (int i = 0; i < 6; i++)
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.6667F));
            outer.Controls.Add(cards, 0, 0);

            Color[] accents = new Color[] { Theme.Sky, Theme.Navy, Theme.Green, Theme.Teal, Theme.Amber, Theme.Red };
            string[] titles = new string[] { "物理内存", "正在使用", "可用内存", "待机缓存", "已修改页", "后台可回收（估算）" };
            Label[] values = new Label[6];
            for (int i = 0; i < 6; i++)
            {
                Label value;
                Panel card = MakeCard(titles[i], accents[i], i == 5, out value);
                values[i] = value;
                cards.Controls.Add(card, i, 0);
            }
            _valTotal = values[0];
            _valUsed = values[1];
            _valAvail = values[2];
            _valStandby = values[3];
            _valModified = values[4];
            _valReclaim = values[5];

            _tip.SetToolTip(_valTotal, "Windows 可用的物理内存总量。");
            _tip.SetToolTip(_valUsed, "总内存 - 可用内存。不包含待机缓存。");
            _tip.SetToolTip(_valAvail, "系统认为可以立即使用的内存，包含空闲页和待机缓存。");
            _tip.SetToolTip(_valStandby, "Windows 的文件缓存，内存紧张时会自动回收；不是故障。");
            _tip.SetToolTip(_valModified, "已修改但尚未写回磁盘的页。");
            _tip.SetToolTip(_valReclaim, "可操作进程的逐项估算合计，含共享页，可能重复计算；这不是精确值。");

            Panel barHost = new Panel();
            barHost.Dock = DockStyle.Fill;
            barHost.BackColor = Theme.Bg;
            outer.Controls.Add(barHost, 0, 1);

            FlowLayoutPanel legend = new FlowLayoutPanel();
            legend.Dock = DockStyle.Fill;
            legend.FlowDirection = FlowDirection.LeftToRight;
            legend.WrapContents = false;
            legend.Padding = new Padding(2, 4, 0, 0);
            barHost.Controls.Add(legend);

            _bar = new MemoryBar();
            _bar.Dock = DockStyle.Top;
            _bar.Height = 13;
            _bar.Margin = new Padding(0);
            barHost.Controls.Add(_bar);

            AddLegend(legend, Theme.Sky, "正在使用");
            AddLegend(legend, Theme.Teal, "待机缓存");
            AddLegend(legend, Theme.Amber, "已修改");
            AddLegend(legend, Color.FromArgb(203, 213, 225), "空闲 / 零页");

            return summary;
        }

        private Panel MakeCard(string title, Color accent, bool last, out Label value)
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.BackColor = Theme.Card;
            card.Margin = new Padding(0, 0, last ? 0 : 8, 0);
            card.Padding = new Padding(14, 7, 14, 5);
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (SolidBrush brush = new SolidBrush(accent))
                    e.Graphics.FillRectangle(brush, 0, 0, card.Width, 3);
                using (Pen pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            TableLayoutPanel inner = new TableLayoutPanel();
            inner.Dock = DockStyle.Fill;
            inner.ColumnCount = 1;
            inner.RowCount = 2;
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
            card.Controls.Add(inner);

            Label v = new Label();
            v.Text = "—";
            v.Dock = DockStyle.Fill;
            v.Font = Theme.UiBold(18F);
            v.ForeColor = Theme.Ink;
            v.TextAlign = ContentAlignment.MiddleLeft;
            v.AutoSize = false;
            inner.Controls.Add(v, 0, 0);

            Label t = new Label();
            t.Text = title;
            t.Dock = DockStyle.Fill;
            t.Font = Theme.Ui(8.3F);
            t.ForeColor = Theme.Muted;
            t.TextAlign = ContentAlignment.TopLeft;
            t.AutoSize = false;
            inner.Controls.Add(t, 0, 1);

            value = v;
            return card;
        }

        private static void AddLegend(FlowLayoutPanel flow, Color color, string text)
        {
            Panel dot = new Panel();
            dot.Size = new Size(9, 9);
            dot.BackColor = color;
            dot.Margin = new Padding(0, 4, 5, 0);
            flow.Controls.Add(dot);

            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = Theme.Ui(8.2F);
            label.ForeColor = Theme.Muted;
            label.Margin = new Padding(0, 1, 17, 0);
            flow.Controls.Add(label);
        }

        private Control BuildCheckupBanner()
        {
            Panel banner = new Panel();
            banner.Dock = DockStyle.Fill;
            banner.BackColor = Theme.TealSoft;
            banner.Padding = new Padding(18, 9, 18, 8);
            banner.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, banner.Height - 1, banner.Width, banner.Height - 1);
            };
            _checkupBanner = banner;

            Label kicker = new Label();
            kicker.Text = "本次体检";
            kicker.Font = Theme.UiBold(7.8F);
            kicker.ForeColor = Theme.Teal;
            kicker.AutoSize = true;
            kicker.Location = new Point(18, 8);
            banner.Controls.Add(kicker);

            _lblCheckupTitle = new Label();
            _lblCheckupTitle.Text = "正在读取内存概况...";
            _lblCheckupTitle.Font = Theme.UiBold(11F);
            _lblCheckupTitle.ForeColor = Theme.Ink;
            _lblCheckupTitle.AutoSize = false;
            _lblCheckupTitle.AutoEllipsis = true;
            _lblCheckupTitle.Location = new Point(18, 23);
            _lblCheckupTitle.Size = new Size(820, 24);
            banner.Controls.Add(_lblCheckupTitle);

            _lblCheckupDetail = new Label();
            _lblCheckupDetail.Text = "扫描完成后，这里会用大白话告诉你：内存够不够、要不要清理。";
            _lblCheckupDetail.Font = Theme.Ui(8.5F);
            _lblCheckupDetail.ForeColor = Theme.Muted;
            _lblCheckupDetail.AutoSize = false;
            _lblCheckupDetail.AutoEllipsis = true;
            _lblCheckupDetail.Location = new Point(18, 48);
            _lblCheckupDetail.Size = new Size(820, 19);
            banner.Controls.Add(_lblCheckupDetail);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Right;
            actions.Width = 300;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.Padding = new Padding(0, 18, 0, 0);
            actions.BackColor = Color.Transparent;
            banner.Controls.Add(actions);

            _btnCheckup = MakeButton("一键体检（10 秒）", true, Color.Empty);
            _btnCheckup.Height = 30;
            _btnCheckup.MinimumSize = new Size(0, 30);
            _btnCheckup.Margin = new Padding(0, 0, 6, 0);
            _btnCheckup.Click += delegate { StartCheckup(); };
            actions.Controls.Add(_btnCheckup);

            _btnExplain = MakeButton("查看解读", false, Color.Empty);
            _btnExplain.Height = 30;
            _btnExplain.MinimumSize = new Size(0, 30);
            _btnExplain.Margin = new Padding(0, 0, 6, 0);
            _btnExplain.Click += delegate { ShowBeginnerReport(); };
            actions.Controls.Add(_btnExplain);

            _tip.SetToolTip(_btnCheckup, "先扫描，再做 10 秒增长采样，最后给出小白版结论。");
            _tip.SetToolTip(_btnExplain, "用大白话解释当前数字，不会操作任何进程。");

            return banner;
        }

        private Control BuildToolbar()
        {
            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.BackColor = Color.White;
            toolbar.Padding = new Padding(18, 11, 18, 10);
            toolbar.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            };

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = false;
            flow.AutoScroll = false;
            toolbar.Controls.Add(flow);

            _btnScan = MakeButton("重新扫描", true, Color.Empty);
            _btnGrowth = MakeButton("10 秒增长采样", false, Color.Empty);
            _btnTrim = MakeButton("回收选中", false, Theme.Amber);
            _btnLimit = MakeButton("限制选中", false, Theme.Teal);
            _btnRestore = MakeButton("恢复默认优先级", false, Color.Empty);
            _btnPurge = MakeButton("清理系统缓存", false, Color.Empty);
            _btnExport = MakeButton("导出报告", false, Color.Empty);
            Button help = MakeButton("说明", false, Color.Empty);

            flow.Controls.Add(_btnScan);
            flow.Controls.Add(_btnGrowth);
            flow.Controls.Add(MakeSeparator());
            flow.Controls.Add(_btnTrim);
            flow.Controls.Add(_btnLimit);
            flow.Controls.Add(_btnRestore);
            flow.Controls.Add(MakeSeparator());
            flow.Controls.Add(_btnPurge);
            flow.Controls.Add(_btnExport);
            flow.Controls.Add(help);

            _btnScan.Click += delegate { StartScan(); };
            _btnGrowth.Click += delegate { StartGrowthSample(); };
            _btnTrim.Click += delegate { TrimSelected(); };
            _btnLimit.Click += delegate { LimitSelected(); };
            _btnRestore.Click += delegate { RestoreSelected(); };
            _btnPurge.Click += delegate { PurgeStandby(); };
            _btnExport.Click += delegate { ExportReport(); };
            help.Click += delegate
            {
                using (HelpForm form = new HelpForm()) form.ShowDialog(this);
            };

            return toolbar;
        }

        private Button MakeButton(string text, bool primary, Color accent)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Height = 32;
            button.MinimumSize = new Size(0, 32);
            button.Padding = new Padding(12, 3, 12, 3);
            button.Margin = new Padding(0, 0, 7, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.Font = Theme.Ui(9F);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;

            if (primary)
            {
                button.BackColor = Theme.Teal;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderSize = 0;
                button.Font = Theme.UiBold(9F);
            }
            else if (accent != Color.Empty)
            {
                button.BackColor = Color.White;
                button.ForeColor = accent;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = accent;
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = Theme.Ink;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Theme.Border;
            }

            return button;
        }

        private static Panel MakeSeparator()
        {
            Panel sep = new Panel();
            sep.Size = new Size(1, 24);
            sep.BackColor = Theme.Border;
            sep.Margin = new Padding(3, 4, 10, 0);
            return sep;
        }

        private Control BuildContent()
        {
            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Vertical;
            _split.SplitterWidth = 1;
            _split.BackColor = Theme.Border;
            _split.FixedPanel = FixedPanel.Panel2;
            _split.Panel1.BackColor = Color.White;
            _split.Panel2.BackColor = Color.White;
            _split.Panel1.Padding = new Padding(18, 8, 18, 10);
            _split.Panel2.Padding = new Padding(16, 10, 16, 12);

            Panel leftHead = new Panel();
            leftHead.Dock = DockStyle.Top;
            leftHead.Height = 30;
            leftHead.BackColor = Color.White;

            Label gridTitle = new Label();
            gridTitle.Text = "进程明细";
            gridTitle.Font = Theme.UiBold(11F);
            gridTitle.ForeColor = Theme.Ink;
            gridTitle.AutoSize = true;
            gridTitle.Dock = DockStyle.Left;
            gridTitle.Padding = new Padding(0, 4, 0, 0);
            leftHead.Controls.Add(gridTitle);

            _lblLastScan = new Label();
            _lblLastScan.Text = "尚未扫描";
            _lblLastScan.Font = Theme.Ui(8.4F);
            _lblLastScan.ForeColor = Theme.Muted;
            _lblLastScan.AutoSize = true;
            _lblLastScan.Dock = DockStyle.Right;
            _lblLastScan.TextAlign = ContentAlignment.MiddleRight;
            _lblLastScan.Padding = new Padding(0, 7, 0, 0);
            leftHead.Controls.Add(_lblLastScan);

            _grid = BuildGrid();
            _split.Panel1.Controls.Add(_grid);
            _split.Panel1.Controls.Add(leftHead);
            _split.Panel2.Controls.Add(BuildSidebar());

            return _split;
        }

        private DataGridView BuildGrid()
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoGenerateColumns = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.ColumnHeadersHeight = 40;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.EnableHeadersVisualStyles = false;
            grid.GridColor = Theme.Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowTemplate.Height = 34;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.ShowCellToolTips = true;
            grid.ScrollBars = ScrollBars.Vertical;
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
            grid.DefaultCellStyle.Font = Theme.Ui(9F);
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Theme.Ink;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
            grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Header;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.UiBold(8.5F);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Header;

            DataGridViewCheckBoxColumn check = new DataGridViewCheckBoxColumn();
            check.HeaderText = "";
            check.Width = 40;
            check.Resizable = DataGridViewTriState.False;
            check.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(check);

            grid.Columns.Add(MakeTextColumn("进程", 148, "name", false));
            grid.Columns.Add(MakeTextColumn("PID", 50, "pid", true));
            grid.Columns.Add(MakeTextColumn("优先级", 78, "priority", false));
            grid.Columns.Add(MakeTextColumn("工作集", 82, "ws", true));
            grid.Columns.Add(MakeTextColumn("私有常驻", 90, "private", true));
            grid.Columns.Add(MakeTextColumn("可回收", 88, "reclaim", true));
            grid.Columns.Add(MakeTextColumn("CPU", 50, "cpu", true));
            grid.Columns.Add(MakeTextColumn("10s增长", 82, "growth", true));

            DataGridViewTextBoxColumn note = MakeTextColumn("状态 / 建议", 170, null, false);
            note.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            note.MinimumWidth = 140;
            grid.Columns.Add(note);

            grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.SelectionChanged += Grid_SelectionChanged;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.DataError += delegate { };

            return grid;
        }

        private static DataGridViewTextBoxColumn MakeTextColumn(string header, int width, string sortKey, bool right)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.HeaderText = header;
            column.Width = width;
            column.ReadOnly = true;
            column.SortMode = sortKey == null ? DataGridViewColumnSortMode.NotSortable : DataGridViewColumnSortMode.Programmatic;
            if (right)
            {
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            else
            {
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            }
            return column;
        }

        private Control BuildSidebar()
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Color.White;
            host.AutoScroll = true;
            _sideHost = host;

            TableLayoutPanel side = new TableLayoutPanel();
            side.Dock = DockStyle.Top;
            side.AutoSize = true;
            side.ColumnCount = 1;
            side.RowCount = 1;
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            host.Controls.Add(side);

            AddRow(side, MakeSideHeading("判定口径"));
            _lblBasis = MakeSideText("可回收 ≈ 共享页×90% + 私有常驻×冷页比例（空闲 35% / 活跃 8%）。这是估算，不是 Windows 官方的“无效内存”指标。");
            AddRow(side, _lblBasis);
            AddRow(side, MakeDivider());

            _chkOnlyReclaim = new CheckBox();
            _chkOnlyReclaim.Text = "只显示可回收 ≥ 20 MB";
            _chkOnlyReclaim.AutoSize = true;
            _chkOnlyReclaim.ForeColor = Theme.Ink;
            _chkOnlyReclaim.Font = Theme.Ui(8.8F);
            _chkOnlyReclaim.Margin = new Padding(0, 0, 0, 6);
            _chkOnlyReclaim.CheckedChanged += delegate { RebuildGrid(); };
            AddRow(side, _chkOnlyReclaim);

            FlowLayoutPanel links = new FlowLayoutPanel();
            links.AutoSize = true;
            links.WrapContents = false;
            links.Margin = new Padding(0, 0, 0, 8);
            LinkLabel selectAll = MakeLink("全选可操作");
            selectAll.LinkClicked += delegate { SelectAllActionable(); };
            LinkLabel clear = MakeLink("清空选择");
            clear.LinkClicked += delegate { _checked.Clear(); RebuildGrid(); };
            links.Controls.Add(selectAll);
            links.Controls.Add(clear);
            AddRow(side, links);

            AddRow(side, MakeDivider());
            AddRow(side, MakeSideHeading("选中进程"));
            _lblSelectedTitle = new Label();
            _lblSelectedTitle.Text = "未选择进程";
            _lblSelectedTitle.Font = Theme.UiBold(9.5F);
            _lblSelectedTitle.ForeColor = Theme.Ink;
            _lblSelectedTitle.AutoSize = true;
            _lblSelectedTitle.MaximumSize = new Size(320, 0);
            _lblSelectedTitle.Margin = new Padding(0, 0, 0, 3);
            AddRow(side, _lblSelectedTitle);

            _lblSelectedBody = MakeSideText("在左侧点选一行，查看工作集、私有常驻、共享页、私有提交和增长情况。");
            AddRow(side, _lblSelectedBody);

            AddRow(side, MakeDivider());
            AddRow(side, MakeSideHeading("健康建议"));
            _lblAdvice = new Label();
            _lblAdvice.Text = "等待扫描...";
            _lblAdvice.Font = Theme.Ui(8.7F);
            _lblAdvice.ForeColor = Theme.Ink;
            _lblAdvice.BackColor = Theme.TealSoft;
            _lblAdvice.Padding = new Padding(10);
            _lblAdvice.AutoSize = true;
            _lblAdvice.MaximumSize = new Size(306, 0);
            _lblAdvice.Margin = new Padding(0, 0, 0, 10);
            AddRow(side, _lblAdvice);

            AddRow(side, MakeDivider());
            AddRow(side, MakeSideHeading("操作边界"));
            AddRow(side, MakeSideText("不会自动清理；不安装驱动、不改注册表和服务；不碰系统关键进程。\r\n\r\n没有提供工作集硬上限：硬上限会造成频繁换页和卡顿，而且不能修复泄漏。这里用的是 Windows 原生内存优先级，属于软限制。"));
            _lblAdminHint = MakeSideText("清理待机缓存需要管理员权限；扫描、回收和限制不需要。");
            _lblAdminHint.ForeColor = Theme.Amber;
            AddRow(side, _lblAdminHint);
            AddRow(side, MakeSideText("提示：待机缓存不是“无效内存”。Windows 会在需要时自动回收。频繁清理只会让程序重新读盘。"));

            return host;
        }

        private static Label MakeSideHeading(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = Theme.UiBold(10.2F);
            label.ForeColor = Theme.Ink;
            label.AutoSize = true;
            label.Margin = new Padding(0, 2, 0, 7);
            return label;
        }

        private static Label MakeSideText(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = Theme.Ui(8.55F);
            label.ForeColor = Theme.Muted;
            label.AutoSize = true;
            label.MaximumSize = new Size(320, 0);
            label.Margin = new Padding(0, 0, 0, 8);
            return label;
        }

        private static Panel MakeDivider()
        {
            Panel panel = new Panel();
            panel.Height = 1;
            panel.Dock = DockStyle.Top;
            panel.BackColor = Theme.Border;
            panel.Margin = new Padding(0, 5, 0, 11);
            return panel;
        }

        private static LinkLabel MakeLink(string text)
        {
            LinkLabel link = new LinkLabel();
            link.Text = text;
            link.AutoSize = true;
            link.Font = Theme.Ui(8.6F);
            link.LinkColor = Theme.Teal;
            link.ActiveLinkColor = Theme.Navy;
            link.VisitedLinkColor = Theme.Teal;
            link.Margin = new Padding(0, 0, 14, 0);
            return link;
        }

        private static void AddRow(TableLayoutPanel panel, Control control)
        {
            if (panel.Controls.Count > 0)
            {
                panel.RowCount++;
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            control.Dock = DockStyle.Top;
            panel.Controls.Add(control, 0, panel.RowCount - 1);
        }

        private Control BuildStatus()
        {
            StatusStrip strip = new StatusStrip();
            strip.Dock = DockStyle.Fill;
            strip.BackColor = Color.White;
            strip.SizingGrip = false;
            strip.AutoSize = false;
            strip.Padding = new Padding(18, 0, 18, 0);
            strip.Font = Theme.Ui(8.5F);

            _status = new ToolStripStatusLabel();
            _status.Text = "就绪";
            _status.Spring = true;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Theme.Muted;

            _progress = new ToolStripProgressBar();
            _progress.Size = new Size(120, 15);
            _progress.Visible = false;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 28;

            _statusRight = new ToolStripStatusLabel();
            _statusRight.Text = "普通权限 · 估算值仅供参考";
            _statusRight.TextAlign = ContentAlignment.MiddleRight;
            _statusRight.ForeColor = Theme.Muted;

            strip.Items.Add(_status);
            strip.Items.Add(_progress);
            strip.Items.Add(_statusRight);
            return strip;
        }

        private void UpdateAdminBadge()
        {
            bool admin = Native.IsAdministrator();
            if (admin)
            {
                _lblAdmin.Text = "管理员";
                _lblAdmin.BackColor = Color.FromArgb(6, 78, 59);
                _lblAdmin.ForeColor = Color.FromArgb(167, 243, 208);
                _statusRight.Text = "管理员 · 待机缓存清理可用";
                if (_lblAdminHint != null) _lblAdminHint.Text = "当前已是管理员，可以清理待机缓存；其他功能不需要管理员。";
            }
            else
            {
                _lblAdmin.Text = "普通权限";
                _lblAdmin.BackColor = Color.FromArgb(51, 65, 85);
                _lblAdmin.ForeColor = Color.FromArgb(226, 232, 240);
                _statusRight.Text = "普通权限 · 清理待机缓存需管理员";
            }
            _tip.SetToolTip(_lblAdmin, admin ? "以管理员身份运行，待机缓存清理可用。" : "普通权限即可扫描、回收工作集、设置内存优先级；清理待机缓存需要管理员。");
        }

        private void StartScan()
        {
            if (_busy) return;
            _scanGeneration++;
            int generation = _scanGeneration;
            SetBusy(true, "正在扫描系统内存...");
            Thread thread = new Thread(delegate()
            {
                try
                {
                    SystemSnapshot snapshot = _scanner.Scan(delegate(string message) { SetStatus(message); });
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (generation != _scanGeneration) return;
                            ApplySnapshot(snapshot);
                        });
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _checkupAfterScan = false;
                            SetBusy(false, "扫描失败");
                            MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        });
                    }
                    catch { }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void ApplySnapshot(SystemSnapshot snapshot)
        {
            bool startCheckup = _checkupAfterScan;
            _checkupAfterScan = false;
            _snapshot = snapshot;
            PruneChecked();
            UpdateSummary();
            RebuildGrid();
            if (_sideHost != null) _sideHost.AutoScrollPosition = new Point(0, 0);
            SetBusy(false, "扫描完成 · 显示 " + snapshot.Processes.Count + " 个进程");
            if (startCheckup) StartGrowthSample(true);
        }

        private void PruneChecked()
        {
            if (_snapshot == null) return;
            HashSet<int> keep = new HashSet<int>();
            foreach (ProcInfo info in _snapshot.Processes)
                if (info.CanAct && _checked.Contains(info.Pid))
                    keep.Add(info.Pid);
            _checked.Clear();
            foreach (int pid in keep) _checked.Add(pid);
        }

        private void UpdateSummary()
        {
            if (_snapshot == null) return;
            SystemSnapshot s = _snapshot;
            _valTotal.Text = Fmt.Bytes(s.TotalPhys);
            _valUsed.Text = Fmt.Bytes(s.UsedBytes);
            _valAvail.Text = Fmt.Bytes(s.AvailPhys);
            _valStandby.Text = s.MemoryListKnown ? Fmt.Bytes(s.StandbyBytes) : "—";
            _valModified.Text = s.MemoryListKnown ? Fmt.Bytes(s.ModifiedBytes) : "—";
            _valReclaim.Text = "~" + Fmt.Bytes((ulong)Math.Max(0, s.ProcessReclaimTotal));
            _lblLastScan.Text = "上次扫描 " + s.CapturedAt.ToString("HH:mm:ss");

            ulong free = s.FreeBytes;
            if (s.MemoryListKnown && s.TotalPhys > s.UsedBytes + s.StandbyBytes + s.ModifiedBytes + free)
                free = s.TotalPhys - s.UsedBytes - s.StandbyBytes - s.ModifiedBytes;
            _bar.SetValues(s.UsedBytes, s.StandbyBytes, s.ModifiedBytes, free, s.TotalPhys);

            _lblAdvice.Text = BuildAdvice(s);
            if (s.AvailablePercent >= 30)
            {
                _lblAdvice.ForeColor = Theme.Green;
                _lblAdvice.BackColor = Theme.TealSoft;
            }
            else if (s.AvailablePercent >= 15)
            {
                _lblAdvice.ForeColor = Color.FromArgb(180, 83, 9);
                _lblAdvice.BackColor = Theme.AmberSoft;
            }
            else
            {
                _lblAdvice.ForeColor = Theme.Red;
                _lblAdvice.BackColor = Theme.RedSoft;
            }

            UpdateCheckupBanner(s);

            if (_lblAdminHint != null && Native.IsAdministrator())
                _lblAdminHint.Text = "当前已是管理员，可以清理待机缓存；其他功能不需要管理员。";
        }

        private void UpdateCheckupBanner(SystemSnapshot s)
        {
            if (_lblCheckupTitle == null || s == null) return;

            bool tight = (s.CommitLimit > 0 && s.CommitPercent >= 92) || s.AvailablePercent < 12;
            bool watch = !tight && s.AvailablePercent < 25;

            if (tight)
            {
                _lblCheckupTitle.Text = "内存偏紧，先找增长，再考虑清理";
                _lblCheckupTitle.ForeColor = Theme.Red;
                _checkupBanner.BackColor = Theme.RedSoft;
            }
            else if (watch)
            {
                _lblCheckupTitle.Text = "内存中等，建议先观察，不必急着清";
                _lblCheckupTitle.ForeColor = Color.FromArgb(180, 83, 9);
                _checkupBanner.BackColor = Theme.AmberSoft;
            }
            else
            {
                _lblCheckupTitle.Text = "内存充足，暂不需要清理";
                _lblCheckupTitle.ForeColor = Theme.Green;
                _checkupBanner.BackColor = Theme.TealSoft;
            }

            _lblCheckupDetail.Text = "可用 " + Fmt.Bytes(s.AvailPhys) + " / " + Fmt.Bytes(s.TotalPhys)
                + "（" + s.AvailablePercent.ToString("0.0") + "%）  ·  待机缓存 " + Fmt.Bytes(s.StandbyBytes)
                + " 是正常缓存  ·  后台可回收估算约 " + Fmt.Bytes((ulong)Math.Max(0, s.ProcessReclaimTotal));
        }

        private void StartCheckup()
        {
            if (_busy) return;
            if (_snapshot == null)
            {
                _checkupAfterScan = true;
                StartScan();
                return;
            }
            StartGrowthSample(true);
        }

        private void ShowBeginnerReport()
        {
            if (_snapshot == null)
            {
                MessageBox.Show(this, "还没有扫描结果，请先点“重新扫描”。", "小白解读", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ShowCheckupDialog();
        }

        private void ShowCheckupDialog()
        {
            if (_snapshot == null) return;
            using (CheckupForm form = new CheckupForm(_snapshot))
                form.ShowDialog();
            if (!IsDisposed)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            }
        }

        private static string BuildAdvice(SystemSnapshot s)
        {
            StringBuilder sb = new StringBuilder();
            if (s.CommitLimit > 0 && s.CommitPercent >= 92)
                sb.Append("提交内存已接近上限。优先检查私有提交持续增长的进程，必要时增加页面文件。\r\n\r\n");
            else if (s.AvailablePercent < 12)
                sb.Append("内存偏紧。先看“10 秒增长”找泄漏，再限制高占用且低 CPU 的后台程序。\r\n\r\n");
            else if (s.AvailablePercent < 25)
                sb.Append("内存压力中等。可以温和限制高占用后台程序，但不必追求“空闲内存越多越好”。\r\n\r\n");
            else
                sb.Append("内存充足。待机缓存和较大的工作集是正常现象，不建议为了数字好看而频繁清理。\r\n\r\n");

            sb.Append("待机缓存 " + Fmt.Bytes(s.StandbyBytes) + " 是 Windows 的文件缓存，不是故障；系统会在需要时自动回收。");
            if (s.ActionableCount > 0)
                sb.Append("\r\n\r\n当前有 " + s.ActionableCount + " 个可操作进程，逐项估算约 " + Fmt.Bytes((ulong)Math.Max(0, s.ProcessReclaimTotal)) + "。");
            return sb.ToString();
        }

        private void RebuildGrid()
        {
            if (_grid == null || _snapshot == null) return;
            _updating = true;
            try
            {
                if (_grid.CurrentRow != null && _grid.CurrentRow.Tag is ProcInfo)
                    _selectedPid = ((ProcInfo)_grid.CurrentRow.Tag).Pid;

                _grid.Rows.Clear();
                List<ProcInfo> rows = new List<ProcInfo>();
                foreach (ProcInfo info in _snapshot.Processes)
                {
                    if (info.Pid == 0) continue;
                    if (_chkOnlyReclaim != null && _chkOnlyReclaim.Checked && info.ReclaimEstimate < 20L * 1024 * 1024) continue;
                    rows.Add(info);
                }
                SortRows(rows);

                foreach (ProcInfo info in rows)
                {
                    int index = _grid.Rows.Add();
                    DataGridViewRow row = _grid.Rows[index];
                    row.Tag = info;
                    row.Cells[0].Value = _checked.Contains(info.Pid);
                    row.Cells[0].ReadOnly = !info.CanAct;
                    row.Cells[1].Value = info.Name;
                    row.Cells[2].Value = info.Pid.ToString();
                    row.Cells[3].Value = info.PriorityKnown ? Fmt.Priority(info.MemoryPriority) : "未知";
                    row.Cells[4].Value = Fmt.Bytes(info.WorkingSet);
                    row.Cells[5].Value = Fmt.Bytes(info.PrivateWorkingSet);
                    row.Cells[6].Value = info.ReclaimEstimate > 0 ? Fmt.Bytes((ulong)info.ReclaimEstimate) : "—";
                    row.Cells[7].Value = info.CpuPercent >= 0 ? Fmt.Percent(info.CpuPercent) : "—";
                    row.Cells[8].Value = info.GrowthKnown ? Fmt.SignedBytes(info.GrowthBytes) : "—";
                    row.Cells[9].Value = info.Note;

                    if (!info.CanAct) row.DefaultCellStyle.ForeColor = Color.FromArgb(148, 163, 184);
                    if (info.ReclaimEstimate >= 100L * 1024 * 1024) row.Cells[6].Style.ForeColor = Theme.Teal;
                    else if (info.ReclaimEstimate >= 20L * 1024 * 1024) row.Cells[6].Style.ForeColor = Color.FromArgb(13, 148, 136);
                    if (info.CpuPercent >= 10) row.Cells[7].Style.ForeColor = Color.FromArgb(180, 83, 9);
                    if (info.GrowthBytes >= 20L * 1024 * 1024) row.Cells[8].Style.ForeColor = Theme.Red;
                    if (_checked.Contains(info.Pid)) row.DefaultCellStyle.BackColor = Theme.TealSoft;

                    row.Cells[0].ToolTipText = info.CanAct ? "勾选后使用上方操作。" : "受保护或不可操作。";
                    row.Cells[1].ToolTipText = "进程名；系统关键进程会灰显。";
                    row.Cells[3].ToolTipText = "1 = 最低，5 = 正常。只影响回收顺序，不是硬上限。";
                    row.Cells[4].ToolTipText = "当前驻留在物理内存中的页，包含共享 DLL/文件页。";
                    row.Cells[5].ToolTipText = "只属于该进程的常驻页。";
                    row.Cells[6].ToolTipText = "估算 = 共享页 × 90% + 私有常驻 × 冷页比例。不是精确值。";
                    row.Cells[7].ToolTipText = "扫描前 0.65 秒的 CPU 占用（按全机归一化）。";
                    row.Cells[8].ToolTipText = "最近一次 10 秒采样的私有提交变化；正数不一定是泄漏。";
                }

                foreach (DataGridViewColumn column in _grid.Columns)
                    column.HeaderCell.SortGlyphDirection = SortOrder.None;
                int sortColumn = SortColumnIndex();
                if (sortColumn >= 0)
                    _grid.Columns[sortColumn].HeaderCell.SortGlyphDirection = _sortDesc ? SortOrder.Descending : SortOrder.Ascending;

                int selectIndex = -1;
                for (int i = 0; i < _grid.Rows.Count; i++)
                {
                    ProcInfo info = _grid.Rows[i].Tag as ProcInfo;
                    if (info != null && info.Pid == _selectedPid) { selectIndex = i; break; }
                }
                if (selectIndex < 0 && _grid.Rows.Count > 0) selectIndex = 0;
                if (selectIndex >= 0)
                {
                    _grid.Rows[selectIndex].Selected = true;
                    _grid.CurrentCell = _grid.Rows[selectIndex].Cells[1];
                }
                else
                {
                    ShowNoSelection();
                }
            }
            finally
            {
                _updating = false;
            }

            if (_grid.CurrentRow != null && _grid.CurrentRow.Tag is ProcInfo)
                UpdateDetails((ProcInfo)_grid.CurrentRow.Tag);
        }

        private void SortRows(List<ProcInfo> rows)
        {
            rows.Sort(delegate(ProcInfo a, ProcInfo b)
            {
                int c;
                if (_sortKey == "name") c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                else if (_sortKey == "pid") c = a.Pid.CompareTo(b.Pid);
                else if (_sortKey == "priority")
                {
                    int pa = a.PriorityKnown ? (int)a.MemoryPriority : 99;
                    int pb = b.PriorityKnown ? (int)b.MemoryPriority : 99;
                    c = pa.CompareTo(pb);
                }
                else if (_sortKey == "ws") c = a.WorkingSet.CompareTo(b.WorkingSet);
                else if (_sortKey == "private") c = a.PrivateWorkingSet.CompareTo(b.PrivateWorkingSet);
                else if (_sortKey == "cpu") c = a.CpuPercent.CompareTo(b.CpuPercent);
                else if (_sortKey == "growth")
                {
                    long ga = a.GrowthKnown ? a.GrowthBytes : long.MinValue;
                    long gb = b.GrowthKnown ? b.GrowthBytes : long.MinValue;
                    c = ga.CompareTo(gb);
                }
                else c = a.ReclaimEstimate.CompareTo(b.ReclaimEstimate);

                if (_sortDesc) c = -c;
                if (c == 0) c = a.Pid.CompareTo(b.Pid);
                return c;
            });
        }

        private int SortColumnIndex()
        {
            if (_sortKey == "name") return 1;
            if (_sortKey == "pid") return 2;
            if (_sortKey == "priority") return 3;
            if (_sortKey == "ws") return 4;
            if (_sortKey == "private") return 5;
            if (_sortKey == "reclaim") return 6;
            if (_sortKey == "cpu") return 7;
            if (_sortKey == "growth") return 8;
            return -1;
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            string key;
            if (e.ColumnIndex == 1) key = "name";
            else if (e.ColumnIndex == 2) key = "pid";
            else if (e.ColumnIndex == 3) key = "priority";
            else if (e.ColumnIndex == 4) key = "ws";
            else if (e.ColumnIndex == 5) key = "private";
            else if (e.ColumnIndex == 6) key = "reclaim";
            else if (e.ColumnIndex == 7) key = "cpu";
            else if (e.ColumnIndex == 8) key = "growth";
            else return;

            if (_sortKey == key) _sortDesc = !_sortDesc;
            else
            {
                _sortKey = key;
                _sortDesc = key != "name";
            }
            RebuildGrid();
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_updating || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            ProcInfo info = row.Tag as ProcInfo;
            if (info == null || !info.CanAct) return;
            bool selected = row.Cells[0].Value != null && Convert.ToBoolean(row.Cells[0].Value);
            if (selected) _checked.Add(info.Pid);
            else _checked.Remove(info.Pid);
            row.DefaultCellStyle.BackColor = selected ? Theme.TealSoft : (e.RowIndex % 2 == 0 ? Color.White : Color.FromArgb(248, 250, 252));
            UpdateDetails(info);
        }

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell != null && _grid.CurrentCell.ColumnIndex == 0)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            if (_updating || _grid.CurrentRow == null) return;
            ProcInfo info = _grid.CurrentRow.Tag as ProcInfo;
            if (info != null)
            {
                _selectedPid = info.Pid;
                UpdateDetails(info);
            }
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex == 0) return;
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            ProcInfo info = row.Tag as ProcInfo;
            if (info == null || !info.CanAct) return;
            bool selected = !(row.Cells[0].Value != null && Convert.ToBoolean(row.Cells[0].Value));
            row.Cells[0].Value = selected;
        }

        private void UpdateDetails(ProcInfo info)
        {
            if (info == null)
            {
                ShowNoSelection();
                return;
            }
            _lblSelectedTitle.Text = info.Name + "  ·  PID " + info.Pid;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("工作集       " + Fmt.Bytes(info.WorkingSet));
            sb.AppendLine("私有常驻     " + Fmt.Bytes(info.PrivateWorkingSet));
            sb.AppendLine("共享页       " + Fmt.Bytes(info.SharedWorkingSet));
            sb.AppendLine("私有提交     " + Fmt.Bytes(info.PrivateBytes));
            sb.AppendLine("内存优先级   " + (info.PriorityKnown ? Fmt.Priority(info.MemoryPriority) : "未知"));
            sb.AppendLine("CPU          " + (info.CpuPercent >= 0 ? Fmt.Percent(info.CpuPercent) : "未知"));
            sb.AppendLine("10 秒增长    " + (info.GrowthKnown ? Fmt.SignedBytes(info.GrowthBytes) : "未采样"));
            sb.AppendLine();
            sb.Append(info.Note);
            if (!info.CanAct) sb.Append("\r\n\r\n此进程受保护，MemLens 不会对它执行操作。");
            _lblSelectedBody.Text = sb.ToString();
        }

        private void ShowNoSelection()
        {
            _lblSelectedTitle.Text = "未选择进程";
            _lblSelectedBody.Text = "在左侧点选一行，查看工作集、私有常驻、共享页、私有提交和增长情况。";
        }

        private void SelectAllActionable()
        {
            if (_snapshot == null) return;
            foreach (ProcInfo info in _snapshot.Processes)
                if (info.CanAct) _checked.Add(info.Pid);
            RebuildGrid();
        }

        private List<ProcInfo> GetChecked()
        {
            List<ProcInfo> list = new List<ProcInfo>();
            if (_snapshot == null) return list;
            foreach (ProcInfo info in _snapshot.Processes)
                if (info.CanAct && _checked.Contains(info.Pid))
                    list.Add(info);
            return list;
        }

        private void StartGrowthSample()
        {
            StartGrowthSample(false);
        }

        private void StartGrowthSample(bool forCheckup)
        {
            if (_busy) return;
            if (_snapshot == null)
            {
                if (forCheckup) _checkupAfterScan = true;
                StartScan();
                return;
            }

            SetBusy(true, forCheckup ? "一键体检：正在做 10 秒增长采样..." : "10 秒增长采样：准备基线...");
            Thread thread = new Thread(delegate()
            {
                try
                {
                    Dictionary<int, long> before = MemScanner.CapturePrivateBytes();
                    for (int i = 1; i <= 10; i++)
                    {
                        Thread.Sleep(1000);
                        SetStatus("10 秒增长采样 " + i + "/10 ...");
                    }
                    Dictionary<int, long> after = MemScanner.CapturePrivateBytes();
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate { FinishGrowthSample(before, after, forCheckup); });
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            SetBusy(false, "增长采样失败");
                            MessageBox.Show(this, ex.Message, "增长采样失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        });
                    }
                    catch { }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void FinishGrowthSample(Dictionary<int, long> before, Dictionary<int, long> after, bool forCheckup)
        {
            int updated = 0;
            if (_snapshot != null)
            {
                foreach (ProcInfo info in _snapshot.Processes)
                {
                    long b;
                    long a;
                    if (!before.TryGetValue(info.Pid, out b)) continue;
                    if (!after.TryGetValue(info.Pid, out a)) continue;
                    long delta = a - b;
                    info.GrowthBytes = delta;
                    info.GrowthKnown = true;
                    info.GrowthSeconds = 10;
                    updated++;
                    if (delta >= 20L * 1024 * 1024 && info.CanAct)
                        info.Note = "增长明显";
                    else if (delta > 0 && info.CanAct && info.Note == "占用小")
                        info.Note = "小幅增长";
                }
            }

            _sortKey = "growth";
            _sortDesc = true;
            RebuildGrid();
            SetBusy(false, "增长采样完成 · 更新 " + updated + " 个进程；正数不一定是泄漏");
            if (forCheckup) ShowCheckupDialog();
        }

        private void TrimSelected()
        {
            List<ProcInfo> selected = GetChecked();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先在左侧勾选要处理的进程。\r\n\r\n系统进程、其他会话进程和本工具自身已保护。", "没有选中进程", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            long estimate = 0;
            foreach (ProcInfo info in selected) estimate += info.ReclaimEstimate;
            string message = "将把 " + selected.Count + " 个进程的工作集页移出物理内存。\r\n\r\n估算可回收约 " + Fmt.Bytes((ulong)Math.Max(0, estimate)) + "。\r\n\r\n程序下次访问会重新读盘，可能短暂卡顿；这不能修复泄漏，工作集也可能很快重新增长。\r\n\r\n继续？";
            if (MessageBox.Show(this, message, "确认回收工作集", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            TrimMany(selected, "回收完成");
        }

        private void TrimMany(List<ProcInfo> selected, string title)
        {
            SetBusy(true, "正在回收工作集...");
            Thread thread = new Thread(delegate()
            {
                List<TrimResult> results = new List<TrimResult>();
                try
                {
                    foreach (ProcInfo info in selected)
                    {
                        if (!info.CanAct) continue;
                        results.Add(_scanner.Trim(info));
                    }
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate { FinishTrim(results, title); });
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            SetBusy(false, "回收失败");
                            MessageBox.Show(this, ex.Message, "回收失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        });
                    }
                    catch { }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void FinishTrim(List<TrimResult> results, string title)
        {
            int ok = 0;
            int fail = 0;
            long freed = 0;
            string firstError = "";
            foreach (TrimResult result in results)
            {
                if (result.Ok) { ok++; freed += result.Freed; }
                else
                {
                    fail++;
                    if (firstError.Length == 0) firstError = result.Name + ": " + Native.Win32Text(result.Error);
                }
            }

            SetBusy(false, "回收完成 · 成功 " + ok + "，失败 " + fail);
            string message = "已处理 " + results.Count + " 个进程。\r\n\r\n成功：" + ok + "\r\n失败：" + fail + "\r\n实测工作集减少：" + Fmt.Bytes((ulong)Math.Max(0, freed));
            if (firstError.Length > 0) message += "\r\n\r\n首个错误：" + firstError;
            message += "\r\n\r\n注意：工作集可能很快重新增长；这不是修复泄漏。";
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            StartScan();
        }

        private void LimitSelected()
        {
            List<ProcInfo> selected = GetChecked();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先在左侧勾选要限制的进程。", "没有选中进程", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (LimitDialog dialog = new LimitDialog(selected.Count))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ApplyLimit(selected, dialog.Mode, dialog.TrimAfter);
            }
        }

        private void RestoreSelected()
        {
            List<ProcInfo> selected = GetChecked();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先勾选要恢复的进程。", "没有选中进程", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ApplyLimit(selected, "reset", false);
        }

        private void ApplyLimit(List<ProcInfo> selected, string mode, bool trimAfter)
        {
            SetBusy(true, "正在应用内存限制...");
            Thread thread = new Thread(delegate()
            {
                int ok = 0;
                int fail = 0;
                long freed = 0;
                string firstError = "";
                try
                {
                    foreach (ProcInfo info in selected)
                    {
                        if (!info.CanAct) continue;
                        if (mode == "low" || mode == "reset")
                        {
                            int error;
                            uint priority = mode == "low" ? 1u : 5u;
                            bool set = _scanner.SetPriority(info, priority, out error);
                            if (set) ok++;
                            else
                            {
                                fail++;
                                if (firstError.Length == 0) firstError = info.Name + ": " + Native.Win32Text(error);
                            }

                            if (set && mode == "low" && trimAfter)
                            {
                                TrimResult trim = _scanner.Trim(info);
                                if (trim.Ok) freed += trim.Freed;
                            }
                        }
                        else if (mode == "trim")
                        {
                            TrimResult trim = _scanner.Trim(info);
                            if (trim.Ok) { ok++; freed += trim.Freed; }
                            else
                            {
                                fail++;
                                if (firstError.Length == 0) firstError = info.Name + ": " + Native.Win32Text(trim.Error);
                            }
                        }
                    }

                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate { FinishLimit(ok, fail, freed, firstError, mode); });
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            SetBusy(false, "应用失败");
                            MessageBox.Show(this, ex.Message, "应用失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        });
                    }
                    catch { }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void FinishLimit(int ok, int fail, long freed, string firstError, string mode)
        {
            SetBusy(false, "操作完成 · 成功 " + ok + "，失败 " + fail);
            string action;
            if (mode == "reset") action = "恢复默认优先级（5 级）";
            else if (mode == "trim") action = "回收工作集";
            else action = "设置为最低内存优先级（1 级）";

            string message = action + "：成功 " + ok + " 个，失败 " + fail + " 个。";
            if (freed > 0) message += "\r\n本次实测工作集减少：" + Fmt.Bytes((ulong)freed) + "。";
            if (firstError.Length > 0) message += "\r\n\r\n首个错误：" + firstError;
            if (mode == "low")
                message += "\r\n\r\n这是软限制：内存优先级在进程退出/重启后失效；系统内存紧张时会优先回收它。";
            MessageBox.Show(this, message, "内存限制", MessageBoxButtons.OK, MessageBoxIcon.Information);
            StartScan();
        }

        private void PurgeStandby()
        {
            using (PurgeDialog dialog = new PurgeDialog(Native.IsAdministrator()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!Native.IsAdministrator())
                {
                    DialogResult result = MessageBox.Show(this,
                        "清理系统待机缓存需要管理员权限。\r\n\r\n是否以管理员身份重新启动 MemLens？重启后请再次点击“清理系统缓存”。",
                        "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (result == DialogResult.Yes) Elevate();
                    return;
                }

                bool knownBefore;
                ulong[] before = Native.QueryMemoryList(out knownBefore);
                int status;
                bool ok = Native.PurgeStandby(dialog.Command, out status);
                bool knownAfter;
                ulong[] after = Native.QueryMemoryList(out knownAfter);
                string message = ok ? "待机缓存已清理。" : "清理失败。";
                message += "\r\n\r\nNTSTATUS：" + Native.NtStatusText(status);
                if (knownBefore && knownAfter)
                {
                    ulong page = (ulong)Native.GetPageSize();
                    message += "\r\n待机页：" + Fmt.Bytes(SumStandby(before) * page) + " → " + Fmt.Bytes(SumStandby(after) * page);
                }
                message += "\r\n\r\n这是缓存，不是故障；程序之后可能重新读盘。";
                MessageBox.Show(this, message, "清理待机缓存", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                if (ok) StartScan();
            }
        }

        private static ulong SumStandby(ulong[] fields)
        {
            if (fields == null) return 0;
            ulong total = 0;
            for (int i = 5; i <= 12 && i < fields.Length; i++) total += fields[i];
            return total;
        }

        private void Elevate()
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath);
                start.UseShellExecute = true;
                start.Verb = "runas";
                Process.Start(start);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法以管理员身份启动：\r\n" + ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportReport()
        {
            if (_snapshot == null)
            {
                MessageBox.Show(this, "还没有扫描结果。", "导出报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string initial = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(initial)) initial = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
                dialog.FileName = "MemLens_report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                dialog.InitialDirectory = initial;
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    ReportWriter.WriteCsv(_snapshot, dialog.FileName);
                    DialogResult open = MessageBox.Show(this, "报告已导出：\r\n" + dialog.FileName + "\r\n\r\n是否打开所在文件夹？", "导出成功", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (open == DialogResult.Yes)
                        Process.Start("explorer.exe", "/select,\"" + dialog.FileName + "\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：\r\n" + ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            if (_btnScan != null) _btnScan.Enabled = !busy;
            if (_btnGrowth != null) _btnGrowth.Enabled = !busy;
            if (_btnTrim != null) _btnTrim.Enabled = !busy;
            if (_btnLimit != null) _btnLimit.Enabled = !busy;
            if (_btnRestore != null) _btnRestore.Enabled = !busy;
            if (_btnPurge != null) _btnPurge.Enabled = !busy;
            if (_btnExport != null) _btnExport.Enabled = !busy;
            if (_btnCheckup != null) _btnCheckup.Enabled = !busy;
            if (_btnExplain != null) _btnExplain.Enabled = !busy;
            if (_progress != null) _progress.Visible = busy;
            if (_status != null) _status.Text = text;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        }

        private void SetStatus(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { SetStatus(text); }); }
                catch { }
                return;
            }
            if (_status != null) _status.Text = text;
        }
    }
}
