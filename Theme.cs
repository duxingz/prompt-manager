// -*- coding: utf-8 -*-
// Theme.cs — 小独爱心 Xiaodu Heart 配色（自 tokens.json 移植，改色需与套件同步）
using System;
using System.Drawing;

namespace PromptDrawer
{
    public static class Theme
    {
        public static bool Dark = false;

        // 双主题色表（亮=小独爱心 / 暗=小独夜，暖黑不发蓝）。改色需与套件同步。
        // 颜色经 Pal() 缓存：以前每个 getter 每次访问都 From(hex) = 3 次 Substring + 3 次
        // Convert.ToInt32，OwnerDraw 每行每帧取 ~8 个颜色，滚动大列表时每秒几千个临时字符串
        // ——与 Font 未缓存时同族的绘制路径垃圾（硬规则 10）。缓存键带主题前缀，Dark 翻转自然失效。
        static readonly System.Collections.Generic.Dictionary<string, string[]> HexPairs
            = new System.Collections.Generic.Dictionary<string, string[]>
        {
            { "Canvas",        new[] { "#FAF6EF", "#211E1A" } },
            { "Surface",       new[] { "#FFFFFF", "#2B2723" } },
            { "Sunken",        new[] { "#F0E9DC", "#1A1714" } },
            { "SurfaceAlt",    new[] { "#F5EFE4", "#262220" } },
            { "Border",        new[] { "#EBE4D8", "#3D3831" } },
            { "BorderStrong",  new[] { "#D9CFC0", "#574F44" } },
            { "Text",          new[] { "#453F36", "#E5DFD5" } },
            { "TextMuted",     new[] { "#6C655B", "#A79F92" } },
            { "Accent",        new[] { "#2478A8", "#4E9CCB" } },
            { "AccentHover",   new[] { "#1E6A94", "#63ABD6" } },
            { "AccentPressed", new[] { "#15506F", "#3B7FA8" } },
            { "AccentText",    new[] { "#FFFFFF", "#10222E" } },
            { "AccentSoft",    new[] { "#E9F2F8", "#1E3442" } },
            { "AccentOnSoft",  new[] { "#1E6A94", "#63ABD6" } },   // 软底文字（4.5:1 档）
            { "Danger",        new[] { "#B0562B", "#E08A5E" } },   // 2026-10-17 随套件 v2.1.0：白字 3.74→4.99
            { "DangerText",    new[] { "#FFFFFF", "#331507" } },
            { "Energy",        new[] { "#F0824C", "#F49B63" } },
            // 2026-10-18 补：心粉（tokens.json color_roles/heart）。规范 v2.3.0 要求每个界面
            // **至少一个爱心形状的粉色位**（爱心/瞳/选中环/徽章），面积 ≤1%。只进爱心形状。
            { "Heart",         new[] { "#F27BAE", "#F591BD" } },
            { "HeartText",     new[] { "#B22F5E", "#F7AFCB" } },
            { "HeartSoft",     new[] { "#FBE7EE", "#3C2733" } },
            { "TooltipBg",     new[] { "#3A342C", "#141210" } },
            { "TooltipText",   new[] { "#FAF6EF", "#EFEAE2" } },
        };

        static readonly System.Collections.Generic.Dictionary<string, Color> PalCache
            = new System.Collections.Generic.Dictionary<string, Color>();

        public static Color Pal(string key)
        {
            string k = (Dark ? "D" : "L") + key;
            Color c;
            if (!PalCache.TryGetValue(k, out c))
            {
                string[] hh = HexPairs[key];
                c = From(Dark ? hh[1] : hh[0]);
                PalCache[k] = c;
            }
            return c;
        }

