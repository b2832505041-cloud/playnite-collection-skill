using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace PlayniteCollectionTool
{
    public class CollectionPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string ApplyVersion = "0.1.0";
        private readonly string outDir;

        public override Guid Id { get { return Guid.Parse("7c4e1a92-5b3d-4f8e-9a21-6d0c3e5f8b47"); } }

        public CollectionPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = false };
            outDir = Path.Combine(api.Paths.ExtensionsDataPath, "playnite-collection-tool");
            try { Directory.CreateDirectory(outDir); } catch { }
            WriteLog("插件构造函数执行成功，outDir=" + outDir);
        }

        private static void WriteLog(string msg)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite", "ExtensionsData", "playnite-collection-tool");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "tool_log.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine, new UTF8Encoding(true));
            }
            catch { }
        }

        public override void OnApplicationStarted(Playnite.SDK.Events.OnApplicationStartedEventArgs args)
        {
            try
            {
                int n = 0;
                foreach (var g in PlayniteApi.Database.Games) n++;
                WriteLog("OnApplicationStarted 诊断：库中游戏数=" + n);
                TryAutoApply();
            }
            catch (Exception ex) { WriteLog("OnApplicationStarted 失败: " + ex.ToString()); }
            base.OnApplicationStarted(args);
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var menu = new List<MainMenuItem>();
            menu.Add(new MainMenuItem { MenuSection = "@Playnite 库整理", Description = "重新导入分类/标签（分类.tsv）", Action = a => DoImport() });
            menu.Add(new MainMenuItem { MenuSection = "@Playnite 库整理", Description = "中文化/补充元数据（游戏数据.tsv）", Action = a => DoApplyMeta() });
            menu.Add(new MainMenuItem { MenuSection = "@Playnite 库整理", Description = "撤销本插件对元数据的全部改动", Action = a => DoRevertMeta() });
            menu.Add(new MainMenuItem { MenuSection = "@Playnite 库整理", Description = "导出当前游戏库数据", Action = a => DoExport() });
            menu.Add(new MainMenuItem { MenuSection = "@Playnite 库整理", Description = "清除本插件分类/标签（回滚）", Action = a => DoClear() });
            return menu;
        }

                // ================= 自动应用 =================
        private void TryAutoApply()
        {
            try
            {
                string catPath = Path.Combine(outDir, "分类.tsv");
                string metaPath = Path.Combine(outDir, "游戏数据.tsv");
                string stampPath = Path.Combine(outDir, "applied_version.txt");
                string stamp = File.Exists(stampPath) ? File.ReadAllText(stampPath, Encoding.UTF8).Trim() : "";
                if (stamp == ApplyVersion) { WriteLog("自动应用跳过：已是 " + ApplyVersion); return; }

                if (File.Exists(metaPath))
                {
                    WriteLog("开始自动应用元数据...");
                    ApplyMeta(metaPath, false);
                    WriteLog("元数据自动应用完成");
                }
                if (File.Exists(catPath))
                {
                    WriteLog("开始自动应用分类...");
                    ApplyCategories(catPath, false);
                    WriteLog("分类自动应用完成");
                }
                File.WriteAllText(stampPath, ApplyVersion, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                WriteLog("自动应用失败: " + ex.ToString());
                try { PlayniteApi.Dialogs.ShowErrorMessage("自动应用失败: " + ex.Message + "\n详见 ExtensionsData\\playnite-collection-tool\\tool_log.txt", "Playnite 库整理"); } catch { }
            }
        }
        // ================= 通用工具 =================
        private static string J(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("?");
                else sb.Append(c);
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private static string ListStr(System.Collections.IEnumerable items)
        {
            if (items == null) return "";
            var parts = new List<string>();
            foreach (var it in items)
            {
                if (it == null) continue;
                var prop = it.GetType().GetProperty("Name");
                string n = prop == null ? it.ToString() : (prop.GetValue(it, null) as string);
                if (string.IsNullOrEmpty(n)) continue;
                parts.Add(n.Replace(";", "/").Replace("|", "/").Replace("\t", " "));
            }
            return string.Join(";", parts);
        }

        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }

        private static List<string> SplitList(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return list;
            foreach (var p in s.Split(new char[] { ';', '|', ',' }))
            {
                var t = p.Trim();
                if (t.Length > 0 && !list.Contains(t)) list.Add(t);
            }
            return list;
        }

        private static int Idx(string[] arr, string name)
        {
            for (int i = 0; i < arr.Length; i++)
                if (string.Equals(arr[i].Trim(), name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private string SourceLabel(Playnite.SDK.Models.Game g)
        {
            if (g.Source != null && !string.IsNullOrEmpty(g.Source.Name)) return g.Source.Name;
            return "";
        }

        private Playnite.SDK.Models.Category FindCategory(string name)
        {
            foreach (var c in PlayniteApi.Database.Categories)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        private Playnite.SDK.Models.Tag FindTag(string name)
        {
            foreach (var t in PlayniteApi.Database.Tags)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }


        // ================= 本地化对照表 =================
        private static readonly Dictionary<string, string> LocMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Steam", "Steam" },
            { "Epic", "Epic 游戏商城" },
            { "Xbox", "Xbox" },
            { "GOG", "GOG" },
            { "Amazon Games", "亚马逊游戏" },
            { "Battle.net", "暴雪战网" },
            { "Humble", "Humble Bundle" },
            { "itch.io", "itch.io" },
            { "Ubisoft Connect", "育碧 Connect" },
            { "Uplay", "育碧 Connect" },
            { "EA app", "EA app" },
            { "Origin", "Origin" },
            { "Rockstar", "Rockstar" },
            { "Bethesda.net", "Bethesda.net" },
            { "Microsoft Store", "微软商店" },
            { "PC (Windows)", "PC (Windows)" },
            { "Microsoft Xbox One", "Xbox One" },
            { "Microsoft Xbox Series", "Xbox Series X|S" },
            { "Sony PlayStation 4", "PlayStation 4" },
            { "Sony PlayStation 5", "PlayStation 5" },
            { "Nintendo Switch", "Nintendo Switch" },
            { "PC (Linux)", "PC (Linux)" },
            { "PC (Mac)", "PC (Mac)" },
            { "Not Played", "未游玩" },
            { "Played", "已游玩" },
            { "Playing", "正在玩" },
            { "Completed", "已通关" },
            { "Beaten", "已通关" },
            { "Abandoned", "已弃坑" },
            { "On Hold", "暂时搁置" },
            { "Plan to Play", "计划游玩" },
            { "Played for a bit", "玩过一会儿" }
        };

        private static string Tr(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string v;
            if (LocMap.TryGetValue(s.Trim(), out v)) return v;
            return s;
        }

        // 只对"来源/完成状态/平台"做替换，避免误伤厂商里的自造词
        private void LocalizeAllLookups(bool dryRun, List<string> report)
        {
            foreach (var src in PlayniteApi.Database.Sources)
            {
                string t = Tr(src.Name);
                if (t != src.Name)
                {
                    if (!dryRun) { src.Name = t; PlayniteApi.Database.Sources.Update(src); }
                    report.Add("来源: " + src.Name + " -> " + t);
                }
            }
            foreach (var cs in PlayniteApi.Database.CompletionStatuses)
            {
                string t = Tr(cs.Name);
                if (t != cs.Name)
                {
                    if (!dryRun) { cs.Name = t; PlayniteApi.Database.CompletionStatuses.Update(cs); }
                    report.Add("完成状态: " + cs.Name + " -> " + t);
                }
            }
            foreach (var pf in PlayniteApi.Database.Platforms)
            {
                string t = Tr(pf.Name);
                if (t != pf.Name)
                {
                    if (!dryRun) { pf.Name = t; PlayniteApi.Database.Platforms.Update(pf); }
                    report.Add("平台: " + pf.Name + " -> " + t);
                }
            }
        }

        // ================= 元数据写入（中文化 + 补充）=================
        private void DoApplyMeta()
        {
            try
            {
                string metaPath = Path.Combine(outDir, "游戏数据.tsv");
                if (!File.Exists(metaPath)) { PlayniteApi.Dialogs.ShowErrorMessage("找不到 " + metaPath, "Playnite 库整理"); return; }
                ApplyMeta(metaPath, true);
            }
            catch (Exception ex) { WriteLog("应用元数据失败: " + ex.ToString()); PlayniteApi.Dialogs.ShowErrorMessage("应用元数据失败: " + ex.Message, "Playnite 库整理"); }
        }

        private void ApplyMeta(string metaPath, bool showDialog)
        {
            var lines = File.ReadAllLines(metaPath, Encoding.UTF8);
            if (lines.Length < 2) { WriteLog("元数据文件为空"); return; }
            var header = lines[0].TrimStart('\uFEFF').Split('\t');
            int iId = Idx(header, "Id");
            int iName = Idx(header, "Name");
            int iSource = Idx(header, "Source");
            int iNewName = Max(Idx(header, "中文名"), Idx(header, "NewName"), Idx(header, "中文名字"));
            int iRelease = Max(Idx(header, "ReleaseDate"), Idx(header, "发行日期"));
            int iGenres = Max(Idx(header, "Genres"), Idx(header, "类型"));
            int iDev = Max(Idx(header, "Developers"), Idx(header, "开发商"));
            int iPub = Max(Idx(header, "Publishers"), Idx(header, "发行商"));
            int iDesc = Max(Idx(header, "Description"), Idx(header, "简介"), Idx(header, "描述"));
            int iSeries = Max(Idx(header, "Series"), Idx(header, "系列"));
            if (iName < 0) { WriteLog("元数据缺少 Name 列"); return; }

            var gameList = new List<Playnite.SDK.Models.Game>();
            foreach (var g in PlayniteApi.Database.Games) gameList.Add(g);

            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var revert = new StringBuilder();
            revert.AppendLine("Id\tName\tReleaseDate\tGenres\tDevelopers\tPublishers\tSeries\tDescription");
            int nRevert = 0;
            int updated = 0, nameChanged = 0, dateFilled = 0, descChanged = 0, genresChanged = 0, devChanged = 0, pubChanged = 0;
            var unmatched = new List<string>();

            using (PlayniteApi.Database.BufferedUpdate())
            {
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var f = lines[i].Split('\t');
                    string name = Cell(f, iName);
                    if (name.Length == 0) continue;
                    string src = Cell(f, iSource);
                    string idCell = Cell(f, iId);

                    var cands = new List<Playnite.SDK.Models.Game>();
                    if (idCell.Length > 0)
                    {
                        Guid gid;
                        if (Guid.TryParse(idCell, out gid))
                        {
                            var byId = PlayniteApi.Database.Games.Get(gid);
                            if (byId != null) cands.Add(byId);
                        }
                    }
                    if (cands.Count == 0)
                        cands = gameList.Where(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (cands.Count > 1 && src.Length > 0)
                    {
                        var wanted = SplitList(src);
                        var filtered = cands.Where(g => g.Source != null && wanted.Any(w => string.Equals(w, g.Source.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(w, Tr(g.Source.Name), StringComparison.OrdinalIgnoreCase))).ToList();
                        if (filtered.Count > 0) cands = filtered;
                    }
                    if (cands.Count == 0) { unmatched.Add(name); continue; }

                    string newName = Cell(f, iNewName);
                    string release = Cell(f, iRelease);
                    string genres = Cell(f, iGenres);
                    string dev = Cell(f, iDev);
                    string pub = Cell(f, iPub);
                    string desc = Cell(f, iDesc);
                    string series = Cell(f, iSeries);

                    foreach (var g in cands)
                    {
                        string before = string.Join("\t", new string[] {
                            g.Id.ToString(), OneLine(g.Name),
                            DateStr(g.ReleaseDate),
                            ListStr(g.Genres), ListStr(g.Developers), ListStr(g.Publishers), ListStr(g.Series),
                            OneLine(g.Description == null ? "" : g.Description) });
                        bool changed = false;

                        if (newName.Length > 0 && !string.Equals(newName, g.Name, StringComparison.Ordinal))
                        { nameMap[g.Name] = newName; g.Name = newName; changed = true; nameChanged++; }
                        if (release.Length > 0)
                        {
                            DateTime d;
                            if (DateTime.TryParse(release, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d) && DateStr(g.ReleaseDate) != d.ToString("yyyy-MM-dd")) { g.ReleaseDate = new Playnite.SDK.Models.ReleaseDate(d); changed = true; dateFilled++; }
                        }
                        if (desc.Length > 1 && !string.Equals(desc, g.Description, StringComparison.Ordinal)) { g.Description = desc; changed = true; descChanged++; }
                        if (series.Length > 0)
                        {
                            var ids = EnsureSeries(series.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                            if (ids.Count > 0 && !SameSet(ids, g.SeriesIds)) { g.SeriesIds = ids; changed = true; }
                        }
                        if (genres.Length > 0)
                        {
                            var ids = EnsureGenres(genres.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                            if (ids.Count > 0 && !SameSet(ids, g.GenreIds)) { g.GenreIds = ids; changed = true; genresChanged++; }
                        }
                        if (dev.Length > 0)
                        {
                            var ids = EnsureCompanies(dev.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                            if (ids.Count > 0 && !SameSet(ids, g.DeveloperIds)) { g.DeveloperIds = ids; changed = true; devChanged++; }
                        }
                        if (pub.Length > 0)
                        {
                            var ids = EnsureCompanies(pub.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                            if (ids.Count > 0 && !SameSet(ids, g.PublisherIds)) { g.PublisherIds = ids; changed = true; pubChanged++; }
                        }

                        if (changed)
                        {
                            PlayniteApi.Database.Games.Update(g);
                            updated++;
                            revert.AppendLine(before);
                            nRevert++;
                        }
                    }
                }

                // 本地化"来源/完成状态/平台"
                var report = new List<string>();
                LocalizeAllLookups(false, report);
            }

            // 改名后，分类标签表里记的还是旧名字，需要用名字映射重新应用一遍
            string catPath = Path.Combine(outDir, "分类.tsv");
            if (File.Exists(catPath) && nameMap.Count > 0)
            {
                int reapplied = ApplyCategories(catPath, false, nameMap);
                WriteLog("改名后重新应用分类，命中 " + reapplied + " 条");
            }

            File.WriteAllText(Path.Combine(outDir, "meta_backup.tsv"), revert.ToString(), new UTF8Encoding(true));

            string msg = "元数据应用完成。\n更新游戏: " + updated + "（改名 " + nameChanged + " / 发行日期 " + dateFilled + " / 简介 " + descChanged + " / 类型 " + genresChanged + " / 开发商 " + devChanged + " / 发行商 " + pubChanged + "）\n未匹配: " + unmatched.Count + "\n备份: meta_backup.tsv";
            WriteLog(msg.Replace("\n", " | "));
            if (showDialog) PlayniteApi.Dialogs.ShowMessage(msg + (unmatched.Count > 0 ? "\n\n未匹配示例:\n" + string.Join("\n", unmatched.Take(8).ToArray()) : ""), "Playnite 库整理");
        }

        private static string DateStr(Playnite.SDK.Models.ReleaseDate? rd)
        {
            if (!rd.HasValue) return "";
            try { return rd.Value.Date.ToString("yyyy-MM-dd"); } catch { return ""; }
        }

        private static int Max(int a, int b) { return a > b ? a : b; }
        private static int Max(int a, int b, int c) { return Max(Max(a, b), c); }
        private static int Max(int a, int b, int c, int d) { return Max(Max(a, b), Max(c, d)); }

        private static string Cell(string[] f, int i)
        {
            if (i < 0 || i >= f.Length) return "";
            return f[i].Trim();
        }

        private static bool SameSet(List<Guid> a, List<Guid> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            var sa = new HashSet<Guid>(a);
            return sa.SetEquals(b);
        }

        private List<Guid> EnsureGenres(string[] names)
        {
            var ids = new List<Guid>();
            foreach (var raw in names)
            {
                string n = Tr(raw.Trim());
                if (n.Length == 0) continue;
                Playnite.SDK.Models.Genre found = null;
                foreach (var g in PlayniteApi.Database.Genres)
                    if (string.Equals(g.Name, n, StringComparison.OrdinalIgnoreCase)) { found = g; break; }
                if (found == null) { found = new Playnite.SDK.Models.Genre(n); PlayniteApi.Database.Genres.Add(found); }
                if (!ids.Contains(found.Id)) ids.Add(found.Id);
            }
            return ids;
        }

        private List<Guid> EnsureCompanies(string[] names)
        {
            var ids = new List<Guid>();
            foreach (var raw in names)
            {
                string n = raw.Trim();
                if (n.Length == 0) continue;
                Playnite.SDK.Models.Company found = null;
                foreach (var c in PlayniteApi.Database.Companies)
                    if (string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)) { found = c; break; }
                if (found == null) { found = new Playnite.SDK.Models.Company(n); PlayniteApi.Database.Companies.Add(found); }
                if (!ids.Contains(found.Id)) ids.Add(found.Id);
            }
            return ids;
        }

        private List<Guid> EnsureSeries(string[] names)
        {
            var ids = new List<Guid>();
            foreach (var raw in names)
            {
                string n = raw.Trim();
                if (n.Length == 0) continue;
                Playnite.SDK.Models.Series found = null;
                foreach (var s in PlayniteApi.Database.Series)
                    if (string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase)) { found = s; break; }
                if (found == null) { found = new Playnite.SDK.Models.Series(n); PlayniteApi.Database.Series.Add(found); }
                if (!ids.Contains(found.Id)) ids.Add(found.Id);
            }
            return ids;
        }

        // ================= 撤销元数据改动 =================
        private void DoRevertMeta()
        {
            try
            {
                string bk = Path.Combine(outDir, "meta_backup.tsv");
                if (!File.Exists(bk)) { PlayniteApi.Dialogs.ShowMessage("没有 meta_backup.tsv，无法撤销。", "Playnite 库整理"); return; }
                string text = File.ReadAllText(bk, Encoding.UTF8);
                int cnt = 0;
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var rec in ParseBackup(text))
                    {
                        Guid id;
                        if (!Guid.TryParse(rec.id, out id)) continue;
                        var g = PlayniteApi.Database.Games.Get(id);
                        if (g == null) continue;
                        if (!string.IsNullOrEmpty(rec.name)) g.Name = rec.name;
                        DateTime d;
                        if (DateTime.TryParse(rec.releaseDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d)) g.ReleaseDate = new Playnite.SDK.Models.ReleaseDate(d); else g.ReleaseDate = null;
                        if (!string.IsNullOrEmpty(rec.genres)) g.GenreIds = EnsureGenres(rec.genres.Split(';'));
                        if (!string.IsNullOrEmpty(rec.developers)) g.DeveloperIds = EnsureCompanies(rec.developers.Split(';'));
                        if (!string.IsNullOrEmpty(rec.publishers)) g.PublisherIds = EnsureCompanies(rec.publishers.Split(';'));
                        if (!string.IsNullOrEmpty(rec.series)) g.SeriesIds = EnsureSeries(rec.series.Split(';'));
                        if (!string.IsNullOrEmpty(rec.description)) g.Description = rec.description;
                        PlayniteApi.Database.Games.Update(g);
                        cnt++;
                    }
                }
                PlayniteApi.Dialogs.ShowMessage("已撤销 " + cnt + " 条游戏的元数据改动。", "Playnite 库整理");
                WriteLog("撤销元数据 " + cnt + " 条");
            }
            catch (Exception ex) { WriteLog("撤销失败: " + ex.ToString()); PlayniteApi.Dialogs.ShowErrorMessage("撤销失败: " + ex.Message, "Playnite 库整理"); }
        }

        private class BkRec { public string id; public string name; public string releaseDate; public string genres; public string developers; public string publishers; public string series; public string description; }

        private static List<BkRec> ParseBackup(string text)
        {
            var list = new List<BkRec>();
            var lines = text.Split(new char[] { '\n' });
            for (int i = 1; i < lines.Length; i++)
            {
                var f = lines[i].TrimEnd('\r').Split('\t');
                if (f.Length < 2) continue;
                var rec = new BkRec();
                rec.id = f[0];
                rec.name = f.Length > 1 ? f[1] : "";
                rec.releaseDate = f.Length > 2 ? f[2] : "";
                rec.genres = f.Length > 3 ? f[3] : "";
                rec.developers = f.Length > 4 ? f[4] : "";
                rec.publishers = f.Length > 5 ? f[5] : "";
                rec.series = f.Length > 6 ? f[6] : "";
                rec.description = f.Length > 7 ? f[7] : "";
                list.Add(rec);
            }
            return list;
        }


        // ================= 导出 =================

        private void DoExport()
        {
            try
            {
                var arr = new List<string>();
                var sbTsv = new StringBuilder();
                sbTsv.AppendLine("Id\tName\tSource\tIsInstalled\tFavorite\tPlaytimeHours\tCompletionStatus\tGenres\tDevelopers\tPublishers\tSeries\tFeatures\tTags\tCategories\tPlatforms\tReleaseYear\tAdded\tManual");
                int count = 0;

                foreach (var g in PlayniteApi.Database.Games)
                {
                    count++;
                    string src = SourceLabel(g);
                    string year = g.ReleaseYear.HasValue ? g.ReleaseYear.Value.ToString() : "";
                    string added = g.Added.HasValue ? g.Added.Value.ToString("yyyy-MM-dd") : "";
                    double hours = Math.Round(((double)g.Playtime) / 3600.0, 1);
                    string st = g.CompletionStatus == null ? "" : g.CompletionStatus.Name;

                    arr.Add("{"
                        + "\"Id\":" + J(g.Id.ToString())
                        + ",\"Name\":" + J(g.Name)
                        + ",\"Source\":" + J(src)
                        + ",\"IsInstalled\":" + (g.IsInstalled ? "true" : "false")
                        + ",\"Favorite\":" + (g.Favorite ? "true" : "false")
                        + ",\"PlaytimeHours\":" + hours.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ",\"CompletionStatus\":" + J(st)
                        + ",\"Genres\":" + J(ListStr(g.Genres))
                        + ",\"Developers\":" + J(ListStr(g.Developers))
                        + ",\"Publishers\":" + J(ListStr(g.Publishers))
                        + ",\"Series\":" + J(ListStr(g.Series))
                        + ",\"Features\":" + J(ListStr(g.Features))
                        + ",\"Tags\":" + J(ListStr(g.Tags))
                        + ",\"Categories\":" + J(ListStr(g.Categories))
                        + ",\"Platforms\":" + J(ListStr(g.Platforms))
                        + ",\"ReleaseYear\":" + J(year)
                        + ",\"Added\":" + J(added)
                        + ",\"Manual\":" + J(g.Manual)
                        + "}");

                    sbTsv.AppendLine(string.Join("\t", new string[] {
                        g.Id.ToString(), OneLine(g.Name), src, g.IsInstalled ? "1" : "0",
                        g.Favorite ? "1" : "0", hours.ToString(System.Globalization.CultureInfo.InvariantCulture), st,
                        ListStr(g.Genres), ListStr(g.Developers), ListStr(g.Publishers), ListStr(g.Series),
                        ListStr(g.Features), ListStr(g.Tags), ListStr(g.Categories), ListStr(g.Platforms),
                        year, added, string.IsNullOrEmpty(g.Manual) ? "0" : "1"
                    }));
                }

                string json = "{\"ExportedAt\":" + J(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    + ",\"PlayniteVersion\":" + J(PlayniteApi.ApplicationInfo.ApplicationVersion.ToString())
                    + ",\"GameCount\":" + count
                    + ",\"Games\":[" + string.Join(",", arr.ToArray()) + "]}";

                string jsonPath = Path.Combine(outDir, "games.json");
                string tsvPath = Path.Combine(outDir, "games.tsv");
                File.WriteAllText(jsonPath, json, new UTF8Encoding(true));
                File.WriteAllText(tsvPath, sbTsv.ToString(), new UTF8Encoding(true));
                WriteLog("导出完成 " + count + " 款 -> " + jsonPath);
                PlayniteApi.Dialogs.ShowMessage("已导出 " + count + " 款游戏。\n\n" + jsonPath + "\n" + tsvPath, "Playnite 库整理");
            }
            catch (Exception ex)
            {
                WriteLog("导出失败: " + ex.ToString());
                PlayniteApi.Dialogs.ShowErrorMessage("导出失败: " + ex.Message, "Playnite 库整理");
            }
        }

        // ================= 导入 =================
        private class Row
        {
            public string Name;
            public string Source;
            public List<string> Cats = new List<string>();
            public List<string> Tags = new List<string>();
        }

        private void DoImport()
        {
            try
            {
                string mapPath = Path.Combine(outDir, "分类.tsv");
                ApplyCategories(mapPath, true);
            }
            catch (Exception ex)
            {
                WriteLog("导入失败: " + ex.ToString());
                PlayniteApi.Dialogs.ShowErrorMessage("导入失败: " + ex.Message, "Playnite 库整理");
            }
        }

        private int ApplyCategories(string mapPath, bool showDialog) { return ApplyCategories(mapPath, showDialog, null); }

        private int ApplyCategories(string mapPath, bool showDialog, Dictionary<string, string> renameMap)
        {
            if (!File.Exists(mapPath))
            {
                WriteLog("找不到映射文件: " + mapPath);
                if (showDialog) PlayniteApi.Dialogs.ShowErrorMessage("找不到映射文件:\n" + mapPath, "Playnite 库整理");
                return 0;
            }
            var lines = File.ReadAllLines(mapPath, Encoding.UTF8);
            if (lines.Length < 2) { WriteLog("映射文件为空"); return 0; }

            var header = lines[0].Split('\t');
            int iName = Idx(header, "Name");
            int iSrc = Idx(header, "Source");
            int iCat = Idx(header, "Categories");
            int iTag = Idx(header, "Tags");
            if (iName < 0) { WriteLog("映射文件缺少 Name 列"); return 0; }

            var rows = new List<Row>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var f = lines[i].Split('\t');
                var r = new Row();
                r.Name = f.Length > iName ? f[iName].Trim() : "";
                if (r.Name.Length == 0) continue;
                r.Source = (iSrc >= 0 && f.Length > iSrc) ? f[iSrc].Trim() : "";
                if (iCat >= 0 && f.Length > iCat) r.Cats = SplitList(f[iCat]);
                if (iTag >= 0 && f.Length > iTag) r.Tags = SplitList(f[iTag]);
                rows.Add(r);
            }

            var gameList = new List<Playnite.SDK.Models.Game>();
            foreach (var g in PlayniteApi.Database.Games) gameList.Add(g);

            var touC = new HashSet<Guid>();
            var touT = new HashSet<Guid>();
            var updatedIds = new List<Guid>();
            var unmatched = new List<string>();
            var ambiguous = new List<string>();
            int updated = 0;

            using (PlayniteApi.Database.BufferedUpdate())
            {
                foreach (var r in rows)
                {
                    string matchName = r.Name;
                    string mappedName;
                    if (renameMap != null && renameMap.TryGetValue(r.Name, out mappedName)) matchName = mappedName;
                    var cands = gameList.Where(g => string.Equals(g.Name, matchName, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (cands.Count == 0) { unmatched.Add(r.Name); continue; }

                    if (cands.Count > 1 && !string.IsNullOrEmpty(r.Source))
                    {
                        var wanted = SplitList(r.Source);
                        var filtered = cands.Where(g => wanted.Any(w => string.Equals(w, SourceLabel(g), StringComparison.OrdinalIgnoreCase) || string.Equals(w, Tr(SourceLabel(g)), StringComparison.OrdinalIgnoreCase))).ToList();
                        if (filtered.Count > 0) cands = filtered;
                    }
                    if (cands.Count > 1) ambiguous.Add(r.Name + " x" + cands.Count);

                    foreach (var g in cands)
                    {
                        var catIds = new List<Guid>();
                        if (g.CategoryIds != null) catIds.AddRange(g.CategoryIds);
                        foreach (var cn in r.Cats)
                        {
                            var cat = FindCategory(cn);
                            if (cat == null) { cat = new Playnite.SDK.Models.Category(cn); PlayniteApi.Database.Categories.Add(cat); }
                            if (!catIds.Contains(cat.Id)) catIds.Add(cat.Id);
                            touC.Add(cat.Id);
                        }
                        var tagIds = new List<Guid>();
                        if (g.TagIds != null) tagIds.AddRange(g.TagIds);
                        foreach (var tn in r.Tags)
                        {
                            var tg = FindTag(tn);
                            if (tg == null) { tg = new Playnite.SDK.Models.Tag(tn); PlayniteApi.Database.Tags.Add(tg); }
                            if (!tagIds.Contains(tg.Id)) tagIds.Add(tg.Id);
                            touT.Add(tg.Id);
                        }
                        g.CategoryIds = catIds;
                        g.TagIds = tagIds;
                        PlayniteApi.Database.Games.Update(g);
                        updatedIds.Add(g.Id);
                        updated++;
                    }
                }
            }

            var catNames = new List<string>();
            foreach (var id in touC) { var c = PlayniteApi.Database.Categories.Get(id); if (c != null) catNames.Add(c.Name); }
            var tagNames = new List<string>();
            foreach (var id in touT) { var t = PlayniteApi.Database.Tags.Get(id); if (t != null) tagNames.Add(t.Name); }
            catNames.Sort();
            tagNames.Sort();

            var res = new StringBuilder();
            res.Append("{\n  \"AppliedAt\": ").Append(J(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))).Append(",\n");
            res.Append("  \"RowsInFile\": ").Append(rows.Count).Append(",\n");
            res.Append("  \"GamesUpdated\": ").Append(updated).Append(",\n");
            res.Append("  \"UnmatchedCount\": ").Append(unmatched.Count).Append(",\n");
            res.Append("  \"Unmatched\": [").Append(string.Join(",", unmatched.Select(x => J(x)).ToArray())).Append("],\n");
            res.Append("  \"Ambiguous\": [").Append(string.Join(",", ambiguous.Select(x => J(x)).ToArray())).Append("],\n");
            res.Append("  \"Categories\": [").Append(string.Join(",", catNames.Select(x => J(x)).ToArray())).Append("],\n");
            res.Append("  \"Tags\": [").Append(string.Join(",", tagNames.Select(x => J(x)).ToArray())).Append("],\n");
            res.Append("  \"CategoryIds\": [").Append(string.Join(",", touC.Select(x => J(x.ToString())).ToArray())).Append("],\n");
            res.Append("  \"TagIds\": [").Append(string.Join(",", touT.Select(x => J(x.ToString())).ToArray())).Append("],\n");
            res.Append("  \"GameIds\": [").Append(string.Join(",", updatedIds.Select(x => J(x.ToString())).ToArray())).Append("]\n}");
            File.WriteAllText(Path.Combine(outDir, "import_result.json"), res.ToString(), new UTF8Encoding(true));

            WriteLog("导入完成 rows=" + rows.Count + " updated=" + updated + " unmatched=" + unmatched.Count + " ambiguous=" + ambiguous.Count + " cats=" + catNames.Count + " tags=" + tagNames.Count);
            if (showDialog)
                PlayniteApi.Dialogs.ShowMessage("导入完成。\n更新游戏: " + updated + "\n未匹配: " + unmatched.Count + "\n同名多条目: " + ambiguous.Count + "\n分类数: " + catNames.Count + "\n标签数: " + tagNames.Count + "\n\n" + Path.Combine(outDir, "import_result.json"), "Playnite 库整理");
            return updated;
        }

        // ================= 清除 =================
        private void DoClear()
        {
            try
            {
                string mp = Path.Combine(outDir, "import_result.json");
                if (!File.Exists(mp)) { PlayniteApi.Dialogs.ShowMessage("没有 import_result.json，无法确定要清除的内容。", "Playnite 库整理"); return; }
                string text = File.ReadAllText(mp, Encoding.UTF8);
                var catIds = ExtractIds(text, "CategoryIds");
                var tagIds = ExtractIds(text, "TagIds");
                if (catIds.Count == 0 && tagIds.Count == 0) { PlayniteApi.Dialogs.ShowMessage("上次导入没有分类/标签记录。", "Playnite 库整理"); return; }

                var confirm = PlayniteApi.Dialogs.ShowMessage("将移除本插件写入的 " + catIds.Count + " 个分类与 " + tagIds.Count + " 个标签（含各游戏上的关联），确定继续？", "Playnite 库整理", System.Windows.MessageBoxButton.YesNo);
                if (confirm != System.Windows.MessageBoxResult.Yes) return;

                var cats = new HashSet<Guid>(catIds);
                var tags = new HashSet<Guid>(tagIds);
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var g in PlayniteApi.Database.Games)
                    {
                        bool changed = false;
                        if (g.CategoryIds != null && g.CategoryIds.Any(x => cats.Contains(x)))
                        { g.CategoryIds = g.CategoryIds.Where(x => !cats.Contains(x)).ToList(); changed = true; }
                        if (g.TagIds != null && g.TagIds.Any(x => tags.Contains(x)))
                        { g.TagIds = g.TagIds.Where(x => !tags.Contains(x)).ToList(); changed = true; }
                        if (changed) PlayniteApi.Database.Games.Update(g);
                    }
                    foreach (var id in cats) { var c = PlayniteApi.Database.Categories.Get(id); if (c != null) PlayniteApi.Database.Categories.Remove(c); }
                    foreach (var id in tags) { var t = PlayniteApi.Database.Tags.Get(id); if (t != null) PlayniteApi.Database.Tags.Remove(t); }
                }
                File.Delete(Path.Combine(outDir, "applied_version.txt"));
                WriteLog("已清除分类与标签");
                PlayniteApi.Dialogs.ShowMessage("已清除。", "Playnite 库整理");
            }
            catch (Exception ex)
            {
                WriteLog("清除失败: " + ex.ToString());
                PlayniteApi.Dialogs.ShowErrorMessage("清除失败: " + ex.Message, "Playnite 库整理");
            }
        }

        private static List<Guid> ExtractIds(string text, string key)
        {
            var list = new List<Guid>();
            int k = text.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return list;
            int lb = text.IndexOf('[', k);
            int rb = text.IndexOf(']', lb);
            if (lb < 0 || rb < 0) return list;
            var body = text.Substring(lb + 1, rb - lb - 1);
            foreach (var part in body.Split(','))
            {
                var s = part.Trim().Trim('"');
                Guid g;
                if (Guid.TryParse(s, out g)) list.Add(g);
            }
            return list;
        }




    }
}
