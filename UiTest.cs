// -*- coding: utf-8 -*-
// UiTest.cs — 被动 UI 自检（PromptDrawer.exe --uitest）
// 规矩：只在屏幕外显示窗口，绝不移动鼠标 / 键盘，绝不整屏截图，绝不读写用户数据。
// 覆盖 2026-10-05 定案的胶囊 bug：Win32 会把顶层窗口钳到 SM_CXMINTRACK×SM_CYMINTRACK
// （本机 150% 缩放实测 202×56），Show() 时把 66×66 的胶囊撑成 202×66。
// 退出码 0 = 全过。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PromptDrawer
{
    static class UiTest
    {
        static int _pass, _fail;

        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        static void Check(string name, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("  PASS  " + name + "   " + detail); }
            else { _fail++; Console.WriteLine("  FAIL  " + name + "   " + detail); }
        }

        // 反射取值必须**顺着继承链**找：QuietDrawer 之类的子类用 GetType() 找不到基类私有字段
        // （2026-10-06 踩过：只查 GetType() → GetField 返回 null → NullReferenceException）
        static FieldInfo Field(Type t, string n)
        {
            while (t != null)
            {
                FieldInfo f = t.GetField(n, Priv);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        static object GetPriv(object o, string n) { return Field(o.GetType(), n).GetValue(o); }

        // Accent 近似判定（描边有抗锯齿过渡，需容差）
        static bool IsAccentish(Color c)
        {
            var a = Theme.Accent;
            return Math.Abs(c.R - a.R) < 40 && Math.Abs(c.G - a.G) < 40 && Math.Abs(c.B - a.B) < 40;
        }

        // z 序探针：EnumWindows 的回调顺序**就是** z 序（从最上层开始），索引越小越上层
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        static int ZOrderIndex(IntPtr h)
        {
            var list = new List<IntPtr>();
            EnumWindows(delegate(IntPtr w, IntPtr l) { list.Add(w); return true; }, IntPtr.Zero);
            return list.IndexOf(h);
        }

        // 在控件树里按 Text 找控件（T23 找记事本顶部那个「存档」按钮）
        static Control FindByText(Control root, string text)
        {
            foreach (Control c in root.Controls)
            {
                if (c.Text == text) return c;
                Control hit = FindByText(c, text);
                if (hit != null) return hit;
            }
            return null;
        }

        // chips 的显示顺序：RefreshChips 按显示顺序把 chip 加进 _chips，所以 Controls 顺序即显示顺序
        static List<string> ChipTexts(DrawerForm d)
        {
            var list = new List<string>();
            Panel chips = (Panel)GetPriv(d, "_chips");
            foreach (Control c in chips.Controls)
            {
                ChipButton chip = c as ChipButton;
                if (chip != null) list.Add(chip.Text);
            }
            return list;
        }
        static void SetPriv(object o, string n, object v) { Field(o.GetType(), n).SetValue(o, v); }
        static void CallPriv(object o, string n, params object[] a)
        {
            // 必须沿继承链找：私有方法不"继承"，QuietDrawer.GetType() 上找不到基类私有方法
            //（与 Field() 同款教训；T17 调 DrawerForm.DoFind 时踩到）
            Type t = o.GetType();
            while (t != null)
            {
                MethodInfo m = t.GetMethod(n, Priv);
                if (m != null) { m.Invoke(o, a); return; }
                t = t.BaseType;
            }
            throw new Exception("找不到方法 " + n);
        }

        // 只泵消息循环，不显示任何窗口、不产生任何输入
        static void Pump(int ms)
        {
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(4); }
        }

        // 泵消息直到条件成立或超时。动画是 WM_TIMER 驱动的，系统负载下会晚到；
        // 用固定 sleep 会假失败——实测（2026-10-05，60 次采样）整段动画实际耗时
        // 201~313ms（标称 8×16=128ms，被 Sleep 粒度拉长），原来的 300ms 窗口正好卡在最大值之下。
        // 超时只在失败路径花时间（达成即返回），所以给足余量。
        static bool PumpUntil(Func<bool> cond, int timeoutMs)
        {
            DateTime end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                if (cond()) return true;
                Application.DoEvents();
                System.Threading.Thread.Sleep(4);
            }
            return cond();
        }

        // 与 CapsuleForm 同构：某条边的"贴齐位"（margin 指定，0 = 贴到屏幕边）
        static Point EdgeFlush(Rectangle rect, Size sz, string edge, int margin)
        {
            int x = rect.Left + rect.Width / 2, y = rect.Top + rect.Height / 2;
            if (edge == "left") x = rect.Left + margin;
            else if (edge == "right") x = rect.Right - sz.Width - margin;
            else if (edge == "top") y = rect.Top + margin;
            else y = rect.Bottom - sz.Height - margin;
            return new Point(x, y);
        }

        public static int Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            int cs = CapsuleForm.CapsuleSize;          // 触发 Program 静态构造（DPI 测定）
            Console.WriteLine("UI selftest  Scale=" + Program.Scale + "  CapsuleSize=" + cs);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Console.WriteLine("  workingArea=" + wa);
            Console.WriteLine();

            Size sz = new Size(cs, cs);
            CapsuleForm cap = new CapsuleForm();
            cap.StartPosition = FormStartPosition.Manual;
            cap.Location = new Point(wa.Left - 3000, wa.Top - 3000);   // 屏幕外，桌面看不到
            cap.Show();
            cap.Update();

            // T1 核心回归：Show() 后窗口必须还是胶囊尺寸（TOOLWINDOW 生效 = 没被系统钳大）
            Check("T1 胶囊 Show 后尺寸不被系统钳大",
                  cap.Size.Width == cs && cap.Size.Height == cs,
                  "Size=" + cap.Size.Width + "x" + cap.Size.Height + "（期望 " + cs + "x" + cs + "）");

            // T2 圆角 Region 必须跟窗口一样大（不一致 = 图标被裁一块）
            bool regOk = false; string regD;
            try
            {
                using (Graphics g = cap.CreateGraphics())
                {
                    RectangleF rb = cap.Region.GetBounds(g);
                    regD = "Region=" + rb.Width + "x" + rb.Height + " vs Size=" + cap.Size.Width + "x" + cap.Size.Height;
                    regOk = Math.Abs(rb.Width - cap.Size.Width) <= 1.5 && Math.Abs(rb.Height - cap.Size.Height) <= 1.5;
                }
            }
            catch (Exception ex) { regD = "异常 " + ex.Message; }
            Check("T2 圆角 Region 与窗口尺寸一致", regOk, regD);
            cap.Hide();

            // T3 贴边几何（纯函数，用真实工作区）：越界夹回 → 认右缘 → 半埋正好露一半
            Point flush = CapsuleForm.Snapped(new Point(wa.Right + 500, wa.Top + 300), sz, wa);
            string edge = CapsuleForm.DetectEdge(flush, sz, wa);
            Point parked = CapsuleForm.Parked(flush, sz, wa, edge == null ? "right" : edge);
            Check("T3a 屏幕外位置被夹回右缘齐平",
                  flush.X == wa.Right - sz.Width - Program.S(2), "flush.X=" + flush.X);
            Check("T3b 右缘被识别为 right", edge == "right", "edge=" + (edge == null ? "null" : edge));
            Check("T3c 半埋后正好露出一半",
                  wa.Right - parked.X == cs / 2, "可见 " + (wa.Right - parked.X) + "px / 共 " + cs + "px");

            // T4 旧存档位置（"窗口被钳大"时代算出来的）必须被重新吸附/半埋
            //    不 Show，避免在用户桌面上闪现；吸附纯几何，不依赖窗口已显示。
            int staleX = wa.Right - cs - 10;          // 距右缘齐平位 7px（< Stick 42）的旧值
            cap.Location = new Point(staleX, wa.Top + 300);
            cap.SnapNow();
            Check("T4 旧存档位置按新尺寸重新半埋",
                  wa.Right - cap.Location.X == cs / 2,
                  "Location=" + cap.Location + "  可见 " + (wa.Right - cap.Location.X) + "px");

            // T5 中部位置不该被乱吸
            cap.Location = new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2);
            cap.SnapNow();
            Check("T5 屏幕中部位置保持不吸附",
                  cap.Location.X == wa.Left + wa.Width / 2 && cap.Location.Y == wa.Top + wa.Height / 2,
                  "Location=" + cap.Location);

            // ---- T6~T8 悬停滑出状态机 ----
            // 回归 2026-10-05 的 bug：动画跑到一半时按下，只停表不摘处理器 → 残留的状态闸
            // 永久卡死 → OnMouseEnter/OnMouseLeave 前提永不成立 → "多试了几下就再也不弹出"。
            // 下面全程窗体保持隐藏（T2 之后已 Hide），位移不会出现在桌面上。
            // （parked / flush 复用 T3 已算好的值：flush=(2491,300) parked=(2527,300)）
            SetPriv(cap, "_parkedEdge", "right");
            SetPriv(cap, "_parkedPos", parked);
            cap.Location = parked;
            Timer t = (Timer)GetPriv(cap, "_popTimer");
            // 滑出目标是"贴屏幕边"的贴齐位（margin=0）——2026-10-05 起不再留 EdgeMargin 的缝。
            // 用生产函数本身算期望值，避免测试自己另写一套公式。
            Point hover = CapsuleForm.Snapped(parked, sz, wa, 0);

            CallPriv(cap, "OnMouseEnter", EventArgs.Empty);
            Pump(50);                                       // 8 步动画走到约第 3 步
            CallPriv(cap, "CapsuleMouseDownReal", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            CallPriv(cap, "CapsuleMouseUpReal", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            Pump(300);
            Check("T6 动画中途按下 → 动画表已停、不留残留", !t.Enabled, "动画表在跑=" + t.Enabled);

            cap.Location = parked;
            CallPriv(cap, "OnMouseEnter", EventArgs.Empty);
            bool restarted = t.Enabled;                     // 卡死的旧版本这里会是 false
            PumpUntil(() => cap.Location == hover, 1500);
            Check("T7 被打断过之后，再移入仍能完整滑出",
                  restarted && cap.Location == hover,
                  "能重启动画=" + restarted + "  Location=" + cap.Location + "（期望 " + hover + "）");

            int dirty = 0;
            for (int k = 0; k < 12; k++)
            {
                cap.Location = parked;
                CallPriv(cap, "OnMouseEnter", EventArgs.Empty);
                Pump(20 + (k % 4) * 12);                    // 每轮打断点都不同
                CallPriv(cap, "CapsuleMouseDownReal", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                CallPriv(cap, "CapsuleMouseUpReal", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Pump(120);
                cap.Location = parked;
                CallPriv(cap, "OnMouseEnter", EventArgs.Empty);   // 再移入：必须还能滑出
                bool ok2 = t.Enabled;
                PumpUntil(() => cap.Location == hover, 1500);
                if (!ok2 || cap.Location != hover) dirty++;
                cap.Location = parked;
            }
            Check("T8 反复打断 12 轮后仍能完整滑出（“多试几下”回归）",
                  dirty == 0, "失败轮数=" + dirty);

            // ---- T9 滑出位必须"贴屏幕边"，不许留缝（鬼畜抖动的根因回归）----
            // 滑出位若留 EdgeMargin 的 3px 缝，滑出后窗口会从光标底下挪走 → 假 MouseLeave →
            // 缩回 → 又压住光标 → 又滑出 → 无限来回。判据：半埋窗口"屏幕内那部分"
            // 必须被滑出窗口完全包含（四条边都要）。
            int worstBand = 0; string bandDetail = "四条边均无漏出";
            string[] allEdges = new string[] { "left", "right", "top", "bottom" };
            foreach (string ed in allEdges)
            {
                Point parkPos = CapsuleForm.Parked(EdgeFlush(wa, sz, ed, Program.S(2)), sz, wa, ed);
                Point outPos = EdgeFlush(wa, sz, ed, 0);        // 滑出位：贴边（margin=0）
                Rectangle vis = Rectangle.Intersect(new Rectangle(parkPos, sz), wa);
                Rectangle cover = Rectangle.Intersect(vis, new Rectangle(outPos, sz));
                int band = vis.IsEmpty ? 0 : Math.Max(vis.Width - cover.Width, vis.Height - cover.Height);
                if (band > worstBand)
                {
                    worstBand = band;
                    bandDetail = ed + " 边漏出 " + band + "px（半埋 " + vis + " vs 滑出 "
                        + new Rectangle(outPos, sz) + "）";
                }
            }
            Check("T9 滑出位贴屏幕边：半埋窗口不被漏出（鬼畜抖动根因）",
                  worstBand == 0, bandDetail);

            // ---- T10 反复重绘不得吃内存（OnPaint 里 FromFile 的回归）----
            // 实测未修前 200 次重绘 +412.7MB（这就是"长时间开着内存涨到 400MB"的根因）。
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Process me = Process.GetCurrentProcess();
            me.Refresh();
            long wsBefore = me.WorkingSet64;
            using (Bitmap tiny = new Bitmap(16, 16))
                for (int i = 0; i < 120; i++) cap.DrawToBitmap(tiny, new Rectangle(0, 0, 16, 16));
            me.Refresh();
            long grew = (me.WorkingSet64 - wsBefore) / 1048576;
            Check("T10 反复重绘 120 次不涨内存（OnPaint 每次 FromFile 的回归）",
                  grew < 8, "WorkingSet 增量 " + grew + "MB（未修前实测约 +247MB）");

            cap.Close();
            cap.Dispose();

            // ---- T11/T12 抽屉：页签接线 / 设置页布局 ----
            // 数据目录指向系统临时目录 —— 绝不碰用户的 prompts_data.json
            string tmp = Path.Combine(Path.GetTempPath(), "pmv3_uitest_data");
            try { Directory.CreateDirectory(tmp); }
            catch (Exception) { }
            Store.DataDir = tmp;
            Store st = new Store();
            st.Load();
            // DrawerForm 会读 Program 的静态 _store（HotkeyLabelText 等）→ 接上它
            typeof(Program).GetField("_store", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, st);

            QuietDrawer dr = new QuietDrawer(st);          // ShowWithoutActivation，不抢焦点
            dr.StartPosition = FormStartPosition.Manual;
            dr.Location = new Point(wa.Left - 3000, wa.Top - 3000);
            dr.Show();
            Pump(400);

            Control header = (Control)GetPriv(dr, "_header");
            int tabBad = 0; string tabDetail = "三个页签都能切页";
            for (int ti = 0; ti < 3; ti++)
            {
                TabButton tb = null;
                foreach (Control c in header.Controls)
                {
                    TabButton x = c as TabButton;
                    if (x != null && x.Index == ti) tb = x;
                }
                if (tb == null) { tabBad++; tabDetail = "找不到第 " + ti + " 个页签按钮"; continue; }
                tb.PerformClick();                          // 等价于点一下，不产生真实输入
                Pump(150);
                int cur = (int)GetPriv(dr, "_tab");
                bool pages = ((Control)GetPriv(dr, "_listPage")).Visible == (ti == 0)
                          && ((Control)GetPriv(dr, "_notePage")).Visible == (ti == 1)
                          && ((Control)GetPriv(dr, "_settingsPage")).Visible == (ti == 2);
                if (cur != ti || !pages)
                {
                    tabBad++;
                    tabDetail = "点第 " + ti + " 个页签后 _tab=" + cur + "、页面切换正确=" + pages;
                }
            }
            Check("T11 三个页签点了都能切页（此前完全没接 Click，记事本/设置点不动）",
                  tabBad == 0, tabDetail);

            // T12 设置页：横纵都相交才算真重叠（2026-10-06 之前 5 处、最多压掉 22px）
            Control sp = (Control)GetPriv(dr, "_settingsPage");
            List<Control> seen = new List<Control>();
            int overlaps = 0; string ovDetail = sp.Controls.Count + " 个控件互不重叠";
            foreach (Control c in sp.Controls)
            {
                foreach (Control o in seen)
                {
                    bool vx = c.Left < o.Right && o.Left < c.Right;
                    bool vy = c.Top < o.Bottom && o.Top < c.Bottom;
                    if (vx && vy)
                    {
                        overlaps++;
                        if (overlaps == 1)
                            ovDetail = "首次重叠：" + (o.Text.Length > 10 ? o.Text.Substring(0, 10) : o.Text)
                                     + " 与 " + (c.Text.Length > 10 ? c.Text.Substring(0, 10) : c.Text);
                    }
                }
                seen.Add(c);
            }
            Check("T12 设置页控件无重叠（此前 5 处真重叠，被压住的按钮点不到）",
                  overlaps == 0, overlaps == 0 ? ovDetail : overlaps + " 处 —— " + ovDetail);

            // ---- T18 自绘复选框：方块必须是皮肤色（2026-10-07 加）----
            // 此前用 OS 原生 CheckBox：暗色下未勾选是近纯白 (243,243,243)、勾选是系统蓝 (0,95,184)，
            // 与皮肤强调色都不一致。判据：真渲染像素 ≈ Theme.Surface（未勾选）/ Theme.Accent（勾选）。
            CheckBox cbx = null;
            foreach (Control c in sp.Controls) { if (c is ThemedCheckBox) { cbx = (CheckBox)c; break; } }
            if (cbx == null)
            {
                Check("T18 自绘复选框走皮肤色", false, "设置页里找不到 ThemedCheckBox");
            }
            else
            {
                int boxPx = Program.S(16);
                Point probePt = new Point(Program.S(2) + boxPx / 2, cbx.Height / 2);
                string d1, d2; bool ok1, ok2;
                cbx.Checked = false; cbx.Refresh(); Application.DoEvents();
                using (Bitmap b = new Bitmap(cbx.Width, cbx.Height))
                {
                    cbx.DrawToBitmap(b, new Rectangle(0, 0, cbx.Width, cbx.Height));
                    Color got = b.GetPixel(probePt.X, probePt.Y);
                    Color want = Theme.Surface;
                    ok1 = Math.Abs(got.R - want.R) <= 12 && Math.Abs(got.G - want.G) <= 12 && Math.Abs(got.B - want.B) <= 12;
                    d1 = "未勾选 " + got.R + "," + got.G + "," + got.B + "（期望≈" + want.R + "," + want.G + "," + want.B + "；OS 原生是 243,243,243）";
                }
                cbx.Checked = true; cbx.Refresh(); Application.DoEvents();
                using (Bitmap b = new Bitmap(cbx.Width, cbx.Height))
                {
                    cbx.DrawToBitmap(b, new Rectangle(0, 0, cbx.Width, cbx.Height));
                    Color got = b.GetPixel(probePt.X, probePt.Y);
                    Color want = Theme.Accent;
                    ok2 = Math.Abs(got.R - want.R) <= 12 && Math.Abs(got.G - want.G) <= 12 && Math.Abs(got.B - want.B) <= 12;
                    d2 = "已勾选 " + got.R + "," + got.G + "," + got.B + "（期望≈" + want.R + "," + want.G + "," + want.B + "；OS 原生是 0,95,184）";
                }
                cbx.Checked = false;
                Check("T18 自绘复选框走皮肤色", ok1 && ok2, d1 + " / " + d2);
            }

            // ---- T19 两处「开机自动启动」的勾必须能互相同步（2026-10-07 加）----
            // 托盘菜单那一项与设置页那个复选框原先各读各的：一处改了另一处不变。
            // 这里只测**同步路径**（纯 UI，不碰注册表！）：
            //   ① 设置页的勾要跟着 SyncAutostartUI 变；② 同步**不能回写 store**（防递归）；
            //   ③ 托盘侧在还没建菜单时调用要安全返回。
            // ⚠️ 绝不在这里 PerformClick 那个复选框 —— 它的 handler 会真去写用户注册表。
            CheckBox acb = null;
            try { acb = (CheckBox)GetPriv(dr, "_cbAutostart"); } catch (Exception) { }
            if (acb == null)
            {
                Check("T19 开机自启动两处勾能同步", false, "设置页里找不到 _cbAutostart");
            }
            else
            {
                bool before = Convert.ToBoolean(st.GetSetting("autostart", true));
                dr.SyncAutostartUI(!before);
                bool uiFollowed = acb.Checked == !before;
                bool storeUntouched = Convert.ToBoolean(st.GetSetting("autostart", true)) == before;
                dr.SyncAutostartUI(before);
                bool backTo = acb.Checked == before;
                Program.SyncTrayAutostart(false);      // _trayAutoItem 为 null（测试没建托盘）→ 必须安全
                Program.SyncTrayAutostart(before);
                Check("T19 开机自启动两处勾能同步（且同步不写 store、托盘未建时安全）",
                      uiFollowed && storeUntouched && backTo,
                      "勾跟着变=" + uiFollowed + "、store未被改=" + storeUntouched + "、能复位=" + backTo);
            }

            // ---- T13 分类 chips 不越界（2026-10-06 视觉评审定位的溢出 bug 回归）----
            // 此前 WrapContents=false：12 个 chip 时第 7/8 个完全被裁出面板右缘（看不到点不到）。
            // 塞 10 个长名分类逼出多行，断言每个 chip 完整落在 _chips 客户区内（横竖两个方向都查）。
            string[] longNames = new string[] { "角色设定", "场景氛围", "质量强化", "构图视角", "风格画师",
                                                "服装道具", "表情动作", "光影色彩", "文字排版", "负面禁忌" };
            foreach (string nm in longNames) st.AddCategory(nm + "测试分类名", "📁", "#2478A8", "");
            dr.RefreshChips();
            Pump(200);
            Panel chips = (Panel)GetPriv(dr, "_chips");   // 2026-10-07 起 5 列等宽网格（手动定位 Panel）
            int clipped = 0; string clipDetail = chips.Controls.Count + " 个 chip 全部完整可见";
            foreach (Control c in chips.Controls)
            {
                if (!chips.ClientRectangle.Contains(c.Bounds))
                {
                    clipped++;
                    if (clipped == 1)
                        clipDetail = "首个被裁 chip「" + c.Text + "」bounds=" + c.Bounds
                                   + " client=" + chips.ClientRectangle;
                }
            }
            Control chipsPanel = (Control)GetPriv(dr, "_chipsPanel");
            Check("T13 分类 chips 网格无一被裁（一行 5 个，超宽截断前 4 位）",
                  clipped == 0 && chips.Controls.Count >= 13,
                  clipDetail + "；chips 行高=" + chipsPanel.Height);

            // ---- T14 Toast 必须挂在控件树里（此前从未 Controls.Add → 所有操作提示隐形）----
            Control toast = (Control)GetPriv(dr, "_toast");
            Check("T14 Toast 挂在窗体控件树上（此前从未 Add → 复制/保存/删除等提示全部隐形）",
                  toast != null && toast.Parent == dr,
                  toast == null ? "toast 为 null" : "Parent=" + (toast.Parent == null ? "null" : toast.Parent.GetType().Name));

            // ---- T15 热键注册失败不落盘（此前失败热键被持久化，RestoreHotkey 读回坏值 → 热键全丢）----
            // 用 Ctrl+Alt+F24 占住热键 → 对抽屉注册同一组合必失败 → 断言设置里没留下这个热键。
            typeof(Program).GetField("_drawer", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, dr);
            bool holderOk = false, regFailed = false, unchanged = false;
            using (Form holder = new Form())
            {
                IntPtr hh = holder.Handle;                           // 先建句柄
                holderOk = RegisterHotKey(hh, 7, 0x2 | 0x1, 0x87);   // Ctrl+Alt+F24 占位
                object before = st.GetSetting("hotkey");
                regFailed = !Program.RegisterDrawerHotkey(0x2u | 0x1u | 0x4000u, 0x87u, "Ctrl+Alt+F24", true);
                object after = st.GetSetting("hotkey");
                UnregisterHotKey(hh, 7);
                unchanged = (before == null) ? (after == null) : before.Equals(after);
            }
            try { UnregisterHotKey(dr.Handle, 1); } catch (Exception) { }
            typeof(Program).GetField("_drawer", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            Check("T15 热键被占用 → 注册失败且不落盘（此前坏热键被保存、回退读到坏值 → 热键全丢）",
                  holderOk && regFailed && unchanged,
                  "占位注册=" + holderOk + " 目标注册失败=" + regFailed + " 设置未变=" + unchanged);

            // ---- T16 置顶守护（用户诉求：悬浮图标永远置顶）----
            // 实测线上实例运行中 WS_EX_TOPMOST 会被环境剥掉（屏幕外 8 轮开合无法复现）。
            // 这里手动扮演环境：对抽屉 HWND_NOTOPMOST 剥位 → 守护定时器必须在 ~1s 内补回。
            typeof(Program).GetField("_drawer", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, dr);
            Program.StartTopmostGuard();
            const uint swp = 0x1 | 0x2 | 0x10;             // NOSIZE | NOMOVE | NOACTIVATE
            SetWindowPos(dr.Handle, new IntPtr(-2), 0, 0, 0, 0, swp);   // HWND_NOTOPMOST 剥位
            int exAfterDemote = GetWindowLong(dr.Handle, -20);
            Pump(1600);                                     // 守护间隔 1s，给足一轮
            int exAfterGuard = GetWindowLong(dr.Handle, -20);
            bool demoted = (exAfterDemote & 0x8) == 0;
            bool restored = (exAfterGuard & 0x8) != 0;
            typeof(Program).GetField("_drawer", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            Check("T16 置顶位被剥后守护自动补回（悬浮图标被网页盖住的根治）",
                  demoted && restored,
                  "剥位成功=" + demoted + " 守护补回=" + restored
                  + "（剥后ex=0x" + exAfterDemote.ToString("X") + " 补后ex=0x" + exAfterGuard.ToString("X") + "）");

            // ---- T17 记事本查找栏（记事本完善批次）----
            // 填充 4 行文本（故意用裸 \n——刷新时会归一为 \r\n）→ 查找 "beta"：
            // 归一后文本为 "alpha\r\nbeta gamma\r\nbeta again\r\nend"，首处应落在 7，次处 19，计数 2/2；
            // 同时验证 CRLF 归一化（frozen9 评审抓到的裸 \n 不换行问题）。
            st.SetNoteContent(st.ActiveNoteId, "alpha\nbeta gamma\nbeta again\nend");
            dr.RefreshNotePages();
            dr.ShowFindBar();
            TextBox findBox = (TextBox)GetPriv(dr, "_findBox");
            findBox.Text = "beta";
            CallPriv(dr, "DoFind", true);
            TextBox noteEd = (TextBox)GetPriv(dr, "_noteEdit");
            bool crlfOk = noteEd.Text == "alpha\r\nbeta gamma\r\nbeta again\r\nend";
            int firstPos = noteEd.SelectionStart;
            CallPriv(dr, "DoFind", true);
            Label findCount = (Label)GetPriv(dr, "_findCount");
            Check("T17 记事本查找栏定位/循环/计数 + CRLF 归一",
                  crlfOk && firstPos == 7 && noteEd.SelectionStart == 19
                  && noteEd.SelectionLength == 4 && findCount.Text == "2/2",
                  "CRLF归一=" + crlfOk + " 首处=" + firstPos + "(期望7) 次处=" + noteEd.SelectionStart
                  + "(期望19) 计数=" + findCount.Text + "(期望2/2)");
            CallPriv(dr, "CloseFind");

            // ---- T20 快速捕获的标题提取（纯函数）----
            // 只测纯逻辑，**绝不碰剪贴板**（剪贴板是用户的，测试不能读写）。
            string fl1 = DrawerForm.FirstLineOf("银发少女\n第二行\n第三行");
            string fl2 = DrawerForm.FirstLineOf("   \r\n  \r\n ");
            string fl3 = DrawerForm.FirstLineOf(new string('长', 60));
            Check("T20 快速捕获标题提取（取首行 / 空白兜底 / 超长截断）",
                  fl1 == "银发少女" && fl2 == "未命名" && fl3.Length == 41 && fl3.EndsWith("…"),
                  "首行=" + fl1 + "、空白=" + fl2 + "、超长长度=" + fl3.Length + "(期望41)");

            // ---- T21 回收站对话框能建起来、条数显示正确 ----
            // 不开 ShowDialog（模态会卡住自检），只 Show 到屏幕外再关掉。
            string tid = null;
            try
            {
                var tp = st.AddPrompt("待回收的条目", "内容", null, "", false, "");
                tid = tp.id;
                st.DeletePrompts(new List<string> { tid });
                using (var td = new TrashDialog(st))
                {
                    td.StartPosition = FormStartPosition.Manual;
                    td.Location = new Point(-4000, -4000);      // 屏幕外
                    td.Show();
                    Pump(200);
                    string capTxt = td.Text;
                    bool ok = td.Visible && capTxt.Contains("1 条") && st.Trash.Count == 1;
                    td.Close();
                    Check("T21 回收站对话框可建/可显示、条数正确", ok, "标题=" + capTxt);
                }
            }
            catch (Exception ex)
            {
                Check("T21 回收站对话框可建/可显示、条数正确", false, ex.GetType().Name + ": " + ex.Message);
            }
            if (tid != null) { try { st.RestoreFromTrash(new List<string> { tid }); st.Save(); } catch (Exception) { } }

            // ---- T22 抽屉**没显示过**时也能刷新 ----
            // 快速捕获（托盘 → 从剪贴板新建）是在抽屉收起时跑的，之后要 RefreshChips/RefreshList；
            // 这里专门验证"从未 Show 过的抽屉"刷新不抛异常（不 Show，避免打扰桌面）。
            try
            {
                using (var hidden = new QuietDrawer(st))
                {
                    hidden.RefreshChips();
                    hidden.RefreshList();
                    Check("T22 抽屉未显示时可刷新（快速捕获在收起状态跑）",
                          hidden.Controls.Count > 0, "控件数=" + hidden.Controls.Count);
                }
            }
            catch (Exception ex)
            {
                Check("T22 抽屉未显示时可刷新（快速捕获在收起状态跑）",
                      false, ex.GetType().Name + ": " + ex.Message);
            }

            // ---- T23 记事本「存档」按钮：点了真的新建一份独立 TXT（端到端）----
            // 数据目录是系统临时目录 → 只写自己的临时目录，绝不碰用户数据 / 剪贴板 / 注册表。
            try
            {
                st.SetNoteContent(st.ActiveNoteId, "存档测试内容 ABC");
                dr.SwitchTab(1, false);      // 必须先切到记事本页：PerformClick 对**不可见**按钮不生效
                dr.RefreshNotePages();
                Pump(120);
                Control saveBtn = FindByText(dr, "存档");
                Button sb = saveBtn as Button;          // PerformClick 在 Button 上（Control 没有）
                string savesDir = Path.Combine(tmp, "saves");
                int before = Directory.Exists(savesDir) ? Directory.GetFiles(savesDir, "*.txt").Length : 0;
                if (sb != null) sb.PerformClick();
                Pump(250);
                string[] files = Directory.Exists(savesDir) ? Directory.GetFiles(savesDir, "*.txt") : new string[0];
                bool made = files.Length == before + 1;
                bool hasTxt = false;
                foreach (string f in files)
                {
                    try { if (File.ReadAllText(f).Contains("存档测试内容 ABC")) hasTxt = true; }
                    catch (Exception) { }
                }
                Check("T23 记事本「存档」按钮：新建独立 TXT 且内容正确",
                      sb != null && made && hasTxt,
                      "按钮=" + (sb != null) + " 新增文件=" + (files.Length - before) + " 内容命中=" + hasTxt);
            }
            catch (Exception ex)
            {
                Check("T23 记事本「存档」按钮：新建独立 TXT 且内容正确", false, ex.GetType().Name + ": " + ex.Message);
            }
            try { dr.SwitchTab(0, false); Pump(100); } catch (Exception) { }   // 切回列表页给 T24 用

            // ---- T24 分类左右移：chips 显示顺序真的跟着换（UI 层）----
            try
            {
                var c1 = st.AddCategory("甲组", "", "#2478A8", "");
                var c2 = st.AddCategory("乙组", "", "#2478A8", "");
                dr.RefreshChips(); Pump(150);
                var o1 = ChipTexts(dr);
                int i1 = o1.IndexOf("甲组"), i2 = o1.IndexOf("乙组");
                bool moved = st.MoveCategory(c1.id, 1);
                dr.RefreshChips(); Pump(150);
                var o2 = ChipTexts(dr);
                int j1 = o2.IndexOf("甲组"), j2 = o2.IndexOf("乙组");
                Check("T24 分类右移后 chips 顺序真的换了（只动这两个）",
                      i1 >= 0 && i2 == i1 + 1 && moved && j1 == i2 && j2 == i1 && o2.Count == o1.Count,
                      "前=" + i1 + "," + i2 + " 后=" + j1 + "," + j2 + " 数量=" + o1.Count + "→" + o2.Count);
            }
            catch (Exception ex)
            {
                Check("T24 分类右移后 chips 顺序真的换了（只动这两个）", false, ex.GetType().Name + ": " + ex.Message);
            }

            // ---- T25 光标在输入框时不自动收起（2026-10-10 用户新语义）----
            // 语义 = 按"**最后聚焦的是不是输入框**"判定（不是"本次展开编辑过"）。
            // 全部用**真聚焦**（真触发 Enter）来验，不塞标志位 —— 这样连"Enter 真的会触发"一起验了。
            // 屏幕外窗口 + 把 Program._capsule 临时置空（MouseInCapsule 会读**真实鼠标位置**）。
            try
            {
                var fDrawer = typeof(Program).GetField("_drawer", BindingFlags.NonPublic | BindingFlags.Static);
                var fCapsule = typeof(Program).GetField("_capsule", BindingFlags.NonPublic | BindingFlags.Static);
                object oldCapsule = fCapsule.GetValue(null);
                fCapsule.SetValue(null, null);
                fDrawer.SetValue(null, dr);
                SetPriv(dr, "_keepOpenWhileEditing", true);
                if (!dr.Visible) { dr.Show(); Pump(200); }

                // (a) 真聚焦记事本正文 → 标记应为"在输入框" → 失去激活时应保留抽屉
                dr.SwitchTab(1, false); Pump(200);
                TextBox ed = (TextBox)GetPriv(dr, "_noteEdit");
                ed.Focus(); Pump(150);
                bool flagOnInput = (bool)GetPriv(dr, "_focusInInput");
                CallPriv(dr, "OnDeactivate", EventArgs.Empty);
                Pump(150);
                bool stayedOnInput = dr.Visible;

                // (b) 真聚焦一个**可见的**非输入控件（列表）→ 标记应翻掉 → 失去激活时应收起
                //     注意：必须切回列表页 —— 隐藏页里的控件 Focus() 无效（第一版 T25 就栽在这儿：
                //     它找到的是列表页那个「＋ 新建」，而当时前台是记事本页）
                dr.SwitchTab(0, false); Pump(250);
                ((Control)GetPriv(dr, "_list")).Focus();
                Pump(150);
                bool flagCleared = !(bool)GetPriv(dr, "_focusInInput");
                CallPriv(dr, "OnDeactivate", EventArgs.Empty);
                Pump(150);
                bool collapsedOffInput = !dr.Visible;
                bool resetOnHide = !(bool)GetPriv(dr, "_focusInInput");

                // (c) 开关关掉 → 光标在输入框里也照旧收起
                dr.Show(); Pump(200);
                dr.SwitchTab(1, false); Pump(200);
                ((TextBox)GetPriv(dr, "_noteEdit")).Focus(); Pump(150);
                SetPriv(dr, "_keepOpenWhileEditing", false);
                bool flagOnInput2 = (bool)GetPriv(dr, "_focusInInput");
                CallPriv(dr, "OnDeactivate", EventArgs.Empty);
                Pump(150);
                bool collapsedWhenOff = !dr.Visible;

                SetPriv(dr, "_keepOpenWhileEditing", true);   // 还原
                fCapsule.SetValue(null, oldCapsule);
                fDrawer.SetValue(null, null);
                dr.Show(); Pump(120);

                Check("T25 光标在输入框才不收起（真聚焦判定 / 焦点移开即恢复 / 开关可关）",
                      flagOnInput && stayedOnInput && flagCleared && collapsedOffInput
                      && resetOnHide && flagOnInput2 && collapsedWhenOff,
                      "入框标记=" + flagOnInput + " 保留=" + stayedOnInput + " 移开清标=" + flagCleared
                      + " 移开后收起=" + collapsedOffInput + " 收起复位=" + resetOnHide
                      + " 关开关时仍在输入框=" + flagOnInput2 + " 关开关后收起=" + collapsedWhenOff);
            }
            catch (Exception ex)
            {
                Check("T25 光标在输入框才不收起（真聚焦判定 / 焦点移开即恢复 / 开关可关）",
                      false, ex.GetType().Name + ": " + ex.Message);
            }

            // ---- T26 对话框必须也置顶（否则被置顶的抽屉盖住 —— 用户实测报的 bug）----
            // 两级证据：① 4 个对话框的 TopMost 属性；② **真实 z 序**（EnumWindows 顺序即 z 序）。
            try
            {
                var dlgs = new List<Form>();
                dlgs.Add(new EditDialog(st, null));
                dlgs.Add(new PromptDialog("标题", "标签", ""));
                dlgs.Add(new HotkeyDialog());
                dlgs.Add(new TrashDialog(st));
                bool allTop = true;
                foreach (Form f in dlgs) if (!f.TopMost) allTop = false;
                foreach (Form f in dlgs) f.Dispose();

                if (!dr.Visible) { dr.Show(); Pump(200); }
                var probe = new PromptDialog("z序探针", "标签", "");
                bool above = false;
                var closer = new System.Windows.Forms.Timer { Interval = 500 };
                closer.Tick += (s, ev) =>
                {
                    closer.Stop();
                    above = ZOrderIndex(probe.Handle) < ZOrderIndex(dr.Handle);   // 索引越小越上层
                    probe.Close();
                };
                closer.Start();
                probe.ShowDialog(dr);
                closer.Dispose();
                Pump(200);
                if (!dr.Visible) { dr.Show(); Pump(150); }

                Check("T26 对话框置顶（属性 + 真实 z 序都在抽屉之上）",
                      allTop && above, "4 个对话框都 TopMost=" + allTop + " 模态框在抽屉之上=" + above);
            }
            catch (Exception ex)
            {
                Check("T26 对话框置顶（属性 + 真实 z 序都在抽屉之上）",
                      false, ex.GetType().Name + ": " + ex.Message);
            }

            // ---- T27 列表抗闪烁机制（SmoothListBox：行内免擦除）----
            // ⚠️ 不能用 DoubleBuffered（绕过 WM_DRAWITEM ⇒ 卡片全不画，frozen17 事故）；
            // ⚠️ 也不能用屏外行缓冲（ClearType 失效 ⇒ 字体变粗变硬，T29 是那件事的回归门）；
            // ⚠️ 更不能给 ListBox 加 WS_EX_COMPOSITED（实测 CreateHandle 抛 Win32Exception、
            // 抽屉 Show() 整个崩、WinExe 还会弹关不掉的异常框 —— 2026-10-11 真实事故）。
            object lb = GetPriv(dr, "_list");
            Check("T27 列表为 SmoothListBox（行内免擦除抗闪烁）",
                  lb is SmoothListBox,
                  "类型=" + lb.GetType().Name);

            // ---- T28 悬停瞬时切换（无动画；悬停元素随 _hoverIndex 出现/消失）----
            // 用合成的 MouseMove 参数驱动真实悬停逻辑（e.Location 是入参，不碰真鼠标）：
            // 悬停第 1 行 → _hoverIndex==0；直调 List_DrawItem 渲到位图 → 卡片顶边出现
            // Accent 描边；移出 → _hoverIndex==-1 且重渲后描边消失（= 无动画状态残留）。
            if (st.Prompts.Count == 0) st.AddPrompt("悬停测试", "内容", null, "", false, "");
            dr.RefreshList();
            ListBox lbList = (ListBox)GetPriv(dr, "_list");
            Rectangle r0 = lbList.GetItemRectangle(0);
            CallPriv(dr, "List_MouseMove", null, new MouseEventArgs(MouseButtons.None, 0, r0.X + 5, r0.Y + 5, 0));
            int hoverIdx = (int)GetPriv(dr, "_hoverIndex");
            Color edgeHovered = Color.Empty;
            using (var bmp = new Bitmap(r0.Width, r0.Height))
            using (var g2 = Graphics.FromImage(bmp))
            {
                var eArgs = new DrawItemEventArgs(g2, Theme.Font(9.5f, false), r0, 0, DrawItemState.None);
                CallPriv(dr, "List_DrawItem", null, eArgs);
                for (int yy = Program.S(2); yy <= Program.S(6) && edgeHovered == Color.Empty; yy++)
                    if (IsAccentish(bmp.GetPixel(r0.Width / 2, yy))) edgeHovered = bmp.GetPixel(r0.Width / 2, yy);   // 卡片顶边描边带
            }
            CallPriv(dr, "List_MouseMove", null, new MouseEventArgs(MouseButtons.None, 0, -2000, -2000, 0));
            int outIdx = (int)GetPriv(dr, "_hoverIndex");
            Color edgePlain = Color.Empty;
            using (var bmp2 = new Bitmap(r0.Width, r0.Height))
            using (var g3 = Graphics.FromImage(bmp2))
            {
                var eArgs2 = new DrawItemEventArgs(g3, Theme.Font(9.5f, false), r0, 0, DrawItemState.None);
                CallPriv(dr, "List_DrawItem", null, eArgs2);
                for (int yy = Program.S(2); yy <= Program.S(6) && edgePlain == Color.Empty; yy++)
                    if (IsAccentish(bmp2.GetPixel(r0.Width / 2, yy))) edgePlain = bmp2.GetPixel(r0.Width / 2, yy);   // 同位置对照
            }
            bool hoveredBlue = edgeHovered != Color.Empty && IsAccentish(edgeHovered);
            bool plainNotBlue = edgePlain == Color.Empty || !IsAccentish(edgePlain);
            Check("T28 悬停瞬时切换（蓝框随悬停出现/消失，无动画状态）",
                  hoverIdx == 0 && hoveredBlue && outIdx == -1 && plainNotBlue,
                  "hover=" + hoverIdx + " 描边=" + edgeHovered + " 移出=" + outIdx
                  + " 无悬停描边=" + edgePlain);

            // ---- T29 抗闪烁靠"顶层窗体 WS_EX_COMPOSITED"，且**不得**加在 ListBox 上 ----
            // 背景（2026-10-11 实测链）：屏外行缓冲能原子上屏（不闪）但出不了 ClearType（字体坏 ✗）；
            // 直绘保 ClearType 但"填背景 → 逐项 WM_DRAWITEM"之间会被 DWM 插一帧（闪 ✗）。
            // 正解 = **直绘 + 顶层窗体 WS_EX_COMPOSITED**（系统级原子合成，两者兼得）。
            // ⚠️ WS_EX_COMPOSITED 加在 ListBox 上会 CreateWindowEx 失败、Show() 整个崩（已踩过）。
            try
            {
                int formEx = GetWindowLong(dr.Handle, -20);
                bool formComposited = (formEx & 0x02000000) != 0;
                ListBox lbF = (ListBox)GetPriv(dr, "_list");
                bool listComposited = (GetWindowLong(lbF.Handle, -20) & 0x02000000) != 0;
                Check("T29 抗闪烁=顶层窗体 WS_EX_COMPOSITED（列表控件上不得有）",
                      formComposited && !listComposited,
                      "窗体 ex=0x" + formEx.ToString("X") + " COMPOSITED=" + formComposited
                      + "；列表 COMPOSITED=" + listComposited + "（必须 False）");
            }
            catch (Exception ex)
            {
                Check("T29 抗闪烁=顶层窗体 WS_EX_COMPOSITED（列表控件上不得有）",
                      false, ex.GetType().Name + ": " + ex.Message);
            }

            // ---- T30 右上头两按钮：等大、等距、同一水平线；收起钮必须真画出「长方形条」----
            // 2026-10-17：用户去掉「×（缩到托盘）」，并把「—」从图标字体换成自绘 12×4 圆角条。
            // 老断言按三个按钮写，这里同步改成两个 + 像素证据（中心应有一条与背景明显不同的实心条）。
            try
            {
                Panel hdr = (Panel)GetPriv(dr, "_header");
                var btns = new List<Control>();
                foreach (Control c in hdr.Controls)
                    if (c is MiniButton) btns.Add(c);
                btns.Sort((a, b) => a.Left.CompareTo(b.Left));
                bool ok = btns.Count == 2;
                string det = "头部 MiniButton 数=" + btns.Count;
                if (ok)
                {
                    Control b1 = btns[0], b2 = btns[1];
                    bool sameSize = b1.Width == b2.Width && b1.Height == b2.Height;
                    bool sameTop = b1.Top == b2.Top;
                    bool sameGap = (b2.Left - b1.Right) > 0 && (b2.Left - b1.Right) <= Program.S(8);
                    bool isGlyph = b2 is RectGlyphButton;
                    bool bar = false;
                    if (isGlyph)
                    {
                        using (var bmp = new Bitmap(b2.Width, b2.Height))
                        {
                            b2.DrawToBitmap(bmp, new Rectangle(0, 0, b2.Width, b2.Height));
                            Color mid = bmp.GetPixel(b2.Width / 2, b2.Height / 2);
                            Color corner = bmp.GetPixel(2, 2);
                            bar = mid != corner && Theme.Contrast(mid, corner) > 2.0;
                            det += " bar=" + mid.R + "," + mid.G + "," + mid.B
                                 + " corner=" + corner.R + "," + corner.G + "," + corner.B;
                        }
                    }
                    ok = sameSize && sameTop && sameGap && isGlyph && bar;
                    det = "尺寸 " + b1.Width + "x" + b1.Height + "/" + b2.Width + "x" + b2.Height
                          + " top " + b1.Top + "/" + b2.Top
                          + " gap " + (b2.Left - b1.Right) + " glyph=" + isGlyph + " " + det;
                }
                Check("T30 头部两按钮等大/等距/同线 + 收起钮矩形条", ok, det);
            }
            catch (Exception ex)
            {
                Check("T30 头部两按钮等大/等距/同线 + 收起钮矩形条", false, ex.GetType().Name + ": " + ex.Message);
            }

            dr.Close();
            dr.Dispose();
            try { Directory.Delete(tmp, true); } catch (Exception) { }   // 测试遗留还原

            Console.WriteLine();
            Console.WriteLine("UI selftest: " + _pass + " 过 / " + _fail + " 失败");
            return _fail == 0 ? 0 : 1;
        }
    }

    // --uitest 专用：显示但不抢焦点（不打扰用户正在用的窗口）
    class QuietDrawer : DrawerForm
    {
        public QuietDrawer(Store s) : base(s) { }
        protected override bool ShowWithoutActivation { get { return true; } }
    }
}
