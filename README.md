# 提示词抽屉 v3（C# 轻量重写版）

**全新轻量实现**：C# / .NET 4.8 WinForms（系统自带 csc 编译，零安装零依赖），
按用户要求重写——上限 1 万条、去掉连锁索引、UI 重排、**内存 ~50MB**（v2 PySide6 为 ~220MB）。

## 与 v2 的关系（v2 已于 2026-10-06 退役）

- **数据格式完全兼容**（prompts_data.json / notes.json 同 schema）。
- **v2 目录已整目录删除**（原因：v2 与 v3 都注册了开机自启、且都抢 `Alt+P` → 开机双开、热键互抢）。
  数据已完整归档到本目录的 `旧版数据\`：**主库 21,900 条 / 40 分类**（`prompts_data-v2-主库.json`）
  ＋ 完整归档包 `prompt-manager-v2-归档-20261006.zip`（含 v2 全部源码、滚动备份、v1 数据归档）。
  详见 `旧版数据\README.md`。
- **首启自动导入已关闭**（上限 1 万条 < 旧库 21,900 条，自动导入会直接超限）；
  要导旧库请用设置页的「从 v2 / JSON 导入…」手动选 `旧版数据\prompts_data-v2-主库.json`。
- **v2 的 build.bat 曾修过一版**：以前每次重建会把开发目录旧数据覆盖到 exe 目录（用户数据被
  "重置"的根因），后改为永不覆盖（随 v2 一起退役）。

## 与 v2 的差异（用户指定）

| 项 | v2 | v3 |
|---|---|---|
| 技术栈 | PySide6 (~220MB) | C# WinForms (~50MB) |
| 提示词上限 | 无 | **无硬上限**（隐性性能要求：1 万条内极速，selftest 有基准门：加载 97ms / 筛选 8ms 实测） |
| 连锁/索引/wiki | [[关联]]跳转 + id/标题索引 | **全部移除** |
| UI 顺序 | 搜索→分类下拉→标签chips | **页签 → 分类chips(全部/收藏/自建分类含二级└) → 搜索+＋新建 → 列表** |
| 底部状态栏 | 有 | **去掉** |
| 卡片 | QWidget 卡 | ListBox OwnerDraw 行（GDI 直绘，1 万条无压力） |
| 收起方式 | — 按钮 | **双击头部 / Esc / 点外部（含桌面）/ Alt+P / — 按钮** |
| 多选删除 | Shift/Ctrl/Ctrl+A | Ctrl/Shift 多选 + Del + 右键批量删除 |
| 排序 | 卡片 hover ↑⤒↓ | 列表行 hover ↑⤒↓（同一套几何） |
| 记事本 | 多页+自动保存 | 同（notes.json 兼容） |

## 保留的核心体验

悬浮胶囊（44×44 logo 圆角图标、拖动、贴边半隐藏、hover 滑出）、Alt+P 全局热键（可改）、
托盘（显隐/自启开关/切主题/退出）、单实例（二次启动唤醒抽屉，Mutex+FindWindow 直投）、
开机自启默认开（HKCU Run）、每日滚动备份（保留 10 份）、原子写 + 损坏改名留存、
小独爱心亮/暗双主题（token 自套件移植成 C# 色表）、1 万条加载 **81ms**、
**分类管理**（chips 行末「＋ 分类」新建，chip 右键重命名/删除，删除的提示词自动归未分类）、
**记事本完善**（查找栏 Ctrl+F/F3、状态栏字数、右键菜单：自动换行/时间戳 F5/导出导入 TXT、
Ctrl+S 手动保存、Ctrl+滚轮字号、拖拽 TXT 导入、CRLF 归一）、
**置顶守护**（每秒核对真实置顶位自动补回——本机 Nahimic 注入 DLL 会拦截置顶写入，
详见 AGENTS.md 硬规则 18；`pmv3_helper.exe` 可手动强制恢复）。
UI 无 emoji 图标（2026-10-07 起）：单字形按钮用 Segoe Fluent Icons，其余纯文字；
分类图标属用户数据不受限。

## 构建 / 测试

```
build.bat                    # csc → dist\PromptDrawer.exe + dist\pmv3_helper.exe（自动跑一次 --uitest 冒烟）
dist\PromptDrawer.exe --selftest   # 数据层自检 50 项（损坏保护/合并导入/备份/分类增删改/回收站/双存档/1 万条性能基准）
dist\PromptDrawer.exe --uitest     # 被动 UI 自检 29 项（胶囊/几何/悬停滑动/重绘内存/页签/设置页/chips 网格/toast/热键失败/置顶守护/记事本查找栏/对话框置顶/列表双缓冲/悬停淡入淡出；屏幕外跑，不动鼠标不截屏）
```

## 结构

```
Program.cs         入口：单实例唤醒/托盘/热键/自启同步/置顶守护
Store.cs           数据层（原子写/损坏留存/1w上限/筛选/排序/记事本/合并导入/导出/备份/分类增删改）
Theme.cs           小独爱心亮暗色表（自 tokens.json 移植；改色需同步套件）+ 图标字体缓存
DrawerForm.cs      抽屉（页签/chips+分类管理/搜索/OwnerDraw列表/记事本/设置/编辑对话框/热键捕获/Toast）
CapsuleForm.cs     悬浮胶囊（拖动/贴边半隐藏/hover滑出，几何与 v2 同源；**必须 WS_EX_TOOLWINDOW**）
SelfTest.cs        --selftest 自检（数据层）
UiTest.cs          --uitest 自检（被动 UI；屏幕外跑，不碰鼠标不截屏）
TopmostHelper.cs   pmv3_helper.exe 源码（一次性置顶补位小工具，绕过 Nahimic 注入拦截）
```

## 已知边界

- wiki `[[关联]]` 跳转与注释链接 chip 已按要求移除（note 字段仍在、仍可搜索）。
- 分类 chips 只到"全部/收藏/各分类（二级带└缩进）"，二级独立筛选=点它本身。
- 自动收起可用环境变量 `PMV3_NO_AUTOCOLLAPSE=1` 临时关闭（测试用）。
- 自检/截图前先杀正式实例（Alt+P 全局热键被占会假失败）。
