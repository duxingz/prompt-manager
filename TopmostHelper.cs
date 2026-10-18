// -*- coding: utf-8 -*-
// TopmostHelper.cs — 一次性置顶补位小工具（pmv3_helper.exe）
// 背景：本机 Nahimic/A-Volute 音效软件会向主程序（PromptDrawer.exe）注入 AudioDevProps2.dll，
// 注入后主进程对自身窗口的 TOPMOST 写入全部"假成功"（返回 ok 但样式位不变）。
// 实测换一个独立 exe 跨进程写入则完全不受影响 → 守护检测到写不动时拉起本工具。
// 用法：pmv3_helper.exe <pid>  —— 把 <pid> 进程的全部可见顶层窗口补上 WS_EX_TOPMOST。
// 全程不创建任何窗口（这正是它能绕开注入的原因之一），用完即退，无内存驻留。
using System;
using System.Runtime.InteropServices;

static class TopmostHelper
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();

    static int _fixed;

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (!SetProcessDpiAwarenessContext(new IntPtr(-2))) SetProcessDPIAware();
            int pid = args.Length > 0 ? int.Parse(args[0]) : (int)GetCurrentProcessId();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint wpid; GetWindowThreadProcessId(h, out wpid);
                if (wpid != (uint)pid || !IsWindowVisible(h)) return true;
                if ((GetWindowLong(h, -20) & 0x8) == 0)     // WS_EX_TOPMOST 缺位才补
                    if (SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10)) // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
                        if ((GetWindowLong(h, -20) & 0x8) != 0) _fixed++;
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception) { }
        return _fixed > 0 ? 0 : 1;   // 0 = 至少补了一个窗口
    }
}