        public static Color Canvas       { get { return Pal("Canvas"); } }
        public static Color Surface      { get { return Pal("Surface"); } }
        public static Color Sunken       { get { return Pal("Sunken"); } }
        public static Color SurfaceAlt   { get { return Pal("SurfaceAlt"); } }
        public static Color Border       { get { return Pal("Border"); } }
        public static Color BorderStrong { get { return Pal("BorderStrong"); } }
        public static Color Text         { get { return Pal("Text"); } }
        public static Color TextMuted    { get { return Pal("TextMuted"); } }
        public static Color Accent       { get { return Pal("Accent"); } }
        public static Color AccentHover  { get { return Pal("AccentHover"); } }
        public static Color AccentPressed{ get { return Pal("AccentPressed"); } }
        public static Color AccentText   { get { return Pal("AccentText"); } }
        public static Color AccentSoft   { get { return Pal("AccentSoft"); } }
        public static Color Danger       { get { return Pal("Danger"); } }
        public static Color DangerText   { get { return Pal("DangerText"); } }
        public static Color Energy       { get { return Pal("Energy"); } }
        public static Color Heart        { get { return Pal("Heart"); } }
        public static Color HeartText    { get { return Pal("HeartText"); } }
        public static Color HeartSoft    { get { return Pal("HeartSoft"); } }
        public static Color TooltipBg    { get { return Pal("TooltipBg"); } }
        public static Color TooltipText  { get { return Pal("TooltipText"); } }

        // ================================================================ 设计系统标度（2026-10-17）
        // 对齐小独爱心套件：逻辑 px，调用处过 Program.S()。改这里 = 全站节奏一起变。
        public const int SpaceXs = 4, SpaceSm = 8, SpaceMd = 12, SpaceLg = 16, SpaceXl = 24;
        public const int RadiusSm = 4, RadiusMd = 8, RadiusLg = 12, RadiusPill = 18;
        // 字号（pt）：说明 8.5 / 正文 9.5 / 次强调 10 / 标题 10.5 / 大标题 11。全站只用这几档。
        public const float FontCaption = 8.5f, FontBody = 9.5f, FontSubtitle = 10f, FontTitle = 10.5f;

        // 软底上的文字：直接用 Accent 只有 4.28:1（暗色 4.27）→ 用深一档
        public static Color TextOnSoft
        {
            get { return Pal("AccentOnSoft"); }
        }
        // 危险色作文字 / 描边（危险填充之上的字仍走 DangerText）
        public static Color DangerTextSoft
        {
            get { return Dark ? From("#E08A5E") : From("#9A4A22"); }
        }

