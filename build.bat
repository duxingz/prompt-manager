@echo off
rem ============================================================
rem PromptDrawer v3 build (C#/.NET 4.8 WinForms, zero-install csc)
rem Output: dist\PromptDrawer.exe + dist\pmv3_helper.exe
rem NOTE: keep this file PURE ASCII. cmd parses .bat as GBK on this
rem machine; UTF-8 Chinese comments misdecode into '&' metacharacters,
rem which executes garbage and pollutes errorlevel -> false BUILD FAILED
rem (hard rule #2 in AGENTS.md; hit for real on 2026-10-10).
rem ============================================================
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d %~dp0
if not exist dist mkdir dist

%CSC% /nologo /target:winexe /platform:anycpu /optimize+ ^
  /out:dist\PromptDrawer.exe ^
  /win32icon:assets\app_icon.ico ^
  /r:System.Web.Extensions.dll ^
  /r:System.IO.Compression.dll ^
  /r:System.IO.Compression.FileSystem.dll ^
  Program.cs Store.cs Theme.cs SelfTest.cs UiTest.cs CapsuleForm.cs DrawerForm.cs

rem [2026-10-07] a failed MAIN compile must stop right here: otherwise its
rem errorlevel gets overwritten by the helper csc below and the smoke gate
rem would see 0 (real incident: UiTest.cs had CS0136 yet BUILD OK was printed).
if not "%errorlevel%"=="0" (
  echo BUILD FAILED ^(PromptDrawer.exe^)
  exit /b 1
)

rem Standalone tiny helper: when the main app's topmost write is blocked by
rem the Nahimic injected DLL, the guard launches this to set topmost from a
rem separate process. Must be a separate exe (same-name processes get
rem injected the same way - tested, writes would not stick).
%CSC% /nologo /target:winexe /platform:anycpu /optimize+ ^
  /out:dist\pmv3_helper.exe ^
  TopmostHelper.cs
rem NOTE: use "if not %errorlevel%==0", NOT "if errorlevel 1".
rem A crash exit code is negative (e.g. 0xC0000005 = -1073741819), and cmd's
rem "if errorlevel 1" compares signed -> it would PASS a crashed build silently.
if not "%errorlevel%"=="0" (
  echo BUILD FAILED ^(pmv3_helper.exe^)
  exit /b 1
)

if not exist dist\assets mkdir dist\assets
copy /y assets\logo_round.png dist\assets\ >nul
copy /y assets\app_icon.ico dist\assets\ >nul
rem app_icon.ico is required at RUNTIME by the tray icon (Program.BuildTray reads
rem assets\app_icon.ico next to the exe) - /win32icon only embeds it in the exe.
rem data is NOT copied: never overwrite user data.

rem smoke: passive UI self-test (offscreen window; no mouse, no screenshot).
rem Output is also archived to dist\uitest_last.txt: this self-check contains
rem timing-sensitive items that failed once in 2026-10-07 without evidence;
rem keeping the log makes the failing item obvious next morning.
dist\PromptDrawer.exe --uitest > dist\uitest_last.txt 2>&1
type dist\uitest_last.txt
if not "%errorlevel%"=="0" (
  echo UITEST FAILED ^(detail above + dist\uitest_last.txt^)
  exit /b 1
)

echo BUILD OK: dist\PromptDrawer.exe
endlocal
