# AGENTS.md — 提示词管理器V2 交接须知（原 prompt-manager-v3，2026-10-24 目录合并改名）

> 🔑 **新接手请先读 `接手指路.md`**（一页纸：五分钟上手 / 代码地图 / 红线 / 最贵的坑 / 现状快照）。
> 本文是**权威细则**：硬规则 1~18 + 历次修改（①~㉔）+ 待办池。用户视角说明见 README.md。
> v3 = C#/.NET 4.8 WinForms 轻量重写（当前主用）。
> **v2（PySide6）已于 2026-10-06 退役、整目录删除**；数据与源码归档在本目录
> `旧版数据\`（主库 21,900 条 / 40 分类 + 完整归档 zip，详见该目录 README）。

## 状态

**v1.0.0 已上线自用**（2026-10-05）：selftest 23 绿 + 真实合成拖拽/单击端到端验证 +
真实数据（用户自建 9 分类全新开局）首启导入。**内存 ~50MB**（v2 是 ~220MB），
1 万条加载 81ms，上限 1 万条（用户指定）。

**2026-10-05 夜修**：胶囊"图标显示不全 + 完全不吸附/不半埋"的根因定位为
**Win32 最小窗口尺寸钳制**（见硬规则 1），加 `WS_EX_TOOLWINDOW` 修好；
新增 `--uitest` 被动 UI 自检当回归门。

**2026-10-05 夜修 ②**：悬停滑动 **"多试了几下就再也不弹出"** —— 根因是动画中途按下时
只停表、不复位动画状态闸（详见硬规则 8）。已按 v2 语义重做动画接管，并按用户裁决
"动画中途移开要**立刻缩回**"去掉闸。`--uitest` 扩到 **10 项**（T6~T8 即此 bug 的回归）。

**2026-10-05 夜修 ③**：两件事一起解决（详见硬规则 10 / 11）——
① **鬼畜抖动**：滑出位留了 3px 缝，滑出时窗口从光标底下挪走 → 假 MouseLeave → 缩回 → 又滑出
   → 无限来回（实测四条边全是 3px 缝，贴边后 0px）；现滑出位一律贴屏幕边 + 移出加位置护栏
   + 按需看门狗。
② **内存膨胀（最严重）**：`OnPaint` 里每次重绘都 `Image.FromFile` 且从不释放 —— 实测
   **每次 +2.06MB / 3.7ms，200 次重绘 +412.7MB**，而且**独占锁住 logo 文件**。用户实例就是这么
   从 52MB 涨到 **422MB** 的。改成启动时解码一次缓存后：200 次重绘 **+0.3MB / 56ms**、文件不锁。
   顺带把 `Theme.Font` 也改成缓存（OwnerDraw 每行每帧取 3~4 次）。
`--uitest` 扩到 **12 项**（T9 = 缝必须为 0，T10 = 反复重绘不得涨内存）。

**2026-10-06 夜修 ④（抽屉 UI + v2 退役）**：
① **记事本/设置两个页签完全点不了** —— `BuildUi()` 里三个 `TabButton` 建出来后**从来没接
   `Click`**（`SwitchTab` 只在启动时调过一次）→ 补齐接线（注意 **C# 5 的 `for` 变量是共享的**，
   必须先 `int idx = i;` 再在闭包里用 idx，否则三个页签全切到 3）。
② **设置页 5 处控件真重叠**（`📥`/`📤`/`💾`/`🗂` 四个按钮两两压 22px、`常驻与行为` 标题压住主题
   按钮 18px）—— 根因是 `SBtn`/`SInfo` **摆放后不推进布局光标 `_sy`**，调用处写的 6px"间隙"
   变成了"间距"。改成"每个 helper 把 `_sy` 推到自己之下 + 调用处只写间隙"。
③ 设置页/`MiniButton` 的**裸逻辑像素**（`_sy += 26`、高度写死 28、表头 `MiniButton("🌙", 24)`）
   全部改走 `Program.S()`（违反硬规则 2 的历史遗留）。
④ **换主题漏改控件**：老 `ApplyColors()` 只手工点名十来个固定控件、且只扫 `_header` 里的
   MiniButton → 暗色下搜索行「＋ 新建」仍是纯白、记事本页按钮、列表空态提示都留亮色。
   现改为 `ApplyControl` **递归整棵树**（并给 `ChipButton`/`MiniButton` 记住"选中态/危险色"）。
⑤ **v2 退役**：v2 与 v3 都注册了开机自启、且都抢 `Alt+P` → 开机双开 + 热键互抢。已删
   `HKCU\...\Run\PromptDrawerV2`、把 v2 **整目录删除**（163MB），数据完整归档到
   `提示词管理器V2\旧版数据\`（主库 21,900 条 / 40 分类 + v2 源码 + 滚动备份 + v1 归档，
   详见该目录 README）。v3 的「首启自动导入」随之**关闭**（旧库 21,900 条 > 1 万上限）。

**2026-10-06 夜修 ⑤（批次 1+2，独立评审双轮通过）**：
① **分类 chips 溢出被裁**（用户 9 分类=12 chip，第 7/8 个完全在面板外）：`WrapContents=true`
   自动换行 + `FitChipsHeight()` 按 chip 实际铺排自适应行高（单行仍 S(38)，多行自动增长）。
② **Toast 从未 `Controls.Add`** —— 所有操作提示（复制/保存/删除…）永远不显示。
③ **热键修改失败 → 热键全丢**：RegisterDrawerHotkey 无条件落盘失败热键 + RestoreHotkey 读回坏值。
   改 `save && ok` 才写热键本身、RestoreHotkey 失败回退默认 Alt+P、启动失败也记
   `hotkey_register_failed` 并在设置页显示「未生效：可能被其他程序占用」。
④ **EditDialog 的分类下拉框从未 `Controls.Add`**（标签下方 55px 空洞，编辑时无法改分类）
   ——与 ② 同款事故，都是独立视觉评审逐像素实锤。
⑤ **暗色列表分类 chip 深底深字**（对比 ~1.5:1）：`Theme.Lighten` 在暗色下是压暗，字仍用原色。
   新增 `Theme.ChipBg/ChipText`（暗色底=表面色向分类色靠 35%、字=向白提亮 60%），亮色不变。
⑥ 其余：`_pageCombo`/`_cat` 两个 DropDownList 下拉框改 OwnerDraw 自绘（暗色纯白——**FlatStyle.Flat
   在视觉样式下根本不生效，探针实测设与不设都是 253,253,253**，交接文档旧假设已证伪；箭头小钮
   仍系统绘制，归 native-chrome 豁免族）；EditDialog/HotkeyDialog 显式主题配色
   （此前暗色黑底黑字）；列表右键菜单（编辑/复制/收藏/置顶/删除，此前空态提示承诺了却不存在）；
   DrawChip/EditDialog 残留裸像素全过 `S()`（150% 下对话框底部按钮曾溢出 4px）；Theme 颜色
   getter 改双主题缓存（原先每次访问 3 次 Substring）；List_DrawItem 漏 Dispose 的 SolidBrush；
   暗色列表分类 chip 深底深字（Theme.ChipBg/ChipText，对比 1.5:1→6:1）；
   选中集随筛选清空（防跨筛选误删）；「立即备份」改 force 不再 24h 跳过；Save 失败退出时警告
   （`Store.SaveFailed`）；crash.log 加 1MB 上限；build.bat 补拷 `app_icon.ico`（运行时托盘图标）。

**2026-10-07 凌修 ⑥（置顶守护，用户报"网页盖住悬浮图标"）**：
用户报胶囊会被网页盖住。实测链：**线上实例的胶囊和抽屉的 WS_EX_TOPMOST 位都是 False**
（胶囊从创建起就没有），而同一份二进制的屏幕外探针（ProbeB/ProbeC，位置 -4000）8 轮
开合循环一个位都不丢 → **有环境在剥离**。逐层排查（对照矩阵）：TOOLWINDOW/DPI 感知/
吞 WM_DPICHANGED/Show() vs Visible/屏幕内外——全部排除；**跨进程写入时灵时不灵**
（00:1x 稳 60s，00:2x 被瞬时回退）→ **间歇性剥离器**。进程里发现
**Nahimic/A-Volute 注入的 AudioDevProps2.dll**（C:\ProgramData\A-Volute\...\Scheduled\），
该模块以"向所有 GUI 进程注入并挂钩窗口 API"闻名，剥离行为与它吻合（未 100% 定罪）。
对策（已上线）：① `Program.EnsureTopMost` 每秒核对真实 EXSTYLE 缺了就补（不信 WinForms
TopMost 属性缓存）；② 窗体 OnVisibleChanged 显示即补；③ 补位小工具 `pmv3_helper.exe <pid>`
（独立 exe，可手动跑强制恢复）。**剥离器活跃期任何写入都会被瞬时回退（应用侧无解），
休眠期守护 1 秒内自动恢复。** 用户侧根治：排查/禁用 Nahimic 服务（NahimicService）。

**2026-10-07 凌修 ⑦（批次三：分类管理 + emoji→图标，评审通过）**：
① **分类管理 UI**（此前增删分类完全没有入口）：chips 行末「＋ 分类」chip 新建（PromptDialog
   单行输入对话框，暗色适配）；分类 chip 右键「重命名/删除」（删除前确认，其中提示词自动归
   未分类）；数据层新增 `Store.RenameCategory`（空名/同级重名拒绝）；`AddCategory` 空 icon
   不再默认 📁（新分类无图标 = chip 只显示名称）；SelfTest 补 5 个分类增删改用例（23→28 项）。
② **emoji→图标**（按 2026-10-06 定稿方案）：头部主题按钮改单字形图标（`Theme.IconFont`
   缓存，Segoe Fluent Icons → MDL2 → 缺则纯文字"日间/夜间"；**统一用 E706——E7A3 太阳字形
   在 Segoe Fluent Icons 里已被裁掉，暗色下渲染成空白**，frozen7 评审抓到后改掉）；
   其余 15 处"emoji+文字"混排全部去 emoji 留纯文字（设置页按钮/搜索占位/chips/记事本下拉/
   编辑对话框/胶囊右键菜单 ✦✕）；★☆↑⤒↓└＋✓ 等单色文本符号保留；**用户自建分类的
   icon 字段是用户数据，不动**（新建分类默认无图标）。
③ 主题按钮字形固定 E706（亮度/太阳形，契合切换语义），`ApplyColors → UpdateThemeButton`
   仅在字体缺失时切纯文字。

**2026-10-07 凌修 ⑧（记事本完善，评审 10/10 通过）**：
① **查找栏**（Ctrl+F 呼出 / Enter 或 ↓ 下一处 / ↑ 上一处 / F3·Shift+F3 / Esc 关闭；不区分大小写、
   循环匹配、计数 i/N）；② **状态栏字数**（第 N/M 页 · X 字符 · Y 行 · 已自动保存）；③ **右键菜单**
   （撤销/剪切/复制/粘贴/全选/自动换行开关/插入时间戳 F5/查找/导出本页 TXT/导入 TXT 为新页/
   字号加减——替换 TextBox 默认菜单）；④ **Ctrl+S 手动保存**、**F5 时间戳**、**Ctrl+滚轮字号**
   （NoteTextBox 子类拦 WndProc——MouseWheel 事件无法标记已处理会边缩放边滚动；Ctrl+0 复位，
   字号存 settings 防抖落盘）；⑤ **拖拽/导入 TXT 自动 GBK 回退**（UTF-8 严格失败→GB18030）。
   两个评审抓到的真问题：**裸 \n 在 Win32 EDIT 不换行**（`DrawerForm.Crlf` 归一，导入/JSON 数据
   统一处理）+ **设 Text 后全文选中、首键整段覆盖**（RefreshNotePages/SwitchNotePage/EditDialog
   设完 Text 一律光标移末尾清选区——提示词编辑对话框同样受益）。`UiTest.CallPriv` 补上沿继承链
   查找（与 Field 同款教训，T17 调 DrawerForm.DoFind 踩到）。

**2026-10-07 凌修 ⑨（上限移除 + chips 网格重排，评审 10/10 通过）**：
① **1 万条硬上限删除**（用户改为隐性性能要求：1 万条内不卡死不卡顿、极快响应、足够轻量）：
   删 Store.MaxPrompts 常量 + AddPrompt 拒绝 + MergeFromFile 截断 + 设置页/页脚"上限"文案；
   **SelfTest 第 6/7 节改为性能基准门**（写 1 万条 → 重载一致且 <1s → 关键词筛选 <500ms →
   第 10001 条仍可写入；实测加载 97ms / 筛选 8ms）。**SwitchTab 不再每次点页签整库落盘**
   （SetSetting 只标脏，退出 Flush 兜底——那是 1 万条时唯一的卡顿源）。旧库 21,900 条由此解锁可导。
② **分类 chips 重排为 5 列等宽圆角网格**（FlowLayoutPanel → 手动定位 Panel）：chip 105×45（略大）、
   圆角 Region；**首行固定 全部/收藏/[置顶分类槽]/未分类/＋分类**（未置顶时第 3 格留空；右键
   "置顶到首行/取消置顶"，pinned_cat 设置持久化，Store.DeleteCategory 删分类自动清槽）；
   第二行起其余分类（被置顶的不重复）；**超宽截断为名称前 4 位**（图标/计数/└ 一并省略），
   完整"名称（N 条）"进悬停提示；chip 面上不再显示 (N) 计数。
   **教训：AddChip 必须调 PlaceChip——漏调时全部 chip 以默认 100×23 叠在 (0,0)（frozen11 评审抓到），
   T13 的"包含在面板内"断言拦不住这种叠放，已靠亲眼读图发现。**
③ **选中描边沿圆角走**（用户报"蓝框只覆盖 4 条边"）：FlatStyle 渲染器的矩形边框被圆角 Region
   裁切后必然缺角 → `FlatAppearance.BorderSize = 0` 归零 + `ChipButton.OnPaint` 沿圆角路径
   自绘描边（选中 Accent 1.8f / 悬停 BorderStrong 1f）+ `ShowFocusCues=false` 关掉矩形虚线焦点框。
   原理：凡是被 Region 裁剪的控件，边框/焦点框都不能依赖渲染器直角绘制。

**2026-10-07 凌修 ⑩（全库复查 4 修复 + 同类调研）**：
① **粘贴绕过 CRLF**：Ctrl+V/Shift+Insert 直接走 EDIT 内部粘贴，裸 \n 又会挤成一行 →
   `NoteTextBox.WndProc` 拦 WM_PASTE(0x0302)，剪贴板文本经 `Crlf` 归一后用 `SelectedText` 插入。
② **TextBox 默认 MaxLength=32767**：笔记/注释/提示词内容超 32k 字符后**静默打不进字**
   → `_noteEdit`/`_content`/`_note` 放宽到 2,000,000。
③ **SwitchTab 不落盘的回归**：自启/复制后收起/贴边半隐三个设置复选框原先靠页签切换落盘，
   该时机已删 → 三个 handler 各补 `_store.Save()`（其余 SetSetting 调用点已核对有落盘时机）。
④ 拖入非 .txt 文件原先无任何反馈 → Toast 提示。测试两轮全绿（selftest 29 / uitest 19，
   1 万条基准：加载 95ms / 筛选 6ms）。

**2026-10-07 凌修 ⑪（小视觉项 + 置顶深化，用户点做）**：
① **复选框改自绘** `ThemedCheckBox`（`DrawerForm.cs` 尾部，与 TabButton/MiniButton 同级）：
   OS 原生方块在暗色下是近纯白 `(243,243,243)`、勾选是系统蓝 `(0,95,184)` → 现自绘 16×16
   圆角方块（`Theme.Accent` 填充 + `Theme.AccentText` 勾线 + 未勾选用 `Theme.Surface`+`BorderStrong`），
   整行可点、`ApplyControl` 里对 CheckBox 补 `Invalidate`。**回归门 = uitest T18**（真渲染像素：
   未勾选 ≈Surface / 勾选 ≈Accent，实测 `255,255,255` 与 `36,120,168` ✓）。
  ⚠️ **勾线颜色是 `Theme.AccentText`（亮=白 / 暗=深 `#10222E`），不是写死白色** —— 独立评审
  曾把暗色深勾判为 bug，复核确认是**评审规格写错**：亮色白勾对 `#2478A8` = 4.4:1，
  暗色深勾对 `#4E9CCB` = 5.5:1；若固定白勾则暗色只剩 ~2.3:1，反而更差。已写进代码注释。
