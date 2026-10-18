// -*- coding: utf-8 -*-
// Store.cs — 数据层：prompts_data.json / notes.json（与 v2 完全同 schema）
// 原子写 + 损坏改名留存 + 每日滚动备份 + 无硬上限（隐性要求：1 万条内极速，SelfTest 有基准门）
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace PromptDrawer
{
    public class Prompt
    {
        public string id = "";
        public string title = "";
        public string content = "";
        public List<string> tags = new List<string>();
        public string category_id = "";
        public bool favorite = false;
        public string note = "";
        public int sort_order = 0;
        public string created_at = "";
        public string updated_at = "";
        public string deleted_at = "";   // 非空 = 在回收站里（2026-10-07 加；活着的提示词恒为空）
    }

    public class Category
    {
        public string id = "";
        public string name = "";
        public string icon = "📁";
        public string color = "#2478A8";
        public string parent_id = "";
    }

    public class NotePage
    {
        public string id = "";
        public string content = "";
        public string created_at = "";
        public string updated_at = "";
        public string deleted_at = "";   // 非空 = 在笔记页回收站里（2026-10-08 加）
    }

    public class Store
    {
        public static string DataDir = "";
        static JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public List<Prompt> Prompts = new List<Prompt>();
        public List<Category> Categories = new List<Category>();
        public Dictionary<string, object> Settings = new Dictionary<string, object>();
        public List<NotePage> NotePages = new List<NotePage>();
        // 回收站（2026-10-07）：删除不再直接丢，先进这里，可在设置页恢复/清空。
        // 上限 TrashCap 条，超出按 deleted_at 丢最旧的（时间戳是 yyyy-MM-dd HH:mm:ss，字符串序即时间序）。
        public List<Prompt> Trash = new List<Prompt>();
        public const int TrashCap = 200;
        // 笔记页回收站（2026-10-08）：删页不再直接丢，可一键恢复（用户要的"防误删丢失"）
        public List<NotePage> TrashNotes = new List<NotePage>();
        public const int NoteTrashCap = 100;
        public string ActiveNoteId = "";
        public string CorruptBackup = "";
        public bool Dirty = false;
        public bool NotesDirty = false;
        // 最近一次 Save/SaveNotes 是否写盘失败（磁盘满/文件被占）。QuitApp 据此弹警告，
        // 不再静默吞掉——此前失败只把 Dirty 置回，用户完全不知情
        public bool SaveFailed = false;

        static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        static string NewId(string prefix)
        {
            return prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        // ------------------------------------------------------------ 路径
        public string DataFile { get { return Path.Combine(DataDir, "prompts_data.json"); } }
        public string NotesFile { get { return Path.Combine(DataDir, "notes.json"); } }

        // ------------------------------------------------------------ 设置读写
        public object GetSetting(string key, object def)
        {
            object v;
            if (Settings.TryGetValue(key, out v)) return v;
            return def;
        }
        public object GetSetting(string key)
        {
            return GetSetting(key, null);
        }
        public void SetSetting(string key, object value)
        {
            Settings[key] = value;
            Dirty = true;
        }

        // ------------------------------------------------------------ 加载
        public void Load()
        {
            LoadMain();
            LoadNotes();
        }

        void LoadMain()
        {
            if (File.Exists(DataFile))
            {
                try
                {
                    var root = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(DataFile));
                    Settings = GetDict(root, "settings");
                    Categories = ToCategories(GetList(root, "categories"));
                    Prompts = ToPrompts(GetList(root, "prompts"));
                    Trash = ToPrompts(GetList(root, "trash"));
                    MigrateLegacy();
                    return;
                }
                catch (Exception)
                {
                    // 损坏保护：改名留存，绝不覆盖（v2 同款策略）
                    try
                    {
                        var bak = DataFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Move(DataFile, bak);
                        CorruptBackup = bak;
                    }
                    catch (Exception) { }
                }
            }
            Defaults();
        }

        void Defaults()
        {
            Settings = new Dictionary<string, object>();
            if (Categories.Count == 0)
                Categories.Add(new Category { id = "cat_default", name = "默认分类", icon = "📁", color = "#2478A8" });
            Save();
        }

        void LoadNotes()
        {
            if (File.Exists(NotesFile))
            {
                try
                {
                    var root = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(NotesFile));
                    NotePages = new List<NotePage>();
                    foreach (var o in GetList(root, "pages"))
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        NotePages.Add(new NotePage
                        {
                            id = Str(d, "id"),
                            content = Str(d, "content"),
                            created_at = Str(d, "created_at"),
                            updated_at = Str(d, "updated_at"),
                            deleted_at = Str(d, "deleted_at"),
                        });
                    }
                    TrashNotes = new List<NotePage>();
                    foreach (var o in GetList(root, "trash_pages"))
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        TrashNotes.Add(new NotePage
                        {
                            id = Str(d, "id"),
                            content = Str(d, "content"),
                            created_at = Str(d, "created_at"),
                            updated_at = Str(d, "updated_at"),
                            deleted_at = Str(d, "deleted_at"),
                        });
                    }
                    ActiveNoteId = Str(root, "active_id");
                }
                catch (Exception)
                {
                    try
                    {
                        var bak = NotesFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Move(NotesFile, bak);
                    }
                    catch (Exception) { }
                    NotePages = new List<NotePage>();
                }
            }
            if (NotePages.Count == 0)
            {
                NotePages.Add(new NotePage { id = NewId("note"), created_at = Now(), updated_at = Now() });
            }
            if (FindNote(ActiveNoteId) == null) ActiveNoteId = NotePages[0].id;
        }

        static Dictionary<string, object> GetDict(Dictionary<string, object> root, string key)
        {
            if (root == null) return new Dictionary<string, object>();
            object v;
            if (root.TryGetValue(key, out v) && v is Dictionary<string, object>)
                return (Dictionary<string, object>)v;
            return new Dictionary<string, object>();
        }
        static List<object> GetList(Dictionary<string, object> root, string key)
        {
            if (root == null) return new List<object>();
            object v;
            if (!root.TryGetValue(key, out v) || v == null) return new List<object>();
            // JavaScriptSerializer 把 JSON 数组反序列化为 ArrayList（不是 List<object>）
            var al = v as System.Collections.ArrayList;
            if (al != null) return new List<object>(al.ToArray());
            if (v is List<object>) return (List<object>)v;
            return new List<object>();
        }

        static List<string> ToStringList(object v)
        {
            var r = new List<string>();
            var al = v as System.Collections.ArrayList;
            if (al != null)
                foreach (var t in al) if (t != null && t.ToString().Trim() != "") r.Add(t.ToString().Trim());
            if (v is List<object>)
                foreach (var t in (List<object>)v) if (t != null && t.ToString().Trim() != "") r.Add(t.ToString().Trim());
            return r;
        }
        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return v.ToString();
            return "";
        }
        static string Str(Dictionary<string, object> d, string key, string def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return v.ToString();
            return def;
        }

        List<Category> ToCategories(List<object> list)
        {
            var outp = new List<Category>();
            foreach (var o in list)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                outp.Add(new Category
                {
                    id = Str(d, "id", NewId("cat")),
                    name = Str(d, "name", "未命名"),
                    icon = Str(d, "icon", "📁"),
                    color = Str(d, "color", "#2478A8"),
                    parent_id = Str(d, "parent_id"),
                });
            }
            return outp;
        }

        List<Prompt> ToPrompts(List<object> list)
        {
            var outp = new List<Prompt>();
            foreach (var o in list)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                object tv;
                var tags = ToStringList(d.TryGetValue("tags", out tv) ? tv : null);
                outp.Add(new Prompt
                {
                    id = Str(d, "id", NewId("p")),
                    title = Str(d, "title", "未命名"),
                    content = Str(d, "content"),
                    tags = tags,
                    category_id = Str(d, "category_id"),
                    favorite = d.TryGetValue("favorite", out tv) && tv is bool && (bool)tv,
                    note = Str(d, "note"),
                    sort_order = d.TryGetValue("sort_order", out tv) && tv is int ? (int)tv : 0,
                    created_at = Str(d, "created_at", Now()),
                    updated_at = Str(d, "updated_at", Now()),
                    deleted_at = Str(d, "deleted_at"),
                });
            }
            return outp;
        }

        void MigrateLegacy()
        {
            bool changed = false;
            foreach (var c in Categories)
            {
                if (string.IsNullOrEmpty(c.color)) { c.color = "#2478A8"; changed = true; }
            }
            if (changed) Dirty = true;
        }

        // ------------------------------------------------------------ 保存（原子写）
        public void Save()
        {
            Dirty = false;
            try
            {
                Directory.CreateDirectory(DataDir);
                var payload = new Dictionary<string, object>();
                payload["settings"] = Settings;
                payload["categories"] = Categories;
                payload["prompts"] = Prompts;
                payload["trash"] = Trash;
                var tmp = DataFile + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(payload));
                if (File.Exists(DataFile)) File.Replace(tmp, DataFile, null);
                else File.Move(tmp, DataFile);
                SaveFailed = false;
            }
            catch (Exception) { Dirty = true; SaveFailed = true; }
        }

        public void SaveNotes()
        {
            NotesDirty = false;
            try
            {
                Directory.CreateDirectory(DataDir);
                var payload = new Dictionary<string, object>();
                payload["pages"] = NotePages;
                payload["active_id"] = ActiveNoteId;
                payload["trash_pages"] = TrashNotes;
                var tmp = NotesFile + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(payload));
                if (File.Exists(NotesFile)) File.Replace(tmp, NotesFile, null);
                else File.Move(tmp, NotesFile);
                SaveFailed = false;
            }
            catch (Exception) { NotesDirty = true; SaveFailed = true; }
        }

        public void Flush() { if (Dirty) Save(); if (NotesDirty) SaveNotes(); }

        // ------------------------------------------------------------ 每日滚动备份
        // force=false：24h 内已有备份则跳过（启动时的自动节奏）
        // force=true ：设置页「立即备份」按钮用——按钮语义就是马上备份，
        //              此前 24h 内点了只会得到"备份跳过"，与语义冲突
        public string RollingBackup() { return RollingBackup(false); }
        public string RollingBackup(bool force)
        {
            if (!File.Exists(DataFile)) return null;
            var bdir = Path.Combine(DataDir, "backups");
            Directory.CreateDirectory(bdir);
            var latest = "";
            if (!force)
            {
                foreach (var f in Directory.GetFiles(bdir, "prompts_backup-*.zip"))
                    if (string.Compare(Path.GetFileName(f), latest) > 0) latest = Path.GetFileName(f);
                if (latest != "")
                {
                    DateTime last;
                    if (DateTime.TryParseExact(
                            latest.Substring("prompts_backup-".Length, "yyyyMMdd-HHmm".Length),
                            "yyyyMMdd-HHmm", null, System.Globalization.DateTimeStyles.None, out last))
                    {
                        if ((DateTime.Now - last).TotalHours < 24) return null;
                    }
                }
            }
            var zip = Path.Combine(bdir, "prompts_backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip");
            using (var fs = new FileStream(zip, FileMode.Create))
            using (var arch = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                arch.CreateEntryFromFile(DataFile, "prompts_data.json", CompressionLevel.Optimal);
                if (File.Exists(NotesFile))
                    arch.CreateEntryFromFile(NotesFile, "notes.json", CompressionLevel.Optimal);
            }
            // 保留 10 份
            var files = Directory.GetFiles(bdir, "prompts_backup-*.zip");
            Array.Sort(files);
            for (int i = 0; i < files.Length - 10; i++) { try { File.Delete(files[i]); } catch (Exception) { } }
            return zip;
        }

        // ------------------------------------------------------------ 分类
        public Category FindCategory(string id)
        {
            foreach (var c in Categories) if (c.id == id) return c;
            return null;
        }
        public List<Category> ChildrenOf(string parentId)
        {
            var r = new List<Category>();
            foreach (var c in Categories) if ((c.parent_id ?? "") == (parentId ?? "")) r.Add(c);
            return r;
        }
        public HashSet<string> DescendantsOf(string catId)
        {
            var r = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(catId);
            while (stack.Count > 0)
            {
                var pid = stack.Pop();
                foreach (var c in ChildrenOf(pid)) { r.Add(c.id); stack.Push(c.id); }
            }
            return r;
        }
        public int CountInCategory(string catId)
        {
            if (string.IsNullOrEmpty(catId))
            {
                int n = 0;
                foreach (var p in Prompts) if (string.IsNullOrEmpty(p.category_id)) n++;
                return n;
            }
            var ids = DescendantsOf(catId); ids.Add(catId);
            int m = 0;
            foreach (var p in Prompts) if (ids.Contains(p.category_id)) m++;
            return m;
        }
        public Category AddCategory(string name, string icon, string color, string parentId)
        {
            name = (name ?? "").Trim();
            if (name == "") return null;
            if (!string.IsNullOrEmpty(parentId) && FindCategory(parentId) == null) return null;
            foreach (var c in ChildrenOf(parentId))
                if (c.name == name) return null;   // 同级重名
            var cat = new Category
            {
                id = NewId("cat"),
                name = name,
                icon = icon ?? "",   // 空 = 无图标（chip/下拉只显示名称；📁 仅作为旧数据缺省值）
                color = string.IsNullOrEmpty(color) ? "#2478A8" : color,
                parent_id = parentId ?? "",
            };
            Categories.Add(cat);
            Dirty = true;
            return cat;
        }

        // 分类改名（分类管理 UI）：空名/同级重名拒绝
        public bool RenameCategory(string catId, string newName)
        {
            var c = FindCategory(catId);
            if (c == null) return false;
            newName = (newName ?? "").Trim();
            if (newName == "") return false;
            foreach (var o in ChildrenOf(c.parent_id))
                if (o.id != catId && o.name == newName) return false;
            c.name = newName;
            Dirty = true;
            return true;
        }
        public bool DeleteCategory(string catId)
        {
            var before = Categories.Count;
            Categories.RemoveAll(c => c.id == catId);
            foreach (var c in Categories) if (c.parent_id == catId) c.parent_id = "";
            foreach (var p in Prompts) if (p.category_id == catId) p.category_id = "";
            if ((string)GetSetting("pinned_cat", "") == catId) SetSetting("pinned_cat", "");   // 置顶槽随分类删除清空
            Dirty = true;
            return Categories.Count < before;
        }

        // ------------------------------------------------------------ 提示词
        public Prompt FindPrompt(string id)
        {
            foreach (var p in Prompts) if (p.id == id) return p;
            return null;
        }
        public Prompt FindByTitle(string title)
        {
            title = (title ?? "").Trim();
            foreach (var p in Prompts) if (p.title.Trim() == title) return p;
            return null;
        }
        int NextOrder()
        {
            int max = -1;
            foreach (var p in Prompts) if (p.sort_order > max) max = p.sort_order;
            return max + 1;
        }
        public string LastError = "";

        public Prompt AddPrompt(string title, string content, List<string> tags,
                                string categoryId, bool favorite, string note)
        {
            LastError = "";
            title = (title ?? "").Trim();
            if (title == "") { LastError = "标题不能为空"; return null; }
            // 无硬上限（2026-10-07）：隐性要求 = 1 万条内极速，SelfTest 第 6/7 节是性能基准门
            var p = new Prompt
            {
                id = NewId("p"),
                title = title,
                content = content ?? "",
                tags = tags ?? new List<string>(),
                category_id = categoryId ?? "",
                favorite = favorite,
                note = note ?? "",
                sort_order = NextOrder(),
                created_at = Now(),
                updated_at = Now(),
            };
            Prompts.Add(p);
            Dirty = true;
            return p;
        }
        public void UpdatePrompt(string pid, string title, string content,
                                 List<string> tags, string categoryId,
                                 bool? favorite, string note)
        {
            var p = FindPrompt(pid);
            if (p == null) return;
            if (title != null) p.title = title.Trim();
            if (content != null) p.content = content;
            if (tags != null) p.tags = tags;
            if (categoryId != null) p.category_id = categoryId;
            if (favorite.HasValue) p.favorite = favorite.Value;
            if (note != null) p.note = note;
            p.updated_at = Now();
            Dirty = true;
        }
        // ---- 回收站（2026-10-07）：删除 = 移入 Trash，可恢复；超过上限按 deleted_at 丢最旧的 ----
        public void DeletePrompts(ICollection<string> ids)
        {
            var set = new HashSet<string>(ids);
            var moved = Prompts.FindAll(p => set.Contains(p.id));
            if (moved.Count == 0) return;
            Prompts.RemoveAll(p => set.Contains(p.id));
            foreach (var p in moved) { p.deleted_at = Now(); Trash.Add(p); }
            TrimTrash();
            Dirty = true;
        }

        // 恢复：id 若与现存提示词撞车（理论上不会）就换新 id，避免覆盖活数据
        public int RestoreFromTrash(ICollection<string> ids)
        {
            var set = new HashSet<string>(ids);
            var back = Trash.FindAll(p => set.Contains(p.id));
            if (back.Count == 0) return 0;
            Trash.RemoveAll(p => set.Contains(p.id));
            foreach (var p in back)
            {
                p.deleted_at = "";
                p.updated_at = Now();
                if (FindPrompt(p.id) != null) p.id = NewId("p");
                Prompts.Add(p);
            }
            Dirty = true;
            return back.Count;
        }

        // 彻底删除（回收站里再删）
        public int RemoveFromTrash(ICollection<string> ids)
        {
            var set = new HashSet<string>(ids);
            int n = Trash.RemoveAll(p => set.Contains(p.id));
            if (n > 0) Dirty = true;
            return n;
        }

        public void EmptyTrash()
        {
            if (Trash.Count == 0) return;
            Trash.Clear();
            Dirty = true;
        }

        void TrimTrash()
        {
            if (Trash.Count <= TrashCap) return;
            Trash.Sort((a, b) => string.CompareOrdinal(a.deleted_at, b.deleted_at));   // 旧的在前
            Trash.RemoveRange(0, Trash.Count - TrashCap);
        }

        // ---- 批量（2026-10-07）：多选后移到分类 / 收藏 ----
        public int MovePrompts(ICollection<string> ids, string catId)
        {
            var set = new HashSet<string>(ids);
            int n = 0;
            foreach (var p in Prompts)
                if (set.Contains(p.id)) { p.category_id = catId ?? ""; p.updated_at = Now(); n++; }
            if (n > 0) Dirty = true;
            return n;
        }

        public int SetFavPrompts(ICollection<string> ids, bool fav)
        {
            var set = new HashSet<string>(ids);
            int n = 0;
            foreach (var p in Prompts)
                if (set.Contains(p.id)) { p.favorite = fav; p.updated_at = Now(); n++; }
            if (n > 0) Dirty = true;
            return n;
        }
        public void ToggleFavorite(string pid)
        {
            var p = FindPrompt(pid);
            if (p == null) return;
            p.favorite = !p.favorite;
            p.updated_at = Now();
            Dirty = true;
        }
        public void SwapOrder(string aId, string bId)
        {
            var a = FindPrompt(aId); var b = FindPrompt(bId);
            if (a == null || b == null) return;
            var t = a.sort_order; a.sort_order = b.sort_order; b.sort_order = t;
            a.updated_at = b.updated_at = Now();
            Dirty = true;
        }
        public void PinTop(string pid)
        {
            var p = FindPrompt(pid);
            if (p == null) return;
            int min = int.MaxValue;
            foreach (var q in Prompts)
                if (q.favorite == p.favorite && q.sort_order < min) min = q.sort_order;
            p.sort_order = min - 1;
            p.updated_at = Now();
            Dirty = true;
        }
        public void ResetAll()
        {
            Prompts.Clear();
            Dirty = true;
        }

        // ------------------------------------------------------------ 筛选
        public List<Prompt> Filter(string keyword, string catId, bool favOnly)
        {
            var result = new List<Prompt>();
            HashSet<string> target = null;
            if (!string.IsNullOrEmpty(catId)) { target = DescendantsOf(catId); target.Add(catId); }
            var kw = (keyword ?? "").Trim().ToLower();
            foreach (var p in Prompts)
            {
                if (favOnly && !p.favorite) continue;
                if (catId != null)
                {
                    if (catId == "") { if (!string.IsNullOrEmpty(p.category_id)) continue; }
                    else if (!target.Contains(p.category_id)) continue;
                }
                if (kw != "")
                {
                    var hay = (p.title + " " + p.content + " " + p.note).ToLower();
                    var hit = hay.Contains(kw);
                    if (!hit)
                        foreach (var t in p.tags)
                            if (t.ToLower().Contains(kw)) { hit = true; break; }
                    if (!hit) continue;
                }
                result.Add(p);
            }
            result.Sort((x, y) =>
            {
                int a = x.favorite ? 0 : 1, b = y.favorite ? 0 : 1;
                if (a != b) return a - b;
                return x.sort_order.CompareTo(y.sort_order);
            });
            return result;
        }

        // ------------------------------------------------------------ 合并导入 / 导出
        // 从 v2 导出的 JSON（或旧版数据文件）合并：分类按名称匹配，提示词按标题去重
        public int MergeFromFile(string path)
        {
            Dictionary<string, object> root;
            try
            {
                root = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            }
            catch (Exception) { return -1; }
            if (root == null || !root.ContainsKey("prompts")) return -1;

            var idMap = new Dictionary<string, string>();
            var imported = ToCategories(GetList(root, "categories"));
            // 先顶级后二级，保证父级先入库
            imported.Sort((a, b) => (a.parent_id == "" ? 0 : 1) - (b.parent_id == "" ? 0 : 1));
            foreach (var c in imported)
            {
                var existing = FindCategoryByName(c.name);
                if (existing != null) { idMap[c.id] = existing.id; continue; }
                var parentId = "";
                if (c.parent_id != "" && idMap.ContainsKey(c.parent_id))
                    parentId = idMap[c.parent_id];
                var cat = AddCategory(c.name, c.icon, c.color, parentId);
                idMap[c.id] = cat != null ? cat.id : "";
            }
            var titles = new HashSet<string>();
            foreach (var p in Prompts) titles.Add(p.title.Trim());
            int added = 0;
            foreach (var o in GetList(root, "prompts"))
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var title = Str(d, "title").Trim();
                if (title == "" || titles.Contains(title)) continue;
                var catId = Str(d, "category_id");
                if (catId != "" && idMap.ContainsKey(catId)) catId = idMap[catId];
                else if (catId != "" && FindCategory(catId) == null) catId = "";
                object tv;
                var tags = ToStringList(d.TryGetValue("tags", out tv) ? tv : null);
                AddPrompt(title, Str(d, "content"), tags, catId,
                    d.TryGetValue("favorite", out tv) && tv is bool && (bool)tv,
                    Str(d, "note"));
                titles.Add(title);
                added++;
            }
            if (added > 0) Dirty = true;
            return added;
        }

        Category FindCategoryByName(string name)
        {
            foreach (var c in Categories) if (c.name == name) return c;
            return null;
        }

        public bool ExportJson(string path)
        {
            try
            {
                var payload = new Dictionary<string, object>();
                payload["settings"] = Settings;
                payload["categories"] = Categories;
                payload["prompts"] = Prompts;
                File.WriteAllText(path, Json.Serialize(payload));
                return true;
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------ 记事本
        public NotePage FindNote(string id)
        {
            foreach (var p in NotePages) if (p.id == id) return p;
            return null;
        }
        public NotePage ActiveNote
        {
            get { return FindNote(ActiveNoteId) ?? NotePages[0]; }
        }
        public string NoteTitle(NotePage p)
        {
            var first = "";
            foreach (var line in (p.content ?? "").Split('\n'))
            {
                var t = line.Trim();
                if (t != "") { first = t.TrimStart('#', '*', '-', '>', ' '); break; }
            }
            if (first == "") first = "空白页";
            if (first.Length > 24) first = first.Substring(0, 24) + "…";
            return first;
        }
        public void SetNoteContent(string pid, string content)
        {
            var p = FindNote(pid);
            if (p == null || p.content == content) return;
            p.content = content;
            p.updated_at = Now();
            NotesDirty = true;
        }
        public NotePage AddNotePage()
        {
            var p = new NotePage { id = NewId("note"), created_at = Now(), updated_at = Now() };
            NotePages.Add(p);
            ActiveNoteId = p.id;
            NotesDirty = true;
            return p;
        }
        // ---- 笔记页回收站（2026-10-08）：删页 = 移入 TrashNotes，可恢复；超上限丢最旧 ----
        public bool DeleteNotePage(string pid)
        {
            if (NotePages.Count <= 1) return false;
            var n = NotePages.Find(p => p.id == pid);
            if (n == null) return false;
            NotePages.Remove(n);
            n.deleted_at = Now();
            TrashNotes.Add(n);
            TrimTrashNotes();
            if (ActiveNoteId == pid) ActiveNoteId = NotePages[0].id;
            NotesDirty = true;
            return true;
        }

        public int RestoreNotePage(ICollection<string> ids)
        {
            var set = new HashSet<string>(ids);
            var back = TrashNotes.FindAll(p => set.Contains(p.id));
            if (back.Count == 0) return 0;
            TrashNotes.RemoveAll(p => set.Contains(p.id));
            foreach (var p in back)
            {
                p.deleted_at = "";
                p.updated_at = Now();
                if (NotePages.Exists(x => x.id == p.id)) p.id = NewId("note");
                NotePages.Add(p);
            }
            NotesDirty = true;
            return back.Count;
        }

        public int RemoveFromTrashNotes(ICollection<string> ids)
        {
            var set = new HashSet<string>(ids);
            int n = TrashNotes.RemoveAll(p => set.Contains(p.id));
            if (n > 0) NotesDirty = true;
            return n;
        }

        public void EmptyTrashNotes()
        {
            if (TrashNotes.Count == 0) return;
            TrashNotes.Clear();
            NotesDirty = true;
        }

        void TrimTrashNotes()
        {
            if (TrashNotes.Count <= NoteTrashCap) return;
            TrashNotes.Sort((a, b) => string.CompareOrdinal(a.deleted_at, b.deleted_at));
            TrashNotes.RemoveRange(0, TrashNotes.Count - NoteTrashCap);
        }

        // ---- 手动存档（2026-10-08，用户要的"双存档并行"）----
        // 自动存档 = notes.json（800ms 防抖、**覆盖写**）；手动存档 = 这里，**每次新建一个独立 TXT**，
        // 永不覆盖任何东西。刻意用纯 TXT：就算 JSON 那条路被写坏/解析失败，这份也一定读得出来，
        // 而且能直接用记事本右键的「导入 TXT 为新页…」导回来（恢复闭环）。
        // prefix="" 正常存档；prefix="已删-" 删页前的自动保险。
        public string ArchiveNotes(string prefix)
        {
            try
            {
                if (prefix == null) prefix = "";
                var dir = Path.Combine(DataDir, "saves");
                Directory.CreateDirectory(dir);
                var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                var path = Path.Combine(dir, prefix + "记事本存档-" + stamp + ".txt");
                int k = 2;
                while (File.Exists(path))    // 同一秒连点也不覆盖：另开一份
                    path = Path.Combine(dir, prefix + "记事本存档-" + stamp + "-" + (k++) + ".txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("提示词管理器V2 · 记事本手动存档");
                sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("页数：" + NotePages.Count);
                sb.AppendLine();
                for (int i = 0; i < NotePages.Count; i++)
                {
                    var p = NotePages[i];
                    sb.AppendLine("===== [" + (i + 1) + "/" + NotePages.Count + "] " + NoteTitle(p) + " =====");
                    sb.AppendLine(Eol(p.content));
                    sb.AppendLine();
                }
                // 带 BOM 存：任何编辑器（含旧记事本）打开中文都不乱码
                File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
                return path;
            }
            catch (Exception) { return null; }
        }

        static string Eol(string s)
        {
            return (s ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        // ---- 分类左右移（2026-10-08）----
        // Categories 列表顺序**就是**显示顺序（Category 没有额外排序字段），所以"左右移"= 在列表里换位。
        // 只在**同级兄弟**之间换（不会把子分类挪到别的父级下）。
        public bool CanMoveCategory(string catId, int dir)
        {
            var me = FindCategory(catId);
            if (me == null) return false;
            var sib = ChildrenOf(me.parent_id);
            int i = sib.FindIndex(c => c.id == catId);
            if (i < 0) return false;
            int j = i + dir;
            return j >= 0 && j < sib.Count;
        }

        public bool MoveCategory(string catId, int dir)
        {
            if (!CanMoveCategory(catId, dir)) return false;
            var me = FindCategory(catId);
            var sib = ChildrenOf(me.parent_id);
            int i = sib.FindIndex(c => c.id == catId);
            var other = sib[i + dir];
            int ia = Categories.IndexOf(me), ib = Categories.IndexOf(other);
            if (ia < 0 || ib < 0) return false;
            var t = Categories[ia]; Categories[ia] = Categories[ib]; Categories[ib] = t;
            Dirty = true;
            return true;
        }
    }
}
