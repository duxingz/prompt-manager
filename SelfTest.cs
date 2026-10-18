// -*- coding: utf-8 -*-
// SelfTest.cs — 无 UI 自检：python selftest.exe --selftest，退出码 0=全过
using System;
using System.Collections.Generic;
using System.IO;

namespace PromptDrawer
{
    public static class SelfTest
    {
        static int _pass, _fail;
        static readonly List<string> _fails = new List<string>();

        static void Check(string name, bool cond, string detail = "")
        {
            if (cond) { _pass++; Console.WriteLine("  PASS " + name); }
            else { _fail++; _fails.Add(name); Console.WriteLine("  FAIL " + name + "  " + detail); }
        }

        // 分类在 Categories 列表里的下标（列表顺序就是显示顺序，所以用它断言左右移）
        static int CatIndex(Store s, string id)
        {
            for (int i = 0; i < s.Categories.Count; i++) if (s.Categories[i].id == id) return i;
            return -1;
        }

        public static int Run()
        {
            Console.WriteLine("== 提示词管理器V2 自检 ==");
            var root = Path.Combine(Path.GetTempPath(),
                "pmv3-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            // ---------- 1. 默认与 CRUD ----------
            var dir1 = Path.Combine(root, "t1");
            Directory.CreateDirectory(dir1);
            Store.DataDir = dir1;
            var s = new Store();
            s.Load();
            Check("默认库：0 条 + 1 分类", s.Prompts.Count == 0 && s.Categories.Count == 1);
            var p1 = s.AddPrompt("词条A", "内容A", new List<string> { "t1" }, "", true, "注A");
            var p2 = s.AddPrompt("词条B", "内容B", new List<string>(), "", false, "");
            var p3 = s.AddPrompt("词条C", "内容C", new List<string>(), "", false, "");
            Check("新增 3 条", s.Prompts.Count == 3 && p1.favorite);
            s.Save();
            Check("原子写无 .tmp 残留", !File.Exists(s.DataFile + ".tmp"));

            var s2 = new Store();
            s2.Load();
            Check("重载一致", s2.Prompts.Count == 3 && s2.FindByTitle("词条A").note == "注A");

            // ---------- 2. 排序 ----------
            // 收藏 A 在最前；B、C 交换后 C 在前
            s2.SwapOrder(p2.id, p3.id);
            var flt = s2.Filter("", null, false);
            Check("收藏置顶 + 交换生效",
                flt[0].title == "词条A" && flt[1].title == "词条C" && flt[2].title == "词条B",
                flt[0].title + "/" + flt[1].title + "/" + flt[2].title);
            s2.PinTop(p3.id);
            var flt2 = s2.Filter("", null, false);
            Check("置顶=普通组最前", flt2[1].title == "词条C");
            Check("跨组交换被 Store 层允许但组序不变",
                s2.Filter("", null, false)[0].title == "词条A");

            // ---------- 3. 筛选 ----------
            var cat = s2.AddCategory("角色", "📁", "#F0824C", "");
            s2.UpdatePrompt(p1.id, null, null, null, cat.id, null, null);
            Check("分类筛选含词条", s2.Filter("", cat.id, false).Count == 1);
            Check("未分类筛选", s2.Filter("", "", false).Count == 2);
            Check("收藏筛选", s2.Filter("", null, true).Count == 1);
            Check("关键词命中标签", s2.Filter("t1", null, false).Count == 1);
            Check("关键词命中内容", s2.Filter("内容B", null, false).Count == 1);

            // ---------- 3b. 分类改名 / 删除（2026-10-07 分类管理 UI 的数据层）----------
            Check("同级重名新增被拒", s2.AddCategory("角色", "📁", "#F0824C", "") == null);
            var cat2 = s2.AddCategory("场景", "🏞", "#2478A8", "");
            Check("新增第二个分类", cat2 != null);
            Check("改名生效", s2.RenameCategory(cat.id, "角色2") && s2.FindCategory(cat.id).name == "角色2");
            Check("同级重名改名被拒", !s2.RenameCategory(cat.id, "场景"));
            var catDel = s2.AddCategory("待删除", "📁", "#2478A8", "");
            s2.UpdatePrompt(p1.id, null, null, null, catDel.id, null, null);
            Check("删除分类后提示词变未分类",
                s2.DeleteCategory(catDel.id) && s2.FindCategory(catDel.id) == null
                && s2.FindPrompt(p1.id).category_id == "");

            // ---------- 4. 记事本 ----------
            var np = s2.AddNotePage();
            s2.SetNoteContent(np.id, "第一行草稿");
            s2.SaveNotes();
            var s3 = new Store();
            s3.Load();
            Check("记事本落盘可读回", s3.ActiveNote.content == "第一行草稿");
            while (s3.NotePages.Count > 1) s3.DeleteNotePage(s3.ActiveNoteId);
            Check("最后一页不可删", !s3.DeleteNotePage(s3.ActiveNoteId));
            s3.AddNotePage();
            Check("新建一页", s3.NotePages.Count == 2);

            // ---------- 5. 合并导入（标题去重 + 分类按名映射） ----------
            var mergeFile = Path.Combine(dir1, "merge.json");
            File.WriteAllText(mergeFile,
                "{\"categories\":[{\"id\":\"mc1\",\"name\":\"角色\",\"icon\":\"📁\",\"color\":\"#F0824C\",\"parent_id\":\"\"}],"
                + "\"prompts\":[{\"title\":\"词条A\",\"content\":\"重复标题应跳过\"},"
                + "{\"title\":\"新词条X\",\"content\":\"新内容\",\"tags\":[\"x\"]}]}");
            var merged = s3.MergeFromFile(mergeFile);
            Check("合并导入：去重后 +1", merged == 1);
            Check("合并后分类未重复", s3.Categories.FindAll(c => c.name == "角色").Count == 1);

            // ---------- 6. 1 万条性能基准（2026-10-07 起无硬上限；隐性要求 = 1 万条内极速） ----------
            while (s3.Prompts.Count < 10000)
                s3.AddPrompt("批量" + s3.Prompts.Count, "c", null, "", false, "");
            Check("写入 1 万条", s3.Prompts.Count == 10000);
            s3.Save();

            // ---------- 7. 1 万条重载/筛选性能 + 无上限 ----------
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var s4 = new Store();
            s4.Load();
            sw.Stop();
            Check("10000 条重载一致且 <1s", s4.Prompts.Count == 10000 && sw.ElapsedMilliseconds < 1000);
            Console.WriteLine(string.Format("  INFO 10000 条加载耗时 {0}ms", sw.ElapsedMilliseconds));
            sw.Restart();
            var hits = s4.Filter("批量9", null, false);
            sw.Stop();
            Check("10000 条关键词筛选 <500ms", sw.ElapsedMilliseconds < 500 && hits.Count > 0);
            Console.WriteLine(string.Format("  INFO 10000 条筛选耗时 {0}ms（命中 {1} 条）", sw.ElapsedMilliseconds, hits.Count));
            Check("超过 1 万条仍可新增（无硬上限）", s4.AddPrompt("一万零一", "c", null, "", false, "") != null);

            // ---------- 8. 损坏保护 ----------
            var dir8 = Path.Combine(root, "t8");
            Directory.CreateDirectory(dir8);
            Store.DataDir = dir8;
            var sf = new Store();
            sf.Load();
            sf.AddPrompt("保命", "c", null, "", false, "");
            sf.Save();
            File.WriteAllText(sf.DataFile, "{broken json!!");
            var s9 = new Store();
            s9.Load();
            Check("损坏文件被改名留存", s9.CorruptBackup != "" && File.Exists(s9.CorruptBackup));
            Check("应用以默认数据可用", s9.Prompts.Count == 0);

            // ---------- 9. 导出 ----------
            var exp = Path.Combine(dir8, "export.json");
            Check("导出成功", s9.ExportJson(exp) && File.Exists(exp));

            // ---------- 10. 回收站 + 批量（2026-10-07） ----------
            var dir10 = Path.Combine(root, "t10");
            Directory.CreateDirectory(dir10);
            Store.DataDir = dir10;
            var st = new Store();
            st.Load();
            var a = st.AddPrompt("回收甲", "内容甲", new List<string>(), "", false, "");
            var b = st.AddPrompt("回收乙", "内容乙", new List<string>(), "", false, "");
            var keepC = st.AddPrompt("留着丙", "内容丙", new List<string>(), "", false, "");
            st.DeletePrompts(new List<string> { a.id, b.id });
            Check("删除 = 移入回收站（不是直接丢）",
                  st.Prompts.Count == 1 && st.Trash.Count == 2 && a.deleted_at != "");
            Check("回收站不混进正常列表", st.Filter("", null, false).Count == 1);
            st.Save();
            var st2 = new Store();
            st2.Load();
            Check("回收站能持久化（重载后还在）", st2.Trash.Count == 2 && st2.Prompts.Count == 1);
            int back = st2.RestoreFromTrash(new List<string> { a.id });
            Check("恢复一条：回主库且 deleted_at 清空",
                  back == 1 && st2.Trash.Count == 1 && st2.FindPrompt(a.id) != null
                  && st2.FindPrompt(a.id).deleted_at == "");
            var cat10 = st2.AddCategory("回收测试分类", "", "#2478A8", "");
            int mv = st2.MovePrompts(new List<string> { keepC.id }, cat10.id);
            Check("批量移到分类", mv == 1 && st2.FindPrompt(keepC.id).category_id == cat10.id);
            int fv = st2.SetFavPrompts(new List<string> { keepC.id, a.id }, true);
            Check("批量收藏", fv == 2 && st2.FindPrompt(keepC.id).favorite && st2.FindPrompt(a.id).favorite);
            st2.EmptyTrash();
            Check("清空回收站", st2.Trash.Count == 0);
            for (int i = 0; i < Store.TrashCap + 5; i++)
            {
                var t = st2.AddPrompt("上限" + i, "x", null, "", false, "");
                st2.DeletePrompts(new List<string> { t.id });
            }
            Check("回收站上限 " + Store.TrashCap + " 条（超出的丢最旧）", st2.Trash.Count == Store.TrashCap,
                  "实际 " + st2.Trash.Count);

            // ---------- 11. 手动存档 + 笔记页回收站 + 分类左右移（2026-10-08） ----------
            var dir11 = Path.Combine(root, "t11");
            Directory.CreateDirectory(dir11);
            Store.DataDir = dir11;
            var sn = new Store();
            sn.Load();
            sn.SetNoteContent(sn.ActiveNoteId, "第一页内容\n第二行");
            sn.AddNotePage();
            sn.SetNoteContent(sn.ActiveNoteId, "第二页内容");
            sn.SaveNotes();

            // 手动存档：每次一个新文件、内容含全部页、同一秒连点也不覆盖
            var ap1 = sn.ArchiveNotes("");
            var ap2 = sn.ArchiveNotes("");
            bool a1ok = ap1 != null && File.Exists(ap1);
            string atxt = a1ok ? File.ReadAllText(ap1) : "";
            Check("手动存档：新建独立 TXT、含全部页、不覆盖前一份",
                  a1ok && ap2 != null && ap1 != ap2 && File.Exists(ap2)
                  && atxt.Contains("第一页内容") && atxt.Contains("第二页内容") && atxt.Contains("页数：2"),
                  "path1=" + Path.GetFileName(ap1 == null ? "" : ap1)
                  + " path2=" + Path.GetFileName(ap2 == null ? "" : ap2));
            var ap3 = sn.ArchiveNotes("已删-");
            Check("删页前的自动保险存档（已删- 前缀）",
                  ap3 != null && File.Exists(ap3) && Path.GetFileName(ap3).StartsWith("已删-"),
                  Path.GetFileName(ap3 == null ? "" : ap3));
            Check("自动存档通道(notes.json)与手动存档互不影响", File.Exists(sn.NotesFile) && File.Exists(ap1));

            // 笔记页回收站
            string delId = sn.ActiveNoteId;
            int beforePages = sn.NotePages.Count;
            Check("删页 = 移入笔记页回收站",
                  sn.DeleteNotePage(delId) && sn.NotePages.Count == beforePages - 1
                  && sn.TrashNotes.Count == 1 && sn.TrashNotes[0].deleted_at != "");
            sn.SaveNotes();
            var sn2 = new Store();
            sn2.Load();
            Check("笔记页回收站能持久化（重载后还在）", sn2.TrashNotes.Count == 1);
            int nb = sn2.RestoreNotePage(new List<string> { sn2.TrashNotes[0].id });
            Check("恢复笔记页：回到页列表且 deleted_at 清空",
                  nb == 1 && sn2.NotePages.Count == beforePages && sn2.TrashNotes.Count == 0);
            sn2.EmptyTrashNotes();
            Check("清空笔记页回收站", sn2.TrashNotes.Count == 0);
            for (int i = 0; i < Store.NoteTrashCap + 3; i++)
            {
                sn2.AddNotePage();
                sn2.SetNoteContent(sn2.ActiveNoteId, "塞 " + i);
                sn2.DeleteNotePage(sn2.ActiveNoteId);
            }
            Check("笔记页回收站上限 " + Store.NoteTrashCap + " 页", sn2.TrashNotes.Count == Store.NoteTrashCap,
                  "实际 " + sn2.TrashNotes.Count);

            // 分类左右移（只在同级兄弟之间）
            var ca = sn2.AddCategory("甲类", "", "#2478A8", "");
            var cb = sn2.AddCategory("乙类", "", "#2478A8", "");
            var cc = sn2.AddCategory("丙类", "", "#2478A8", "");
            // 边界用**同级真正的首/末**来测（默认库里本来就有分类，不能假设"甲类"是第一个）
            var sibsTop = sn2.ChildrenOf("");
            var firstTop = sibsTop[0];
            var lastTop = sibsTop[sibsTop.Count - 1];
            Check("左边界：同级第一个不能再左移",
                  !sn2.CanMoveCategory(firstTop.id, -1) && !sn2.MoveCategory(firstTop.id, -1));
            Check("右边界：同级最后一个不能再右移", !sn2.CanMoveCategory(lastTop.id, 1));
            Check("右移：甲、乙换位",
                  sn2.MoveCategory(ca.id, 1) && CatIndex(sn2, cb.id) < CatIndex(sn2, ca.id));
            var kid = sn2.AddCategory("子类", "", "#2478A8", ca.id);
            Check("二级分类不能跨父级左右移（同级里只有它自己）",
                  !sn2.CanMoveCategory(kid.id, -1) && !sn2.CanMoveCategory(kid.id, 1));
            Check("移动父级不会把子分类挪到别的父级下",
                  sn2.MoveCategory(ca.id, -1) && sn2.FindCategory(kid.id).parent_id == ca.id);

            try { Directory.Delete(root, true); } catch (Exception) { }

            Console.WriteLine();
            Console.WriteLine(string.Format("结果：{0} 通过 / {1} 失败", _pass, _fail));
            if (_fail > 0)
            {
                Console.WriteLine("失败项：" + string.Join("、", _fails.ToArray()));
                return 1;
            }
            return 0;
        }
    }
}