② **修复「修改」按钮右对齐差 6 物理 px**（`_sx + _sw - S(64)` → `- S(60)`）：按构造右缘 =
   `_sx+_sw` = 534；重渲染后像素扫描显示整页最右缘主峰 **x=533**（294 行）、**原来那个 527 群消失** ✓。
③ **记事本竖滚动条 = 明确豁免**（native chrome）：`_noteEdit.ScrollBars` 是非客户区，
   `BackColor` 管不到；换 RichTextBox 也一样。**结论：不修**，与下拉框箭头小钮同归豁免族
   （除非用户明确说"这个亮条看着难受"）。
④ **置顶：真根因已定案**（详见硬规则 18，**这一段原来的"Nahimic 剥离"结论已作废**）：
   真因 = **WinForms 进程里第一个 Form 拿不到 `WS_EX_TOPMOST`**，胶囊正是第一个；修法 =
   `Program.WarmupFirstForm()`。Nahimic 已实测洗清（停服务 + 重启程序、确认新进程无该 DLL，
   现象不变）。`run_ab_test.cmd` 已无用。

**2026-10-07 凌修 ⑫（托盘「开机自动启动」改了不落盘）**：
用户报"重启后不自启"。现场：`HKCU\...\Run\PromptDrawerV3` **项不存在**，且设置文件里
**连 `autostart` 键都没有**。根因 = `Program.cs` 托盘那个开关（`auto.Click`）调了
`SetSetting` + `Autostart.Set` 却**没有 `_store.Save()`** —— 而注册表 Run 项是**立即**删/写的、
`SetSetting` 只标脏（批次⑨"退出 Flush 兜底"的设计）⇒ 中间被强杀就留下
"**Run 项没了、设置也没记下**"的不一致；又因为"缺键 = 默认开"，下次启动会自动写回
（自愈，所以用户只要启动一次就恢复）。修法 = 该 handler 补 `_store.Save()`
（与设置页那三个复选框一致，ZCode 当时漏了托盘这处）。
**同类排查**：全项目 15 处 `SetSetting` —— 其余 14 处要么紧跟 Save、要么是**故意的**
"标脏 + 退出 Flush"（tab / pinned_cat / note_font 等）。**规律：凡有"立即生效的副作用"
（注册表 / 服务 / 文件）的地方，配套设置必须立刻落盘。**

**2026-10-07 凌修 ⑬（两处自启开关互相同步 + 又抓到一个假绿灯门禁）**：
① **同步**：托盘菜单「开机自动启动」与设置页那个复选框原先**各读各的**（都在建 UI 时读一次），
   一处改了另一处不变 —— 托盘改完设置页的勾要等重启/切主题才跟上，设置页改完托盘的勾永不变。
   改法（**只同步 UI，不回写设置、不碰注册表**，防递归）：
   - `Program._trayAutoItem` 存住托盘的项 + `Program.SyncTrayAutostart(bool)`（改 `Checked`
     **不会**触发 `Click`）；
   - `DrawerForm._cbAutostart` 存住设置页的勾 + `bool _syncingAutostart` 抑制回写 +
     `public DrawerForm.SyncAutostartUI(bool)`（改完 `Invalidate()`，自绘复选框要重画）；
   - 两个 handler 各自在末尾调对方；`SwitchTab` 进设置页时再从 store 刷一次勾。
   **回归门 = uitest T19**（断言勾跟着变、**同步不写 store**、托盘未建时安全返回；
   ⚠️ 测试里**绝不** PerformClick 那个复选框 —— 它的 handler 会真写用户注册表）。
