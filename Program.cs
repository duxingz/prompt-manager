// -*- coding: utf-8 -*-
// Program.cs — 提示词抽屉 v3 入口：单实例(唤醒) / 托盘 / 全局热键 / 首启导入 / 备份
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PromptDrawer
{
    static class Program
    {
        public const string AppName = "提示词管理器V2";
        // v2 已退役（2026-10-06，目录已整目录删除）：主库（21,900 条 / 40 分类）归档在
        //   D:\workwork\提示词管理器V2\旧版数据\prompts_data-v2-主库.json（+ 完整归档 zip）
        // 「首启自动导入」仍关闭：旧库 21,900 条远超 1 万条性能基准，首启大文件导入风险高。
        // 上限已移除（2026-10-07，隐性要求改为 1 万条内极速，SelfTest 有基准门），
        // 需要时请用设置页的「从 v2 / JSON 导入…」手动选那个文件。
        public const string V2DataPath = "";

        static Mutex _mutex;
        static Store _store;
        static DrawerForm _drawer;
        static CapsuleForm _capsule;
        static NotifyIcon _tray;
        static ToolStripMenuItem _trayAutoItem;   // 托盘的「开机自动启动」（供设置页那边同步勾选状态）

        [DllImport("user32.dll")]
        static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        static readonly IntPtr HwndTopmost = new IntPtr(-1);
        const uint SwpNosize = 0x1, SwpNomove = 0x2, SwpNoactivate = 0x10;
        const int GwlExstyle = -20, WsExTopmost = 0x8;

        // 高分屏缩放（150% 屏 = 1.5）；所有像素尺寸经 S() 缩放
        public static float Scale = 1f;
        public static int S(int px) { return (int)Math.Round(px * Scale); }

        // 显式静态构造：在 Main 任何代码前完成 DPI 感知与 Scale 测定。
        // 若用字段初始化器（beforefieldinit），CapsuleForm/DrawerForm 的
        // static readonly 尺寸会在 Scale=1 时被提前算死（图标糊/缩放错乱的根因）。
        static Program()
        {
            try
            {
                if (!SetProcessDpiAwarenessContext(new IntPtr(-2)))
                    SetProcessDPIAware();
            }
            catch (Exception) { try { SetProcessDPIAware(); } catch (Exception) { } }
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
                Scale = g.DpiX / 96f;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowW(string className, string windowTitle);
        static readonly IntPtr HwndBroadcast = (IntPtr)0xFFFF;
        const uint ModAlt = 0x1, ModCtrl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;

        public static string AppDir()
        {
            return Application.StartupPath;
        }

        // 崩溃日志统一入口：超过 1MB 先删再写（此前无限增长，永不清零）
        static void LogCrash(string tag, object detail)
        {
            try
            {
                string p = Path.Combine(AppDir(), "crash.log");
                try
                {
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 1024 * 1024) fi.Delete();
                }
                catch (Exception) { }
                File.AppendAllText(p, DateTime.Now + " [" + tag + "]" + Environment.NewLine
                    + detail + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception) { }
        }

        static Image _logo;
        // 图标只解码一次。三件事必须一起做到：
        // ① 每次 OnPaint 都 Image.FromFile 会漏 ~2MB/次 —— 实测 200 次重绘 +412.7MB / 耗时 742ms，
        //    缓存后 +0.3MB / 56ms（2026-10-05 实测，这就是"长时间开着"内存涨到 400MB 的根因）；
        // ② Image.FromFile 会**独占锁住** logo 文件直到被回收（会让 build.bat 的 copy 失败）；
        // ③ 读字节 → 解码 → 拷成自管位图，脱离流，不锁文件。
        public static Image Logo()
        {
            if (_logo == null)
            {
                try
                {
                    string p = Path.Combine(AppDir(), "assets", "logo_round.png");
                    if (File.Exists(p))
                    {
                        byte[] raw = File.ReadAllBytes(p);
                        using (var ms = new MemoryStream(raw))
                        using (var src = Image.FromStream(ms))
                            _logo = new Bitmap(src);
                    }
                }
                catch (Exception) { _logo = null; }
            }
            return _logo;
        }

        public static bool MouseInCapsule
        {
            get { return _capsule != null && _capsule.Visible && _capsule.Bounds.Contains(Cursor.Position); }
        }

        // ------------------------------------------------------------ 置顶守护
        // 用户诉求：悬浮图标必须永远置顶（点开网页不能把它盖住）。
        // 根因（2026-10-07 实测链）：本机 Nahimic/A-Volute 音效软件（主板附带）向所有带窗口的
        // 进程注入 AudioDevProps2.dll（Get-Process 模块列表可见），注入后**本进程**对自身窗口的
        // TOPMOST 写入（SetWindowPos / SetWindowLong）返回成功但样式位不变；而**跨进程**写入
        // 有效且稳定（外部置顶实测 >60s 含真实前台切换）。胶囊从创建起就没有置顶位，抽屉同理。
        // 对策三层：① 窗体 OnVisibleChanged 显示即补位；② 守护定时器每秒核对真实 EXSTYLE；
        // ③ 本进程写入无效时（写完读回仍缺位），拉起一次性 "--settopmost <pid>" 子进程跨进程
        // 补位（限频 3s，平时零开销）。跨进程写入实测可生效，详见 AGENTS.md 硬规则 17/18。
        public static void EnsureTopMost(Form f)
        {
            try
            {
                if (f == null || !f.IsHandleCreated || !f.Visible) return;
                int ex = GetWindowLong(f.Handle, GwlExstyle);
                if ((ex & WsExTopmost) == 0)
                {
                    // 本机存在间歇性置顶剥离（疑似 Nahimic 注入 DLL 的行为）：活跃期任何进程的写入
                    // 都会被瞬时回退，休眠期一切正常。守护唯一能做的是每秒补写一次——
                    // 剥离一旦停歇，1 秒内置顶自动恢复。
                    SetWindowPos(f.Handle, HwndTopmost, 0, 0, 0, 0, SwpNosize | SwpNomove | SwpNoactivate);
                    // 写完**立刻回读**：仍是缺位 = 被外部程序挡住了（活跃期连本进程写都无效）
                    if (f == _capsule)
                    {
                        if ((GetWindowLong(f.Handle, GwlExstyle) & WsExTopmost) == 0) NoteTopmostMiss();
                        else NoteTopmostOk();
                    }
                }
                else if (f == _capsule) NoteTopmostOk();
            }
            catch (Exception) { }
        }

        // ---- 置顶被外部程序持续挡住的状态（2026-10-07 加：让"图标被盖住"变成看得见的提示）----
        public static bool TopmostBlocked = false;
        static int _topmostMissStreak = 0;
        static DateTime _lastTopmostTip = DateTime.MinValue;

        static void NoteTopmostMiss()
        {
            _topmostMissStreak++;
            if (_topmostMissStreak < 5 || TopmostBlocked) return;   // 连续 5 秒补不上才认定"被干扰"
            TopmostBlocked = true;
            if (_tray == null || (DateTime.Now - _lastTopmostTip).TotalMinutes < 5) return;  // 限频 5 分钟
            _lastTopmostTip = DateTime.Now;
            try
            {
                _tray.ShowBalloonTip(5000, AppName,
                    "悬浮图标被其他程序挡住了置顶（本机已知的音频增强组件会间歇性干扰）。"
                    + "这段时间窗口可能盖住图标；干扰停歇后会自动恢复。",
                    ToolTipIcon.Warning);
            }
            catch (Exception) { }
        }

        static void NoteTopmostOk()
        {
            _topmostMissStreak = 0;
            TopmostBlocked = false;
        }

        static System.Windows.Forms.Timer _topmostGuard;
        public static void StartTopmostGuard()
        {
            if (_topmostGuard != null) return;
            _topmostGuard = new System.Windows.Forms.Timer { Interval = 1000 };
            _topmostGuard.Tick += (s, ev) => { EnsureTopMost(_capsule); EnsureTopMost(_drawer); };
            _topmostGuard.Start();
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--selftest")
            {
                // ⚠️ 自检路径**不走 Application.Run**，所以下面那个 Application.ThreadException
                // 钩子根本不生效：自检里一抛异常，.NET 就弹"未处理的异常"对话框（用户侧关不掉）。
                // 2026-10-11 真实发生（uitest 在 Show() 抛 Win32Exception）。这里统一兜住：
                // 写 crash.log + 退出码 1，**绝不弹窗**。
                try { Environment.Exit(SelfTest.Run()); }
                catch (Exception ex) { LogCrash("selftest", ex); Environment.Exit(1); }
                return;
            }
            if (args.Length == 1 && args[0] == "--uitest")
            {
                // 被动 UI 自检：窗口只在屏幕外出现，不移动鼠标、不截屏、不碰用户数据
                try { Environment.Exit(UiTest.Run()); }
                catch (Exception ex) { LogCrash("uitest", ex); Environment.Exit(1); }
                return;
            }
            if (args.Length >= 1 && args[0] == "--uishots")
            {
                // 渲染 QA 出图（2026-10-24）：同 uitest 规矩（屏幕外/不碰输入/数据只进临时目录），
                // 产出 6 张 PNG 供四色配额检查与独立视觉验收
                try { Environment.Exit(UiTest.RunShots(args.Length > 1 ? args[1] : "_ui-shots")); }
                catch (Exception ex) { LogCrash("uishots", ex); Environment.Exit(1); }
                return;
            }

            bool created;
            _mutex = new Mutex(true, "PromptDrawerV3_Mutex", out created);
            if (!created)
            {
                // 已有实例：直接向其抽屉窗口投递唤醒（找不到再广播）
                var wake = RegisterWindowMessage("PromptDrawerV3Wake");
                var hwnd = FindWindowW(null, AppName);
                var okPost = hwnd != IntPtr.Zero
                    ? PostMessage(hwnd, wake, IntPtr.Zero, IntPtr.Zero)
                    : PostMessage(HwndBroadcast, wake, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s2, e2) => LogCrash("ThreadException", e2.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s2, e2) => LogCrash("Unhandled", e2.ExceptionObject);

            Store.DataDir = AppDir();
            _store = new Store();
            _store.Load();
            FirstRunImport();
            _store.Flush();
            _store.RollingBackup();
            Theme.Dark = (string)_store.GetSetting("theme", "light") == "dark";

            var wakeMsg = (int)RegisterWindowMessage("PromptDrawerV3Wake");
            DrawerForm.WakeMsg = wakeMsg;

            WarmupFirstForm();     // 必须：占掉"进程里第一个 Form"的名额（见该方法注释）

            _capsule = new CapsuleForm();
            _capsule.Clicked += (s, ev) => ToggleDrawer();
            _capsule.PositionCommitted += (s, ev) => SaveBarPos();
            _capsule.SetHalfHide(Convert.ToBoolean(_store.GetSetting("edge_half_hide", true)));

            _drawer = new DrawerForm(_store);
            _drawer.Capsule = _capsule;

            BuildTray();
            StartTopmostGuard();   // 置顶守护：每秒核对真实 EXSTYLE（本进程写不动时拉起跨进程补位）

            // 开机自启同步（默认开）
            Autostart.Set(Convert.ToBoolean(_store.GetSetting("autostart", true)));

            // 全局热键（默认 Alt+P；注册在抽屉窗体上）
            uint mods = ModAlt | ModNoRepeat;
            uint vk = 0x50;
            var hk = _store.GetSetting("hotkey") as Dictionary<string, object>;
            if (hk != null && hk.ContainsKey("mods") && hk.ContainsKey("vk"))
            {
                try { mods = Convert.ToUInt32(hk["mods"]); vk = Convert.ToUInt32(hk["vk"]); }
                catch (Exception) { }
            }
            var hkOk = RegisterDrawerHotkey(mods, vk, HotkeyLabelText(), false);
            if (!hkOk) _store.Save();   // 把 hotkey_register_failed 落盘，设置页才能看出来

            // 胶囊按记忆位置显示
            _capsule.ShowRestored(GetBarPos());

            try { Application.Run(); }
            catch (Exception ex)
            {
                LogCrash("Run", ex);
                throw;
            }
        }

        // 【必须】WinForms 进程里"创建的第一个 Form"拿不到 WS_EX_TOPMOST —— 2026-10-07 实测：
        //   同一个 Form 配置连做三次 → 第 1 个建句柄时 off、事后 SetWindowPos(HWND_TOPMOST) 也无效；
        //   第 2、3 个建句柄就是 ON。先造一个一次性窗体再建同样的窗体 → 第 1 个也 ON。
        // 而胶囊恰好是本进程第一个 Form（抽屉在它之后）→ 它永远压不住别的窗口 = 用户报的
        // "网页盖住悬浮图标"。这里先建一个**屏幕外**的一次性窗体占掉那个名额，再建胶囊。
        // 屏幕外(-4000)所以不会闪一下；用完即弃。
        static void WarmupFirstForm()
        {
            try
            {
                using (var w = new Form
                {
                    ShowInTaskbar = false,
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-4000, -4000),
                    Size = new Size(8, 8),
                })
                {
                    w.Show();
                    Application.DoEvents();
                    w.Hide();
                }
            }
            catch (Exception) { }
        }

        static void FirstRunImport()
        {
            // 全新数据（无提示词、≤1 分类）且发现可导入的旧库 → 复制导入（绝不写源文件）
            // V2DataPath 为空 = v2 已退役、自动导入关闭（见该常量的注释）
            if (V2DataPath.Length == 0) return;
            if (_store.Prompts.Count > 0 || _store.Categories.Count > 1) return;
            if (!File.Exists(V2DataPath)) return;
            try
            {
                // 守卫条件已确保本地只有默认空库，此处覆盖安全
                File.Copy(V2DataPath, _store.DataFile, true);
                _store.Load();
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ 抽屉显隐
        public static void ToggleDrawer()
        {
            if (_drawer.Visible) CollapseDrawer();
            else ShowDrawer();
        }

        public static void ShowDrawer()
        {
            var wa = Screen.FromPoint(_capsule.Location).WorkingArea;
            bool rightSide = _capsule.Location.X + _capsule.Width / 2 > wa.Left + wa.Width / 2;
            _drawer.Location = new Point(
                rightSide ? wa.Right - DrawerForm.DrawerWidth - 4 : wa.Left + 4,
                wa.Top + 8);
            _drawer.RefreshChips();
            _drawer.RefreshList();
            _drawer.SwitchTab(Convert.ToInt32(_store.GetSetting("tab", 0)), false);
            _drawer.Show();
            _drawer.Activate();
            _capsule.Hide();
        }

        public static void CollapseDrawer()
        {
            if (_drawer == null || !_drawer.Visible) return;
            _drawer.Hide();
            SaveBarPos();
            if (_capsule != null && !_capsule.Visible) _capsule.ShowRestored(GetBarPos());
        }

        // 头部 × 按钮：抽屉 + 胶囊都隐藏，只留托盘图标（用户 2026-10-10 指定语义）。
        // 恢复路径：托盘双击 / 托盘菜单「显示/隐藏抽屉」/ Alt+P —— 任一都会走 ShowDrawer。
        public static void MinimizeToTray()
        {
            try
            {
                if (_drawer != null && _drawer.Visible) _drawer.Hide();
                if (_capsule != null && _capsule.Visible) _capsule.Hide();
            }
            catch (Exception) { }
        }

        static Point GetBarPos()
        {
            try
            {
                int x = Convert.ToInt32(_store.GetSetting("bar_x", int.MinValue));
                int y = Convert.ToInt32(_store.GetSetting("bar_y", int.MinValue));
                if (x != int.MinValue && y != int.MinValue) return new Point(x, y);
            }
            catch (Exception) { }
            var wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Right - _capsule.Width - Program.S(8), wa.Top + wa.Height / 2);
        }

        static void SaveBarPos()
        {
            if (_capsule == null) return;
            _store.SetSetting("bar_x", _capsule.Location.X);
            _store.SetSetting("bar_y", _capsule.Location.Y);
            _store.Save();
        }

        // ------------------------------------------------------------ 热键
        // ① 无论成败都记录 hotkey_register_failed（设置页可见化：否则「Alt+P 被抢」用户无从自查）
        // ② save && ok 才持久化热键本身——此前失败的热键也被存盘，RestoreHotkey 读回的正是
        //    坏值再失败 → 旧热键已注销、新热键没注上 → 一个热键都不剩（2026-10-06 整体检查发现）
        public static bool RegisterDrawerHotkey(uint mods, uint vk, string label, bool save)
        {
            try { UnregisterHotKey(_drawer.Handle, 1); } catch (Exception) { }
            var ok = RegisterHotKey(_drawer.Handle, 1, mods, vk);
            _store.SetSetting("hotkey_register_failed", !ok);
            if (save)
            {
                if (ok)
                {
                    _store.SetSetting("hotkey", new Dictionary<string, object>
                    {
                        { "mods", (int)mods }, { "vk", (int)vk },
                        { "label", label }, { "register_failed", false },
                    });
                }
                _store.Save();
            }
            return ok;
        }

        public static bool RestoreHotkey()
        {
            var ok = false;
            var hk = _store.GetSetting("hotkey") as Dictionary<string, object>;
            if (hk != null && hk.ContainsKey("mods") && hk.ContainsKey("vk"))
            {
                try
                {
                    ok = RegisterDrawerHotkey(Convert.ToUInt32(hk["mods"]), Convert.ToUInt32(hk["vk"]),
                        hk.ContainsKey("label") ? hk["label"].ToString() : "Alt+P", false);
                }
                catch (Exception) { ok = false; }
            }
            // 存档热键注册不上（可能正是上次失败被记下的坏值）→ 回退默认 Alt+P
            if (!ok) ok = RegisterDrawerHotkey(ModAlt | ModNoRepeat, 0x50, "Alt+P", false);
            return ok;
        }

        public static string HotkeyLabelText()
        {
            var hk = _store.GetSetting("hotkey") as Dictionary<string, object>;
            if (hk != null && hk.ContainsKey("label")) return hk["label"].ToString();
            return "Alt+P";
        }

        // ------------------------------------------------------------ 托盘
        static void BuildTray()
        {
            _tray = new NotifyIcon();
            var iconPath = Path.Combine(AppDir(), "assets", "app_icon.ico");
            if (File.Exists(iconPath)) _tray.Icon = new Icon(iconPath);
            _tray.Text = AppName;
            _tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏抽屉", null, (s, ev) => ToggleDrawer());
            menu.Items.Add(new ToolStripSeparator());
            var auto = new ToolStripMenuItem("开机自动启动")
            {
                Checked = Convert.ToBoolean(_store.GetSetting("autostart", true)),
            };
            _trayAutoItem = auto;
            auto.Click += (s, ev) =>
            {
                auto.Checked = !auto.Checked;
                _store.SetSetting("autostart", auto.Checked);
                Autostart.Set(auto.Checked);
                // 必须立刻落盘：注册表那边是**立即生效**的（删/写 Run 项），而 SetSetting 只标脏、
                // 要等退出 Flush —— 中间若程序被强杀，就会留下"Run 项没了、设置也没记下"的
                // 不一致状态（2026-10-07 用户真实遇到：重启后没自启）。与设置页那三个复选框保持一致。
                _store.Save();
                // 让设置页那个勾跟着变
                if (_drawer != null) _drawer.SyncAutostartUI(auto.Checked);
            };
            menu.Items.Add(auto);
            menu.Items.Add("切换主题", null, (s, ev) => _drawer.ToggleTheme());
            menu.Items.Add(new ToolStripSeparator());
            // 2026-10-07 新增：快速捕获（剪贴板 → 提示词）与回收站。
            // 放在托盘而不是设置页，是为了不给抽屉布局加按钮（用户偏好：优先手势/右键，而非新增控件）。
            menu.Items.Add("从剪贴板新建提示词", null, (s, ev) =>
            {
                if (_drawer != null) _drawer.CaptureFromClipboard();
            });
            menu.Items.Add("回收站…", null, (s, ev) =>
            {
                if (_drawer != null) _drawer.ShowTrash();
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, ev) => QuitApp());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, ev) => ToggleDrawer();
        }

        // 设置页改了「开机自动启动」后，把托盘菜单那一项的勾同步过来
        // （只改勾：不写设置、不碰注册表、不触发 Click → 不会递归）
        public static void SyncTrayAutostart(bool on)
        {
            try
            {
                if (_trayAutoItem == null || _trayAutoItem.Checked == on) return;
                _trayAutoItem.Checked = on;   // 改 Checked 不会触发 Click
            }
            catch (Exception) { }
        }

        // 托盘气泡（2026-10-07）：抽屉收起时 Toast 看不见，所以"不进抽屉的操作"
        // （快速捕获 / 回收站等）的结果一律用它报。
        public static void Balloon(string msg)
        {
            try
            {
                if (_tray == null) return;
                _tray.ShowBalloonTip(4000, AppName, msg, ToolTipIcon.Info);
            }
            catch (Exception) { }
        }

        public static void QuitApp()
        {
            try
            {
                if (_drawer != null && _drawer.Visible) SaveBarPos();
                _store.Flush();
            }
            catch (Exception) { }
            // 落盘失败（磁盘满/文件被占）必须让人知道：此前静默吞掉，退出即丢改动
            try
            {
                if (_store.SaveFailed)
                    MessageBox.Show("警告：数据未能写入磁盘（可能是磁盘已满或文件被占用）。\n"
                        + "现在退出的话，本次的改动可能丢失。请先清理磁盘或关闭占用该文件的程序。",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception) { }
            try
            {
                if (_drawer != null) UnregisterHotKey(_drawer.Handle, 1);
            }
            catch (Exception) { }
            if (_tray != null) _tray.Visible = false;
            Application.Exit();
        }
    }

    public static class Autostart
    {
        const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string ValueName = "PromptDrawerV3";

        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (on)
                        k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue(ValueName) != null)
                        k.DeleteValue(ValueName);
                }
            }
            catch (Exception) { }
        }
    }
}
