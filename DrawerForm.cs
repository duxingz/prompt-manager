// -*- coding: utf-8 -*-
// DrawerForm.cs — 抽屉主窗体：页签(提示词/记事本/设置) / 分类chips / 搜索 / 卡片列表
// 布局（用户指定）：顶部页签 → 分类chips(全部/收藏/自建分类) → 搜索 → 列表
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PromptDrawer
{
    public class DrawerForm : Form
    {
        readonly Store _store;
        public static readonly int DrawerWidth = Program.S(384);

        // 控件
        Panel _header;
        Panel _chipsPanel;
        Panel _chips;              // 5 列等宽网格（手动定位，不再用 FlowLayoutPanel 自动流）
        MiniButton _themeBtn;
        // chips 网格常量（2026-10-07 批次：等宽圆角网格，一行上限 5 个）
        static readonly int ChipCols = 5;
        static readonly int ChipW = (DrawerWidth - Program.S(16) - Program.S(4) * 4) / 5;
        static readonly int ChipH = Program.S(30);   // 略大（此前 ~S(25)）
        static readonly int ChipGapX = Program.S(4);
        static readonly int ChipGapY = Program.S(6);
        int _chipCol, _chipRow;
        ToolTip _chipTip;
        Panel _searchPanel;
        TextBox _search;
        ListBox _list;
        Panel _listPage;
        Panel _notePage;
        ComboBox _pageCombo;
        NoteTextBox _noteEdit;
        Label _noteStatus;
        Panel _findBar;        // 记事本查找栏（Ctrl+F 呼出，Esc 关闭）
        TextBox _findBox;
        Label _findCount;
        ContextMenuStrip _noteMenu;
        float _noteFont = 10.5f;
        DateTime _lastNoteSave = DateTime.MinValue;
        DateTime _manualSaveAt = DateTime.MinValue;   // 手动存档（TXT 通道）最近一次时间
        Panel _settingsPage;
        CheckBox _cbAutostart;      // 设置页的「开机自动启动」（要与托盘菜单那一项互相同步）
        bool _syncingAutostart;     // 同步期间抑制 handler，防止两边来回触发
        Label _toast;
        Timer _toastTimer;
        Timer _copyTimer;      // 单击复制延迟（让位双击编辑）
        Timer _saveTimer;      // 记事本自动保存 800ms
        Timer _fontSaveTimer;  // 记事本字号防抖落盘
        Timer _searchTimer;    // 搜索防抖 150ms

        // 状态
        int _tab;                          // 0提示词 1记事本 2设置
        string _catId = null;              // null=全部 ""=未分类 其他=分类id
        bool _favOnly = false;
        List<Prompt> _filtered = new List<Prompt>();
        readonly HashSet<string> _selIds = new HashSet<string>();
        string _anchorId = null;
        int _hoverIndex = -1;
        Control _emptyReset;   // 空态里的「重置筛选」：只在有筛选/搜索时才显示（2026-10-18 视觉关：空操作按钮多余）
        string _pendingCopyId = null;
        bool _suspendCollapse = false;
        // 光标在不在输入框里（2026-10-10 用户点做，方案甲+乙）：
        // 判据 = **最后获得焦点的是不是输入框**（给整棵树每个控件挂 Enter：TextBox→true，其它→false）。
        // ⚠️ 不能改用 TextBox.Leave 来清 —— 点别的窗口时 WinForms 先让输入框 Leave（已执行完）
        // 再触发 OnDeactivate，靠 Leave 会在判定前就被清掉 ✗。
        bool _focusInInput = false;
        bool _editTipShown = false;             // 每个展开周期只提示一次
        bool _keepOpenWhileEditing = true;      // 设置项 edit_keep_open（方案乙：可关掉）
        bool _dark;

        public CapsuleForm Capsule;        // 主窗体互相引用（Program 接线）

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);
        const int EM_SETCUEBANNER = 0x1501;

        public DrawerForm(Store store)
        {
            _store = store;
            _dark = (string)_store.GetSetting("theme", "light") == "dark";
            _keepOpenWhileEditing = Convert.ToBoolean(_store.GetSetting("edit_keep_open", true));
            Theme.Dark = _dark;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            Text = Program.AppName;
            CapsuleForm.RoundCorners(this, 8);
            BuildUi();
            ApplyColors();
            HookEditInputs();   // 2026-10-10：给抽屉里所有输入框挂"编辑中"标记（点别处不自动收起的判据）
            SwitchTab(Convert.ToInt32(_store.GetSetting("tab", 0)), true);

            _copyTimer = new Timer { Interval = 260 };
            _copyTimer.Tick += (s, ev) =>
            {
                _copyTimer.Stop();
                if (_pendingCopyId != null)
                {
                    var p = _store.FindPrompt(_pendingCopyId);
                    if (p != null)
                    {
                        Clipboard.SetText(p.content == "" ? p.title : p.content);
                        Toast("已复制到剪贴板 ✓");
                        if (Convert.ToBoolean(_store.GetSetting("copy_collapse", false)))
                            Program.CollapseDrawer();
                    }
                }
                _pendingCopyId = null;
            };
            _searchTimer = new Timer { Interval = 150 };
            _searchTimer.Tick += (s, ev) => { _searchTimer.Stop(); _selIds.Clear(); RefreshList(); };
            _saveTimer = new Timer { Interval = 800 };
            _saveTimer.Tick += (s, ev) =>
            {
                _saveTimer.Stop();
                _store.SaveNotes();
                _lastNoteSave = DateTime.Now;
                UpdateNoteStatus();
            };
            _noteFont = Convert.ToSingle(_store.GetSetting("note_font", 10.5f));
            if (_noteFont < 8.5f || _noteFont > 15f) _noteFont = 10.5f;
            // 最近一次手动存档（跨重启保留，状态栏要显示"两条通道"各自的时间）
            try
            {
                DateTime last;
                if (DateTime.TryParse(Convert.ToString(_store.GetSetting("note_manual_save_at", "")), out last))
                    _manualSaveAt = last;
            }
            catch (Exception) { }
            _fontSaveTimer = new Timer { Interval = 1200 };
            _fontSaveTimer.Tick += (s, ev) => { _fontSaveTimer.Stop(); _store.Save(); };
        }

        // ================================================================ 构建
        // 给控件树里**每个**控件挂 Enter：输入框 → 光标算在输入框里；其它可聚焦控件（列表 / 按钮 /
        // 下拉框）→ 光标不在输入框。非焦点控件（面板 / 标签）天然不触发 Enter，所以不用判断谁能聚焦。
        void HookEditInputs() { HookOne(this); }

        void HookOne(Control root)
        {
            foreach (Control c in root.Controls)
            {
                Control cur = c;                    // 闭包各持一份（C# 5 的 for 变量共享教训）
                bool isInput = cur is TextBox;
                cur.Enter += (s, ev) => SetFocusInInput(isInput);
                HookOne(cur);
            }
        }

        void SetFocusInInput(bool inInput)
        {
            if (_focusInInput == inInput) return;
            _focusInInput = inInput;
            if (inInput && _keepOpenWhileEditing && !_editTipShown)
            {
                _editTipShown = true;
                // 状态栏放不下这句（150% 缩放下正文状态栏只有 384 逻辑宽，实测会溢出被裁）→ 用 Toast
                Toast("光标在输入框：点别处不会自动收起（按 Esc 收起）");
            }
        }

        void BuildUi()
        {
            Size = new Size(DrawerWidth, Screen.PrimaryScreen.WorkingArea.Height - Program.S(16));
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 4, area.Top + 8);

            // ---- 头部：logo + 分段页签 + 主题 + 收起 ----
            _header = new Panel { Dock = DockStyle.Top, Height = Program.S(40), Padding = new Padding(Program.S(8), Program.S(6), Program.S(8), 0) };
            var logo = new PictureBox
            {
                Size = new Size(Program.S(26), Program.S(26)),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image = Program.Logo(),   // 缓存图标：不再每次 FromFile（会漏内存 + 锁文件）
                Location = new Point(Program.S(8), Program.S(7)),
            };
            _header.Controls.Add(logo);
            var tabs = new[] { "提示词", "记事本", "设置" };
            for (int i = 0; i < 3; i++)
            {
                var b = new TabButton(tabs[i], i);
                b.Location = new Point(Program.S(42) + i * Program.S(78), Program.S(6));
                b.Size = new Size(Program.S(74), Program.S(26));
                int idx = i;   // C# 5：for 的循环变量是共享的，必须先拷一份，否则三个页签全切到 3
                b.Click += (s, ev) => SwitchTab(idx, true);
                _header.Controls.Add(b);
            }
            // ---- 右上角三按钮：主题 / 收起 / 缩到托盘 ----
            // 2026-10-11 用户报"三个按钮大小不一样、没对齐" —— 实测根因（渲染图 frozen26 量字形墨迹）：
            //   主题钮 18×20、垂直中心 29.5 ／「—」只有 1px 高 ／「×」8×9、中心 31.0
            //   ⇒ 主题钮走**图标字体**、另两个走**中文字体**，两套字形度量差一个量级 ⇒ 必然大小不一、上下不齐。
            // 修法：三个都走**图标字体 + 同一字号**（度量一致 ⇒ 天然等大且垂直对齐），
            //   并统一 宽 S(28) / 间距 4px / 右边距 14px / **按头部中线垂直居中**（不再写死 Y=S(6)）。
            // 字体缺失时三个一起退回文本（主题钮退成单字"日/夜"），**宽度仍统一 S(28)**（免得一宽一窄更难看）。
            var iconFont = Theme.IconFont(10f);
            int hdrW = Program.S(28), hdrGap = Program.S(4), hdrRight = Program.S(14);
            var themeBtn = iconFont != null
                ? new MiniButton(Theme.Dark ? Theme.IconSun : Theme.IconMoon, hdrW)
                : new MiniButton(Theme.Dark ? "日" : "夜", hdrW);
            // 2026-10-17 用户裁决：去掉「×（缩到托盘）」——用不到；「—」换成长方形自绘条。
            // 「隐藏到托盘」改由悬浮胶囊的右键菜单提供（CapsuleForm）。
            var hideBtn = new RectGlyphButton(hdrW);
            if (iconFont != null) themeBtn.Font = iconFont;
            int hdrY = (_header.Height - themeBtn.Height) / 2;      // 两个按钮同高 ⇒ 同一个 Y
            hideBtn.Location = new Point(DrawerWidth - hdrRight - hdrW, hdrY);
            themeBtn.Location = new Point(hideBtn.Left - hdrGap - hdrW, hdrY);
            themeBtn.Click += (s, ev) => ToggleTheme();
            hideBtn.Click += (s, ev) => Program.CollapseDrawer();
            _header.Controls.Add(themeBtn);
            _header.Controls.Add(hideBtn);
            _themeBtn = themeBtn;
            // 双击头部任意处收起
            _header.MouseDoubleClick += (s, ev) => Program.CollapseDrawer();
            // 头部拖动
            Point headerDrag = Point.Empty;
            _header.MouseDown += (s, ev) => { if (ev.Button == MouseButtons.Left) headerDrag = Cursor.Position; };
            _header.MouseMove += (s, ev) =>
            {
                if (headerDrag != Point.Empty && ev.Button == MouseButtons.Left)
                {
                    var d = new Point(Cursor.Position.X - headerDrag.X, Cursor.Position.Y - headerDrag.Y);
                    if (Math.Abs(d.X) + Math.Abs(d.Y) > 4)
                    {
                        Location = new Point(Location.X + Cursor.Position.X - headerDrag.X,
                                             Location.Y + Cursor.Position.Y - headerDrag.Y);
                        headerDrag = Cursor.Position;
                    }
                }
            };
            _header.MouseUp += (s, ev) => headerDrag = Point.Empty;

            // ---- 分类 chips 行 ----
            // 行高按网格行数计算（RefreshChips → FitChipsHeight）。5 列等宽：首行固定
            // 全部/收藏/[置顶分类]/未分类/＋分类，第二行起其余分类，超宽截断为名称前 4 位。
            _chipsPanel = new Panel { Dock = DockStyle.Top, Height = Program.S(40), Padding = new Padding(Program.S(8), Program.S(4), Program.S(8), 0) };
            _chips = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            _chipsPanel.Controls.Add(_chips);
            _chipTip = new ToolTip();

            // ---- 搜索行 ----
            _searchPanel = new Panel { Dock = DockStyle.Top, Height = Program.S(28), Padding = new Padding(Program.S(8), 0, Program.S(8), 0), BackColor = Theme.Canvas };
            // 搜索框：文本框本身画不了主题色边框（FixedSingle 恒为系统冷灰），
            // 这里用一个自绘边框的宿主面板包住无边框 TextBox，顺带把行高对齐成 28。
            var searchHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Name = "searchHost" };
            searchHost.Paint += (s, ev) =>
            {
                // 2026-10-24：边界走 BorderField（WCAG 1.4.11 非文本 3:1，原 BorderStrong 只有 ~1.5:1
                // 是"白底上一道虚影"）；聚焦时整框染 Accent —— 键盘焦点可见（清单规则 37），鼠标用户无感。
                bool sf = _search != null && _search.Focused;
                using (var pen = new Pen(sf ? Theme.Accent : Theme.BorderField))
                    ev.Graphics.DrawRectangle(pen, 0, 0, searchHost.Width - 1, searchHost.Height - 1);
                // 2026-10-18 修回：占位文字必须画在这里，不能用 Label —— 视觉终验抓到 Label 不透明，
                // 把搜索框白底与上下边框整条盖掉（4 张图全中）。直接画字不占底色、不碰边框。
                if (_search != null && _search.Text.Length == 0)
                    TextRenderer.DrawText(ev.Graphics, "搜索标题 / 内容 / 标签…", Theme.Font(Theme.FontBody, false),
                        new Rectangle(Program.S(9), 0, searchHost.Width - Program.S(18), searchHost.Height),
                        Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            };
            _search = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Theme.Font(Theme.FontBody, false),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
            };
            searchHost.Controls.Add(_search);
            // 2026-10-18 修（视觉关 FAIL）：原生 cue banner 用系统固定灰 —— 暗色下 #575757 落在 #2B2723
            // 上只有 2.05:1，用户明确报"暗色下搜索提示看不清"。改成自绘占位 Label：颜色走 Theme.TextMuted
            // （暗色 #A79F92 ⇒ 约 5:1），有字就藏；点它把焦点交给输入框。
            var placeholder = new Label
            {
                Name = "searchPlaceholder",
                Text = "搜索标题 / 内容 / 标签…",
                Font = Theme.Font(Theme.FontBody, false),
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Surface,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.IBeam,
            };
            searchHost.Controls.Add(placeholder);
            placeholder.BringToFront();
            placeholder.Click += (s, ev) => _search.Focus();
            searchHost.Resize += (s, ev) =>
            {
                _search.Location = new Point(Program.S(8), (searchHost.Height - _search.Height) / 2);
                _search.Width = Math.Max(Program.S(40), searchHost.Width - Program.S(16));
                placeholder.Location = new Point(Program.S(9), 0);
                placeholder.Size = new Size(Math.Max(Program.S(40), searchHost.Width - Program.S(18)), searchHost.Height);
            };
            searchHost.Click += (s, ev) => _search.Focus();
            // 2026-10-24：聚焦态要重画边框（GotFocus/LostFocus 各 invalidate 一次宿主）
            _search.GotFocus += (s, ev) => searchHost.Invalidate();
            _search.LostFocus += (s, ev) => searchHost.Invalidate();
            // 原生 cue banner 已弃用（不可调色），保持关闭
            placeholder.Visible = false;   // 2026-10-18：占位改由 searchHost.Paint 画（Label 会盖掉白底与边框）
            _search.TextChanged += (s, ev) => { placeholder.Visible = false; searchHost.Invalidate(); _searchTimer.Stop(); _searchTimer.Start(); };
            var btnHost = new Panel { Dock = DockStyle.Right, Width = Program.S(88), BackColor = Theme.Canvas };
            var newBtn = new MiniButton("＋ 新建", Program.S(80), Program.S(28)) { Primary = true };
            newBtn.Click += (s, ev) => NewPrompt();
            btnHost.Controls.Add(newBtn);
            btnHost.Resize += (s, ev) => newBtn.Location = new Point(0, (btnHost.Height - newBtn.Height) / 2);
            _searchPanel.Controls.Add(searchHost);
            _searchPanel.Controls.Add(btnHost);

            // ---- 提示词页（chips+search+list 顺序：页签在窗体顶部，分类在搜索上面） ----
            // Dock 顺序：后加的 Dock.Top 排更上面，故先加 search 再加 chips 再加 header
            _list = new SmoothListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Program.S(96),
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
            };
            _list.DrawItem += List_DrawItem;
            _list.MouseMove += List_MouseMove;
            _list.MouseLeave += (s, ev) =>
            {
                int old = _hoverIndex;
                _hoverIndex = -1;
                // 悬停变化只影响旧/新两行（蓝框/排序钮出现与消失）
                if (old >= 0 && old < _list.Items.Count) _list.Invalidate(_list.GetItemRectangle(old));
            };
            _list.MouseDown += List_MouseDown;
            _list.DoubleClick += (s, ev) =>
            {
                _copyTimer.Stop(); _pendingCopyId = null;
                EditSelected();
            };
            _list.KeyDown += List_KeyDown;
            _list.MouseUp += List_MouseUp;

            var emptyHint = BuildEmptyState();
            _listPage = new Panel { Dock = DockStyle.Fill };
            _listPage.Controls.Add(_list);
            _listPage.Controls.Add(emptyHint);
            _listPage.Controls.Add(_searchPanel);
            _listPage.Controls.Add(_chipsPanel);

            // ---- 记事本页 ----
            _notePage = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Program.S(8), Program.S(6), Program.S(8), Program.S(6)) };
            var noteTop = new Panel { Dock = DockStyle.Top, Height = Program.S(36) };
            _pageCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.Font(9.5f, false),
                Location = new Point(0, Program.S(2)),
                Size = new Size(DrawerWidth - Program.S(190), Program.S(26)),
                FlatStyle = FlatStyle.Flat,
            };
            //DropDownList 的 BackColor 在视觉样式开启时被 OS 忽略——FlatStyle.Flat 救不了
            //（探针实测收起面 253,253,253，与没设 Flat 时一样）→ 必须 OwnerDraw 自绘。
            //箭头小钮仍是系统浅色（与复选框 d / 滚动条 c 同属 native-chrome，已豁免族）。
            ThemeComboDraw(_pageCombo);
            _pageCombo.SelectedIndexChanged += (s, ev) => SwitchNotePage();
            var noteAdd = new MiniButton("＋ 新建", Program.S(64)) { Location = new Point(DrawerWidth - Program.S(182), Program.S(2)) };
            noteAdd.Click += (s, ev) =>
            {
                _store.AddNotePage();
                _store.SaveNotes();
                RefreshNotePages();
                Toast("已新建一页");
            };
            var noteDel = new MiniButton("删除", Program.S(52)) { Location = new Point(DrawerWidth - Program.S(114), Program.S(2)), Danger = true };
            noteDel.Click += (s, ev) => DeleteNotePage();
            // 「存档」= 手动存档（2026-10-08，用户要的"双存档并行"）：
            // 自动存档是 notes.json（800ms 防抖、覆盖写）；这个每点一次**新建一份独立 TXT**，永不覆盖。
            var noteSave = new MiniButton("存档", Program.S(52)) { Location = new Point(noteDel.Right + Program.S(6), Program.S(2)) };
            noteSave.Click += (s, ev) => ManualArchiveNotes("");
            noteDel.ApplyTheme();   // Danger 在构造后才置位，需重上一次色（含 hover 基色）
            // 记事本顶部行统一按**面板内宽**排版：noteTop 在带 Padding S(8) 的 _notePage 里，
            // 内宽 = DrawerWidth - S(16)，**不是 DrawerWidth**。
            // 2026-10-08 独立视觉评审逐像素抓到：新加的「存档」按钮原先按 DrawerWidth 定位 →
            // 右缘越界 15px 被面板裁掉（右边框整条消失、文字贴边）。这里改成右对齐 + 等间距，
            // 并把下拉框收窄让出位置（四个控件全部落在 552 内宽里）。
            {
                int rowW = DrawerWidth - Program.S(16);
                int gap = Program.S(8);
                int wSave = Program.S(52), wDel = Program.S(52), wAdd = Program.S(64);
                noteSave.Location = new Point(rowW - wSave - Program.S(4), Program.S(2));
                noteDel.Location = new Point(noteSave.Left - gap - wDel, Program.S(2));
                noteAdd.Location = new Point(noteDel.Left - gap - wAdd, Program.S(2));
                _pageCombo.Width = noteAdd.Left - Program.S(8);   // 下拉框吃剩下的宽度
                // ↑ 必须是 S(8)=12 物理像素：与上面 btnGap 同为 12，四段可见间隙才等距。
                //   （原先写成 S(12)=18 物理，评审量出"18/12/12"不齐 —— 单位算错了。）
            }
            // DropDownList 的 ComboBox 高度由字体决定、设 Size.Height 无效 → 按"与同排按钮垂直居中"重摆一次
            _pageCombo.Top = Program.S(2) + (noteAdd.Height - _pageCombo.Height) / 2;
            noteTop.Controls.Add(_pageCombo);
            noteTop.Controls.Add(noteAdd);
            noteTop.Controls.Add(noteDel);
            noteTop.Controls.Add(noteSave);
            _noteEdit = new NoteTextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                Font = Theme.Font(_noteFont, false),
                BorderStyle = BorderStyle.FixedSingle,
                WordWrap = Convert.ToBoolean(_store.GetSetting("note_wrap", true)),
                AllowDrop = true,
                MaxLength = 2000000,   // TextBox 默认 32767 字符，超了会静默打不进字（自查发现）
            };
            _noteEdit.ScrollBars = _noteEdit.WordWrap ? ScrollBars.Vertical : ScrollBars.Both;
            _noteEdit.CtrlWheel = d => ZoomNote(d > 0 ? 1 : -1);   // Ctrl+滚轮缩放（编辑器事实标准）
            _noteEdit.TextChanged += (s, ev) =>
            {
                if (_noteLoading) return;
                _store.SetNoteContent(_store.ActiveNoteId, _noteEdit.Text);
                _saveTimer.Stop(); _saveTimer.Start();
                UpdateNoteStatus();   // 字数即时刷新（保存仍走 800ms 防抖）
            };
            _noteEdit.DragEnter += (s, ev) =>
            {
                if (ev.Data.GetDataPresent(DataFormats.FileDrop)) ev.Effect = DragDropEffects.Copy;
            };
            _noteEdit.DragDrop += (s, ev) =>
            {
                var files = ev.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    if (files[0].ToLower().EndsWith(".txt")) ImportNoteFile(files[0]);
                    else Toast("只支持导入 .txt 文件");
                }
            };
            BuildNoteMenu();
            _noteEdit.ContextMenuStrip = _noteMenu;
            _noteStatus = new Label
            {
                Dock = DockStyle.Bottom,
                Height = Program.S(22),
                TextAlign = ContentAlignment.MiddleRight,
                Font = Theme.Font(Theme.FontCaption, false),
                ForeColor = Theme.TextMuted,
                Name = "noteStatus",
            };
            BuildNoteFindBar();
            _notePage.Controls.Add(_noteEdit);
            _notePage.Controls.Add(_findBar);    // 先于 noteTop 添加：Dock.Top 里排在工具行下方
            _notePage.Controls.Add(noteTop);
            _notePage.Controls.Add(_noteStatus);

            // ---- 设置页 ----
            _settingsPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(Program.S(8), Program.S(6), Program.S(8), Program.S(6)) };
            BuildSettings(_settingsPage);

            // ---- Toast ----
            _toast = new Label
            {
                BackColor = Theme.TooltipBg,
                ForeColor = Theme.TooltipText,
                Font = Theme.Font(9.5f, false),
                Padding = new Padding(Program.S(10), Program.S(5), Program.S(10), Program.S(5)),
                AutoSize = true,
                Visible = false,
            };
            _toastTimer = new Timer { Interval = 1600 };
            _toastTimer.Tick += (s, ev) => { _toastTimer.Stop(); _toast.Visible = false; };

            Controls.Add(_listPage);
            Controls.Add(_notePage);
            Controls.Add(_settingsPage);
            Controls.Add(_header);
            // Toast 必须挂进控件树：此前从未 Add，Toast() 设的 Visible/Location 全发生在
            // 无父控件上 → 所有操作反馈（复制/保存/删除…）永远不显示
            Controls.Add(_toast);
            _notePage.Visible = _settingsPage.Visible = false;
        }

        // 设置页布局光标（C#5 无局部函数，用类字段游标 + helper 方法）
        // ⚠️ 每个 helper 都必须把 _sy 推到"自己这个控件之下"，调用处的 `_sy += ...` 只表示**间隙**。
        //    2026-10-06 实测：SBtn/SInfo 当时不推进 _sy → 调用处写的 6px"间隙"变成了"间距"，
        //    4 个数据按钮两两重叠 22px、标题压住主题按钮 18px（共 5 处真重叠，叠在下面的按钮点不到）。
        int _sy;
        int _sx = Program.S(8);
        int _sw;
        Panel _spage;

        void SInit(Panel page)
        {
            _spage = page;
            _sy = Program.S(6);
            _sw = DrawerWidth - Program.S(16);   // 2026-10-18 修：原本 S(36) ⇒ 右边距 42px 与左侧 12px 不对称（视觉关 FAIL），改 S(16) ⇒ 左右各 12px，与列表卡片右缘对齐
            // 2026-10-18 四色配额（规范 v2.3.0）：设置页顶部放「粉心 + 橙星」品牌位（纯装饰，不是控件语义）。
            // 放页顶而不是页脚 —— 设置页是可滚动长页，页脚会掉到折叠线以下（实测渲染里看不到）。
            var brandHeart = new Label
            {
                Text = "♥", ForeColor = Theme.Heart, BackColor = Theme.Canvas,
                Font = Theme.Font(Theme.FontBody, false), AutoSize = false,
                Size = new Size(Program.S(20), Program.S(20)),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(_sx, _sy - Program.S(2)),
            };
            var brandStar = new Label
            {
                Text = "★", ForeColor = Theme.Energy, BackColor = Theme.Canvas,
                Font = Theme.Font(Theme.FontBody, false), AutoSize = false,
                Size = new Size(Program.S(20), Program.S(20)),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(_sx + Program.S(22), _sy - Program.S(2)),
            };
            // 2026-10-18：不再往 page 加 Label（视觉终验抓到它被裁成半片 + 和下面的 Paint 重影）
            _sy += Program.S(22);
            // 2026-10-18 兜底：Label 在这个面板里实测不出画面（渲染里 0 像素）⇒ 直接在页面上画两笔
            // （列表行里的 owner-draw 星是能画的，所以走 Paint 这条路可靠）
            page.Paint += (s3, ev3) =>
            {
                Graphics g3 = ev3.Graphics;
                using (var hb = new SolidBrush(Theme.Heart))
                    g3.DrawString("♥", Theme.Font(Theme.FontTitle, false), hb, new PointF(Program.S(2), Program.S(-1)));
                using (var eb = new SolidBrush(Theme.Energy))
                    g3.DrawString("★", Theme.Font(Theme.FontBody, false), eb, new PointF(Program.S(22), Program.S(3)));
            };
        }

        Label STitle(string t)
        {
            var l = new Label
            {
                Text = t,
                Location = new Point(_sx, _sy),
                Size = new Size(_sw, Program.S(20)),
                Font = Theme.Font(9.5f, true),
            };
            _spage.Controls.Add(l);
            _sy += Program.S(26);
            return l;
        }

        CheckBox SCb(string text, bool val, EventHandler on)
        {
            var c = new ThemedCheckBox
            {
                Text = text,
                Location = new Point(_sx, _sy),
                Size = new Size(_sw, Program.S(22)),
                Checked = val,
            };
            c.CheckedChanged += on;
            _spage.Controls.Add(c);
            _sy += Program.S(26);
            return c;
        }

        MiniButton SBtn(string text, int bw, EventHandler on)
        {
            var b = new MiniButton(text, bw) { Location = new Point(_sx, _sy) };
            if (on != null) b.Click += on;
            _spage.Controls.Add(b);
            _sy = b.Bottom;      // 关键：光标推到按钮之下（否则后面控件全部叠上来）
            return b;
        }

        Label SInfo(string t)
        {
            var l = new Label
            {
                Text = t,
                Location = new Point(_sx, _sy),
                Size = new Size(_sw, Program.S(20)),
                Font = Theme.Font(9f, false),
            };
            _spage.Controls.Add(l);
            _sy = l.Bottom;      // 同上
            return l;
        }

        void SGap(int h) { _sy += h; }

        void BuildSettings(Panel page)
        {
            SInit(page);
            // 2026-10-17 重排：外观开关已在头部（月亮/太阳），设置页不再重复占一整行。
            // 分组：常规 → 热键 → 数据 → 危险操作；间距只走 Theme.Space* 标度。

            STitle("常规");
            _cbAutostart = SCb("开机自动启动", Convert.ToBoolean(_store.GetSetting("autostart", true)),
                (s, ev) =>
                {
                    if (_syncingAutostart) return;          // 同步过来的变化不再回写，防递归
                    var on = ((CheckBox)s).Checked;
                    _store.SetSetting("autostart", on);
                    Autostart.Set(on);
                    _store.Save();   // SwitchTab 已不落盘（1 万条卡顿源），设置项自行立即落盘
                    Program.SyncTrayAutostart(on);          // 让托盘菜单的勾跟着变
                });
            SCb("光标在输入框时不自动收起", _keepOpenWhileEditing,
                (s, ev) =>
                {
                    _keepOpenWhileEditing = ((CheckBox)s).Checked;
                    _store.SetSetting("edit_keep_open", _keepOpenWhileEditing);
                    _store.Save();
                    Toast(_keepOpenWhileEditing ? "编辑中不再自动收起 ✓" : "已恢复：点别处就收起");
                });
            SCb("复制提示词后自动收起抽屉", Convert.ToBoolean(_store.GetSetting("copy_collapse", false)),
                (s, ev) =>
                {
                    _store.SetSetting("copy_collapse", ((CheckBox)s).Checked);
                    _store.Save();
                });
            SCb("贴边时只露出一半图标（鼠标移过滑出）", Convert.ToBoolean(_store.GetSetting("edge_half_hide", true)),
                (s, ev) =>
                {
                    var on = ((CheckBox)s).Checked;
                    _store.SetSetting("edge_half_hide", on);
                    if (Capsule != null) Capsule.SetHalfHide(on);
                    _store.Save();
                });
            SGap(Program.S(Theme.SpaceXl));

            STitle("全局热键");
            var hk = new Label
            {
                Text = HotkeyStatusText(),
                Location = new Point(_sx, _sy),
                Size = new Size(_sw - Program.S(70), Program.S(22)),
                Font = Theme.Font(Theme.FontBody, false),
                ForeColor = Theme.TextMuted,
            };
            _spage.Controls.Add(hk);
            var hkBtn = new MiniButton("修改", Program.S(60), Program.S(24))
            { Location = new Point(_sx + _sw - Program.S(60), _sy - Program.S(1)) };
            hkBtn.Click += (s, ev) => CaptureHotkey(hk);
            _spage.Controls.Add(hkBtn);
            _sy = hk.Bottom + Program.S(Theme.SpaceXl);

            STitle("数据");
            SInfo(string.Format("当前 {0:N0} 条", _store.Prompts.Count));
            SGap(Program.S(Theme.SpaceXs));
            SBtn("从 v2 / JSON 导入…", _sw, (s, ev) => ImportJson());
            SGap(Program.S(Theme.SpaceSm));
            SBtn("导出全部…", _sw, (s, ev) => ExportAll());
            SGap(Program.S(Theme.SpaceSm));
            SBtn("立即备份", _sw, (s, ev) =>
            {
                var z = _store.RollingBackup(true);   // force：按钮语义就是马上备份，不吃 24h 节流
                Toast(z != null ? "已备份" : "备份失败");
            });
            SGap(Program.S(Theme.SpaceSm));
            SBtn("打开数据文件夹", _sw, (s, ev) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Store.DataDir); }
                catch (Exception) { }
            });
            SGap(Program.S(Theme.SpaceXl));

            STitle("危险操作");
            SInfo("会移除全部提示词，建议先「立即备份」");
            SGap(Program.S(Theme.SpaceXs));
            SBtn("一键还原（清空所有提示词）", _sw, (s, ev) =>
            {
                if (MessageBox.Show(this,
                        string.Format("确认要移除全部 {0:N0} 条提示词吗？", _store.Prompts.Count),
                        "一键还原", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                    != DialogResult.Yes) return;
                if (MessageBox.Show(this, "此操作不可撤销（可先「立即备份」），确定继续吗？",
                        "一键还原", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                    != DialogResult.Yes) return;
                _store.ResetAll();
                _store.Save();
                RefreshList();
                RefreshChips();
                Toast("已清空");
            });
            var resetBtn = _spage.Controls[_spage.Controls.Count - 1] as MiniButton;
            resetBtn.Danger = true;      // 描边危险，不实心（实心会让破坏性操作成为全场最亮）
            resetBtn.ApplyTheme();
            SGap(Program.S(Theme.SpaceXl));

            // 2026-10-18 四色配额（规范 v2.3.0）：设置页也要四色齐 —— 品牌位已在 SInit 里放到页顶，
            // 这里不再重复（页脚位置会掉到折叠线以下，视觉关测不到）
            SInfo("提示词管理器V2 · 本地离线 · 小独爱心 UI");
        }

        // DropDownList ComboBox 的暗色适配：视觉样式下 OS 忽略 BackColor（FlatStyle.Flat 也无效，
        // 2026-10-06 探针实测），只能 OwnerDraw 自绘收起面板与下拉项。箭头小钮仍由系统绘制。
        internal static void ThemeComboDraw(ComboBox cb)
        {
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.ItemHeight = Program.S(19);
            cb.DrawItem += (s2, e2) =>
            {
                using (var b = new SolidBrush(cb.BackColor))
                    e2.Graphics.FillRectangle(b, e2.Bounds);
                var t = e2.Index >= 0 && e2.Index < cb.Items.Count ? cb.Items[e2.Index].ToString() : "";
                TextRenderer.DrawText(e2.Graphics, t, cb.Font, e2.Bounds, cb.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        string HotkeyLabel()
        {
            var hk = _store.GetSetting("hotkey") as Dictionary<string, object>;
            if (hk != null && hk.ContainsKey("label")) return hk["label"].ToString();
            return "Alt+P";
        }

        // 热键当前注册失败（被别的程序占用等）时，设置页必须能看出来——
        // 否则「Alt+P 不灵了」用户无从自查（交接文档 §6 点名的要求）
        string HotkeyStatusText()
        {
            var t = "全局热键：" + Program.HotkeyLabelText();
            if (Convert.ToBoolean(_store.GetSetting("hotkey_register_failed", false)))
                return t + "（未生效：可能被其他程序占用）";
            return t;
        }

        // ================================================================ 主题
        public void ToggleTheme()
        {
            var t = (string)_store.GetSetting("theme", "light") == "dark" ? "light" : "dark";
            _store.SetSetting("theme", t);
            _store.Save();
            var keep = _tab;
            Theme.Dark = (t == "dark");
            ApplyColors();
            // 设置页控件颜色随主题重建
            _settingsPage.Controls.Clear();
            BuildSettings(_settingsPage);
            ApplyColors();
            RefreshNotePages();
        }

        // 换主题：**整棵控件树**都要重上色。
        // 2026-10-06 实测的坑：老版本只手工改了十来个固定控件（+ 只扫 _header 里的 MiniButton），
        // 于是搜索行的「＋ 新建」在暗色下仍是纯白 (255,255,255)、记事本页两个按钮、
        // 列表空态提示 Label 也都留着亮色。现在统一走 ApplyControl 递归，不再一个个点名。
        public void ApplyColors()
        {
            ApplyControl(this);
            UpdateThemeButton();
            _list.Invalidate();
        }

        // 主题按钮字形随主题切换：亮色=新月（点击进夜间）、暗色=太阳（点击回日间）——
        // 图标表示"点击后去哪"。两码点均经探针实测渲染正常（E7A3 缺字形弃用）。
        void UpdateThemeButton()
        {
            if (_themeBtn == null) return;
            if (Theme.IconFont(10f) != null) _themeBtn.Text = Theme.Dark ? Theme.IconSun : Theme.IconMoon;
            else _themeBtn.Text = Theme.Dark ? "日间" : "夜间";
        }

        void ApplyControl(Control c)
        {
            if (c is TextBox || c is ComboBox)          // 输入类走 Surface
            {
                c.BackColor = Theme.Surface;
                c.ForeColor = Theme.Text;
            }
            else if (c == _toast)                       // 浮出提示用 tooltip 配色
            {
                c.BackColor = Theme.TooltipBg;
                c.ForeColor = Theme.TooltipText;
            }
            else if (c.Name == "emptyHint")             // 空态提示：底色随主题、文字用弱色
            {
                c.BackColor = Theme.Canvas;
                c.ForeColor = Theme.TextMuted;
            }
            else if (c.Name == "emptyTitle") { c.BackColor = Theme.Canvas; c.ForeColor = Theme.Text; }
            else if (c.Name == "emptyDesc") { c.BackColor = Theme.Canvas; c.ForeColor = Theme.TextMuted; }
            else if (c.Name == "emptyIcon") { c.BackColor = Theme.Canvas; }
            else if (c.Name == "searchHost") { c.BackColor = Theme.Surface; c.Invalidate(); }   // 自绘边框要重画
            else if (c.Name == "noteStatus") { c.BackColor = Theme.Canvas; c.ForeColor = Theme.TextMuted; }
            else
            {
                c.BackColor = Theme.Canvas;
                c.ForeColor = Theme.Text;
            }
            TabButton tab = c as TabButton; if (tab != null) tab.ApplyTheme();
            MiniButton mini = c as MiniButton; if (mini != null) mini.ApplyTheme();
            ChipButton chip = c as ChipButton; if (chip != null) chip.ApplyTheme(chip.Selected);
            if (c is ThemedCheckBox) c.Invalidate();     // 自绘方块要重画（原生 CheckBox 不需要）
            foreach (Control k in c.Controls) ApplyControl(k);
        }

        // ================================================================ 消息
        public const int WM_HOTKEY = 0x0312;
        public static int WakeMsg = -1;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) return;   // WM_DPICHANGED：尺寸全物理像素自管
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == 1)
            {
                Program.ToggleDrawer();
                return;
            }
            if (WakeMsg > 0 && m.Msg == WakeMsg)
            {
                Program.ShowDrawer();
                return;
            }
            base.WndProc(ref m);
        }

        // ================================================================ 页签
        // 托盘菜单改了「开机自动启动」后，把设置页那个勾同步过来
        // （只改 UI：不回写设置、不碰注册表、不触发保存 → 不会递归）
        public void SyncAutostartUI(bool on)
        {
            try
            {
                if (_cbAutostart == null || _cbAutostart.Checked == on) return;
                _syncingAutostart = true;
                _cbAutostart.Checked = on;
                _cbAutostart.Invalidate();     // 自绘复选框要重画
            }
            catch (Exception) { }
            finally { _syncingAutostart = false; }
        }

        public void SwitchTab(object tabObj, bool save = true)
        {
            _tab = tabObj is int ? (int)tabObj : 0;
            if (_tab < 0 || _tab > 2) _tab = 0;
            _listPage.Visible = _tab == 0;
            _notePage.Visible = _tab == 1;
            _settingsPage.Visible = _tab == 2;
            foreach (Control c in _header.Controls)
            {
                var tab = c as TabButton;
                if (tab != null)
                {
                    tab.Checked = tab.Index == _tab;
                    tab.ApplyTheme();
                }
            }
            if (_tab == 1) { RefreshNotePages(); _noteEdit.Focus(); }
            if (_tab == 2)
            {
                // 刷新条数显示
                foreach (Control c in _settingsPage.Controls)
                {
                var l = c as Label;
                if (l != null && l.Text.StartsWith("当前 "))
                    l.Text = string.Format("当前 {0:N0} 条", _store.Prompts.Count);
                }
                // 「开机自动启动」的勾也从 store 刷一次：托盘菜单那边可能刚改过
                SyncAutostartUI(Convert.ToBoolean(_store.GetSetting("autostart", true)));
            }
            if (save)
            {
                // 只标脏不落盘：页签是临时 UI 状态，1 万条时每次点页签整库写盘会卡顿；
                // 下一次任何自然保存（增删改/切分类）或退出 Flush 时落盘
                _store.SetSetting("tab", _tab);
            }
        }

        // ================================================================ 分类 chips
        // 5 列等宽网格：首行固定 全部/收藏/[置顶分类]/未分类/＋分类（未置顶时第 3 槽留空），
        // 第二行起其余分类（被置顶的不重复出现）；超宽截断为名称前 4 位，完整信息进悬停提示。
        public void RefreshChips()
        {
            _chips.Controls.Clear();
            _chipCol = 0; _chipRow = 0;
            AddChip("全部", () => { _catId = null; _favOnly = false; }, _catId == null && !_favOnly);
            var favChip = AddChip("收藏", () => { _catId = null; _favOnly = true; }, _favOnly);
            favChip.HeartMark = true;   // 心粉配额落点（规范：列表类 = 爱心/选中环）
            favChip.Invalidate();
            var pinnedId = PinnedCat();
            var pinned = pinnedId == "" ? null : _store.FindCategory(pinnedId);
            if (pinned != null)
            {
                var pc = pinned;
                var pcb = AddChip(FitChipText(pc), () => { _catId = pc.id; _favOnly = false; }, _catId == pc.id && !_favOnly);
                AttachCategoryMenu(pcb, pc);
                _chipTip.SetToolTip(pcb, pc.name + "（" + _store.CountInCategory(pc.id) + " 条）· 已置顶到首行");
            }
            else _chipCol++;   // 首行第 3 槽为置顶预留位：未置顶时留空，位置感清晰
            AddChip("未分类", () => { _catId = ""; _favOnly = false; }, _catId == "" && !_favOnly);
            AddChip("＋ 分类", () => NewCategory(), false);
            foreach (var c in _store.Categories)
            {
                if (pinned != null && c.id == pinned.id) continue;
                var cat = c;
                var isSub = !string.IsNullOrEmpty(c.parent_id);
                var cb = AddChip(FitChipText(cat), () =>
                {
                    _catId = cat.id;
                    _favOnly = false;
                }, _catId == cat.id && !_favOnly);
                AttachCategoryMenu(cb, cat);
                _chipTip.SetToolTip(cb, (isSub ? "└ " : "") + cat.name
                    + "（" + _store.CountInCategory(cat.id) + " 条）");
            }
            FitChipsHeight();
        }

        // chip 文字超宽时只显示名称前 4 位（图标/计数/└ 前缀一并省略，否则放不下；用户指定规则）
        string FitChipText(Category c)
        {
            string full = (c.icon == "" ? "" : c.icon + " ") + c.name;
            if (TextRenderer.MeasureText(full, Theme.Font(9.5f, false)).Width <= ChipW - Program.S(14)) return full;
            return c.name.Length > 4 ? c.name.Substring(0, 4) : c.name;
        }

        // 网格定位：等宽圆角，一行最多 5 个
        void PlaceChip(ChipButton b)
        {
            b.AutoSize = false;
            b.Size = new Size(ChipW, ChipH);
            b.Location = new Point(_chipCol * (ChipW + ChipGapX), _chipRow * (ChipH + ChipGapY));
            using (var p = RoundedRect(new Rectangle(0, 0, ChipW, ChipH), Program.S(6)))
                b.Region = new Region(p);   // 圆角长方形
            _chipCol++;
            if (_chipCol >= ChipCols) { _chipCol = 0; _chipRow++; }
        }

        string PinnedCat()
        {
            return Convert.ToString(_store.GetSetting("pinned_cat", ""));
        }

        void TogglePin(Category cat)
        {
            bool pin = PinnedCat() != cat.id;
            _store.SetSetting("pinned_cat", pin ? cat.id : "");
            _store.Save();
            RefreshChips();
            Toast(pin ? "已置顶到首行 ✓" : "已取消置顶 ✓");
        }

        // 分类 chip 的右键菜单：置顶到首行 / 左移 / 右移 / 重命名 / 删除
        void AttachCategoryMenu(ChipButton chip, Category cat)
        {
            var menu = new ContextMenuStrip();
            menu.ShowItemToolTips = true;
            bool isPinned = PinnedCat() == cat.id;
            menu.Items.Add(isPinned ? "取消置顶" : "置顶到首行", null, (s2, ev) => TogglePin(cat));
            // 左移 / 右移（2026-10-08）：分类显示顺序 = Categories 列表顺序，所以换位即持久化。
            // 只在**同级兄弟**之间换（二级分类不会被挪到别的父级下）。
            // 置顶的分类在第一行是固定槽位 → 移动它没有肉眼效果，置灰并给出原因。
            var miLeft = new ToolStripMenuItem("左移", null, (s2, ev) => MoveCategoryFlow(cat, -1));
            var miRight = new ToolStripMenuItem("右移", null, (s2, ev) => MoveCategoryFlow(cat, 1));
            miLeft.Enabled = _store.CanMoveCategory(cat.id, -1);
            miRight.Enabled = _store.CanMoveCategory(cat.id, 1);
            if (isPinned)
            {
                miLeft.Enabled = miRight.Enabled = false;
                string why = "已置顶到首行：第一行是固定槽位（全部/收藏/置顶槽/未分类/＋分类），左右移对它没有效果";
                miLeft.ToolTipText = why;
                miRight.ToolTipText = why;
            }
            menu.Items.Add(miLeft);
            menu.Items.Add(miRight);
            menu.Items.Add("重命名", null, (s2, ev) => RenameCategoryFlow(cat));
            menu.Items.Add("删除", null, (s2, ev) => DeleteCategoryFlow(cat));
            chip.ContextMenuStrip = menu;   // Button 自带右键弹出
        }

        void MoveCategoryFlow(Category cat, int dir)
        {
            if (!_store.MoveCategory(cat.id, dir)) { Toast("已经到这一组的头/尾了"); return; }
            _store.Save();
            RefreshChips();
            Toast(dir < 0 ? "已左移 ✓" : "已右移 ✓");
        }

        void NewCategory()
        {
            _suspendCollapse = true;
            var name = PromptDialog.Show(this, "新建分类", "分类名称：", "");
            _suspendCollapse = false;
            if (name == null) return;
            if (name.Trim() == "") { Toast("名称不能为空"); return; }
            var c = _store.AddCategory(name.Trim(), "", "#2478A8", "");
            if (c == null) { Toast("同级已存在同名分类"); return; }
            _store.Save();
            RefreshChips();
            RefreshList();
            Toast("已新建分类 ✓");
        }

        void RenameCategoryFlow(Category cat)
        {
            _suspendCollapse = true;
            var name = PromptDialog.Show(this, "重命名分类", "新名称：", cat.name);
            _suspendCollapse = false;
            if (name == null) return;
            if (!_store.RenameCategory(cat.id, name)) { Toast("改名失败（空名或同级重名）"); return; }
            _store.Save();
            RefreshChips();
            RefreshList();
            Toast("已重命名 ✓");
        }

        void DeleteCategoryFlow(Category cat)
        {
            _suspendCollapse = true;
            int n = _store.CountInCategory(cat.id);
            var ok = MessageBox.Show(this,
                string.Format("删除分类「{0}」？\n其中 {1} 条提示词将变为未分类。", cat.name, n),
                "删除分类", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ok == DialogResult.Yes)
            {
                _store.DeleteCategory(cat.id);
                _store.Save();
                if (_catId == cat.id) _catId = null;
                RefreshChips();
                RefreshList();
                Toast("已删除分类 ✓");
            }
            _suspendCollapse = false;
        }

        // 面板高度按网格行数计算（PlaceChip 推进 _chipCol/_chipRow，无需 PerformLayout）
        void FitChipsHeight()
        {
            int rows = _chipRow + (_chipCol > 0 ? 1 : 0);
            int need = _chipsPanel.Padding.Top + rows * (ChipH + ChipGapY) - ChipGapY + Program.S(6);
            if (need < Program.S(40)) need = Program.S(40);
            _chipsPanel.Height = need;
        }

        ChipButton AddChip(string text, Action onClick, bool selected)
        {
            var b = new ChipButton(text, selected);
            // 换分类 = 换筛选上下文 → 清空选中集。否则 Ctrl+A 后切分类再按 Delete，
            // 会连当前看不见的已选项一起删掉
            b.Click += (s, ev) => { onClick(); _selIds.Clear(); RefreshChips(); RefreshList(); };
            _chips.Controls.Add(b);
            PlaceChip(b);   // 网格定位+圆角+固定尺寸（漏了这行 9 个 chip 会全部叠在 (0,0) 默认大小）
            return b;
        }

        // ================================================================ 列表
        // 空状态：图形 + 标题 + 一句引导 + 主按钮（原来只有两行同色文字，且把用户指到 640px 外）
        Control BuildEmptyState()
        {
            var host = new Panel { Dock = DockStyle.Fill, Name = "emptyHint", BackColor = Theme.Canvas };
            var inner = new Panel { Size = new Size(Program.S(280), Program.S(212)), BackColor = Theme.Canvas };
            var icon = new PictureBox
            {
                Name = "emptyIcon",
                Size = new Size(Program.S(52), Program.S(52)),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image = Program.Logo(),
                BackColor = Theme.Canvas,
                Location = new Point((inner.Width - Program.S(52)) / 2, 0),
            };
            // 2026-10-18 四色配额（规范 v2.3.0：每个界面四色都要出现）：空态放「橙星 + 粉心」两枚装饰，
            // 一左一右衬着 logo。纯装饰、不是控件语义；**logo 仍用用户给的那张图，不改**。
            var emptyStar = new Label
            {
                Name = "emptyStar", Text = "★", ForeColor = Theme.Energy, BackColor = Theme.Canvas,
                Font = Theme.Font(Theme.FontBody, false), TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(Program.S(22), Program.S(22)),
                Location = new Point((inner.Width - Program.S(52)) / 2 - Program.S(28), Program.S(15)),
            };
            var emptyHeart = new Label
            {
                Name = "emptyHeart", Text = "♥", ForeColor = Theme.Heart, BackColor = Theme.Canvas,
                Font = Theme.Font(Theme.FontTitle, false), TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(Program.S(24), Program.S(24)),
                Location = new Point((inner.Width - Program.S(52)) / 2 + Program.S(52) + Program.S(4), Program.S(14)),
            };
            var title = new Label
            {
                Name = "emptyTitle",
                Text = "还没有提示词",
                Font = Theme.Font(Theme.FontTitle, true),
                ForeColor = Theme.Text,
                BackColor = Theme.Canvas,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(inner.Width, Program.S(24)),
                Location = new Point(0, Program.S(62)),
            };
            var desc = new Label
            {
                Name = "emptyDesc",
                Text = "新建一条，或换个分类看看",
                Font = Theme.Font(Theme.FontBody, false),
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Canvas,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(inner.Width, Program.S(22)),
                Location = new Point(0, Program.S(90)),
            };
            var add = new MiniButton("＋ 新建提示词", Program.S(148), Program.S(28))
            {
                Primary = true,
                Location = new Point((inner.Width - Program.S(148)) / 2, Program.S(124)),
            };
            add.Click += (s, ev) => NewPrompt();
            var reset = new MiniButton("重置筛选", Program.S(96), Program.S(24))
            { Location = new Point((inner.Width - Program.S(96)) / 2, Program.S(170)) };
            reset.Click += (s, ev) =>
            {
                _search.Text = "";
                _catId = null; _favOnly = false;
                RefreshChips(); RefreshList();
            };
            _emptyReset = reset;
            inner.Controls.Add(icon);
            inner.Controls.Add(emptyStar);
            inner.Controls.Add(emptyHeart);
            inner.Controls.Add(title);
            inner.Controls.Add(desc);
            inner.Controls.Add(add);
            inner.Controls.Add(reset);
            host.Controls.Add(inner);
            // 2026-10-18 四色配额兜底：Label 在这个容器里不出画面 ⇒ 直接在 host 顶角画「粉心 + 橙星」
            // （空态时列表页没有收藏星，橙位就靠这颗；粉位另有分类栏「收藏」chip 的心）
            host.Paint += (s4, ev4) =>
            {
                Graphics g4 = ev4.Graphics;
                using (var hb = new SolidBrush(Theme.Heart))
                    g4.DrawString("♥", Theme.Font(Theme.FontTitle, false), hb, new PointF(Program.S(6), Program.S(2)));
                using (var eb = new SolidBrush(Theme.Energy))
                    g4.DrawString("★", Theme.Font(Theme.FontBody, false), eb, new PointF(Program.S(28), Program.S(6)));
            };
            host.Resize += (s, ev) =>
            {
                inner.Location = new Point((host.Width - inner.Width) / 2, (host.Height - inner.Height) / 2);
            };
            return host;
        }

        public void RefreshList()
        {
            _filtered = _store.Filter(_search.Text, _catId, _favOnly);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var p in _filtered) _list.Items.Add(p);
            _list.EndUpdate();
            // 空态用「隐藏列表」而不是叠一层：两个 Dock=Fill 兄弟靠 z 序抢显示，
            // 截图上实测会被列表完全盖住（BringToFront 对 docked 兄弟不保证绘制顺序）。
            bool empty = _filtered.Count == 0;
            // 「重置筛选」只在真有筛选（分类/收藏/搜索）时才有意义；全是"全部"时它是空操作（视觉关 flag）
            if (_emptyReset != null)
                _emptyReset.Visible = _catId != null || _favOnly || _search.Text.Length > 0;
            _list.Visible = !empty;
            foreach (Control c in _listPage.Controls)
                if (c.Name == "emptyHint")
                {
                    c.Visible = empty;
                    if (empty) c.BringToFront();
                }
            _list.Invalidate();
        }

        void List_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _filtered.Count) return;
            var p = _filtered[e.Index];
            var r = new Rectangle(0, e.Bounds.Y, _list.Width, e.Bounds.Height);
            bool selected = _selIds.Contains(p.id);
            // 【直绘 + 系统级原子合成】（2026-10-11 定稿，取代"屏外行缓冲"）
            // 背景+文字+chip+悬停元素直接画到 e.Graphics；**不**走内存位图 ——
            // 实测：内存位图无论 Rgb 还是 Argb 都出不了 ClearType（亚像素彩色 19% vs 直绘 43%），
            // 字会变粗变硬（"字体和之前不一样"）。
            // 而闪烁的根源是"先填背景、再逐项 WM_DRAWITEM"之间被 DWM 插了一帧 —— 这靠
            // **DrawerForm 的 WS_EX_COMPOSITED**（整窗系统级合成）解决，**不靠牺牲绘制路径**。
            // 坐标：DrawRowContent 内部一律用 row.Y + 偏移，所以把 row.Y 置成 e.Bounds.Y 即可。
            DrawRowContent(e.Graphics, e.Index, p, selected,
                new Rectangle(0, e.Bounds.Y, _list.Width, e.Bounds.Height));
        }

        // 在**行本地坐标系**（row.Y=0）里画一整行（背景+前景+悬停元素）。
        void DrawRowContent(Graphics g, int index, Prompt p, bool selected, Rectangle row)
        {
            using (var canvasBrush = new SolidBrush(Theme.Canvas))
                g.FillRectangle(canvasBrush, row);
            using (var bg = new SolidBrush(selected ? Theme.AccentSoft : Theme.Surface))
            using (var path = RoundedRect(new Rectangle(row.X + Program.S(6), row.Y + Program.S(3), row.Width - Program.S(14), row.Height - Program.S(6)), 6))
                g.FillPath(bg, path);
            using (var pen = new Pen(selected ? Theme.Accent : Theme.Border))
                using (var path = RoundedRect(new Rectangle(row.X + Program.S(6), row.Y + Program.S(3), row.Width - Program.S(14), row.Height - Program.S(6)), 6))
                    g.DrawPath(pen, path);

            var cardX = row.X + Program.S(16);
            var cardW = row.Width - Program.S(34);
            var textX = cardX + Program.S(26);          // 星标占固定 26 列：标题/正文/chips 共用同一条左轨
            // 星标 + 标题
            var star = p.favorite ? "★" : "☆";
            // 2026-10-18 四色配额（规范 v2.3.0）：蜜橙必须"常驻"。收藏星每行都在 ⇒ 两种状态都用
            // exact Energy（#F0824C），靠字形区分（实心 ★ / 空心 ☆），不再用灰色。
            TextRenderer.DrawText(g, star, Theme.Font(11, false),
                new Point(cardX, row.Y + Program.S(8)), Theme.Energy);
            TextRenderer.DrawText(g, p.title, Theme.Font(Theme.FontTitle, true),
                new Rectangle(textX, row.Y + Program.S(6), cardW - Program.S(26) - Program.S(66), Program.S(20)),
                Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            // 预览两行
            var preview = (p.content ?? "").Replace("\r", "").Replace("\n", " ");
            if (preview.Length > 120) preview = preview.Substring(0, 120) + "…";
            TextRenderer.DrawText(g, preview, Theme.Font(Theme.FontBody, false),
                new Rectangle(textX, row.Y + Program.S(28), cardW - Program.S(26), Program.S(32)),
                Theme.TextMuted,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            // chips：分类 + 前 2 个标签
            int cx = textX;
            var catName = "未分类";
            var catColor = Color.FromArgb(120, Theme.TextMuted);
            var cat = _store.FindCategory(p.category_id);
            if (cat != null) catName = (cat.icon == "" ? "" : cat.icon + " ") + cat.name;
            if (cat != null) catColor = ColorTranslator.FromHtml(cat.color);
            cx = DrawChip(g, catName, cx, row.Y + Program.S(64), catColor, true);
            for (int i = 0; i < p.tags.Count && i < 2; i++)
                cx = DrawChip(g, "#" + p.tags[i], cx, row.Y + Program.S(64), Theme.Accent, false);

            // 悬停：蓝色圆角描边（瞬时切换，无动画；排序钮后画、叠在框内右上角）。
            if (index == _hoverIndex)
            {
                using (var pen = new Pen(Theme.Accent, 1.8f))
                    g.DrawPath(pen, RoundedRect(new Rectangle(row.X + Program.S(6), row.Y + Program.S(3), row.Width - Program.S(14), row.Height - Program.S(6)), 6));
            }

            // hover 排序按钮（↑ ⤒ ↓）
            if (index == _hoverIndex)
            {
                var zones = ArrowZones(row);
                var labels = new[] { "↑", "⤒", "↓" };
                for (int i = 0; i < 3; i++)
                {
                    using (var bg = new SolidBrush(Theme.SurfaceAlt))
                    using (var path = RoundedRect(zones[i], 4))
                        g.FillPath(bg, path);
                    TextRenderer.DrawText(g, labels[i], Theme.Font(10, false), zones[i],
                        Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        static readonly StringFormat ArrowGlyphFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        Rectangle[] ArrowZones(Rectangle row)
        {
            var y = row.Y + Program.S(6);
            // 右缘收进卡片描边内（描边在 row.Right - S(8)，最右按钮留 S(8) 内距，
            // 2026-10-11 评审抓到 ↓ 钮溢出描边并盖断右边框）
            return new[]
            {
                new Rectangle(row.Right - Program.S(84), y, Program.S(20), Program.S(20)),
                new Rectangle(row.Right - Program.S(60), y, Program.S(20), Program.S(20)),
                new Rectangle(row.Right - Program.S(36), y, Program.S(20), Program.S(20)),
            };
        }

        int DrawChip(Graphics g, string text, int x, int y, Color color, bool filled)
        {
            var size = TextRenderer.MeasureText(text, Theme.Font(Theme.FontCaption, false));
            // 三个裸像素（12/18/5）都过 S()：此前 chip 高度写死 18 物理 = 150% 下只有 12 逻辑px，
            // 比其它 UI 明显矮一截（硬规则 2 的历史遗留）
            var w = size.Width + Program.S(12);
            var rect = new Rectangle(x, y, w, Program.S(18));
            // 亮：底=Lighten 柔和粉彩、字=原分类色（不变）；暗：底=ChipBg、字=ChipText
            //（旧暗色=压暗底+原色字，分类色偏深时深底深字 ~1.5:1）。未填底(标签chip)仍走 AccentSoft。
            var bg = !filled ? Theme.AccentSoft
                   : (Theme.Dark ? Theme.ChipBg(color) : Theme.Lighten(color));
            using (var b = new SolidBrush(bg))
            using (var path = RoundedRect(rect, Program.S(Theme.RadiusSm)))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, text, Theme.Font(Theme.FontCaption, false), rect,
                filled ? Theme.ChipTextFor(bg, color) : Theme.TextOnSoft,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return x + w + Program.S(5);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // 统一对话框外观：暗色标题栏（DWM 20）+ 圆角（DWM 33）。
        // 原来只有客户区跟主题，标题栏在暗色下是一条刺眼的浅蓝横带（对比 1.87:1）。
        internal static void ApplyChrome(Form f)
        {
            IntPtr h = f.Handle;   // 强制建句柄
            try { int dark = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(h, 20, ref dark, 4); } catch (Exception) { }
            try { int pref = 2; DwmSetWindowAttribute(h, 33, ref pref, 4); } catch (Exception) { }
        }

        internal static GraphicsPath RoundedRect(Rectangle r, int rad)
        {
            var path = new GraphicsPath();
            if (rad <= 0) { path.AddRectangle(r); return path; }
            var d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        void List_MouseMove(object sender, MouseEventArgs e)
        {
            var idx = _list.IndexFromPoint(e.Location);
            if (idx == _hoverIndex) return;
            int old = _hoverIndex;
            _hoverIndex = idx;
            // 悬停变化只影响旧/新两行（蓝框/排序钮出现与消失）
            if (old >= 0 && old < _list.Items.Count) _list.Invalidate(_list.GetItemRectangle(old));
            if (idx >= 0 && idx < _list.Items.Count) _list.Invalidate(_list.GetItemRectangle(idx));
        }

        void List_MouseDown(object sender, MouseEventArgs e)
        {
            var idx = _list.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= _filtered.Count) return;
            var p = _filtered[idx];
            // 排序按钮命中（仅 hover 行）
            var rowRect = _list.GetItemRectangle(idx);
            var zones = ArrowZones(rowRect);
            if (e.Button == MouseButtons.Left && e.Y >= zones[0].Y && e.Y <= zones[0].Bottom)
            {
                if (e.X >= zones[0].X && e.X <= zones[0].Right)
                {
                    MovePrompt(p, -1); return;
                }
                if (e.X >= zones[1].X && e.X <= zones[1].Right)
                {
                    _store.PinTop(p.id);
                    AfterOrder(p.id); return;
                }
                if (e.X >= zones[2].X && e.X <= zones[2].Right)
                {
                    MovePrompt(p, 1); return;
                }
            }
            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) != 0)
            {
                if (_selIds.Contains(p.id)) _selIds.Remove(p.id);
                else { _selIds.Add(p.id); _anchorId = p.id; }
                _list.Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Shift) != 0)
            {
                if (_anchorId != null)
                {
                    var a = _filtered.FindIndex(x => x.id == _anchorId);
                    var b = idx;
                    if (a >= 0)
                    {
                        _selIds.Clear();
                        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) _selIds.Add(_filtered[i].id);
                        _list.Invalidate();
                        return;
                    }
                }
                _selIds.Add(p.id); _anchorId = p.id;
                _list.Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                // 单击=复制（260ms 让位双击编辑）
                _pendingCopyId = p.id;
                _copyTimer.Stop();
                _copyTimer.Start();
            }
        }

        void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && _selIds.Count > 0)
            {
                DeleteSelected();
                e.Handled = true;
            }
        }

        // 列表右键菜单：空态提示一直写着"右键列表"，此前根本没有这个菜单
        void List_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            var idx = _list.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= _filtered.Count) return;
            _list.SelectedIndex = idx;
            var p = _filtered[idx];
            var menu = new ContextMenuStrip();
            menu.Items.Add("编辑", null, (s2, ev) => EditPrompt(p));
            menu.Items.Add("复制内容", null, (s2, ev) =>
            {
                Clipboard.SetText(p.content == "" ? p.title : p.content);
                Toast("已复制到剪贴板 ✓");
            });
            menu.Items.Add(p.favorite ? "取消收藏" : "收藏", null, (s2, ev) =>
            {
                _store.ToggleFavorite(p.id);
                AfterOrder(p.id);
            });
            menu.Items.Add("置顶", null, (s2, ev) =>
            {
                _store.PinTop(p.id);
                AfterOrder(p.id);
            });
            // ---- 批量操作（2026-10-07）：有 Ctrl/Shift 多选就作用于那一批，否则作用于右键这一条 ----
            var targets = _selIds.Count > 0 ? new List<string>(_selIds) : new List<string> { p.id };
            string many = targets.Count > 1 ? "（" + targets.Count + " 条）" : "";
            var moveMenu = new ToolStripMenuItem("移到分类" + many);
            moveMenu.DropDownItems.Add("未分类", null, (s2, ev) => BatchMove(targets, ""));
            foreach (var c0 in _store.Categories)
            {
                var cat = c0;
                moveMenu.DropDownItems.Add((string.IsNullOrEmpty(c0.parent_id) ? "" : "└ ") + c0.name,
                    null, (s2, ev) => BatchMove(targets, cat.id));
            }
            menu.Items.Add(moveMenu);
            menu.Items.Add("收藏" + many, null, (s2, ev) => BatchFav(targets, true));
            menu.Items.Add("取消收藏" + many, null, (s2, ev) => BatchFav(targets, false));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("删除（进回收站）", null, (s2, ev) =>
            {
                _selIds.Clear();          // 只删右键这一条，不吃残留的多选
                _list.SelectedIndex = idx;
                DeleteSelected();
            });
            menu.Show(_list, e.Location);
        }

        void MovePrompt(Prompt p, int delta)
        {
            var idx = _filtered.FindIndex(x => x.id == p.id);
            var j = idx + delta;
            if (j < 0 || j >= _filtered.Count) { Toast("已到分组边缘"); return; }
            var other = _filtered[j];
            if (other.favorite != p.favorite) { Toast("已到分组边缘（收藏组在前）"); return; }
            _store.SwapOrder(p.id, other.id);
            AfterOrder(p.id);
        }

        void AfterOrder(string pid)
        {
            _store.Save();
            RefreshChips();
            RefreshList();
            Highlight(pid);
        }

        void Highlight(string pid)
        {
            var idx = _filtered.FindIndex(x => x.id == pid);
            if (idx < 0) return;
            _list.SelectedIndex = idx;
            var t = new Timer { Interval = 700 };
            t.Tick += (s, ev) => { t.Stop(); t.Dispose(); };
            t.Start();
        }

        // ================================================================ 增删改
        public void NewPrompt()
        {
            _suspendCollapse = true;
            using (var dlg = new EditDialog(_store, null))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var r = dlg.Result;
                    var p = _store.AddPrompt(r.Title, r.Content, r.Tags, r.CategoryId, r.Favorite, r.Note);
                    if (p == null) Toast(_store.LastError);
                    _store.Save();
                    RefreshChips();
                    RefreshList();
                    Toast("已新建 ✓");
                }
            }
            _suspendCollapse = false;
        }

        // 从一段文本取标题：第一行、压掉换行、最多 40 字。
        // 快速捕获与"笔记→提示词"共用；纯函数 → 可被 --uitest 直接断言（测试里绝不碰剪贴板）。
        internal static string FirstLineOf(string s)
        {
            if (s == null) return "未命名";
            string t = Crlf(s).Trim();
            int nl = t.IndexOf('\n');
            if (nl >= 0) t = t.Substring(0, nl);
            t = t.Replace("\r", "").Trim();
            if (t.Length == 0) return "未命名";
            if (t.Length > 40) t = t.Substring(0, 40) + "…";
            return t;
        }

        // 快速捕获（2026-10-07，托盘菜单调用）：把剪贴板里的文本一键收进提示词库。
        // 结果用**托盘气泡**报（抽屉可能是收起的，Toast 看不见）。
        public void CaptureFromClipboard()
        {
            string txt = "";
            try { if (Clipboard.ContainsText()) txt = Clipboard.GetText(); } catch (Exception) { }
            if (txt == null || txt.Trim().Length == 0) { Program.Balloon("剪贴板里没有文本"); return; }
            txt = txt.Trim();
            foreach (var p in _store.Prompts)
                if (p.content != null && p.content.Trim() == txt) { Program.Balloon("这条已经在库里了，没有重复添加"); return; }
            string catId = _catId == null ? "" : _catId;      // 当前选中的分类；「全部」→ 未分类
            var np = _store.AddPrompt(FirstLineOf(txt), txt, new List<string>(), catId, false, "");
            if (np == null) { Program.Balloon(_store.LastError); return; }
            _store.Save();
            RefreshChips();
            RefreshList();
            var c = _store.FindCategory(catId);
            Program.Balloon("已收进提示词 → " + (c == null ? "未分类" : c.name) + "：" + FirstLineOf(txt));
        }

        // 回收站（2026-10-07）：托盘菜单调用；关掉后把列表/分类计数刷新一遍
        public void ShowTrash()
        {
            _suspendCollapse = true;
            using (var dlg = new TrashDialog(_store)) dlg.ShowDialog(this);
            _suspendCollapse = false;
            RefreshChips();
            RefreshList();
        }

        void EditSelected()
        {
            var idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _filtered.Count) return;
            EditPrompt(_filtered[idx]);
        }

        void EditPrompt(Prompt p)
        {
            _suspendCollapse = true;
            using (var dlg = new EditDialog(_store, p))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var r = dlg.Result;
                    _store.UpdatePrompt(p.id, r.Title, r.Content, r.Tags, r.CategoryId, r.Favorite, r.Note);
                    _store.Save();
                    RefreshChips();
                    RefreshList();
                    Toast("已保存 ✓");
                }
            }
            _suspendCollapse = false;
        }

        // 笔记 → 提示词（2026-10-07）：把笔记里选中的一段带进"新建"对话框预填，
        // 顺手可以命名/选分类（所以走对话框而不是直接建）。
        void NoteToPrompt()
        {
            string sel = _noteEdit.SelectedText;
            if (sel == null || sel.Trim().Length == 0) { Toast("先在笔记里选中要存的文字"); return; }
            _suspendCollapse = true;
            using (var dlg = new EditDialog(_store, null, FirstLineOf(sel), sel.Trim()))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var r = dlg.Result;
                    var p = _store.AddPrompt(r.Title, r.Content, r.Tags, r.CategoryId, r.Favorite, r.Note);
                    if (p == null) Toast(_store.LastError);
                    _store.Save();
                    RefreshChips();
                    RefreshList();
                    Toast("已存为提示词 ✓");
                }
            }
            _suspendCollapse = false;
        }

        void BatchMove(List<string> ids, string catId)
        {
            int n = _store.MovePrompts(ids, catId);
            _store.Save();
            _selIds.Clear();
            RefreshChips();
            RefreshList();
            var c = _store.FindCategory(catId);
            Toast("已把 " + n + " 条移到「" + (c == null ? "未分类" : c.name) + "」");
        }

        void BatchFav(List<string> ids, bool fav)
        {
            int n = _store.SetFavPrompts(ids, fav);
            _store.Save();
            _selIds.Clear();
            RefreshChips();
            RefreshList();
            Toast((fav ? "已收藏 " : "已取消收藏 ") + n + " 条");
        }

        void DeleteSelected()
        {
            var ids = new List<string>(_selIds);
            if (ids.Count == 0)
            {
                if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _filtered.Count)
                    ids.Add(_filtered[_list.SelectedIndex].id);
            }
            if (ids.Count == 0) return;
            var preview = "";
            foreach (var id in ids)
            {
                var p = _store.FindPrompt(id);
                if (p != null) preview += "、" + p.title;
            }
            if (preview.StartsWith("、")) preview = preview.Substring(1);
            if (preview.Length > 60) preview = preview.Substring(0, 60) + "…";
            _suspendCollapse = true;
            var ok = MessageBox.Show(this,
                string.Format("确定删除选中的 {0} 条提示词吗？\n\n{1}\n\n（会先进回收站，可在托盘右键 →「回收站」恢复）", ids.Count, preview),
                "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            _suspendCollapse = false;
            if (ok != DialogResult.Yes) return;
            _store.DeletePrompts(ids);
            _selIds.Clear();
            _store.Save();
            RefreshChips();
            RefreshList();
            Toast("已移入回收站 " + ids.Count + " 条");
        }

        void ImportJson()
        {
            _suspendCollapse = true;
            using (var dlg = new OpenFileDialog { Filter = "JSON 文件|*.json", Title = "导入提示词" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    // 交给 Store：直接把目标文件复制过来会覆盖 → 用合并语义：读入后按标题去重追加
                    var n = _store.MergeFromFile(dlg.FileName);
                    _store.Save();
                    RefreshChips();
                    RefreshList();
                    Toast(n >= 0 ? "导入 " + n + " 条 ✓" : "导入失败（文件格式不对）");
                }
            }
            _suspendCollapse = false;
        }

        void ExportAll()
        {
            _suspendCollapse = true;
            using (var dlg = new SaveFileDialog
            {
                Filter = "JSON 文件|*.json",
                FileName = "prompts_export.json",
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _store.ExportJson(dlg.FileName);
                    Toast("已导出 ✓");
                }
            }
            _suspendCollapse = false;
        }

        // ================================================================ 记事本
        bool _noteLoading = false;

        public void RefreshNotePages()
        {
            _noteLoading = true;
            _pageCombo.Items.Clear();
            foreach (var p in _store.NotePages)
                _pageCombo.Items.Add(_store.NoteTitle(p));
            var idx = _store.NotePages.FindIndex(p => p.id == _store.ActiveNoteId);
            _pageCombo.SelectedIndex = Math.Max(0, idx);
            _noteEdit.Text = Crlf(_store.ActiveNote.content);
            _noteLoading = false;
            _lastNoteSave = DateTime.Now;
            // 光标移到末尾并取消选区：WinForms 设 Text 后会全选全文，此时敲第一个键会整段覆盖笔记
            _noteEdit.SelectionStart = _noteEdit.TextLength;
            _noteEdit.SelectionLength = 0;
            UpdateNoteStatus();
        }

        void SwitchNotePage()
        {
            if (_noteLoading) return;
            if (_pageCombo.SelectedIndex < 0) return;
            _store.ActiveNoteId = _store.NotePages[_pageCombo.SelectedIndex].id;
            _store.NotesDirty = true;
            _store.SaveNotes();
            _lastNoteSave = DateTime.Now;
            _noteLoading = true;
            _noteEdit.Text = Crlf(_store.ActiveNote.content);
            _noteLoading = false;
            _noteEdit.SelectionStart = _noteEdit.TextLength;
            _noteEdit.SelectionLength = 0;
            UpdateNoteStatus();
        }

        void DeleteNotePage()
        {
            if (_store.NotePages.Count <= 1) { Toast("至少保留一页"); return; }
            var title = _store.NoteTitle(_store.ActiveNote);
            if (MessageBox.Show(this,
                "确定删除「" + title + "」吗？\n\n（删除前会自动存一份 TXT 存档；删掉后也能在 托盘右键 →「回收站」里一键恢复）",
                "删除页面", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            ManualArchiveNotes("已删-");                    // 甲：删前自动保险（独立 TXT）
            _store.DeleteNotePage(_store.ActiveNoteId);    // 乙：进笔记页回收站
            _store.SaveNotes();
            RefreshNotePages();
            Toast("已移入回收站（托盘右键 → 回收站 可恢复）");
        }

        // Win32 EDIT 只认 CRLF：JSON/导入来的裸 \n 在 TextBox 里不换行（整段挤成一行，
        // frozen9 评审抓到）。统一归一为 \r\n 再进 TextBox。
        internal static string Crlf(string s)
        {
            return (s ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        // 手动存档（2026-10-08）：写一份全新的独立 TXT；文件里带上"双通道"状态可见信息。
        // prefix 由"删页前的自动保险"传 "已删-"。
        void ManualArchiveNotes(string prefix)
        {
            var path = _store.ArchiveNotes(prefix);
            if (path == null) { Toast("存档失败（磁盘不可写？）"); return; }
            _manualSaveAt = DateTime.Now;
            _store.SetSetting("note_manual_save_at", _manualSaveAt.ToString("yyyy-MM-dd HH:mm:ss"));
            _store.Save();
            UpdateNoteStatus();
            Toast((prefix == "" ? "已存档" : "已在删除前自动存档") + "：" + Path.GetFileName(path));
        }

        void UpdateNoteStatus()
        {
            var idx = _store.NotePages.FindIndex(p => p.id == _store.ActiveNoteId) + 1;
            string txt = _noteEdit.Text;
            int lines = 1;
            foreach (char ch in txt) if (ch == '\n') lines++;
            // 一行只留两个语义段：位置/规模 + 保存状态（原来 5 段同色同重，读不出重点）
            string saved = _manualSaveAt == DateTime.MinValue
                ? "已自动保存 " + _lastNoteSave.ToString("HH:mm:ss")
                : "已保存 " + _manualSaveAt.ToString("HH:mm:ss");
            _noteStatus.Text = string.Format("第 {0}/{1} 页 · {2:N0} 字 · {3} 行   {4}",
                idx, _store.NotePages.Count, (long)txt.Length, lines, saved);
        }

        // ============================== 记事本查找栏（Ctrl+F / F3 / Esc）==============================
        void BuildNoteFindBar()
        {
            _findBar = new Panel { Dock = DockStyle.Top, Height = Program.S(34), Visible = false };
            _findBox = new TextBox
            {
                Font = Theme.Font(9.5f, false),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Location = new Point(Program.S(6), Program.S(5)),
                Size = new Size(DrawerWidth - Program.S(242), Program.S(24)),
            };
            _findBox.TextChanged += (s, ev) => UpdateFindCount();
            _findBox.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Enter) { DoFind(true); ev.SuppressKeyPress = true; }
                else if (ev.KeyCode == Keys.Escape) { CloseFind(); ev.SuppressKeyPress = true; }
            };
            var up = new MiniButton("↑", Program.S(30)) { Location = new Point(DrawerWidth - Program.S(230), Program.S(3)) };
            up.Click += (s, ev) => DoFind(false);
            var down = new MiniButton("↓", Program.S(30)) { Location = new Point(DrawerWidth - Program.S(196), Program.S(3)) };
            down.Click += (s, ev) => DoFind(true);
            _findCount = new Label
            {
                Text = "0/0",
                Font = Theme.Font(8.5f, false),
                Location = new Point(DrawerWidth - Program.S(162), Program.S(9)),
                Size = new Size(Program.S(104), Program.S(18)),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var close = new MiniButton("关闭", Program.S(48)) { Location = new Point(DrawerWidth - Program.S(52), Program.S(3)) };
            close.Click += (s, ev) => CloseFind();
            _findBar.Controls.Add(_findBox);
            _findBar.Controls.Add(up);
            _findBar.Controls.Add(down);
            _findBar.Controls.Add(_findCount);
            _findBar.Controls.Add(close);
        }

        public void ShowFindBar()
        {
            _findBar.Visible = true;
            _findBox.Focus();
            _findBox.SelectAll();
        }

        void CloseFind()
        {
            _findBar.Visible = false;
            _noteEdit.Focus();
        }

        // 不区分大小写收集全部匹配位置（Occurrences 计数 + 上/下一个定位共用）
        System.Collections.Generic.List<int> FindAll(string hay, string needle)
        {
            var r = new System.Collections.Generic.List<int>();
            if (needle == "") return r;
            int i = 0;
            while (true)
            {
                int p = hay.IndexOf(needle, i, StringComparison.OrdinalIgnoreCase);
                if (p < 0) break;
                r.Add(p);
                i = p + needle.Length;
            }
            return r;
        }

        void DoFind(bool down)
        {
            var ps = FindAll(_noteEdit.Text, _findBox.Text);
            if (ps.Count == 0) { _findCount.Text = "无匹配"; return; }
            int cur = _noteEdit.SelectionStart;
            int pick;
            if (down)
            {
                int from = cur + _noteEdit.SelectionLength;   // 当前选中即匹配时跳到它之后
                pick = -1;
                foreach (int p in ps) if (p >= from) { pick = p; break; }
                if (pick < 0) pick = ps[0];                   // 循环回首处
            }
            else
            {
                pick = -1;
                for (int i = ps.Count - 1; i >= 0; i--) if (ps[i] < cur) { pick = ps[i]; break; }
                if (pick < 0) pick = ps[ps.Count - 1];        // 循环回末处
            }
            int ord = 1;
            for (int i = 0; i < ps.Count; i++) if (ps[i] == pick) { ord = i + 1; break; }
            _noteEdit.Focus();
            _noteEdit.Select(pick, _findBox.Text.Length);
            _noteEdit.ScrollToCaret();
            _findCount.Text = ord + "/" + ps.Count;
        }

        void UpdateFindCount()
        {
            int n = FindAll(_noteEdit.Text, _findBox.Text).Count;
            _findCount.Text = n == 0 ? (_findBox.Text == "" ? "0/0" : "无匹配") : n + " 处";
        }

        // ============================== 记事本右键菜单 ==============================
        void BuildNoteMenu()
        {
            _noteMenu = new ContextMenuStrip();
            _noteMenu.Items.Add("撤销", null, (s, ev) => _noteEdit.Undo());
            _noteMenu.Items.Add(new ToolStripSeparator());
            _noteMenu.Items.Add("剪切", null, (s, ev) => _noteEdit.Cut());
            _noteMenu.Items.Add("复制", null, (s, ev) => _noteEdit.Copy());
            _noteMenu.Items.Add("粘贴", null, (s, ev) => _noteEdit.Paste());
            _noteMenu.Items.Add("全选", null, (s, ev) => _noteEdit.SelectAll());
            _noteMenu.Items.Add(new ToolStripSeparator());
            var wrap = new ToolStripMenuItem("自动换行") { CheckOnClick = true, Checked = _noteEdit.WordWrap };
            wrap.Click += (s, ev) => ToggleWordWrap();
            _noteMenu.Items.Add(wrap);
            _noteMenu.Items.Add("插入时间戳（F5）", null, (s, ev) => InsertTimestamp());
            _noteMenu.Items.Add("查找…（Ctrl+F）", null, (s, ev) => ShowFindBar());
            // 笔记 → 提示词（2026-10-07）：选中一段直接存成提示词（走编辑对话框，可顺手命名/归类）
            _noteMenu.Items.Add("把选中的一段存为提示词…", null, (s, ev) => NoteToPrompt());
            _noteMenu.Items.Add(new ToolStripSeparator());
            _noteMenu.Items.Add("导出本页为 TXT…", null, (s, ev) => ExportNote());
            _noteMenu.Items.Add("导入 TXT 为新页…", null, (s, ev) => ImportNote());
            _noteMenu.Items.Add(new ToolStripSeparator());
            _noteMenu.Items.Add("字号 +（Ctrl+滚轮）", null, (s, ev) => ZoomNote(1));
            _noteMenu.Items.Add("字号 −", null, (s, ev) => ZoomNote(-1));
            _noteMenu.Opening += (s, ev) =>
            {
                wrap.Checked = _noteEdit.WordWrap;
                foreach (ToolStripItem it in _noteMenu.Items)
                    if (it.Text == "撤销") it.Enabled = _noteEdit.CanUndo;
                    else if (it.Text == "剪切" || it.Text == "复制"
                             || it.Text == "把选中的一段存为提示词…") it.Enabled = _noteEdit.SelectionLength > 0;
            };
        }

        void ToggleWordWrap()
        {
            _noteEdit.WordWrap = !_noteEdit.WordWrap;
            _noteEdit.ScrollBars = _noteEdit.WordWrap ? ScrollBars.Vertical : ScrollBars.Both;
            _store.SetSetting("note_wrap", _noteEdit.WordWrap);
            _store.Save();
        }

        void InsertTimestamp()
        {
            _noteEdit.SelectedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        void ZoomNote(int dir)
        {
            _noteFont = dir == 0 ? 10.5f
                : Math.Max(8.5f, Math.Min(15f, _noteFont + dir * 0.5f));
            _noteEdit.Font = Theme.Font(_noteFont, false);
            _store.SetSetting("note_font", _noteFont);
            _fontSaveTimer.Stop();
            _fontSaveTimer.Start();   // 防抖落盘（滚轮连击时不每格写盘）
        }

        static string SanitizeFileName(string s)
        {
            foreach (char bad in new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })
                s = s.Replace(bad, '_');
            return s;
        }

        void ExportNote()
        {
            _suspendCollapse = true;
            using (var dlg = new SaveFileDialog
            {
                Filter = "文本文件|*.txt",
                FileName = SanitizeFileName(_store.NoteTitle(_store.ActiveNote)) + ".txt",
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(dlg.FileName, _noteEdit.Text, new System.Text.UTF8Encoding(true));
                    Toast("已导出 ✓");
                }
            }
            _suspendCollapse = false;
        }

        void ImportNote()
        {
            _suspendCollapse = true;
            using (var dlg = new OpenFileDialog { Filter = "文本文件|*.txt" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _suspendCollapse = false;
                    ImportNoteFile(dlg.FileName);
                    return;
                }
            }
            _suspendCollapse = false;
        }

        void ImportNoteFile(string path)
        {
            string text;
            try { text = File.ReadAllText(path, new System.Text.UTF8Encoding(false, true)); }   // UTF-8 严格
            catch (Exception) { text = File.ReadAllText(path, System.Text.Encoding.GetEncoding("GB18030")); }   // 回退 GBK 系
            text = Crlf(text);   // 裸 \n 的 txt（Unix 系导出）也必须正常换行
            _store.AddNotePage();
            _store.SetNoteContent(_store.ActiveNoteId, text);
            _store.SaveNotes();
            RefreshNotePages();
            Toast("已导入为新页 ✓");
        }

        // ================================================================ 热键
        void CaptureHotkey(Label lbl)
        {
            _suspendCollapse = true;
            using (var dlg = new HotkeyDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Vk != 0)
                {
                    var ok = Program.RegisterDrawerHotkey((uint)dlg.Mods, (uint)dlg.Vk, dlg.Label, true);
                    if (ok) Toast("热键已设为 " + dlg.Label);
                    else
                    {
                        Toast("热键注册失败（可能被占用）");
                        Program.RestoreHotkey();
                    }
                    lbl.Text = HotkeyStatusText();
                }
            }
            _suspendCollapse = false;
        }

        // ================================================================ Toast
        public void Toast(string text)
        {
            if (text == "") return;
            _toast.Text = text;
            _toast.Location = new Point((Width - _toast.PreferredWidth) / 2, Height - Program.S(56));
            _toast.Visible = true;
            _toast.BringToFront();
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        // ================================================================ 收起
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Size = new Size(DrawerWidth, Screen.PrimaryScreen.WorkingArea.Height - Program.S(16));
        }

        // 显示即核对真实置顶位（同 CapsuleForm——运行中被环境剥掉的 WS_EX_TOPMOST 不会自愈）
        // 【抗闪烁·系统级原子合成】2026-10-11 定稿：把 WS_EX_COMPOSITED 加在**顶层窗体**上
        // （**绝不能**加在 ListBox 这种子控件上 —— 实测 CreateWindowEx 直接失败、
        //  CreateHandle 抛 Win32Exception、整个 Show() 崩，还会弹关不掉的异常框）。
        // 作用：整个窗体（含所有子控件绘制）先合成、再一次性上屏 ⇒ "填背景 → 逐项 WM_DRAWITEM"
        // 之间不再被 DWM 插帧；而绘制路径仍是**直绘** ⇒ 文字保住 ClearType ✓
        // （屏外行缓冲虽也能原子上屏，但它出不了 ClearType，已实测排除）。
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED
                return cp;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Program.EnsureTopMost(this);
            // 收起即复位：下次展开时按光标实际位置重新判定（不会"粘住"）
            else
            {
                _focusInInput = false; _editTipShown = false;
                _hoverIndex = -1;
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // 点抽屉外（含桌面/其他应用）自动收起；胶囊上点击与自有对话框豁免
            if (_suspendCollapse) return;
            if (Program.MouseInCapsule) return;
            // 光标在输入框里就不自动收起（2026-10-10）：点别的窗口不会丢光标位置、滚动位置、
            // 正在看第几页。光标不在输入框（点了列表/按钮/别处）→ 照旧自动收起。
            // 出口 = Esc（ProcessCmdKey）/ 点胶囊 / 托盘。
            if (_focusInInput && _keepOpenWhileEditing) return;
            Program.CollapseDrawer();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _copyTimer.Stop(); _searchTimer.Stop(); _saveTimer.Stop(); _toastTimer.Stop();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_tab == 1)   // 记事本页快捷键（2026-10-07 批次：记事本完善）
            {
                if (keyData == (Keys.Control | Keys.F)) { ShowFindBar(); return true; }
                if (keyData == (Keys.Control | Keys.S))
                {
                    _store.SaveNotes();
                    _lastNoteSave = DateTime.Now;
                    UpdateNoteStatus();
                    Toast("已保存 ✓");
                    return true;
                }
                if (keyData == Keys.F5) { InsertTimestamp(); return true; }
                if (keyData == Keys.F3 && _findBar.Visible) { DoFind((ModifierKeys & Keys.Shift) == 0); return true; }
                if (keyData == (Keys.Control | Keys.D0)) { ZoomNote(0); return true; }   // Ctrl+0 恢复默认字号
                if (keyData == Keys.Escape && _findBar.Visible) { CloseFind(); return true; }
            }
            if (keyData == Keys.Escape)
            {
                if (_selIds.Count > 0) { _selIds.Clear(); _list.Invalidate(); }
                else Program.CollapseDrawer();
                return true;
            }
            if (keyData == (Keys.Control | Keys.A) && _tab == 0)
            {
                foreach (var p in _filtered) _selIds.Add(p.id);
                _list.Invalidate();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // ====================================================================
    // 小控件：分段页签 / 迷你按钮 / chip / 热键捕获 / 编辑对话框
    // ====================================================================
    public class TabButton : Button
    {
        public readonly int Index;
        public bool Checked;
        bool _hot;

        public TabButton(string text, int index)
        {
            Text = text;
            Index = index;
            Font = Theme.Font(Theme.FontBody, true);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Canvas;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }      // 焦点环重画
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        // 自绘胶囊：不用窗口 Region —— Region 只影响窗口合成，WM_PRINT/DrawToBitmap 抓不到，
        // 截图里会退化成方块（2026-10-17 独立评审实测）。画成路径后任何截取方式都一致。
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentBg = Parent != null ? Parent.BackColor : Theme.Canvas;
            using (var bg = new SolidBrush(parentBg)) g.FillRectangle(bg, ClientRectangle);
            using (var path = DrawerForm.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
            {
                Color fill = Checked ? Theme.AccentSoft : (_hot ? Theme.Sunken : parentBg);
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (Checked)
                {
                    using (var pen = new Pen(Theme.Accent, 1f)) g.DrawPath(pen, path);
                }
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle,
                Checked ? Theme.TextOnSoft : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    public class MiniButton : Button
    {
        public bool Danger;      // 危险操作（描边红，不实心）
        public bool Primary;     // 主操作（一屏只该有一个）
        Color _bg, _fg, _bd;
        bool _hot, _down;

        public MiniButton(string text, int w) : this(text, w, Program.S(28)) { }

        public MiniButton(string text, int w, int h)
        {
            Text = text;
            Size = new Size(w, h);
            // 2026-10-24：恢复默认 TabStop=true（原 false）——按钮进 Tab 链（清单规则 37），
            // 焦点环只在键盘聚焦时画，鼠标用户视觉零变化。
            Font = Theme.Font(Theme.FontBody, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            if (Danger) { _bg = Theme.Surface; _fg = Theme.DangerTextSoft; _bd = Theme.Danger; }
            else if (Primary) { _bg = Theme.Accent; _fg = Theme.AccentText; _bd = Theme.Accent; }
            else { _bg = Theme.Surface; _fg = Theme.Text; _bd = Theme.BorderStrong; }
            BackColor = _bg;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }      // 焦点环重画
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentBg = Parent != null ? Parent.BackColor : Theme.Canvas;
            using (var fillBg = new SolidBrush(parentBg)) g.FillRectangle(fillBg, ClientRectangle);

            Color fill = _bg, border = _bd, text = _fg;
            if (!Enabled) { fill = Theme.Sunken; text = Theme.TextMuted; border = Theme.Border; }
            else if (_down) fill = Theme.Mix(_bg, Color.Black, Theme.Dark ? 0.16 : 0.10);
            else if (_hot) fill = (_bg == Theme.Surface) ? Theme.SurfaceAlt : Theme.Mix(_bg, _fg, 0.10);

            int r = Math.Min(Height / 2, Program.S(Theme.RadiusMd));
            using (var path = DrawerForm.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), r))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                // 2026-10-24 键盘焦点环（规则 37）：实心主按钮用 AccentPressed（Accent 环贴 Accent 底隐形，
                // 与套件 Qt 同款处理）；环宽 1.8f 与悬停描边同族，只在 Focused 时画。
                if (Focused && Enabled)
                    using (var pen = new Pen(Primary ? Theme.AccentPressed : Theme.Accent, 1.8f))
                        g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // 收起按钮：图标不是字形，而是自绘的「一枚小长方条」（12×4 圆角 2）。
    public class RectGlyphButton : MiniButton
    {
        bool _hot2;
        public RectGlyphButton(int w) : base("", w) { TabStop = false; }
        protected override void OnMouseEnter(EventArgs e) { _hot2 = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot2 = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int bw = Program.S(12), bh = Program.S(4);
            var r = new Rectangle((Width - bw) / 2, (Height - bh) / 2, bw, bh);
            using (var path = DrawerForm.RoundedRect(r, Program.S(2)))
            using (var b = new SolidBrush(_hot2 ? Theme.Accent : Theme.Text))
                e.Graphics.FillPath(b, path);
        }
    }

    public class ChipButton : Button
    {
        public bool Selected;      // 记住选中态：换主题时要按原状态重上色（否则 chip 会停留在旧主题的配色）
        public ChipButton(string text, bool selected)
        {
            Text = text;
            FlatStyle = FlatStyle.Flat;
            // 2026-10-24：恢复默认 TabStop=true（原 false）——进 Tab 链（清单规则 37），
            // 焦点环只在键盘聚焦时画，鼠标用户视觉零变化。
            Font = Theme.Font(9.5f, selected);   // 略大；尺寸由网格 PlaceChip 固定（不再 AutoSize）
            ApplyTheme(selected);
        }
        public void ApplyTheme(bool selected)
        {
            Selected = selected;
            // 渲染器的矩形边框归零：直角框会被圆角 Region 裁得只剩四条直边（用户报的显示问题）。
            // 蓝色选中框改由 OnPaint 沿圆角路径自己描。
            FlatAppearance.BorderSize = 0;
            BackColor = selected ? Theme.AccentSoft : Theme.Surface;
            ForeColor = selected ? Theme.TextOnSoft : Theme.TextMuted;
            if (IsHandleCreated) Invalidate();
        }
        // 选中：Accent 圆角描边；悬停：淡描边（反馈）；其余不描
        public bool HeartMark;   // 「收藏」chip：右上角画一颗心粉小爱心（规范 color_roles/heart：粉只进爱心形状）
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var path = DrawerForm.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Program.S(6)))
            {
                if (Selected)
                {
                    using (var pen = new Pen(Theme.Accent, 1.8f))
                        e.Graphics.DrawPath(pen, path);
                }
                else if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
                {
                    using (var pen = new Pen(Theme.BorderStrong, 1f))
                        e.Graphics.DrawPath(pen, path);
                }
                else
                {
                    // 未选中也给一圈极淡描边：只靠底色平铺时，可点与不可点在暗色下几乎一样
                    using (var pen = new Pen(Theme.Border, 1f))
                        e.Graphics.DrawPath(pen, path);
                }
                // 2026-10-24 键盘焦点环（规则 37）：选中态已有 Accent 描边，聚焦时加粗一档区分
                if (Focused)
                    using (var pen = new Pen(Theme.Accent, Selected ? 3f : 1.8f))
                        e.Graphics.DrawPath(pen, path);
            }
            if (HeartMark)
            {
                // 11px 小爱心：面积远小于 1% 配额；位置在右上角，不压 chip 文字
                var hr = new Rectangle(Width - Program.S(14), Program.S(3), Program.S(11), Program.S(12));
                TextRenderer.DrawText(e.Graphics, "♥", Theme.Font(9f, false), hr, Theme.Heart,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
        protected override bool ShowFocusCues { get { return false; } }   // 关掉矩形虚线焦点框（同款直角问题；自绘环见 OnPaint）
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }      // 焦点环重画
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // 自绘复选框：OS 原生方块在暗色下是近纯白 (243,243,243)、勾选是系统蓝 (0,95,184)，
    // 与亮/暗强调色（#2478A8 / #4E9CCB）都不一致。这里自己画 16×16 圆角方块（Accent 填充 + 白勾），
    // 文字用 Theme.Text；整行可点（与原生 CheckBox 的命中范围一致）。
    public class ThemedCheckBox : CheckBox
    {
        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }      // 焦点环重画
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int box = Program.S(16);
            int left = Program.S(2);
            var r = new Rectangle(left, (Height - box) / 2, box, box);
            using (GraphicsPath path = DrawerForm.RoundedRect(r, Program.S(4)))
            {
                if (Checked)
                {
                    using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                    // 勾线颜色 = Theme.AccentText（**故意**不写死白色）：亮色下是白（4.4:1），
                    // 暗色下是深色 #10222E（5.5:1）——它本来就是"强调色之上"的语义色。
                    // 2026-10-07 独立视觉评审曾把暗色深勾判为 bug，复核后确认是**评审规格写错**：
                    // 若强行固定白勾，暗色强调色 #4E9CCB 上的对比只有约 2.3:1，反而更差。
                    using (var p = new Pen(Theme.AccentText, Program.S(2)))
                        g.DrawLines(p, new PointF[]
                        {
                            new PointF(r.Left + r.Width * 0.24f, r.Top + r.Height * 0.52f),
                            new PointF(r.Left + r.Width * 0.43f, r.Top + r.Height * 0.72f),
                            new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
                        });
                }
                else
                {
                    using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                    using (var p = new Pen(Theme.BorderField, 1f)) g.DrawPath(p, path);   // 2026-10-24：勾选框边界同样走 3:1 档
                }
            }
            // 2026-10-24 键盘焦点环（规则 37）：环绕指示器+文字整块，只在 Focused 时画
            if (Focused)
                using (var p = new Pen(Theme.Accent, 1.8f))
                    g.DrawPath(p, DrawerForm.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Program.S(4)));
            int tx = r.Right + Program.S(6);
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(tx, 0, Math.Max(0, Width - tx), Height),
                Enabled ? ForeColor : Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    public class HotkeyDialog : Form
    {
        public int Mods;
        public int Vk;
        public string Label = "";
        readonly Label _tip;

        public HotkeyDialog()
        {
            Text = "设置全局热键";
            // 显式走主题配色：此前什么都不设 → 暗色下仍是系统浅灰窗体
            BackColor = Theme.Canvas;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            // 2026-10-10：抽屉是 TopMost，而**非置顶窗口永远压不过置顶窗口** ⇒ 对话框必须也置顶，
            // 否则「新建提示词」等会被主界面盖住（用户实测报的 bug）
            TopMost = true;
            Shown += (s2, e2) => DrawerForm.ApplyChrome(this);
            Size = new Size(Program.S(360), Program.S(140));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            KeyPreview = true;
            _tip = new Label
            {
                Text = "请按下新的热键组合\n（需包含 Ctrl 或 Alt，Esc 取消）",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.Font(10f, false),
            };
            Controls.Add(_tip);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            return false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) return;
            base.WndProc(ref m);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var key = e.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu
                || key == Keys.LWin || key == Keys.RWin)
            {
                _tip.Text = "已按住修饰键，请继续按下主键…";
                return;
            }
            if (key == Keys.Escape) { Close(); return; }
            bool hasMod = (e.Modifiers & (Keys.Control | Keys.Alt)) != 0;
            if (!hasMod) { _tip.Text = "热键需包含 Ctrl 或 Alt，请重新按。"; return; }
            var vk = e.KeyValue;
            Mods = ((e.Modifiers & Keys.Control) != 0 ? 0x2 : 0)
                 | ((e.Modifiers & Keys.Alt) != 0 ? 0x1 : 0)
                 | ((e.Modifiers & Keys.Shift) != 0 ? 0x4 : 0)
                 | 0x4000;   // MOD_NOREPEAT
            Vk = vk;
            Label = new KeysConverter().ConvertToString(e.Modifiers | key);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public class EditResult
    {
        public string Title;
        public string Content;
        public List<string> Tags;
        public string CategoryId;
        public bool Favorite;
        public string Note;
    }

    public class EditDialog : Form
    {
        readonly Store _store;
        readonly Prompt _prompt;
        TextBox _title;
        ComboBox _cat;
        CheckBox _fav;
        TextBox _tags;
        TextBox _note;
        TextBox _content;

        public EditResult Result;
        string _preTitle, _preContent;   // 仅 prompt == null 时用于预填（笔记→提示词 / 预填标题）

        public EditDialog(Store store, Prompt prompt) : this(store, prompt, null, null) { }

        public EditDialog(Store store, Prompt prompt, string preTitle, string preContent)
        {
            _store = store;
            _prompt = prompt;
            _preTitle = preTitle;
            _preContent = preContent;
            Text = prompt == null ? "新建提示词" : "编辑提示词";
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;      // 同上：抽屉置顶，对话框不置顶就会被盖住（2026-10-10 用户报的 bug）
            Shown += (s2, e2) => DrawerForm.ApplyChrome(this);
            Size = new Size(Program.S(384), Program.S(560));
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            Font = Theme.Font(9.5f, false);
            // BackColor+ForeColor 都要显式设：只设 Back 时 L() 建的标签继承 WinForms 默认
            // 黑 ForeColor → 暗色下黑底黑字（2026-10-06 整体检查发现）
            BackColor = Theme.Canvas;
            ForeColor = Theme.Text;
            Shown += (s2, e2) => DrawerForm.ApplyChrome(this);

            int fw = ClientSize.Width - Program.S(28);      // 字段宽跟客户区走（原来写死 418，改宽度就错）
            var y = Program.S(12);
            Func<string, Label> L = delegate(string t)
            {
                var l = new Label { Text = t, Location = new Point(Program.S(14), y), Size = new Size(fw, Program.S(18)) };
                y += Program.S(18) + Program.S(4);          // 标签贴住自己的字段（4 = 组内间距）
                Controls.Add(l);
                return l;
            };
            Func<int, bool, TextBox> T = delegate(int h, bool multi)
            {
                var t = new TextBox
                {
                    Location = new Point(Program.S(14), y),
                    Size = new Size(fw, h),
                    Multiline = multi,
                    BackColor = Theme.Surface,
                    ForeColor = Theme.Text,
                    BorderStyle = BorderStyle.FixedSingle,
                };
                y += h + Program.S(12);
                Controls.Add(t);
                return t;
            };

            L("标题 *");
            _title = T(24, false);
            L("分类目录");
            _cat = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(Program.S(14), y),
                Size = new Size(fw, Program.S(24)),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat,
            };
            // 此前漏了这句：下拉框建出来从未加入对话框 → 标签下方一个 55px 的空洞，
            // 用户在编辑时根本没法改分类（2026-10-06 独立视觉评审逐像素实锤）
            Controls.Add(_cat);
            // 暗色适配：视觉样式忽略 DropDownList 的 BackColor（Flat 无效）→ OwnerDraw 自绘
            DrawerForm.ThemeComboDraw(_cat);
            _cat.Items.Add("未分类");
            _cat.SelectedIndex = 0;
            foreach (var c in store.Categories)
            {
                _cat.Items.Add((string.IsNullOrEmpty(c.parent_id) ? "" : "└ ")
                    + (c.icon == "" ? "" : c.icon + " ") + c.name);
                if (prompt != null && prompt.category_id == c.id) _cat.SelectedIndex = _cat.Items.Count - 1;
            }
            y += Program.S(30);
            _fav = new ThemedCheckBox
            {
                Text = "收藏置顶",
                Location = new Point(Program.S(14), y),
                Size = new Size(Program.S(200), Program.S(22)),
            };
            Controls.Add(_fav);
            y += Program.S(28);
            L("标签（逗号分隔）");
            _tags = T(24, false);
            L("注释说明");
            _note = T(50, true);
            _note.MaxLength = 2000000;   // TextBox 默认 32767，长注释会静默打不进字
            L("提示词内容");
            _content = new TextBox
            {
                Location = new Point(Program.S(14), y),
                Size = new Size(fw, ClientSize.Height - y - Program.S(48)),
                Multiline = true,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical,
                MaxLength = 2000000,   // 长提示词不设 32k 默认上限
            };
            Controls.Add(_content);
            // 裸 38 是物理像素：按钮高 S(28)=42，放 H-38 底部会超出客户区 4px 被裁
            y = ClientSize.Height - Program.S(38);

            if (prompt != null)
            {
                _title.Text = prompt.title;
                _content.Text = DrawerForm.Crlf(prompt.content);
                _note.Text = DrawerForm.Crlf(prompt.note);
                _tags.Text = string.Join(", ", prompt.tags.ToArray());
                _fav.Checked = prompt.favorite;
                // 光标移到末尾：WinForms 设 Text 后全文选中，首键会整段覆盖（记事本批次教训同样适用）
                _title.SelectionStart = _title.TextLength; _title.SelectionLength = 0;
                _content.SelectionStart = _content.TextLength; _content.SelectionLength = 0;
                _note.SelectionStart = _note.TextLength; _note.SelectionLength = 0;
                _tags.SelectionStart = _tags.TextLength; _tags.SelectionLength = 0;
            }
            else if (!string.IsNullOrEmpty(_preTitle) || !string.IsNullOrEmpty(_preContent))
            {
                // 预填（笔记→提示词 / 快速捕获带标题）：同样要把光标移到末尾，否则首键会整段覆盖
                if (!string.IsNullOrEmpty(_preContent)) _content.Text = DrawerForm.Crlf(_preContent);
                if (!string.IsNullOrEmpty(_preTitle)) _title.Text = _preTitle;
                _title.SelectionStart = _title.TextLength; _title.SelectionLength = 0;
                _content.SelectionStart = _content.TextLength; _content.SelectionLength = 0;
            }

            var save = new MiniButton("保存", Program.S(80)) { Location = new Point(ClientSize.Width - Program.S(14) - Program.S(80), y) };
            save.Primary = true;                     // 主操作：实心蓝，一屏唯一
            save.ApplyTheme();
            save.Click += (s2, ev) => OnSave();
            var cancel = new MiniButton("取消", Program.S(80)) { Location = new Point(save.Left - Program.S(12) - Program.S(80), y) };
            cancel.Click += (s2, ev) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        static void RoundCornersStatic(Form f)
        {
            try
            {
                int pref = 2;
                DwmSetWindowAttribute(f.Handle, 33, ref pref, 4);
            }
            catch (Exception) { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) return;
            base.WndProc(ref m);
        }

        void OnSave()
        {
            if (_title.Text.Trim() == "")
            {
                MessageBox.Show(this, "请填写标题。", "提示");
                return;
            }
            var tags = new List<string>();
            foreach (var t in _tags.Text.Split(',')) if (t.Trim() != "") tags.Add(t.Trim());
            var catId = "";
            if (_cat.SelectedIndex > 0)
                catId = _store.Categories[_cat.SelectedIndex - 1].id;
            Result = new EditResult
            {
                Title = _title.Text,
                Content = _content.Text,
                Tags = tags,
                CategoryId = catId,
                Favorite = _fav.Checked,
                Note = _note.Text,
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // 单行输入小对话框（新建/重命名分类用）。暗色适配：BackColor+ForeColor 都要显式设
    // （教训同 EditDialog：只设 Back 时标签继承默认黑字）
    public class PromptDialog : Form
    {
        readonly TextBox _tb;
        public string InputText { get { return _tb.Text; } }

        public PromptDialog(string title, string label, string text)
        {
            Text = title;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            Shown += (s2, e2) => DrawerForm.ApplyChrome(this);      // 同上（2026-10-10 用户报的 bug）
            Size = new Size(Program.S(360), Program.S(150));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Canvas;
            ForeColor = Theme.Text;
            var l = new Label
            {
                Text = label,
                Location = new Point(Program.S(12), Program.S(10)),
                Size = new Size(Program.S(324), Program.S(20)),
                Font = Theme.Font(9.5f, false),
            };
            _tb = new TextBox
            {
                Location = new Point(Program.S(12), Program.S(38)),
                Size = new Size(Program.S(324), Program.S(24)),
                Font = Theme.Font(9.5f, false),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Text = text,
            };
            var okX = Program.S(360) - Program.S(12) - Program.S(70);
            var ok = new MiniButton("确定", Program.S(70))
            {
                Location = new Point(okX, Program.S(80)),
                BackColor = Theme.Accent,
                ForeColor = Theme.AccentText,
            };
            ok.Click += (s, e) =>
            {
                if (_tb.Text.Trim() == "") { MessageBox.Show(this, "请输入内容。", "提示"); return; }
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = new MiniButton("取消", Program.S(70))
            {
                Location = new Point(okX - Program.S(78), Program.S(80)),
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(l);
            Controls.Add(_tb);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
        }

        public static string Show(IWin32Window owner, string title, string label, string text)
        {
            using (var dlg = new PromptDialog(title, label, text))
            {
                if (dlg.ShowDialog(owner) == DialogResult.OK) return dlg.InputText;
                return null;
            }
        }
    }

    // 列表抗闪烁：**不能**用 DoubleBuffered（实测它会让 Win32 ListBox 的 WM_DRAWITEM
    // 管线被绕过 → 所有卡片不绘制、列表空白，frozen17 事故）。**也不能**用屏外行缓冲
    // （缓冲把文字画进内存 DIB ⇒ ClearType 失效 ⇒ 字体变粗变硬，2026-10-11 实测）。
    // ⚠️ **也不要**给 ListBox 加 WS_EX_COMPOSITED：实测 CreateWindowEx 直接失败 →
    // CreateHandle 抛 Win32Exception（"创建窗口位句柄时出错"），抽屉 Show() 整个崩掉
    // （2026-10-11 真实事故；exe 是 WinExe，异常还会弹"关不掉的对话框"）。
    // ⇒ 抗闪烁就靠下面这条**行内免擦除**（+ 悬停只失效新旧两行），不动任何绘制管线。
    public class SmoothListBox : ListBox
    {
        [DllImport("gdi32.dll")] static extern int GetClipBox(IntPtr hdc, out RECT rect);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014)   // WM_ERASEBKGND
            {
                // 裁剪框完全在"最后一行底边"以内 = 纯 item 行区域：跳过擦除（DrawItem 整行覆盖）
                RECT rc;
                if (GetClipBox(m.WParam, out rc) > 0 && rc.bottom <= Items.Count * ItemHeight)
                {
                    m.Result = (IntPtr)1;
                    return;
                }
                // 其余（最后一行以下/整页）：交给 base 正常擦除（换主题、缩放时需要）
            }
            base.WndProc(ref m);
        }
    }

    // 记事本编辑框：Ctrl+滚轮缩放字号（浏览器/VS Code 事实标准）。
    // 必须拦在 WndProc：WinForms 的 MouseWheel 事件无法标记已处理，会边缩放边滚动文本。
    // 回收站（2026-10-07 提示词 / 2026-10-08 加笔记页）：删除只是把条目移进来，所以误删能救。
    // 左右两栏并排（各自带按钮行），列表用标准 ListBox（与 PromptDialog 的 TextBox 同风格，不做自绘）。
    public class TrashDialog : Form
    {
        Store _store;
        ListBox _list;        // 左栏：提示词
        ListBox _noteList;    // 右栏：笔记页
        Label _tipL, _tipR;

        class Row
        {
            public Prompt P;
            public Row(Prompt p) { P = p; }
            public override string ToString()
            {
                string t = P.title == null ? "" : P.title;
                if (t.Length > 30) t = t.Substring(0, 30) + "…";
                return P.deleted_at + "   " + t;
            }
        }

        class NoteRow
        {
            public NotePage P;
            public NoteRow(NotePage p) { P = p; }
            public override string ToString()
            {
                string t = _title;
                return P.deleted_at + "   " + t;
            }
            string _title;
            public NoteRow Set(string t) { _title = t; return this; }
        }

        public TrashDialog(Store store)
        {
            _store = store;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            Shown += (s2, e2) => DrawerForm.ApplyChrome(this);      // 同上（2026-10-10 用户报的 bug）
            Size = new Size(Program.S(900), Program.S(520));
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            Font = Theme.Font(9.5f, false);
            BackColor = Theme.Canvas;
            ForeColor = Theme.Text;

            int colW = (ClientSize.Width - Program.S(42)) / 2;
            int listTop = Program.S(36);
            int listH = ClientSize.Height - Program.S(36) - Program.S(78);
            int btnY = listTop + listH + Program.S(8);

            // ---------------- 左栏：提示词 ----------------
            _tipL = new Label
            {
                Location = new Point(Program.S(14), Program.S(12)),
                Size = new Size(colW, Program.S(20)),
                ForeColor = Theme.TextMuted,
                Font = Theme.Font(9f, false),
            };
            Controls.Add(_tipL);

            _list = new ListBox
            {
                Location = new Point(Program.S(14), listTop),
                Size = new Size(colW, listH),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended,
                Font = Theme.Font(9.5f, false),
            };
            Controls.Add(_list);

            int halfBtn = (colW - Program.S(12)) / 3;
            var restore = new MiniButton("恢复选中", halfBtn) { Location = new Point(Program.S(14), btnY) };
            restore.Click += (s, ev) =>
            {
                var ids = SelIds();
                if (ids.Count == 0) { MessageBox.Show(this, "先在左边列表里选中要恢复的提示词。", "回收站"); return; }
                int n = _store.RestoreFromTrash(ids);
                _store.Save();
                Fill();
                MessageBox.Show(this, "已恢复 " + n + " 条提示词。", "回收站");
            };
            Controls.Add(restore);

            var all = new MiniButton("全部恢复", halfBtn) { Location = new Point(Program.S(14) + halfBtn + Program.S(6), btnY) };
            all.Click += (s, ev) =>
            {
                if (_store.Trash.Count == 0) return;
                var ids = new List<string>();
                foreach (var p in _store.Trash) ids.Add(p.id);
                int n = _store.RestoreFromTrash(ids);
                _store.Save();
                Fill();
                MessageBox.Show(this, "已恢复 " + n + " 条提示词。", "回收站");
            };
            Controls.Add(all);

            var clr = new MiniButton("清空", halfBtn) { Location = new Point(Program.S(14) + (halfBtn + Program.S(6)) * 2, btnY) };
            clr.Danger = true;
            clr.Click += (s, ev) =>
            {
                if (_store.Trash.Count == 0) return;
                if (MessageBox.Show(this, "彻底删除回收站里的 " + _store.Trash.Count + " 条提示词？此操作不可撤销。",
                        "清空提示词回收站", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                _store.EmptyTrash();
                _store.Save();
                Fill();
            };
            Controls.Add(clr);

            // ---------------- 右栏：笔记页（2026-10-08）----------------
            int rx = Program.S(14) + colW + Program.S(14);
            _tipR = new Label
            {
                Location = new Point(rx, Program.S(12)),
                Size = new Size(colW, Program.S(20)),
                ForeColor = Theme.TextMuted,
                Font = Theme.Font(9f, false),
            };
            Controls.Add(_tipR);

            _noteList = new ListBox
            {
                Location = new Point(rx, listTop),
                Size = new Size(colW, listH),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended,
                Font = Theme.Font(9.5f, false),
            };
            Controls.Add(_noteList);

            var nRestore = new MiniButton("恢复选中", halfBtn) { Location = new Point(rx, btnY) };
            nRestore.Click += (s, ev) =>
            {
                var ids = SelNoteIds();
                if (ids.Count == 0) { MessageBox.Show(this, "先在右边列表里选中要恢复的笔记页。", "回收站"); return; }
                int n = _store.RestoreNotePage(ids);
                _store.SaveNotes();
                Fill();
                MessageBox.Show(this, "已恢复 " + n + " 页笔记。", "回收站");
            };
            Controls.Add(nRestore);

            var nAll = new MiniButton("全部恢复", halfBtn) { Location = new Point(rx + halfBtn + Program.S(6), btnY) };
            nAll.Click += (s, ev) =>
            {
                if (_store.TrashNotes.Count == 0) return;
                var ids = new List<string>();
                foreach (var p in _store.TrashNotes) ids.Add(p.id);
                int n = _store.RestoreNotePage(ids);
                _store.SaveNotes();
                Fill();
                MessageBox.Show(this, "已恢复 " + n + " 页笔记。", "回收站");
            };
            Controls.Add(nAll);

            var nClr = new MiniButton("清空", halfBtn) { Location = new Point(rx + (halfBtn + Program.S(6)) * 2, btnY) };
            nClr.Danger = true;
            nClr.Click += (s, ev) =>
            {
                if (_store.TrashNotes.Count == 0) return;
                if (MessageBox.Show(this, "彻底删除 " + _store.TrashNotes.Count + " 页已删笔记？此操作不可撤销。\n"
                        + "（提示：记事本的「存档」按钮存下的 TXT 是独立文件，不受这里影响）",
                        "清空笔记页回收站", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                _store.EmptyTrashNotes();
                _store.SaveNotes();
                Fill();
            };
            Controls.Add(nClr);

            // ---------------- 关闭 ----------------
            var close = new MiniButton("关闭", Program.S(80))
            {
                Location = new Point(ClientSize.Width - Program.S(94), btnY + Program.S(38)),
            };
            close.Click += (s, ev) => Close();
            Controls.Add(close);

            Fill();
        }

        List<string> SelIds()
        {
            var ids = new List<string>();
            foreach (var o in _list.SelectedItems) ids.Add(((Row)o).P.id);
            return ids;
        }

        List<string> SelNoteIds()
        {
            var ids = new List<string>();
            foreach (var o in _noteList.SelectedItems) ids.Add(((NoteRow)o).P.id);
            return ids;
        }

        void Fill()
        {
            var items = new List<Prompt>(_store.Trash);
            items.Sort((a, b) => string.CompareOrdinal(b.deleted_at, a.deleted_at));   // 新的在前
            _list.Items.Clear();
            foreach (var p in items) _list.Items.Add(new Row(p));
            _tipL.Text = "提示词回收站：" + items.Count + " 条（上限 " + Store.TrashCap + "，超出自动丢最旧）";

            var notes = new List<NotePage>(_store.TrashNotes);
            notes.Sort((a, b) => string.CompareOrdinal(b.deleted_at, a.deleted_at));
            _noteList.Items.Clear();
            foreach (var p in notes)
            {
                string t = _store.NoteTitle(p);
                if (t.Length > 30) t = t.Substring(0, 30) + "…";
                _noteList.Items.Add(new NoteRow(p).Set(t));
            }
            _tipR.Text = "已删除的笔记页：" + notes.Count + " 页（删页前还会自动存一份 TXT 存档）";
            Text = "回收站 — 提示词 " + items.Count + " 条 / 笔记页 " + notes.Count + " 页";
        }
    }

    public class NoteTextBox : TextBox
    {
        public Action<int> CtrlWheel;   // delta：+120 上滚 / -120 下滚

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A && (Control.ModifierKeys & Keys.Control) != 0)   // WM_MOUSEWHEEL
            {
                if (CtrlWheel != null)
                {
                    CtrlWheel(m.WParam.ToInt32() >> 16 > 0 ? 120 : -120);
                    return;   // 吞掉：缩放时不滚动正文
                }
            }
            if (m.Msg == 0x0302)   // WM_PASTE：外部剪贴板文本常是裸 \n，粘贴统一归一 CRLF
            {                      // （否则 Ctrl+V 会让"换行挤成一行"的 bug 从右键菜单外绕回来）
                string t = Clipboard.ContainsText() ? DrawerForm.Crlf(Clipboard.GetText()) : "";
                if (t != "") SelectedText = t;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