② **假绿灯门禁（第 2 个）**：`build.bat` 的成败判定只写在**第 2 条 csc（helper）之后** ⇒
   主程序 csc 失败时，它的 errorlevel 被下一条 csc 的**成功值覆盖**，冒烟门看到 0 →
   直接 `echo BUILD OK`（真实踩到：UiTest.cs 报 CS0136 却报 BUILD OK ✗）。
   现已在**每条 csc 之后**各加一道 `if not "%errorlevel%"=="0"` ✓，并用一次真实编译错误自证
   （输出 `BUILD FAILED (PromptDrawer.exe)` + exit 1 ✓）。
③ **冒烟输出留档**：`--uitest` 的输出改为同时写 `dist\uitest_last.txt`（并 type 出来）——
   因为该自检有**时序敏感项偶发失败**（2026-10-07 遇到 1/7 次，当时被我的过滤条件吞了、没抓到是哪项 ✗）。
   **下次再偶发，先看这个文件。**（已连跑 5 次 + 构建内跑全绿，未复现。）

**2026-10-07 凌修 ⑭（回收站 + 批量 + 剪贴板快速捕获 + 笔记→提示词，用户点做 A/B/C/E）**：
① **回收站（C）**：`Prompt` 加 `deleted_at`（活着的恒为空）；`Store` 加 `List<Prompt> Trash`
   + `TrashCap = 200`；**`DeletePrompts` 由"直接删"改为"移入 Trash"**（打时间戳，超上限按
   `deleted_at` 丢最旧）；新增 `RestoreFromTrash`（id 撞车就换新 id）/`RemoveFromTrash`/
   `EmptyTrash`/`TrimTrash`；`Load`/`Save` 读写 `trash` 键（**持久化**，重载还在）。
   入口 = **托盘右键 →「回收站…」**（不开抽屉也能进）→ `TrashDialog`（主题化：标准 ListBox +
   `MiniButton`「恢复选中/全部恢复/清空回收站」+「关闭」，清空键走 `MiniButton.Danger`）；
   删除确认文案已改为提示"会先进回收站"。
② **批量（E）**：`Store.MovePrompts(ids,catId)` / `SetFavPrompts(ids,fav)`；列表右键新增
   **「移到分类 ▸」（子菜单列全部分类）/「收藏（N 条）」「取消收藏（N 条）」** ——
   有 Ctrl/Shift 多选就作用于那一批，否则作用于右键那一条（与 `DeleteSelected` 同语义）。
③ **快速捕获（A）**：托盘右键新增**「从剪贴板新建提示词」**→ `DrawerForm.CaptureFromClipboard()`：
   读剪贴板文本 → **内容与现有条目完全相同则不重复添加** → 否则按当前选中分类（"全部"→未分类）
   新建，标题用 `FirstLineOf`（纯函数）。结果一律走**托盘气泡** `Program.Balloon()`
   （抽屉收起时 Toast 看不见）。
④ **笔记→提示词（B）**：记事本右键新增**「把选中的一段存为提示词…」**（无选区则禁用）→
   预填进 `EditDialog`（为此加了 `EditDialog(store, prompt, preTitle, preContent)` 重载 +
   同样的"光标移末尾防首键覆盖"处理），可顺手命名/归类。
⑤ **回归门**：`SelfTest` 第 10 节 ⇒ **37 项**（移入回收站/不混进列表/**持久化**/恢复/批量移动/
   批量收藏/清空/上限丢最旧）；`--uitest` 新增 **T20**（`FirstLineOf`：首行/空白兜底/超长截断）
   + **T21**（回收站对话框可建可显示、条数正确）+ **T22**（抽屉**从未显示过**时也能 RefreshChips/
   RefreshList —— 快速捕获正是在收起状态跑的）⇒ uitest **24 项**。
   **视觉验收（独立只读子代理）3/3 PASS**：3 条记录真渲染出来了（超长标题 8 倍放大逐字核对
   未截断）、空态确实无记录（列表内部 351,664 像素只有一种颜色）、说明文字右边距 72px 未截断、
   4 个按钮无重叠且「关闭」在最右、亮暗**字形掩码逐列 100% 重合**（只差配色，暗色 B−R=−8 不发蓝）。
   已知豁免：系统标题栏（未参与主题化，全应用一致）、标准 ListBox、窗口本体边框
   （与既有 `08_editdialog_light.png` 同名像素签名逐点相同，非本对话框引入）。
   ⚠️ 测试**绝不碰剪贴板**（是用户的东西），也**不 PerformClick 会写注册表的控件**。
⑥ 本轮自己踩的坑：新加的局部变量**不能与外层同名**（CS0136/CS0128 —— c/cat/cap 三个都撞了，
   **build.bat 的门禁正确拦下** ✓）；pwsh 里 `[System.IO.File]::ReadAllText("相对路径")`
   会找不到文件（**.NET 当前目录 ≠ PS location**），必须绝对路径。