        // WCAG 对比度
        public static double Contrast(Color a, Color b)
        {
            double la = RelLum(a), lb = RelLum(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        static double Chan(double v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        static double RelLum(Color c)
        {
            return 0.2126 * Chan(c.R) + 0.7152 * Chan(c.G) + 0.0722 * Chan(c.B);
        }

        // 分类 chip 的文字色：优先用分类色本身；不足 4.5:1 就逐步压深/提亮，
        // 仍不达标则退回正文色。用户可自选任意分类色，所以必须在绘制时兜底。
        public static Color ChipTextFor(Color bg, Color preferred)
        {
            if (Contrast(preferred, bg) >= 4.5) return preferred;
            Color c = preferred;
            for (int i = 0; i < 8; i++)
            {
                c = Dark ? Mix(c, Color.White, 0.18) : Mix(c, Color.Black, 0.18);
                if (Contrast(c, bg) >= 4.5) return c;
            }
            return Contrast(Text, bg) >= 4.5 ? Text : (Dark ? Color.White : Color.Black);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(255,
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static readonly string FontStack = "Microsoft YaHei UI";

        public static Color From(string hex)
        {
            return Color.FromArgb(
                Convert.ToInt32(hex.Substring(1, 2), 16),
                Convert.ToInt32(hex.Substring(3, 2), 16),
                Convert.ToInt32(hex.Substring(5, 2), 16));
        }

        // 字体缓存：OwnerDraw 列表的行绘制里每行每帧要取 3~4 次字体（DrawerForm 的 DrawItem /
        // chip 绘制），原来每次 new Font —— 实测每次约 430 字节垃圾，15,000 次就是 +6.5MB，
        // 滚动时每秒几百个。缓存后常驻内存是个定值，且绘制更快。
        // 安全性：实测 WinForms 控件**不会** Dispose 交给它的 Font（也不会 Dispose 共享 Image），
        // 所以可以放心共享；这些 Font 进程期内不释放（数量固定、体量小）。
        static readonly System.Collections.Generic.Dictionary<string, Font> FontCache
            = new System.Collections.Generic.Dictionary<string, Font>();

        public static Font Font(float size, bool bold)
        {
            string key = size.ToString("0.###") + (bold ? "B" : "R");
            Font f;
            if (!FontCache.TryGetValue(key, out f))
            {
                f = new Font(FontStack, size, bold ? FontStyle.Bold : FontStyle.Regular);
                FontCache[key] = f;
            }
            return f;
        }

        public static Color Lighten(Color c)
        {
            if (Dark)
                return Color.FromArgb(255, c.R * 55 / 100, c.G * 55 / 100, c.B * 55 / 100);
            return Color.FromArgb(255,
                Math.Min(255, c.R + (255 - c.R) * 70 / 100),
                Math.Min(255, c.G + (255 - c.G) * 70 / 100),
                Math.Min(255, c.B + (255 - c.B) * 70 / 100));
        }

        // 分类 chip 的暗色专用配色（2026-10-06 独立评审抓到的深底深字）：
        // 旧实现底=Lighten(暗色下=压暗55%)、字=原色 → 用户分类色偏深时对比仅 ~1.5:1。
        // 现：底 = 表面色向分类色靠拢 35%（仍带分类色相、但与卡片底拉开）；字 = 分类色向白提亮 60%。
        public static Color ChipBg(Color c)
        {
            Color b = Surface;
            return Color.FromArgb(255,
                (b.R * 65 + c.R * 35) / 100,
                (b.G * 65 + c.G * 35) / 100,
                (b.B * 65 + c.B * 35) / 100);
        }

        public static Color ChipText(Color c)
        {
            return Color.FromArgb(255,
                Math.Min(255, c.R + (255 - c.R) * 60 / 100),
                Math.Min(255, c.G + (255 - c.G) * 60 / 100),
                Math.Min(255, c.B + (255 - c.B) * 60 / 100));
        }

        // 图标字体（emoji→图标方案 2026-10-07）：单字形按钮用系统图标字体，随 ForeColor 走主题。
        // Segoe Fluent Icons（Win11）→ 缺则 Segoe MDL2 Assets（Win10）→ 都缺返回 null（调用方回退纯文字）。
        // 码点实测（2026-10-10 探针 glyphs.png）：E706=太阳 ✓、E708=新月 ✓ 渲染正常；
        // E7A3 缺字形渲染成豆腐框，弃用。
        public const string IconSun = "\uE706";    // 太阳（Brightness）——暗色主题显示，点击回日间
        public const string IconMoon = "\uE708";   // 新月（ClearNight）——亮色主题显示，点击进夜间
        // 头部另两个按钮也用**图标字体**：2026-10-11 用户报"三个按钮大小不一样、没对齐" ——
        // 实测根因是主题钮走图标字体、另两个走中文字体，两套字形度量差一个量级
        //（渲染图量得：主题字形 18×20、中心 29.5 ／「—」只有 1px 高 ／「×」8×9、中心 31.0）。
        // 三个统一图标字体后度量一致 ⇒ 天然等大、垂直对齐。
        public const string IconMin = "\uE921";    // ChromeMinimize（一条横线）
        public const string IconClose = "\uE8BB";  // ChromeClose（×）

        static string _iconFontName;
        static bool _iconFontProbed;
        static readonly System.Collections.Generic.Dictionary<string, Font> IconFontCache
            = new System.Collections.Generic.Dictionary<string, Font>();

        public static Font IconFont(float size)
        {
            if (!_iconFontProbed)
            {
                _iconFontProbed = true;
                try
                {
                    using (var ifc = new System.Drawing.Text.InstalledFontCollection())
                        foreach (var fam in ifc.Families)
                        {
                            if (fam.Name == "Segoe Fluent Icons") { _iconFontName = "Segoe Fluent Icons"; break; }
                            if (fam.Name == "Segoe MDL2 Assets") _iconFontName = "Segoe MDL2 Assets";
                        }
                }
                catch (Exception) { }
            }
            if (_iconFontName == null) return null;   // 两个字体都没有
            string key = size.ToString("0.###");
            Font f;
            if (!IconFontCache.TryGetValue(key, out f))
            {
                f = new Font(_iconFontName, size);
                IconFontCache[key] = f;
            }
            return f;
        }
    }
}
