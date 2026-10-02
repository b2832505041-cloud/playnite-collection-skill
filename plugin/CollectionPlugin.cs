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
            LocMap = new Dictionary<string, string>(DefaultLocMap, StringComparer.OrdinalIgnoreCase);
            LoadLocMapOverride();
            WriteLog("Plugin constructed, outDir=" + outDir);
        }

        private void WriteLog(string msg)
        {
            try
            {
                Directory.CreateDirectory(outDir);
                File.AppendAllText(Path.Combine(outDir, "tool_log.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine, new UTF8Encoding(true));
            }
            catch { }
        }

        public override void OnApplicationStarted(Playnite.SDK.Events.OnApplicationStartedEventArgs args)
        {
            try
            {
                int n = 0;
                foreach (var g in PlayniteApi.Database.Games) n++;
                WriteLog("OnApplicationStarted diagnostics: game count=" + n);
                TryAutoApply();
            }
            catch (Exception ex) { WriteLog("OnApplicationStarted failed: " + ex.ToString()); }
            base.OnApplicationStarted(args);
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var menu = new List<MainMenuItem>();
            menu.Add(new MainMenuItem { MenuSection = "@Playnite Collection Tool", Description = "Apply categories/tags (分类.tsv)", Action = a => DoImport() });
            menu.Add(new MainMenuItem { MenuSection = "@Playnite Collection Tool", Description = "Apply localized metadata (游戏数据.tsv)", Action = a => DoApplyMeta() });
            menu.Add(new MainMenuItem { MenuSection = "@Playnite Collection Tool", Description = "Revert all metadata changes", Action = a => DoRevertMeta() });
            menu.Add(new MainMenuItem { MenuSection = "@Playnite Collection Tool", Description = "Export library data", Action = a => DoExport() });
            menu.Add(new MainMenuItem { MenuSection = "@Playnite Collection Tool", Description = "Remove categories/tags (rollback)", Action = a => DoClear() });
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
                if (stamp == ApplyVersion) { WriteLog("Auto-apply skipped: " + ApplyVersion); return; }

                if (File.Exists(metaPath))
                {
                    WriteLog("Auto-apply metadata...");
                    ApplyMeta(metaPath, false);
                    WriteLog("Metadata auto-apply done");
                }
                if (File.Exists(catPath))
                {
                    WriteLog("Auto-apply categories...");
                    ApplyCategories(catPath, false);
                    WriteLog("Categories auto-apply done");
                }
                File.WriteAllText(stampPath, ApplyVersion, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                WriteLog("Auto-apply failed: " + ex.ToString());
                try { PlayniteApi.Dialogs.ShowErrorMessage("Auto-apply failed: " + ex.Message + "\nSee ExtensionsData\\playnite-collection-tool\\tool_log.txt", "Playnite Collection Tool"); } catch { }
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
        // 默认映射（英文 -> 简体中文）；可用 loc_map.json 覆盖成任意目标语言。
        // loc_map.json 放在 ExtensionsData\playnite-collection-tool\loc_map.json，
        // 格式：{ "English Name": "目标语言名称", ... }，只需覆盖你想改的键。
        private static readonly Dictionary<string, string> DefaultLocMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

        private readonly Dictionary<string, string> LocMap;

        private string Tr(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string v;
            if (LocMap.TryGetValue(s.Trim(), out v)) return v;
            return s;
        }

        private void LoadLocMapOverride()
        {
            try
            {
                LocMap.Clear();
                foreach (var kv in DefaultLocMap) LocMap[kv.Key] = kv.Value;
                string locFile = Path.Combine(outDir, "loc_map.json");
                if (!File.Exists(locFile)) return;
                var obj = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(locFile, Encoding.UTF8));
                foreach (var prop in obj.Properties())
                {
                    if (prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                        LocMap[prop.Name] = prop.Value.ToString();
                }
                WriteLog("Loaded locale override: " + locFile);
            }
            catch (Exception ex) { WriteLog("Failed to load loc_map.json (using defaults): " + ex.Message); }
        }

        // 只对"来源/完成状态/平台"做替换，避免误伤厂商里的自造词。
        // 改动记录到 lookup_backup.tsv（Type<TAB>Id<TAB>OldName），可随元数据一起回滚。
        private void LocalizeAllLookups()
        {
            var newRows = new List<string>();
            string backupPath = Path.Combine(outDir, "lookup_backup.tsv");
            var existing = new Dictionary<Guid, string>();
            if (File.Exists(backupPath))
            {
                foreach (var line in File.ReadAllLines(backupPath, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    if (p.Length >= 3)
                    {
                        Guid gid;
                        if (Guid.TryParse(p[1], out gid) && !existing.ContainsKey(gid)) existing[gid] = p[2];
                    }
                }
            }
            foreach (var src in PlayniteApi.Database.Sources)
            {
                string t = Tr(src.Name);
                if (t != src.Name)
                {
                    if (!existing.ContainsKey(src.Id)) { existing[src.Id] = src.Name; newRows.Add("Source\t" + src.Id + "\t" + src.Name.Replace("\t", " ")); }
                    src.Name = t; PlayniteApi.Database.Sources.Update(src);
                }
            }
            foreach (var cs in PlayniteApi.Database.CompletionStatuses)
            {
                string t = Tr(cs.Name);
                if (t != cs.Name)
                {
                    if (!existing.ContainsKey(cs.Id)) { existing[cs.Id] = cs.Name; newRows.Add("CompletionStatus\t" + cs.Id + "\t" + cs.Name.Replace("\t", " ")); }
                    cs.Name = t; PlayniteApi.Database.CompletionStatuses.Update(cs);
                }
            }
            foreach (var pf in PlayniteApi.Database.Platforms)
            {
                string t = Tr(pf.Name);
                if (t != pf.Name)
                {
                    if (!existing.ContainsKey(pf.Id)) { existing[pf.Id] = pf.Name; newRows.Add("Platform\t" + pf.Id + "\t" + pf.Name.Replace("\t", " ")); }
                    pf.Name = t; PlayniteApi.Database.Platforms.Update(pf);
                }
            }
            if (newRows.Count > 0)
            {
                File.AppendAllText(backupPath, string.Join("\n", newRows) + "\n", new UTF8Encoding(true));
                WriteLog("Localized lookups, recorded " + newRows.Count + " entries for rollback");
            }
        }

        // ================= 元数据写入（本地化 + 补充）=================
        private void DoApplyMeta()
        {
            try
            {
                string metaPath = Path.Combine(outDir, "游戏数据.tsv");
                if (!File.Exists(metaPath)) { PlayniteApi.Dialogs.ShowErrorMessage("Not found: " + metaPath, "Playnite Collection Tool"); return; }
                int renamed = ApplyMeta(metaPath, true);
                // 改名后，分类.tsv 里记的最终名需要重新匹配一次
                if (renamed > 0)
                {
                    string catPath = Path.Combine(outDir, "分类.tsv");
                    if (File.Exists(catPath))
                    {
                        int reapplied = ApplyCategories(catPath, false);
                        WriteLog("Re-applied categories after rename, matched " + reapplied + " rows");
                    }
                }
            }
            catch (Exception ex) { WriteLog("Apply metadata failed: " + ex.ToString()); PlayniteApi.Dialogs.ShowErrorMessage("Apply metadata failed: " + ex.Message, "Playnite Collection Tool"); }
        }

        private int ApplyMeta(string metaPath, bool showDialog)
        {
            var lines = File.ReadAllLines(metaPath, Encoding.UTF8);
            if (lines.Length < 2) { WriteLog("Metadata file empty"); return 0; }
            var header = lines[0].TrimStart('\uFEFF').Split('\t');
            int iId = Idx(header, "Id");
            int iName = Idx(header, "Name");
            int iSource = Idx(header, "Source");
            int iNewName = Max(Idx(header, "LocalName"), Idx(header, "中文名"), Idx(header, "NewName"), Idx(header, "中文名字"));
            int iRelease = Max(Idx(header, "ReleaseDate"), Idx(header, "发行日期"));
            int iGenres = Max(Idx(header, "Genres"), Idx(header, "类型"));
            int iDev = Max(Idx(header, "Developers"), Idx(header, "开发商"));
            int iPub = Max(Idx(header, "Publishers"), Idx(header, "发行商"));
            int iDesc = Max(Idx(header, "Description"), Idx(header, "简介"), Idx(header, "描述"));
            int iSeries = Max(Idx(header, "Series"), Idx(header, "系列"));
            if (iName < 0) { WriteLog("Metadata missing Name column"); return 0; }

            var gameList = new List<Playnite.SDK.Models.Game>();
            foreach (var g in PlayniteApi.Database.Games) gameList.Add(g);

            // 读取既有备份，保留「首次改动前」的原始值，避免多次应用后覆盖丢失
            string backupPath = Path.Combine(outDir, "meta_backup.tsv");
            var backedUp = new HashSet<Guid>();
            string existingBackup = "";
            if (File.Exists(backupPath))
            {
                existingBackup = File.ReadAllText(backupPath, Encoding.UTF8);
                foreach (var rec in ParseBackup(existingBackup))
                {
                    Guid bid;
                    if (Guid.TryParse(rec.id, out bid)) backedUp.Add(bid);
                }
            }
            var newRows = new StringBuilder();
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
                        { g.Name = newName; changed = true; nameChanged++; }
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
                            if (!backedUp.Contains(g.Id)) { newRows.AppendLine(before); backedUp.Add(g.Id); }
                        }
                    }
                }

                // 本地化"来源/完成状态/平台"（改动记录到 lookup_backup.tsv，可回滚）
                LocalizeAllLookups();
            }

            // 合并备份：旧备份行（保留首次原始值）+ 本次新增行
            var backupSb = new StringBuilder();
            backupSb.AppendLine("Id\tName\tReleaseDate\tGenres\tDevelopers\tPublishers\tSeries\tDescription");
            if (existingBackup.Length > 0)
            {
                foreach (var line in existingBackup.Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.Length > 0 && !line.StartsWith("Id\tName", StringComparison.Ordinal)) backupSb.AppendLine(line);
                }
            }
            backupSb.Append(newRows.ToString());
            File.WriteAllText(backupPath, backupSb.ToString(), new UTF8Encoding(true));

            string msg = "Metadata applied.\nUpdated games: " + updated + " (renamed " + nameChanged + " / release date " + dateFilled + " / description " + descChanged + " / genres " + genresChanged + " / developers " + devChanged + " / publishers " + pubChanged + ")\nUnmatched: " + unmatched.Count + "\nBackup: meta_backup.tsv";
            WriteLog(msg.Replace("\n", " | "));
            if (showDialog) PlayniteApi.Dialogs.ShowMessage(msg + (unmatched.Count > 0 ? "\n\nUnmatched samples:\n" + string.Join("\n", unmatched.Take(8).ToArray()) : ""), "Playnite Collection Tool");
            return nameChanged;
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
                string lk = Path.Combine(outDir, "lookup_backup.tsv");
                if (!File.Exists(bk) && !File.Exists(lk)) { PlayniteApi.Dialogs.ShowMessage("No backup to revert.", "Playnite Collection Tool"); return; }

                int cnt = 0;
                if (File.Exists(bk))
                {
                    string text = File.ReadAllText(bk, Encoding.UTF8);
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
                            g.Description = string.IsNullOrEmpty(rec.description) ? null : rec.description;
                            PlayniteApi.Database.Games.Update(g);
                            cnt++;
                        }
                    }
                }

                int nLookup = RevertLookups();

                string msg = "Reverted " + cnt + " games' metadata" + (nLookup > 0 ? " and " + nLookup + " lookup names" : "") + ".";
                PlayniteApi.Dialogs.ShowMessage(msg, "Playnite Collection Tool");
                WriteLog("Reverted metadata " + cnt + " rows, lookups " + nLookup);
            }
            catch (Exception ex) { WriteLog("Revert failed: " + ex.ToString()); PlayniteApi.Dialogs.ShowErrorMessage("Revert failed: " + ex.Message, "Playnite Collection Tool"); }
        }

        // 还原被本地化的来源/完成状态/平台名称（依据 lookup_backup.tsv）
        private int RevertLookups()
        {
            int n = 0;
            string backupPath = Path.Combine(outDir, "lookup_backup.tsv");
            if (!File.Exists(backupPath)) return 0;
            foreach (var line in File.ReadAllLines(backupPath, Encoding.UTF8))
            {
                var p = line.Split('\t');
                if (p.Length < 3) continue;
                Guid gid;
                if (!Guid.TryParse(p[1], out gid)) continue;
                string oldName = p[2];
                switch (p[0])
                {
                    case "Source":
                        {
                            var s = PlayniteApi.Database.Sources.Get(gid);
                            if (s != null && s.Name != oldName) { s.Name = oldName; PlayniteApi.Database.Sources.Update(s); n++; }
                        }
                        break;
                    case "CompletionStatus":
                        {
                            var c = PlayniteApi.Database.CompletionStatuses.Get(gid);
                            if (c != null && c.Name != oldName) { c.Name = oldName; PlayniteApi.Database.CompletionStatuses.Update(c); n++; }
                        }
                        break;
                    case "Platform":
                        {
                            var pf = PlayniteApi.Database.Platforms.Get(gid);
                            if (pf != null && pf.Name != oldName) { pf.Name = oldName; PlayniteApi.Database.Platforms.Update(pf); n++; }
                        }
                        break;
                }
            }
            return n;
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
                WriteLog("Export done " + count + " games -> " + jsonPath);
                PlayniteApi.Dialogs.ShowMessage("Exported " + count + " games.\n\n" + jsonPath + "\n" + tsvPath, "Playnite Collection Tool");
            }
            catch (Exception ex)
            {
                WriteLog("Export failed: " + ex.ToString());
                PlayniteApi.Dialogs.ShowErrorMessage("Export failed: " + ex.Message, "Playnite Collection Tool");
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
                WriteLog("Import failed: " + ex.ToString());
                PlayniteApi.Dialogs.ShowErrorMessage("Import failed: " + ex.Message, "Playnite Collection Tool");
            }
        }

        private int ApplyCategories(string mapPath, bool showDialog)
        {
            if (!File.Exists(mapPath))
            {
                WriteLog("Mapping file not found: " + mapPath);
                if (showDialog) PlayniteApi.Dialogs.ShowErrorMessage("Mapping file not found:\n" + mapPath, "Playnite Collection Tool");
                return 0;
            }
            var lines = File.ReadAllLines(mapPath, Encoding.UTF8);
            if (lines.Length < 2) { WriteLog("Mapping file empty"); return 0; }

            var header = lines[0].Split('\t');
            int iName = Idx(header, "Name");
            int iSrc = Idx(header, "Source");
            int iCat = Idx(header, "Categories");
            int iTag = Idx(header, "Tags");
            if (iName < 0) { WriteLog("Mapping file missing Name column"); return 0; }

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
                    var cands = gameList.Where(g => string.Equals(g.Name, r.Name, StringComparison.OrdinalIgnoreCase)).ToList();
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

            WriteLog("Import done rows=" + rows.Count + " updated=" + updated + " unmatched=" + unmatched.Count + " ambiguous=" + ambiguous.Count + " cats=" + catNames.Count + " tags=" + tagNames.Count);
            if (showDialog)
                PlayniteApi.Dialogs.ShowMessage("Import done.\nUpdated games: " + updated + "\nUnmatched: " + unmatched.Count + "\nAmbiguous: " + ambiguous.Count + "\nCategories: " + catNames.Count + "\nTags: " + tagNames.Count + "\n\n" + Path.Combine(outDir, "import_result.json"), "Playnite Collection Tool");
            return updated;
        }

        // ================= 清除 =================
        private void DoClear()
        {
            try
            {
                string mp = Path.Combine(outDir, "import_result.json");
                if (!File.Exists(mp)) { PlayniteApi.Dialogs.ShowMessage("No import_result.json, cannot determine what to remove.", "Playnite Collection Tool"); return; }
                string text = File.ReadAllText(mp, Encoding.UTF8);
                var catIds = ExtractIds(text, "CategoryIds");
                var tagIds = ExtractIds(text, "TagIds");
                if (catIds.Count == 0 && tagIds.Count == 0) { PlayniteApi.Dialogs.ShowMessage("No category/tag records from last import.", "Playnite Collection Tool"); return; }

                var confirm = PlayniteApi.Dialogs.ShowMessage("This will remove " + catIds.Count + " categories and " + tagIds.Count + " tags (including associations on games). Continue?", "Playnite Collection Tool", System.Windows.MessageBoxButton.YesNo);
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
                WriteLog("Removed categories/tags");
                PlayniteApi.Dialogs.ShowMessage("Removed.", "Playnite Collection Tool");
            }
            catch (Exception ex)
            {
                WriteLog("Clear failed: " + ex.ToString());
                PlayniteApi.Dialogs.ShowErrorMessage("Clear failed: " + ex.Message, "Playnite Collection Tool");
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