**2026-10-08 凌修 ⑮（双存档并行 + 笔记页回收站 + 分类左右移，用户点做）**：
① **双存档并行**（用户明确要的语义：手动存档必须**另开一份全新文件**，不复用自动存档那条路）：
   - **自动存档**（老通道，不动）：敲键后 800ms 防抖写 `notes.json`（原子写、**覆盖式**）。
   - **手动存档**（新通道）：记事本顶部新增「**存档**」按钮 → `Store.ArchiveNotes(prefix)` →
     在 `<数据目录>\saves\` 下**新建一份独立 TXT**（`记事本存档-yyyy-MM-dd_HHmmss.txt`，
     含**全部页**+页标题分隔+时间+页数），**永不覆盖**（同一秒连点也另开 `-2` ✓）、**不自动删**。
     刻意用**纯 TXT**（自动＝JSON / 手动＝TXT，两种格式**物理隔离**）：就算 JSON 那条路被写坏，
     这份也一定读得出来；恢复走记事本右键**已有的「导入 TXT 为新页…」**（闭环 ✓）。
     状态栏改为同时显示两条通道：「已自动保存 HH:mm:ss · 手动存档 HH:mm:ss」
     （手动时间存 `note_manual_save_at` ✓ 跨重启保留）。
   - **删页前自动保险**（用户选的"甲"）：`DeleteNotePage` 先 `ArchiveNotes("已删-")` 再删；
     确认框文案改成告诉用户"删前会自动存档 + 可在回收站恢复"。
② **笔记页回收站**（用户选的"乙"）：`NotePage` 加 `deleted_at`；`Store.TrashNotes` + `NoteTrashCap = 100`；
   `DeleteNotePage` 由"直接删"改为**移入 TrashNotes**；新增 `RestoreNotePage` / `RemoveFromTrashNotes` /
   `EmptyTrashNotes` / `TrimTrashNotes`；`notes.json` 读写 `trash_pages`（**持久化**）。
   `TrashDialog` 改成**左右两栏**（左＝提示词 / 右＝已删除的笔记页，各自一排
   「恢复选中 / 全部恢复 / 清空」+ 右下「关闭」），尺寸 900×520 逻辑。
③ **分类左移/右移**（用户点名要，**不用拖拽**——他以前明确否决过拖拽）：
   分类显示顺序**就是** `Categories` 列表顺序（`Category` 无排序字段）⇒ 换位即持久化。
   `Store.CanMoveCategory(id, dir)` / `MoveCategory(id, dir)`：**只在同级兄弟之间**换
   （用 `ChildrenOf(parent_id)` ✓，子分类不会被挪到别的父级下）；chips 右键菜单新增
   「左移 / 右移」（越界**置灰**；**被置顶**的分类在第一行是固定槽位 → 置灰 + `ToolTipText`
   说明原因，`menu.ShowItemToolTips = true`）。
④ **回归门**：`SelfTest` 第 11 节 ⇒ **50 项**（手动存档新文件/不覆盖/含全部页/已删-前缀/
   两条通道互不影响 · 笔记页回收站移入·持久化·恢复·清空·上限 · 分类左右移边界/**同级限定**/
   移父级不动子级）；`--uitest` 新增 **T23**（**点真按钮** → 断言真在 `saves\` 里多出一份 TXT
   且内容含标记文字）+ **T24**（`MoveCategory` 后 chips **显示顺序**真的换了、数量不变）
   ⇒ uitest **26 项**。
⑤ **本轮踩的坑（都很典型，全靠门禁/自检抓到）**：
   - **`Control` 没有 `PerformClick`**（在 `Button` 上）→ 编译错误被 build 门禁拦下 ✓。
   - **T23 一开始 FAIL**：`PerformClick()` 对**不可见**的控件**不生效**（存档按钮在记事本页，
     而测试当时停在列表页）→ 必须先 `SwitchTab(1, false)` 切过去再点 ✓。
   - **SelfTest 一开始 1 项 FAIL**：我的"左边界"断言假设新加的「甲类」是同级第一个 ✗，
     而**默认库里本来就有分类** → 改为取 `ChildrenOf("")` 的**真正首/末**来断言 ✓。
     **教训：测试里别假设"我新建的就是第一个/唯一的"，先取真实集合再断言。**
⑥ 渲染 + 独立视觉评审：记事本页（4 个控件零重叠、右端 560 < 面板 576 ✓）+ 回收站两栏
   （1350×780、中缝有 85 列纯背景 = 两栏真的分开 ✓）。
⑦ **独立评审抓到的真 bug（已修，值得记）**：`03_notes_light.png` 判 **FAIL** —— 新加的
   「存档」按钮**右缘越出面板 15px 被裁**（右边框整条消失、文字被挤到贴边、同排间距 12/6/9 不齐）。
   **根因**：`_notePage` 带 `Padding = (S(8), S(6), S(8), S(6))` ⇒ `noteTop` 的**内宽只有
   `DrawerWidth - S(16)`（=552 物理）**，而这一行的控件一直用 `DrawerWidth`（=576）当锚点 ✗
   —— 老的两个按钮恰好没越界所以没暴露，我新加的第三个就溢出被裁了。
   **修法**：这一行改成**按面板内宽右对齐 + 等间距（12px）**，并把 `_pageCombo` 收窄让位
   （`_pageCombo.Width = noteAdd.Left - S(12)` ⇒ 291→252）；复验最右墨迹 536（内容右界 563，余量 27px）。
   **⚠️ 硬教训：控件装在**带 Padding 的父容器**里时，锚点必须用"父容器内宽"，
   绝不能用窗体宽 `DrawerWidth`（老代码里那两处是"侥幸没溢出"）。**
   另：评审的"次级观察"（正文框右边框缺失）**不是缺陷** —— 那个位置是记事本的**原生竖滚动条**
   （539..563，native-chrome 豁免族，见凌修⑤⑥）；复评用**全图逐像素 diff** 证实：该区域
   修复前后**0 像素差异**，文本框自身右边框 x537 **完整贯穿 y123..1461**。
⑧ **复评（第二轮）结果**：**Q1 通过** ✓ 四边框齐全（上/下 78/78、左/右 42/42）、右缘 557 未越界、
   文字左右留白 20/22 与「删除」完全一致（修复前是右留白只剩 4px + 右边框整条不存在）。
   **Q3 确认既有**（见上）。**Q2 还差 6px** —— 可见间隙 18/12/12：
   根因是**我把 `Program.S(12)` 当成 12 物理像素了，实际 S(12)=18**（150% 缩放）。
   改 `Program.S(8)`（=12 物理）后复测：下拉框右缘 263→**269**，与「＋新建」的可见间隙 = **12**；
   三个按钮（按边框行量）282..377 / 390..467 / 480..557、间隙 **12/12**、最右 557（距右界 6px）
   ⇒ **三段间隙全 12px、等距** ✓。**教训：`Program.S()` 里写的是"逻辑像素"，
   想对齐物理像素时要么换算、要么统一用同一个 S() 值比大小**（这次自己把自己坑了一次）。

**2026-10-10 凌修 ⑯（编辑中不自动收起，用户点做「甲+乙」）**：
① **问题**：`OnDeactivate`（点抽屉外任何窗口/桌面）无条件 `Program.CollapseDrawer()` ⇒ 在记事本、
   搜索框里写东西时点一下别处就被收起，**光标位置/滚动位置/正在看第几页全丢**（内容本身已自动保存 ✓）。
② **方案甲（行为）**：新增 `_editing` 状态 —— 「**本次展开期间**进过输入框」即不再自动收起；
   出口 = **Esc**（本来就有：先清多选、否则收起 ✓，见 `ProcessCmdKey`）/ 点胶囊 / 托盘 ✓。
   进入编辑时 `Toast` 提示一次（"编辑中：点别处不会自动收起（按 Esc 收起）"，每个展开周期一次）。
③ **方案乙（开关）**：设置页「常驻与行为」新增自绘复选框「**编辑时不自动收起（默认开启）**」
   （`edit_keep_open`，默认 true），关掉即恢复"点别处立刻收起"的老行为。
④ **⚠️ 关键实现坑（别改回去）**：**不能**用 `TextBox.Leave` 清 `_editing` —— 点别的窗口时
   WinForms **先丢焦点（Leave 已经跑完）再**触发 `OnDeactivate`，靠 Leave 会在判定前就被清掉 ✗。
   现在靠 `Enter` 打标（`HookEditInputs()` 递归整棵树给所有 `TextBox` 挂 → 以后新增输入框自动覆盖），
   **只在"收起/隐藏"时统一复位**（`OnVisibleChanged` 的 `!Visible` 分支）⇒ 语义精确等于
   "本次展开里编辑过"，且天然没有时序竞争 ✓。
⑤ **回归门 = uitest T25**（反射调**真实的** `OnDeactivate`）：非编辑 → 照旧隐藏 ✓；编辑中 →
   保持可见 ✓；把开关关掉 → 编辑中也隐藏 ✓；收起后 `_editing` 复位 ✓。
   ⚠️ 测试里必须临时把 `Program._capsule` 置空 —— `MouseInCapsule` 读的是**真实鼠标位置**
   （`Cursor.Position` 是否落在胶囊矩形内），不置空就等于依赖"用户当时鼠标在哪" = 不确定 ✗。
   ⇒ uitest **27 项**。
⑥ **一处没做（说明理由）**：本想同时在记事本状态栏常驻显示"编辑中…"，但实测那行状态栏在 150%
   缩放下只有 384 逻辑宽，加上这句会**溢出被裁** ✗ ⇒ 只用 Toast 报一次。
⑦ 渲染 + 独立视觉评审：设置页（新复选框那一行）亮/暗各一张（**2/2 PASS**：文字 x52..292、
   右余量 283px 未截断；上下间隙 23/21px 无重叠；与「修改」按钮水平不相交；暗色几何逐字节相同、
   勾选框 #4E9CCB + 深色勾；插入点以下整体下移恰好 39px = 纯插入非挤压）。

**2026-10-10 凌修 ⑰（⑯ 的两处修正：对话框被盖住的真 bug + 语义按用户要求改精细）**：
① **用户报的真 bug：新建提示词对话框被主界面盖住** ✗ —— **代码实证**：抽屉是 `TopMost = true`
   （`DrawerForm.cs:90`），而**全项目 4 个对话框没有一个设 `TopMost`** ✗ ⇒ Windows 规矩
   "**非置顶窗口永远压不过置顶窗口**" ⇒ 必然被盖 ✓（回收站对话框同理，只是它够大不易察觉）。
   ⚠️ **这是既存问题、不是⑯ 引入的**（抽屉一直是置顶的），只是用户长时间开着抽屉 + 常用新建才撞上。
   **修法**：4 个对话框（`EditDialog` / `PromptDialog` / `HotkeyDialog` / `TrashDialog`）各加
   `TopMost = true`（4 行）✓。**回归门 = uitest T26**：① 断言 4 个对话框 `TopMost == true`；
   ② **真实 z 序**断言 —— `EnumWindows` 的回调顺序**就是** z 序，在**模态循环里**（用
   `System.Windows.Forms.Timer` 定时关窗，避免 ShowDialog 阻塞）比较索引，断言对话框在抽屉之上 ✓。
② **语义改精细（用户原话："只要我的光标不在输入框里，点别的地方就能自动收起"）**：
   ⑯ 的 `_editing`（"本次展开期间编辑过就一直不收"）**太粘** ✗ → 改为 `_focusInInput`
   （判据 = **最后获得焦点的是不是输入框**）⇒ 光标在输入框 → 不收 ✓；焦点跑到列表/按钮/别处
   → 照旧自动收起 ✓✓。
   **实现**：`HookEditInputs()` 给控件树里**每个**控件挂 `Enter`：`TextBox` → true，
   其它可聚焦控件（列表/按钮/下拉）→ false；**非焦点控件（面板/标签）天然不触发** ⇒
   不用判断谁能聚焦 ✓，而且"最后聚焦的是谁"就是唯一判据、**没有时序竞争** ✓。
   ⚠️ 仍然**绝不能**用 `TextBox.Leave` 判（点别的窗口时它先跑完，见⑯④）。
   文案同步改：气泡 →「光标在输入框：点别处不会自动收起（按 Esc 收起）」；设置项 →
   「**光标在输入框时不自动收起（默认开启）**」（键名 `edit_keep_open` 不变 ✓）。
③ **回归门 T25 重写**（改成**真聚焦**，不再塞标志位 —— 连"Enter 真的会触发"一起验了）：
   真 `Focus()` 记事本正文 → `_focusInInput == true` ✓ + 触达 `OnDeactivate` → 抽屉保留 ✓；
   再真 `Focus()` **列表**（可见的非输入控件）→ 标记翻掉 ✓ + 触达 → 收起 ✓ + 收起即复位 ✓；
   关掉开关 → 光标在输入框里也照旧收起 ✓。⇒ 七项断言全过 ✓，uitest **28 项**。
④ **踩到的测试坑（第一版 T25 FAIL）**：我用 `FindByText(dr, "＋ 新建")` 找"非输入控件"，
   它返回的是**树序里第一个**同名控件 = **列表页**那个 ✗，而当时前台是**记事本页** ⇒
   那个按钮**不可见** ⇒ `Focus()` 静默无效 ⇒ 标记没翻、断言挂 ✗。
   **教训：测试里 `Focus()` 之前必须确认目标控件真的可见（隐藏页里的控件 Focus() 无效），
   要找"必然可见"的控件（这次换成 `_list`）**。
   另：`NEW` 拼 `System.Windows.Forms.Timer` 要写全名 —— UiTest 同时引了 `System.Threading`，
   裸 `Timer` 有歧义风险 ✓。

**2026-10-10 凌修 ⑱（列表悬停闪烁修复 + build.bat ASCII 违规修复，评审 4/4 通过）**：
① **用户报"鼠标移到提示词上会闪烁"** —— 三层叠加：`_list` 未开双缓冲（擦背景米白 → 重画卡片，
  间隙肉眼可见）× 悬停变化 `_list.Invalidate()` **整列表失效**（视觉只变两行、实际全列表陪画）×
  OwnerDraw 单行较重（圆角路径/chip MeasureText）。静止不闪（MouseMove 有 idx 变化门槛）✓与描述吻合。
  **修法**：`_list` 换 `SmoothListBox : ListBox`（ctor 开 `DoubleBuffered`——protected 属性子类设最干净）；
  悬停变化只 `Invalidate` **旧/新两行**的 `GetItemRectangle`（越界防护），MouseLeave 只失效原行；
  低频场景（筛选/换主题/多选清空）保持全量失效。
② **回归门 = uitest T27**：断言 `_list` 是 `SmoothListBox` 且 `DoubleBuffered==true`
  （反射查 `Control.DoubleBuffered` —— 属性声明在 Control，直接查 Control 类型即可，
  不用走继承链）。⇒ uitest **29 项**。
③ **build.bat ASCII 违规修复（硬规则 2 的现行犯）**：⑭~⑮ 会话往 build.bat 加了 UTF-8 中文注释，
  cmd 按 GBK 解析时某段字节误读出 `&` 元字符 → 注释行被拆成命令执行 → errorlevel 污染 9009 →
  **csc 明明成功却误判 BUILD FAILED**（本会话实测踩到；之前会话的 shell 上下文侥幸未触发）。
  已整文件重写为**纯 ASCII**（中文说明全部改英文注释），构建恢复干净 BUILD OK。
  **教训重申：.bat 永远纯 ASCII，加一行中文注释都不行。**

**2026-10-10 凌修 ⑲（三个显示优化，评审 5/5 通过）**：
① **主题钮字形随主题切换**（用户报"点了还是太阳"）：亮色 = **E708 新月**（点击进夜间）、
   暗色 = **E706 太阳**（点击回日间）——图标表示"点击后去哪"。码点探针实测（glyphs.png）：
   E708 渲染正常、E7A3 豆腐框弃用；`Theme` 常量改名 `IconSun=E706 / IconMoon=E708`。
② **头部加 × 按钮（缩到托盘）**：排版 ☀/—/×，× = `Program.MinimizeToTray()`——抽屉+胶囊
   都隐藏、只留托盘图标；恢复 = 托盘双击 / 托盘菜单 / Alt+P（任一走 ShowDrawer）。
   注意：× 之后 `CollapseDrawer` 不会再弹胶囊（胶囊本就隐藏），下次 ShowDrawer 后恢复
   正常胶囊⇄抽屉流。宽 S(24)，右缘 W-S(14)，— 在 W-S(66)、主题钮在 W-S(94)。
③ **列表悬停 = 卡片蓝色圆角描边**（用户报悬停"闪烁/晃眼"的真身 = 排序钮突现 + 标题宽度
   悬停时骤减 S(66) 导致文字重排跳位）：描边 = Accent 1.8f 沿卡片圆角（`List_DrawItem` 内
   悬停分支先描边后画排序钮）；**标题恒定让位 S(66)**（悬停与否宽度一致 ⇒ 文字零跳动）；
   排序钮保留悬停出现（画在框内右上）。双缓冲 + 行级失效（凌修⑱）继续有效。

**2026-10-10 凌修 ⑳（悬停淡入淡出，用户要求"保留悬停出现、减少观感不适"，评审 2/2 通过）**：
蓝框+排序钮从"突现/突消"改为 **~100ms 透明度淡入淡出**（60fps）：
① **状态机**：`_animRow`（正在渐变的行）+ `_alpha`（0~255）+ `_fadeIn` + 16ms UI 定时器
   （步进 48 ⇒ 约 5~6 帧完成）；快速跨行 = 新行重新淡入、旧行立即消失（动画只保一路，无竞争）；
   移出列表 = 旧行淡出，归零后 `_animRow=-1` 清掉。
② **DrawItem** 按 `_alpha` 混合：描边 `Color.FromArgb(_alpha, Theme.Accent)` 1.8f、
   排序钮底 `SurfaceAlt`/字 `TextMuted` 同 alpha；每帧只 `Invalidate(_animRow)` 行矩形。
③ **收起复位**：`OnVisibleChanged` !Visible 分支停表清状态（与 `_focusInInput` 复位同点）。
④ **回归门 = uitest T28**：合成 MouseMove 驱动真实悬停逻辑（e.Location 是入参、不碰真鼠标）
   —— 悬停第 1 行 Pump(120) 后 alpha>100 且 `_animRow==0`；移出 Pump(200) 后 alpha==0 且
   `_animRow==-1`。⇒ uitest **30 项**。
⑤ **踩坑两个**：`MouseEventArgs` 构造是 **5 参**（漏 delta 会 CS1729）；`CallPriv` 传参
   别忘了 sender（`CallPriv(dr,"List_MouseMove", null, mouseArgs)`——params 只给 e 会
   TargetParameterCountException）。

**2026-10-11 凌修 ㉑（行缓冲 blit 事故与修复，评审 6/6 通过）**：
⚠️ **本次事故链（用户实测撞上，已全部修复）**：
① 行缓冲 blit 上线时，缓冲位图被 `using` 包住释放 → **第一次重绘正常、之后每次重绘读已释放
   位图的 Width 抛"参数无效"** → 每次重绘都抛。
② 渲染脚手架（DrawerRender）**没挂 Application.ThreadException 处理器** → .NET 默认
   异常对话框**每次重绘弹一次** → 用户侧"一直弹窗关不掉"（348 次，日志留档）。
   **教训：任何带 UI 定时器/重绘的脚手架必须先挂 ThreadException 日志器。**
③ **`DoubleBuffered = true` 加在 Win32 ListBox 上会绕过 WM_DRAWITEM 管线** → 所有卡片
   不绘制、列表空白（frozen17 渲染实证；凌修⑱ 的"双缓冲"是安慰剂且有害——T27 属性断言
   拦不住，视觉关抓到）。**已撤掉 DoubleBuffered**。
**㉑ 当时定稿（已被 ㉒/㉓ 取代，以 ㉓ 为准）**：`SmoothListBox` 跳过行内 WM_ERASEBKGND
+ 行缓冲 blit；㉒ 撤缓冲（救字体）、㉓ 改成**顶层窗体 WS_EX_COMPOSITED**（救闪烁）。
仍然有效的结论：T27 = 类型断言（SmoothListBox）、T28 = 悬停状态机；**淡入动画已删**
（快速掠过时 alpha 反复归零 = 闪烁放大器；悬停高亮用**瞬时切换**）；**渲染脚手架必须跟着源码演进**
（反射注入的字段随源码删除会静默死亡：render exit=1 且无 `_error.txt` = 脚手架与源码不同步）；
**悬停排序钮右缘收进描边内**（`ArrowZones` 右缘 = row.Right - S(36)）。

**2026-10-11 凌修 ㉒（列表字体回归根治：撤行缓冲；附一次自造事故的完整复盘）**：
① **用户报"字体和之前不一样"**（上一个 agent 修了半天没修好）——**根因实测**：手工行缓冲把文字
   画进**内存 DIB** ⇒ **ClearType 亚像素抗锯齿失效** ⇒ 退化成灰度/硬边。**受控对照**
   （同一脚手架、同一张 `01_prompts_light.png`、只差代码路径）：
   | 版本 | 亚像素彩色占比 | 笔画像素（标题 / 预览） |
   |---|---|---|
   | `frozen14`（加缓冲**之前**，基准） | 42.9% / 51.6% | 1565 / 1567 |
   | `frozen23`（缓冲期） | 19.1% / 61.9% | **2244 / 2991**（+43% / +91%） |
   | `frozen24`（改回直绘后） | **42.9% / 51.6%** | **1565 / 1567（±0%）** |
   **决定性对照**：列表**以外**的搜索行两版**逐字节相同**（1851 像素 / 44.5%）⇒ 变的只有
   列表这一条绘制路径 ✓。⚠️ 上一个 agent 修的是**另一个**问题（`DrawImage(img,x,y)` 会按
   图像 DPI→屏幕 DPI 把整行放大 1.5 倍 = 真模糊，那个他确实修好了）；但**只要还走缓冲，
   ClearType 就回不来** ⇒ 他越修越偏 ✗。
② **修法**：`List_DrawItem` **删掉行缓冲，直接画 `e.Graphics`**（坐标等价做法：把 `row.Y`
   置成 `e.Bounds.Y`——`DrawRowContent` 内部一律用 `row.Y + 偏移`）⇒ 字体与基准
   **逐带 +0% 完全一致** ✓✓。
③ **⚠️ 自造事故（已修，详见 ㉓③）**：给 **ListBox** 加 `WS_EX_COMPOSITED` ⇒ `CreateWindowEx` 失败、
   `Show()` 崩、且弹"关不掉的异常框"（uitest 不走 `Application.Run`、钩子无效 ✗）。⇒ **子控件绝不能加**；
   两条自检入口已各加 try/catch（只写 `crash.log`，不弹窗 ✓）。
④ **回归门**：T29 现为"**窗体**带 `WS_EX_COMPOSITED`、列表控件上不得有"（㉓④ 改判）；**像素证据一律
   由渲染脚手架给出**（uitest 进程没像真屏那样合成，像素门会反向 ✗）。⇒ uitest **31 项**。

**2026-10-11 凌修 ㉓（"字体 ↔ 闪烁"拉锯终结：直绘 + **顶层窗体** WS_EX_COMPOSITED）**：
① **用户报"字体修好了但闪烁又回来了，不要反复"** —— 先做**受控实验**排除一个错误假设：
   疑 ㉑ 用 `Format32bpp**Rgb**` 缓冲才拿不到 ClearType ⇒ 改 **Argb** 重渲染（frozen25）：
   **数字与 Rgb 版一模一样（19.1% / 61.9%）** ⇒ **内存位图无论什么像素格式都出不了 ClearType** ✗
   （ClearType 需要真实显示表面）。⇒ "缓冲 ↔ 字体"是**真矛盾**，不可能靠换格式化解。
② **闪烁机理**（读码 + 机理确认）：悬停**只失效新旧两行**（`GetItemRectangle`，⑱ 的修复还在 ✓）
   ⇒ 闪烁不在失效范围；而在 **Win32 列表重绘的"先填背景 → 再逐项 WM_DRAWITEM"之间被 DWM
   插了一帧**（OwnerDraw 重：圆角路径 + chip 测量）。㉑ 的缓冲能治闪，正因为它把整行做成
   **一次原子 blit**。⇒ 要"直绘（保 ClearType）+ 原子上屏"，合成只能放到**操作系统层**。
③ **定稿方案**：`List_DrawItem` **直绘 `e.Graphics`**（撤缓冲 ⇒ ClearType ✓）+
   **`DrawerForm.CreateParams` 加 `WS_EX_COMPOSITED(0x02000000)`**（整窗系统级合成 ⇒ 不插帧 ✓）。
   ⚠️ **必须加在顶层窗体上**：㉒ 加在 **ListBox** 上 ⇒ `CreateWindowEx` 失败、`Show()` 崩、弹异常框 ✗。
④ **验证（两条独立证据）**：
   - 重渲染 `frozen26` 对照基准 `frozen14`：**24 个文字带笔画全部 +0%、亚像素彩色占比逐个相同**；
     **列表区域差异像素 = 0** ✓✓（整图仅 1730 个差异像素、全在头部 y9..50 = ⑲ 的 × 按钮 ✓）
   - uitest **T29 改判**：断言**窗体**带 `WS_EX_COMPOSITED`（实测 `ex=0x2010008`）**且列表控件上必须没有** ✓
   ⇒ **31/31 全绿** ✓、`crash.log` 无 ✓、抽屉置顶/可见性未受影响 ✓。
⑤ ⚠️ **诚实边界**：`DrawToBitmap`/渲染脚手架走 **WM_PRINT**，未必经过合成路径 ⇒ 那条"0 差异"
   证明的是**绘制路径**与基准一致；**屏幕最终观感（字体 + 是否还闪）必须用户亲眼看**
   （闪烁是时间维度，机器量不到）。若真屏上字体仍不对 ⇒ 下一手是"缓冲 + GDI+
   `TextRenderingHint.ClearTypeGridFit`"（内存位图里出 ClearType 的唯一办法），但与 GDI 字形有
   细微差别，优先级低于本方案。

**2026-10-11 凌修 ㉔（右上角三按钮统一：等大/等距/同一水平线，uitest 32 项）**：
① **用户报"三个按钮大小不一样、也没对齐，看着难受"** —— 实测根因（**量渲染图里三个字形的墨迹**）：
   **主题钮走图标字体、另两个（— ×）走中文字体** ⇒ 字形度量差一个量级：
   改前 主题 18×20 中心 29.5 ／「—」20×**1**（只剩一根发丝）／「×」8×9 中心 31.0。
   ⇒ 靠调位置解决不了 ✗，**必须统一字体**。
② **修法**：三个都走**图标字体同一字号** + 宽统一 `S(28)` + 间距 4px + 右边距 14px +
   **按头部中线垂直居中**（不再写死 `Y=S(6)`）；`—`/`×` 换图标码 **E921(ChromeMinimize) /
   E8BB(ChromeClose)**（新增 `Theme.IconMin/IconClose`）。改后实测：17×20 中心 29.5 ／
   19×2 中心 28.5 ／ **20×20** 中心 29.5 ✓（原来 8×9 的 × 现在是正常大小）。
③ **字形探针（必做，㉑ 踩过 E7A3）**：`_tmp_zlog\glyph_probe2\glyphs.png` + **图像子代理逐格判定**
   —— E921/E8BB 都是**真字形**；E7A3 由子代理**读字体 cmap**（Segoe Fluent Icons v1.54 无该码点、
   回退 `.notdef`，外接框与渲染方框像素级吻合）确证是豆腐框 ✓。子代理另给更细的备选
   **E738/E711**（描边 2.3/2.1px、与日月同族），但**10pt 下细档只剩 ~0.9px 会发虚** ⇒ 采用粗档 ✓。
④ **回归门 = uitest T30**：断言头部三个 MiniButton **同宽同高、Top 相同、间隙相等**
   （实测 42x42/42x42/42x42、Top 9/9/9、间隙 6/6 ✓）⇒ **32 项**；列表区仍与基准**逐像素 0 差异** ✓。
⑤ **✅ 用户已亲眼看验并确认「很完美」**（同日）—— 本条与 ㉓（字体/闪烁）**均已验收，别再"顺手优化"这两处**
   （改前/改后放大对照图留在 `_tmp_zlog\hdr_cmp\header_ab.png`；字形探针在 `_tmp_zlog\glyph_probe2\`）。
   **独立视觉关判定 PASS**（逐像素量过，数字与本条完全吻合）：底板 36×42→**42×42**（24×28→28×28 逻辑px，方形）、
   × 从 8×9→**20×20**、图标中心间距差 3.5→**0.5 物理px**。**两条已知可接受的残留（别再改）**：
   ① 收起横线的垂直中心比月牙/× **高 1 物理px**（0.67 逻辑px，1:1 看不出）；
   ② **字重不齐**：月牙是细线空心（≈1px）、横线与 × 是实心（≈2px）—— 月牙是**既有主题图标**、本轮没动它，
   而更细的备选 E738/E711 在 10pt 下只剩 ~0.9px **会发虚**（子代理实测），所以现状是这对取舍里更好的一侧。
   另：按钮左端 421 与最后一个页签墨迹右缘 370 之间**空档 51 物理px**，**没有挤到任何东西** ✓。

**2026-10-17 视觉重设计**：Theme 加标度 Space/Radius/Font、TextOnSoft、ChipTextFor()；控件改自绘、**弃用窗口 Region 圆角**；
头部改「主题 + 长方条」，**删 ×**（动作移到胶囊右键「隐藏到托盘」）；Primary 实心 / Danger 描边；设置页重排四组；
空态=图标+标题+引导+主按钮（空库时隐藏列表再显示）；对话框 ApplyChrome() + 等宽按钮；搜索框自绘边框；T30 改两按钮像素断言；
uitest 32/32、selftest 50/50；渲染取证改用 PrintWindow。
**2026-10-18 凌修 ㉕（改名 提示词管理器V2 + 四色配额落地 + 视觉关四处修复）**：
① **改名**：Program.AppName / 窗口标题 / 胶囊提示 / 设置页品牌行 / 存档 TXT 抬头 / 自检标题 ⇒ **提示词管理器V2**；
   FindWindowW 改为引用 AppName（原来写死标题 ⇒ 改一处漏一处会让"再点一次没反应"）。
   **不动**：exe 文件名、互斥体名、自启 Run 项名（动哪个都会让现有自启动失效）。
② **四色配额（规范 v2.3.0，之前四屏全 FAIL）**：粉 = 「收藏」chip 的 ♥（ChipButton.HeartMark）+ 空态 host.Paint + 设置页 page.Paint 的心；
   橙 = 列表收藏星两态都用 **exact Energy**（★/☆ 靠字形区分）+ 两处 Paint 的 ★。
   Theme.cs 补 Heart/HeartText/HeartSoft（#F27BAE / #F591BD，值抄自 tokens.json）。
   **终验 check_usage.py --render 四屏全 exit=0**（energy 14/14/5/14，heart 37/37/7/58）。
③ **视觉关四处修复**：①暗色搜索占位 2:1 ⇒ **弃用原生 EM_SETCUEBANNER（系统灰不可调）**，改自绘占位 Label（TextMuted ⇒ ~5:1），有字即藏、点击交焦点；
   ②设置页右边距 42px 不对称 ⇒ _sw 改 S(16) ⇒ 左右各 12px、与列表卡片右缘对齐；③空态「重置筛选」是空操作 ⇒ 只在有筛选/搜索时显示（_emptyReset）；
   ④**未修、留档**：自绘按钮角部有 1px 直边露出圆角、列表右缘三套边距差 ~10px（要动布局，等点名）。
④ **⚠️ 踩到的怪事（重要）**：Label 在 _settingsPage 与空态 inner 里**实测不出画面**（渲染 0 像素，但 T12 数得到控件）
   ⇒ 凡"必须出现在面板上的装饰"，用**父容器 Paint 直接画**，别指望 Label。
⑤ 自检 **32/32**（T12 设置页 20 控件零重叠）+ 编译 OK。
**2026-10-24 凌修 ㉖（UI规则 v3.0.0 对齐：色表同步 + BorderField + 键盘焦点 + --uishots 出图）**：
背景：UI设计规则 套件升至 v3.0.0（45 条清单），用户裁决"按新规则优化本程序 UI"且明确 **"别做太大改动改丑了"** —— 本轮只做 token 同步与无感修复，布局/尺寸/字体零改动。
① **色表同步**（Theme.cs）：暗色 Surface `#2B2723→#322D28`、Border `#3D3831→#4D463E`（套件 v2.2 层级分离，
   暗色卡片对画布 1.09→1.22，视觉终验确认"柔和可分层、不发蓝、不刺眼"）；新增 **BorderField**（#96897A/#837B6C，
   WCAG 1.4.11 非文本 3:1）。亮色语义值零变化。
