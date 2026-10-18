// -*- coding: utf-8 -*-
// CapsuleForm.cs — 44×44 圆角 logo 悬浮胶囊：拖动/贴边吸附/半隐藏/悬停滑出
// 几何逻辑与 v2 float_bar.py 同源（半隐藏=沿边推出一半）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PromptDrawer
{
    public class CapsuleForm : Form
    {
        public static readonly int CapsuleSize = Program.S(44);
        public static readonly int Stick = Program.S(28);
        public static readonly int EdgeMargin = Program.S(2);

        public event EventHandler Clicked;               // 单击（非拖动）
        public event EventHandler PositionCommitted;     // 停靠位变更（持久化）

        bool _dragged = false;
        bool _halfHide = true;
        Point _pressGlobal;
        Point _pressWindow;
        string _parkedEdge = null;    // left/right/top/bottom
        Point _parkedPos;

        Timer _popTimer;              // 悬停滑出动画（逐步逼近，省依赖）
        EventHandler _popTick;        // 当前动画的 Tick 处理器（停表时必须一并摘掉）
        Timer _watchdog;              // 按需看门狗（只在疑似卡住时开，状态一致即停，不常驻）

        public CapsuleForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Size = new Size(CapsuleSize, CapsuleSize);
            DoubleBuffered = true;
            ToolTipText();
            SetCursor(Cursors.Hand);
            SetRegion();
        }

        // 圆角窗口区域：按**实际客户区**重建（与 logo 的圆角一致）。
        // 必须跟 ClientSize 走 —— Region 与窗口尺寸一旦不一致，图标就会被裁掉一块。
        void SetRegion()
        {
            int cs = Math.Min(ClientSize.Width, ClientSize.Height);
            if (cs < 24) return;          // 句柄未建/尺寸异常时不碰 Region
            using (var path = new GraphicsPath())
            {
                int r = 10, d = r * 2;
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(cs - d, 0, d, d, 270, 90);
                path.AddArc(cs - d, cs - d, d, d, 0, 90);
                path.AddArc(0, cs - d, d, d, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            }
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            SetRegion();
        }

        // 【必须】WS_EX_TOOLWINDOW（= v2 里 PySide6 的 Qt::Tool）：
        // 少了它，Windows 会按 SM_CXMINTRACK/SM_CYMINTRACK 把顶层窗口钳到"系统最小窗口尺寸"
        // （本机 150% 缩放实测 202×56），Show() 那一刻把 66×66 的胶囊撑成 202×66
        // → Region 裁掉图标 + 吸附/半埋几何全部错位（2026-10-05 定案，见 AGENTS.md）。
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        void ToolTipText()
        {
            // 无原生 tooltip 于无句柄前；用延迟设置
            var tip = new ToolTip { InitialDelay = 400 };
            tip.SetToolTip(this, "提示词管理器V2 · 单击展开 / Alt+P 呼出\n拖动移动 · 贴边半隐藏 · 右键菜单");
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        public static void RoundCorners(Form f, int radius)
        {
            try
            {
                // DWMWA_WINDOW_CORNER_PREFERENCE=33, DWMWCP_ROUND=2
                int pref = 2;
                DwmSetWindowAttribute(f.Handle, 33, ref pref, 4);
            }
            catch (Exception) { }
        }
        void SetCursor(Cursor c) { Cursor = c; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Image logo = Program.Logo();      // 缓存；绝不要在 OnPaint 里 Image.FromFile（漏内存+锁文件）
            if (logo != null)
                e.Graphics.DrawImage(logo, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            else
            {
                using (var b = new SolidBrush(Theme.Accent))
                    e.Graphics.FillEllipse(b, Program.S(4), Program.S(4),
                        ClientSize.Width - Program.S(8), ClientSize.Height - Program.S(8));
            }
            base.OnPaint(e);
        }

        public void SetHalfHide(bool on)
        {
            _halfHide = on;
            if (!on && _parkedEdge != null)
            {
                _parkedEdge = null;
                Location = FlushPos();
            }
        }

        // ------------------------------------------------------------ 几何
        public static Point Snapped(Point pos, Size sz, Rectangle rect)
        {
            return Snapped(pos, sz, rect, EdgeMargin);
        }

        // margin = 贴边留的缝；停放位用 EdgeMargin，**滑出位必须用 0**（见 FlushPos）
        public static Point Snapped(Point pos, Size sz, Rectangle rect, int margin)
        {
            var x = pos.X; var y = pos.Y;
            if (x < rect.Left + margin) x = rect.Left + margin;
            if (x > rect.Right - sz.Width - margin) x = rect.Right - sz.Width - margin;
            if (y < rect.Top + margin) y = rect.Top + margin;
            if (y > rect.Bottom - sz.Height - margin) y = rect.Bottom - sz.Height - margin;
            if (x - rect.Left < Stick) x = rect.Left + margin;
            if (rect.Right - (x + sz.Width) < Stick) x = rect.Right - sz.Width - margin;
            if (y - rect.Top < Stick) y = rect.Top + margin;
            if (rect.Bottom - (y + sz.Height) < Stick) y = rect.Bottom - sz.Height - margin;
            return new Point(x, y);
        }

        public static string DetectEdge(Point pos, Size sz, Rectangle rect)
        {
            var dl = Math.Abs(pos.X - (rect.Left + EdgeMargin));
            var dr = Math.Abs(pos.X - (rect.Right - sz.Width - EdgeMargin));
            var dt = Math.Abs(pos.Y - (rect.Top + EdgeMargin));
            var db = Math.Abs(pos.Y - (rect.Bottom - sz.Height - EdgeMargin));
            var min = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
            if (min > Stick) return null;
            if (min == dl) return "left";
            if (min == dr) return "right";
            if (min == dt) return "top";
            return "bottom";
        }

        public static Point Parked(Point flush, Size sz, Rectangle rect, string edge)
        {
            switch (edge)
            {
                case "left": return new Point(rect.Left - sz.Width / 2, flush.Y);
                case "right": return new Point(rect.Right - (sz.Width - sz.Width / 2), flush.Y);
                case "top": return new Point(flush.X, rect.Top - sz.Height / 2);
                default: return new Point(flush.X, rect.Bottom - (sz.Height - sz.Height / 2));
            }
        }

        Rectangle WorkArea()
        {
            return Screen.FromPoint(Location).WorkingArea;
        }

        Point FlushPos()
        {
            // 滑出目标位**贴到屏幕边（margin=0）**：若按 EdgeMargin 留那 3px 缝，滑出时窗口右缘会
            // 从 2593 退到 2557，把停在 2557~2559 的光标"漏"到窗外 → 假 MouseLeave → 缩回 →
            // 又压住光标 → 又滑出 → **鬼畜抖动**（2026-10-05 实测缝=3px，贴边后=0px）。
            // 停放位仍走 Snapped(...)（带 EdgeMargin），只有滑出位贴边。
            return Snapped(_parkedPos != Point.Empty ? _parkedPos : Location,
                           Size, WorkArea(), 0);
        }

        // ------------------------------------------------------------ 拖动/单击
        void CapsuleMouseDown(MouseEventArgs e)
        {
        }
        void CapsuleMouseDownReal(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressGlobal = Cursor.Position;
                _pressWindow = Location;
                _dragged = false;
                StopPop();
            }
        }

        void CapsuleMouseMoveReal()
        {
            if (_pressGlobal == Point.Empty) return;
            var d = Cursor.Position;
            d = new Point(d.X - _pressGlobal.X, d.Y - _pressGlobal.Y);
            if (Math.Abs(d.X) + Math.Abs(d.Y) > 5)
            {
                _dragged = true;
                _parkedEdge = null;
                Location = new Point(_pressWindow.X + (Cursor.Position.X - _pressGlobal.X),
                                     _pressWindow.Y + (Cursor.Position.Y - _pressGlobal.Y));
            }
        }

        void CapsuleMouseUpReal(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _pressGlobal == Point.Empty) { return; }
            var wasDrag = _dragged;
            _pressGlobal = Point.Empty;
            _dragged = false;   // 必须复位：否则首次 hover 不滑出
            if (wasDrag) SnapNow();
            else if (Clicked != null) Clicked(this, EventArgs.Empty);
        }

        // 进出**都不设动画闸**（与 v2 float_bar.py 一致）：动画跑到一半就移开 → 立刻缩回；
        // 缩回途中又移入 → 立刻滑出。安全性由 StopPop() 保证（每段动画互相干净接管）。
        //
        // 移出这边**再加一道位置护栏**（2026-10-05 鬼畜抖动的根治）：
        // 滑出时窗口会从光标底下往屏幕里挪，如果按"收到 MouseLeave 就缩回"，就会
        // 滑出→假移出→缩回→又压住光标→又滑出 …… 无限来回。
        // 所以移出时先看光标**现在**在哪：还在热区（停放位 ∪ 滑出位）里 = 它其实仍压在图标上
        // → 不缩回，改开一次按需看门狗；等光标真走了再缩回。
        // 进入仍由事件驱动（否则测试与真实手感都依赖轮询）。
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (_parkedEdge == null || _dragged || _pressGlobal != Point.Empty) return;
            SlideTo(FlushPos());
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_parkedEdge == null || !_halfHide || _dragged || _pressGlobal != Point.Empty) return;
            if (WantOut()) { ArmWatchdog(); return; }   // 光标还在图标邻域 → 不缩回（防抖）
            SlideTo(_parkedPos);
        }

        // 只在位置真的需要变时才动 → 重复事件天然是空操作，不会自激
        void SlideTo(Point target)
        {
            if (Location != target) PopTo(target);
        }

        bool WantOut()
        {
            Point p = Cursor.Position;      // 只读，绝不移动光标
            return new Rectangle(_parkedPos, Size).Contains(p)
                || new Rectangle(FlushPos(), Size).Contains(p);
        }

        // 按需看门狗：只在"疑似卡住"时开（200ms 复查），收回即停。正常情况永不运行。
        void ArmWatchdog()
        {
            if (_watchdog == null)
            {
                _watchdog = new Timer { Interval = 200 };
                _watchdog.Tick += WatchdogTick;
            }
            if (!_watchdog.Enabled) _watchdog.Start();
        }

        void WatchdogTick(object s, EventArgs e)
        {
            if (_parkedEdge == null || !_halfHide || _dragged || _pressGlobal != Point.Empty)
            { _watchdog.Stop(); return; }
            if (WantOut()) return;              // 还压在图标上 → 保持滑出，继续等
            SlideTo(_parkedPos);                // 光标真走了 → 缩回
            _watchdog.Stop();                   // 收敛即停表（不常驻）
        }

        // 停掉当前滑出动画。**必须同时摘掉 Tick 处理器**：只 Stop() 不摘的话，处理器会残留在
        // Timer 上当孤儿——它继续数自己的步数，数满时又去 Stop() 掉新动画，行为彻底乱掉。
        // （v3 早期版本这里还额外维护一个 `_hoverAnim` 闸，用它挡"动画进行中的 enter/leave"；
        //   结果动画中途按下只 Stop() 没复位该标志 → 永久卡 true → 滑动探出彻底失效，
        //   2026-10-05 用户实测"多试了几下就再也不弹出"。现按 v2 语义去掉该闸：进出随时互相打断。）
        void StopPop()
        {
            _popTimer.Stop();
            if (_popTick != null)
            {
                _popTimer.Tick -= _popTick;
                _popTick = null;
            }
        }

        void PopTo(Point target)
        {
            // 130ms 滑出动画：8 步插值（无依赖、CPU 可忽略）
            StopPop();
            var start = Location;
            int steps = 8, i = 0;
            _popTimer.Interval = 16;
            EventHandler tick = null;
            tick = (s, ev) =>
            {
                i++;
                double t = Math.Min(1.0, i / (double)steps);
                double ease = 1 - Math.Pow(1 - t, 3);
                Location = new Point(
                    start.X + (int)((target.X - start.X) * ease),
                    start.Y + (int)((target.Y - start.Y) * ease));
                if (i >= steps)
                {
                    Location = target;          // 收尾对齐，消掉取整误差
                    _popTimer.Stop();
                    _popTimer.Tick -= tick;
                    if (_popTick == tick) _popTick = null;
                }
            };
            _popTick = tick;
            _popTimer.Tick += tick;
            _popTimer.Start();
        }

        public void SnapNow()
        {
            var rect = WorkArea();
            var flush = Snapped(Location, Size, rect);
            var edge = DetectEdge(flush, Size, rect);
            if (edge != null && _halfHide)
            {
                _parkedEdge = edge;
                _parkedPos = Parked(flush, Size, rect, edge);
                Location = _parkedPos;
            }
            else
            {
                _parkedEdge = null;
                _parkedPos = Point.Empty;
                Location = flush;
            }
            if (PositionCommitted != null) PositionCommitted(this, EventArgs.Empty);
        }

        public void ShowRestored(Point pos)
        {
            // 找可见面积最大的屏幕；几乎完全在屏幕外 → 吸到左缘半隐藏
            Rectangle best = Rectangle.Empty; long bestArea = -1;
            foreach (Screen s in Screen.AllScreens)
            {
                var inter = Rectangle.Intersect(new Rectangle(pos, Size), s.Bounds);
                long area = (long)inter.Width * inter.Height;
                if (area > bestArea) { bestArea = area; best = s.Bounds; }
            }
            if (best == Rectangle.Empty || bestArea < (long)Size.Width * Size.Height * 0.15)
            {
                var wa = (best == Rectangle.Empty ? Screen.PrimaryScreen : Screen.FromRectangle(best)).WorkingArea;
                var flush = Snapped(pos, Size, wa);
                pos = Parked(flush, Size, wa, "left");
            }
            Location = pos;
            var rect = Screen.FromPoint(pos).WorkingArea;
            if (Location.X < rect.Left) _parkedEdge = "left";
            else if (Location.X + Size.Width > rect.Right) _parkedEdge = "right";
            else if (Location.Y < rect.Top) _parkedEdge = "top";
            else if (Location.Y + Size.Height > rect.Bottom) _parkedEdge = "bottom";
            else _parkedEdge = null;
            _parkedPos = _parkedEdge != null ? pos : Point.Empty;
            Show();
            // 存档里的 bar_x/bar_y 可能是"窗口被钳大"时代算出来的（202 宽窗口贴右缘会存 2459），
            // 按当前真实尺寸重算一次吸附/半隐藏，否则图标会以"整块全露、离边一截"的样子出现。
            SnapNow();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_watchdog != null) { _watchdog.Stop(); _watchdog.Dispose(); _watchdog = null; }
            base.OnFormClosed(e);
        }

        // .NET 4.8 PMv2 下框架会在句柄创建/显示器变化时按"假设设计DPI=96"重排窗体，
        // 导致内容被 Region 裁切、吸附几何错位。本程序全物理像素自管尺寸，
        // 直接吞掉 WM_DPICHANGED(0x02E0)，框架永远不碰尺寸。
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) return;   // WM_DPICHANGED
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Size = new Size(CapsuleSize, CapsuleSize);   // 复位（防框架重排）
        }

        // 每次变为可见就核对一次真实置顶位：TopMost 属性只在建句柄时挂一次
        // WS_EX_TOPMOST，运行中被环境剥掉后自己不会恢复（置顶守护的兜底之一）
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Program.EnsureTopMost(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _popTimer = new Timer { Interval = 16 };
            // 子控件事件统一走窗体（窗体即整个图标）
            MouseDown += (s, ev) => CapsuleMouseDownReal(ev);
            MouseMove += (s, ev) => CapsuleMouseMoveReal();
            MouseUp += (s, ev) => CapsuleMouseUpReal(ev);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Right)
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("展开抽屉", null, (s, ev) =>
                { if (Clicked != null) Clicked(this, EventArgs.Empty); });
                // 2026-10-17：抽屉头部那个「×（缩到托盘）」去掉后，这个动作挪到悬浮胶囊右键，
                // 托盘菜单不再加（用户裁决）。
                menu.Items.Add("隐藏到托盘（只留图标）", null, (s, ev) => Program.MinimizeToTray());
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("退出", null, (s, ev) => Program.QuitApp());
                menu.Show(Cursor.Position);
            }
        }
    }
}