② **边界换 3:1 档**：搜索框宿主边框 BorderStrong(1.5:1)→**BorderField(3.34:1)**；复选框未选边框同换。
③ **键盘焦点（清单规则 37）**：搜索框聚焦整框染 Accent（GotFocus/LostFocus 各 invalidate 宿主）；
   MiniButton/TabButton/ChipButton/ThemedCheckBox 恢复 TabStop=true（原全 false = 键盘用户够不到）+
   Focused 时自绘 1.8f Accent 环（实心主按钮用 AccentPressed——Accent 环贴 Accent 底隐形，与套件 Qt 同款）；
   保留 ShowFocusCues=false（直角虚线框丑）。**鼠标用户视觉零变化**（环只在键盘聚焦时画）。
④ **新增 `--uishots [dir]`**：渲染 QA 出图模式（同 uitest 规矩：屏幕外/不碰输入/数据只进临时目录），
   出 prompts/notes/settings × light/dark 6 张。**两个坑**：主题必须写进测试 store（DrawerForm 构造时
   GetSetting("theme") 会覆盖 Theme.Dark）；必须复刻 Program.ShowDrawer() 的 **RefreshChips()+RefreshList()**
   （正式路径靠热键弹出时才刷——漏了 RefreshChips 则 chips 行整行不建、粉位消失）。
⑤ **验收链全绿**：build（32/32 uitest）+ selftest 50/50 + check_usage --render 四色齐（prompts/settings × 亮暗）+
   独立视觉验收两轮全 PASS（新旧 A/B：token 零漂移、搜索框边界实测 #96897A、暗色分层 1.22:1、无焦点环串扰）。
⑥ **未修、留档**：记事本页缺橙/粉（既有状态，非本轮引入）——按规范"禁止为凑色硬塞"条款维持现状
   （文字编辑页塞爱心=装饰噪音）；设置页 0 个 BorderField 像素属复选框全选中+抗锯齿，非缺陷；
   RectGlyphButton（收起钮）仍 TabStop=false；对话框内 TextBox 仍系统 FixedSingle 边框。
## 硬规则（DPI 是本项目最大的坑，动窗口代码前必读）

1. **【胶囊必须带 `WS_EX_TOOLWINDOW`】** —— `CapsuleForm.CreateParams` 里
   `cp.ExStyle |= 0x00000080`。Windows 对顶层窗口强制"系统最小窗口尺寸"
   （本机 150% 缩放实测 `SM_CXMINTRACK=202`、`SM_CYMINTRACK=56`），`Show()` 那一刻会把
   66×66 的胶囊**撑成 202×66**，后果正好就是用户报的两个症状：
   ① 圆角 Region 仍是 66×66 → 内容被裁掉一块（"图标显示不全"）；
   ② 吸附/半埋几何按 202 宽算 → 埋掉的是**空白窗口区**，图标整块露着、离边一截
   （"完全没有埋进去，连吸附都没有"）。
   `WS_EX_TOOLWINDOW`（= v2 里 PySide6 的 `Qt::Tool`）直接绕开钳制（探针实测：加与不加，
   `Show()` 后 `66x66` vs `202x66`，四组对照全一致）。
   **回归门 = `dist\PromptDrawer.exe --uitest`**。
   ⚠️ 这个症状**不要**再从 DPI / manifest / WM_DPICHANGED 方向找——2026-10-05 在这儿烧了一整天。
2. **布局尺寸一律经 `Program.S(px)` 缩放**，禁止裸像素；`Scale` 在 Program 的
   **显式静态构造函数**里测定（144）。**绝不能改回字段初始化器**：没有显式静态构造时
   CLR 是 beforefieldinit——CapsuleForm/DrawerForm 的 static readonly 尺寸会在
   Scale=1 时被提前算死（图标糊+缩放错乱的根因之二）。
3. 四个窗体全部 `AutoScaleMode = None`（显式），防止框架二次缩放。
   **DPI 感知的真实来源 = `Program` 显式静态构造里的运行时 `SetProcessDpiAwarenessContext`**；
   exe 里嵌的 manifest 其实是 **csc 默认的、没有 dpiAwareness 段**（`build.bat` 从未传
   `/win32manifest`）。实测"有/无 PerMonitorV2 manifest"两种构建行为**完全一致**
   （framework `DeviceDpi` 都停在 96、都不做二次缩放）→ **别为了 DPI 去折腾 manifest**；
   旧规则"必须走 manifest、否则框架再缩 2/3"是当初误诊留下的，已作废。
4. **`GetWindowRect` 就是真实窗口矩形，等于 `Form.Size`**（探针实测逐像素相等，**不含**任何
   DWM 阴影边距）。旧规则"右边会多出 ~100px 阴影、别拿它当客户区"是错的——当年那 202
   就是窗口真宽，正是硬规则 1 的钳制造成的。
5. 数据层：原子写 + 损坏改名留存 `.corrupt-*` **绝不能改回静默重置**；
   `JavaScriptSerializer` 把 JSON 数组反序列化成 **ArrayList 不是 List<object>**；
   设置读取一律 `Convert.ToXxx`（装箱 long 拆箱 (int) 会抛 InvalidCastException）。
6. **单实例唤醒 = FindWindow(标题) 直投 WM_WAKE**；HWND_BROADCAST 对 DPI 虚拟化
   窗口不可靠（实测丢失）。
7. **跑 --selftest / 拖拽测试前先杀正式实例**（Alt+P 全局热键被占会假失败）。
8. **悬停滑动动画：停表必须连 Tick 处理器一起摘（`StopPop()`），且不要再引入"动画闸"标志。**
   早期版本在 `PopTo` 里维护 `_hoverAnim` 当闸挡住"动画进行中的 enter/leave"，
   而按下（`CapsuleMouseDownReal`）时只 `_popTimer.Stop()` 没复位它 → 只要在 130ms 动画
   中途抓一下胶囊，该标志就**永久卡 true** → 进出前提永不成立 →
   **"多试了几下就再也不弹出"**（2026-10-05 用户实测，`--uitest` T6~T8 是它的回归门）。
   另外只 `Stop()` 不摘处理器会留下**孤儿处理器**：它继续数自己的步数，数满时反手把新动画
   `Stop()` 掉。现按 v2 `_stop_anim()` 语义：进出随时互相打断、每段动画干净接管
   （用户裁决：动画中途移开要**立刻缩回**，故两道闸都已去掉）。
   同类"状态标志卡住"还有：拖放后 `_dragged` 必须复位（否则首次悬停不滑出）。
   其余：WinForms **Button 没有 Checked**（自定义字段）；`toolBtn` 类固定窄宽会裁字。
9. csc 只支持 **C# 5**：无局部函数/字符串插值/out var/范围运算符。build.bat 与
   含中文的 .cs 必须 UTF-8，但 **.bat 本身只能 ASCII**（cmd 按 GBK 解析）。
10. **【绘制路径里绝不允许每次重新加载资源】** —— `OnPaint` / `DrawItem` 里**不许**
    `Image.FromFile` / `new Font` / `new Bitmap` 之类的重活。实测代价（2026-10-05）：
    - `Image.FromFile` 每次重绘 **+2.06MB 本地内存、+3.7ms**，200 次重绘 = **+412.7MB**；
      而且**独占锁住那个文件**直到被 GC（会让程序运行时 `build.bat` 的 copy 失败）。
      用户实例就是这样从 52MB 涨到 **422MB** 的。→ 必须缓存：`Program.Logo()`（读字节→解码一次→
      拷成自管位图，脱离流所以不锁文件）。缓存后 200 次重绘 **+0.3MB / 56ms**。
    - `Theme.Font(size,bold)` 以前每次 `new Font`（OwnerDraw 每行每帧取 3~4 次），实测每次约
      430 字节垃圾 → 已改成字典缓存。**安全性已实测**：WinForms 控件**不会** Dispose 交给它的
      `Font` / `Image`（Label/Button 都验过），所以共享缓存是安全的。
    - **回归门 = `--uitest` T10**（反复重绘 120 次，WorkingSet 增量必须 < 8MB；未修前约 +247MB）。
11. **【滑出位必须贴屏幕边（margin=0），移出必须带位置护栏】** —— 滑出位若按 `EdgeMargin`
    留 3px 缝，滑出时窗口会从光标底下挪走那 3px → 假 `MouseLeave` → 缩回 → 又压住光标 →
    又滑出 → **"鬼畜抖动"**（2026-10-05 用户实测；四条边全是 3px 缝，贴边后 0px）。
    - `FlushPos()` 必须用 `Snapped(..., margin: 0)`；**停放位仍用 `EdgeMargin`**（别改错）。
    - `OnMouseLeave` 里先判 `WantOut()`（光标是否还在 停放位∪滑出位 矩形内）：还在就**不缩回**，
      改开按需看门狗；进入仍走事件（否则测试与手感都依赖轮询）。
    - 看门狗（`_watchdog`）**只在疑似卡住时开**，收敛即 `Stop()`，**不常驻**（实测常驻
      100ms 定时器本身几乎零成本：不起线程、托管堆零增长、CPU 低于噪声底，但仍不默认开）。
    - **回归门 = `--uitest` T9**（四条边：半埋窗口的屏幕内部分必须被滑出窗口完全包含）。
12. **【每个"布局 helper"都必须把布局光标推到自己之下】** —— 设置页用类字段 `_sy` 当光标：
    `STitle`/`SCb`/`SBtn`/`SInfo` 摆完控件后必须 `_sy = 控件.Bottom`（或 `+= 控件高+间隙`），
    调用处的 `_sy += n` 只表示**间隙**。2026-10-06 实测：`SBtn`/`SInfo` 当时不推进 → 调用处写的
    6px"间隙"变成"间距"，4 个数据按钮两两**重叠 22px**、标题压住按钮 18px（共 5 处真重叠），
    被压住的按钮点不到。**回归门 = `--uitest` T12**（设置页控件二维重叠数必须为 0）。
13. **【换主题必须遍历整棵控件树】** —— 别手工点名控件。老 `ApplyColors()` 只改了十来个固定控件
    （且只扫 `_header` 里的 MiniButton）→ 暗色下搜索行「＋ 新建」仍纯白 (255,255,255)、记事本页
    按钮、列表空态提示全留着亮色。现统一 `ApplyControl(Control)` 递归，并按类型分派：
    输入类→Surface、`_toast`→Tooltip 配色、`emptyHint`→Canvas+TextMuted、其余→Canvas+Text，
    再让 `TabButton`/`MiniButton`/`ChipButton` 各自 `ApplyTheme()`。
    `ChipButton` 必须记住 `Selected`、`MiniButton` 必须记住 `Danger`，否则换主题会丢状态色。
14. **【`build.bat` 里判断成败不能用 `if errorlevel 1`】** —— cmd 的 `errorlevel` 比较是**有符号**的，
    而崩溃退出码是**负数**（如 `0xC0000005` = `-1073741819`）→ `if errorlevel 1` 会**判成成功**，
    冒烟门"崩溃也绿灯"（2026-10-06 真实踩到：uitest 崩了却被报成 BUILD OK）。
    必须写 `if not "%errorlevel%"=="0" (...)`。
15. **【控件建出来必须 `Controls.Add`，UI 元素必须有可见性回归门】** —— 2026-10-06 两次同款事故：
    `_toast` 从未加入控件树 → 所有操作提示隐形；EditDialog 的 `_cat` 下拉框从未加入 →
    编辑时无法改分类。两者都编译通过、逻辑正常，只是**永远不显示**。
    **回归门 = T14（toast.Parent）**；下拉框靠渲染评审抓（01/08/09 逐像素）。
16. **【热键注册失败绝不能落盘为当前热键】** —— `RegisterDrawerHotkey` 必须 `save && ok` 才写
    `hotkey` 本身；`RestoreHotkey()` 注册失败要回退默认 Alt+P。否则坏热键被持久化、
    回退读回的正是坏值 → 旧热键已注销、新热键没注上，**一个热键都不剩**。
    **回归门 = T15**（占位窗体抢 Ctrl+Alt+F24 → 注册失败 → 断言设置未变）。
17. **【DropDownList ComboBox 的 BackColor 在视觉样式下被 OS 忽略，`FlatStyle.Flat` 也无效】** ——
    2026-10-06 探针实测：Flat/System/Popup 三种 FlatStyle 下收起面板都是 (253,253,253)。
    唯一可靠解 = `DrawMode.OwnerDrawFixed` + `DrawItem` 自绘（`DrawerForm.ThemeComboDraw`，
    收起面与下拉项一起变暗；右侧箭头小钮仍系统绘制，归 native-chrome 豁免族）。
    曾以为"_cat 设了 Flat 就正常"是误诊——编辑对话框从未渲染过，没人真看过它。
18. **【置顶真根因：WinForms 进程里"第一个 Form"拿不到 `WS_EX_TOPMOST`（2026-10-07 定案，推翻了旧的"剥离器"说法）】** ——
    ① **真根因（实测复现）**：同一个 Form 配置连做三次 → **第 1 个**建句柄时就是 `off`，事后
    `SetWindowPos(HWND_TOPMOST)` / `SetWindowLong` **都无效**（返回 True、位不变）；
    **第 2、3 个**建句柄就是 `ON`。**先造一个一次性窗体再建同样的窗体 → 第 1 个也是 ON**。
    胶囊恰好是本进程第一个 Form（`Program.cs:241`，抽屉在它之后）⇒ 它永远压不住别的窗口
    = 用户报的"网页盖住悬浮图标"。抽屉（第 2 个）一直是正常的 ✓，与现象完全吻合。
    **修法 = `Program.WarmupFirstForm()`**：在 `new CapsuleForm()` 之前先建一个**屏幕外**
    (-4000) 的 8×8 无边框一次性窗体，占掉那个名额，用完即弃（不会闪一下）。
    **修复实测**：胶囊 `ex` 从 `0x10080` → **`0x10088`(TOPMOST)**；15 秒采样 off=0；守护日志
    不再出现（出生就带位，不需要补）✓。回归门 = uitest T16（`--uitest` 里对抽屉做剥位→补位）。
    ② **⚠️ 旧结论作废：不要再往"外部剥离器"方向查**。曾经的判断（Nahimic/A-Volute 注入
    `AudioDevProps2.dll` 会剥 TOPMOST）**已被实测否定**：把 `NahimicService` 停掉、并且
    **重启程序**（新进程里确认**没有**那个 DLL —— 注入发生在进程启动时，停服务不会从已运行
    的进程里卸掉）之后，现象**一模一样**（胶囊 `off`、写入被吞）。⇒ **Nahimic 是清白的。**
    ③ **旧的"跨进程写入被吞"观察是我的探针手法有诈**：`SetWindowLong` 改**别的进程**的窗口
    EXSTYLE 本来就无效；而在"第一个窗体"这个条件下 `SetWindowPos` 也确实无效 —— 两者叠一起
    造成了"有个东西在剥离"的错觉。**跨进程探针只能做参考，结论必须用进程内证据（日志）兜底。**
    ④ 仍然保留的防线（无害，且对"窗口被重建/DPI 变化"等情况有用）：
    `Program.EnsureTopMost`（真实 EXSTYLE 检查 + 补位）+ 1s 守护 `StartTopmostGuard` +
    窗体 `OnVisibleChanged` 即补 + 连续 5 秒补不上就判定"被干扰"并发一次托盘气泡（限频 5 分钟）
    + `pmv3_helper.exe <pid>` 手动补位。

## 测试

```
dist\PromptDrawer.exe --selftest    # 数据层 50 项（第 10/11 节：提示词回收站·批量 / 双存档·笔记页回收站·分类左右移）
dist\PromptDrawer.exe --uitest      # 被动 UI 32 项（…+ T27 SmoothListBox 免擦除 + T28 悬停瞬时切换 + T29 抗闪烁=顶层窗体 WS_EX_COMPOSITED、列表控件上不得有 + T30 头部两按钮等大/等距/同线 + 收起钮矩形条）+ 构建期自动跑并留档 dist\uitest_last.txt
```
`build.bat` 重建时会自动跑一次 `--uitest` 当冒烟；**判定必须用 `if not "%errorlevel%"=="0"`**
（见硬规则 14，`if errorlevel 1` 会把崩溃当成功）。
`--uitest` 只在**屏幕外**开窗，不碰鼠标键盘、不截屏；数据目录指向**系统临时目录**，
跑完自动删除（T11/T12 需要构造 DrawerForm，所以会用到 Store，但绝不碰用户的 prompts_data.json）。
反射取私有字段必须**顺着继承链**找（`QuietDrawer` 之类子类用 `GetType()` 找不到基类私有字段，
2026-10-06 踩过 → NullReferenceException）。

**⛔ 绝不操控用户的鼠标/键盘**（2026-10-05 用户连说 5 次「不要随意操控我的鼠标，
我还在干别的事情」）。合成输入（`SetCursorPos` / `mouse_event`）和**整屏截图**一律禁止
——整屏截图会拍到用户当时在看的别的东西（当时就拍到了）。
允许的验证手段：屏幕外建窗 + `--uitest`、只读 `GetWindowRect`、`PrintWindow` **只抓本应用
自己的窗口**、`DrawToBitmap` 内存渲染、应用内日志。

## 待办池

- **【留痕·别主动提】** 用户原有的 **21,900 条 / 40 分类**提示词库现在归档在本目录
  `旧版数据\`（`prompts_data-v2-主库.json` 24.2MB ＋ `prompt-manager-v2-归档-20261006.zip`
  21.5MB，含 v2 全部源码 / 滚动备份 / v1 数据归档）。v3 现库是后来"全新开局"的 0 条 / 8 分类，
  **与任何 bug 无关、没有数据丢失**。**1 万条上限已移除（2026-10-07），旧库随时可用设置页
  「从 v2 / JSON 导入…」手动导回**（导入按标题去重；21,900 条超出 1 万条性能基准，实测预计仍流畅但未基准化）。
- ✅ v2 退役**已完成**（2026-10-06）：目录已删 163MB、`HKCU\...\Run\PromptDrawerV2` 已删、
  数据与源码归档在 `旧版数据\`。**回退路径**：从那个 zip 解出 `源码/` 重新打包即可（数据同 schema）。
- ✅ 分类管理 UI **已完成**（2026-10-07，批次三）：chips 行末「＋ 分类」+ chip 右键改名/删除。
- ✅ emoji→图标 **已完成**（2026-10-07，批次三）：UI chrome 无 emoji；用户分类 icon 不动。
- 多显示器不同 DPI：系统级感知下副屏会被位图拉伸（用户单屏，暂不处理）。
- **✅ 置顶（"网页盖住悬浮图标"）已真修复**（2026-10-07，见硬规则 18）：
  根因 = **WinForms 进程里第一个 Form 拿不到 `WS_EX_TOPMOST`**，胶囊正是第一个（抽屉是第二个、一直正常）。
  修法 = `Program.WarmupFirstForm()`（先建一个屏幕外一次性窗体占名额）。实测胶囊 `ex` `0x10080`→`0x10088`、
  15 秒采样 0% 掉位。**旧"外部剥离器/Nahimic"结论已否定**，别再往那方向查
  （`_tmp_zlog\topmost_probe\` 下那批探针保留作证据；`run_ab_test.cmd` 已无用）。
  ⚠️ **遗留现场**：那次 A/B 实验把 `NahimicService` 停掉了（脚本的 `Start-Service` 卡住、未能恢复）
  —— 恢复用 `_tmp_zlog\topmost_probe\run_restore_nahimic.cmd`（一次 UAC），或直接重启电脑（服务是自动启动）。
- **❌ 后续优化方向已被用户否决（2026-10-07）**：下面 6 条调研产物用户原话「**1-6 感觉对我来说都没啥价值**」
  → **不要再主动提议、不要再开工**（留着仅作记录）：
  ① **变量模板**：提示词内 `{{变量}}` 占位，复制时弹小窗填参（对标 Espanso 光标定位/表单填充、
     Raycast Snippets——AI 提示词工具的头部刚需）；
  ② **全局快速呼出搜索面板**：热键唤起迷你搜索框，输关键字 → 回车直接复制（对标
     Beeftext Combo Picker、uTools/Raycast 面板——比打开整个抽屉快一个量级）；
  ③ **使用统计**：记录每条复制次数/最近使用，支持按热度排序（本地 json 即可）；
  ④ **导入格式扩展**：CSV / Markdown / Notion 导出（现仅 JSON 合并 + TXT）；
  ⑤ **二级分类独立筛选语义**（现点二级 = 筛它本身，不含父级反向聚合）；
  ⑥ **分类拖拽排序**（chips 网格已等宽，拖换位置成本已降低）。
  调研参照物：Beeftext（开源 Windows 文本展开器，Combo Picker 模式）、Espanso（YAML 文本扩展，
  变量/光标定位）、PhraseExpress（商业宏）、微软新记事本（轻量哲学）、AI Prompt Organizer
  （微软商店本地提示词库）、Agenta/Langfuse（团队向，参考其版本化思路）。
