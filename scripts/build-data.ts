/**
 * 汇总所有来源，生成两个给 Playnite 插件用的文件：
 *   - 分类.tsv   Name<TAB>Source<TAB>Categories<TAB>Tags
 *   - 游戏数据.tsv  Id<TAB>Name(原名)<TAB>Source<TAB>中文名<TAB>ReleaseDate<TAB>Genres<TAB>Developers<TAB>Publishers<TAB>Description
 *
 * 数据来源（都用 --xxx 指定，缺省视为没有）：
 *   --games      out/playnite_games.json   原始库（必须，含 Id/Name/Source）
 *   --steam      out/steam_meta.json       Steam 官方元数据（fetch-steam-meta 产出）
 *   --confirmed  out/confirmed.json        appid 确认结果（fetch-appdetails 产出）
 *   --names      map/manual_names.json     {"原名": "中文名"} 人工名（优先级最高）
 *   --versions   map/version_labels.json   {"原名": "带版本后缀的中文名"}
 *   --ids        map/manual_ids.json       {"原名": "游戏GUID"} 用于消歧（重名条目）
 *   --map        map/classification.tsv    人工分类表：Name<TAB>类型1;类型2[<TAB>来源]
 *   --outdir     out
 *
 * 规则：
 *   1) 中文名优先级：人工 > 版本标签 > 确认结果 > Steam 官方名 > 人工表 > 保留原名
 *   2) 同平台不同版本：加版本后缀（由 version_labels 提供）
 *   3) 跨平台同名：自动追加（来源）
 *   4) 输出前校验：最终名称不得重复
 */
import * as fs from "node:fs";
import * as path from "node:path";
import { cleanOfficialName, hasCJK, normalizeName, parseArgs, readJson, writeTsv } from "./lib.ts";

const args = parseArgs();
const outDir = String(args.outdir || "out");
const games = readJson<any[]>(path.join(outDir, "playnite_games.json")) as any;
const list: any[] = games?.Games || games;
const steam = readJson<Record<string, any>>(String(args.steam || path.join(outDir, "steam_meta.json")), {});
const confirmed = readJson<Record<string, any>>(String(args.confirmed || path.join(outDir, "confirmed.json")), {});
const manualNames = readJson<Record<string, string>>(String(args.names || "map/manual_names.json"), {});
const versionLabels = readJson<Record<string, string>>(String(args.versions || "map/version_labels.json"), {});
const manualIds = readJson<Record<string, string>>(String(args.ids || "map/manual_ids.json"), {});

const steamByPlaynite: Record<string, any> = {};

/** 把各种日期格式统一成 ISO yyyy-MM-dd（Steam 中文日期 / ISO / 仅年份） */
function toIsoDate(s: string): string {
  const cn = String(s || "").match(/(\d{4})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日/);
  if (cn) return cn[1] + "-" + cn[2].padStart(2, "0") + "-" + cn[3].padStart(2, "0");
  const iso = String(s || "").match(/(\d{4})-(\d{1,2})-(\d{1,2})/);
  if (iso) return iso[1] + "-" + iso[2].padStart(2, "0") + "-" + iso[3].padStart(2, "0");
  return "";
}
for (const v of Object.values(steam)) steamByPlaynite[v.playniteName] = v;

// ---------- 分类 ----------
let classification: Record<string, { cats: string; src: string }> = {};
if (args.map && fs.existsSync(String(args.map))) {
  const lines = fs.readFileSync(String(args.map), "utf8").replace(/^\uFEFF/, "").split(/\r?\n/).filter(l => l.trim() && !l.startsWith("#"));
  const header = lines[0].split("\t");
  const iName = header.indexOf("Name");
  const iCat = Math.max(header.indexOf("Categories"), header.indexOf("类型"));
  const iSrc = header.indexOf("Source");
  for (const line of lines.slice(1)) {
    const f = line.split("\t");
    if (iName < 0 || iCat < 0) continue;
    classification[normalizeName(f[iName]) + "||" + (iSrc >= 0 ? (f[iSrc] || "").trim() : "")] = { cats: f[iCat] || "", src: iSrc >= 0 ? (f[iSrc] || "").trim() : "" };
    classification[normalizeName(f[iName]) + "||"] = { cats: f[iCat] || "", src: "" };
  }
}

// ---------- 元数据 ----------
type Row = {
  id: string; name: string; source: string; cn: string; date: string;
  genres: string; dev: string; pub: string; desc: string; cats: string; tags: string;
};
const rows: Row[] = [];
const pending: string[] = [];

for (const g of list) {
  const name = g.Name;
  const src = g.Source || "";
  const sm = steamByPlaynite[name];
  const cf = confirmed[name];
  let cn = "";
  let date = "";
  let desc = "";
  let genres = "";
  let dev = "";
  let pub = "";

  if (manualNames[name]) cn = cleanOfficialName(manualNames[name]);
  if (versionLabels[name]) cn = versionLabels[name];
  if (!cn && cf && cf.cnName) cn = cleanOfficialName(cf.cnName);
  if (!cn && sm && hasCJK(sm.cnName)) cn = cleanOfficialName(sm.cnName);

  if (sm) {
    date = sm.releaseDate || "";
    if (!desc) desc = sm.desc || "";
    genres = (sm.genres || []).join(";");
    dev = (sm.developers || []).join(";");
    pub = (sm.publishers || []).join(";");
  }
  if (cf) {
    if (!date) date = cf.release || "";
    if (!desc) desc = cf.desc || "";
    if (!genres) genres = (cf.genres || []).join(";");
    if (!dev) dev = (cf.dev || []).join(";");
    if (!pub) pub = (cf.pub || []).join(";");
  }

  // 去掉「中文 + 重复英文」里的英文尾巴（仅当英文确实是原名开头）
  if (cn && !versionLabels[name]) {
    const m = cn.match(/^([\u4e00-\u9fff].*?)\s+([A-Za-z][A-Za-z0-9 .:&®™\-]*)$/);
    if (m && m[1].trim().length >= 2 && name.toLowerCase().startsWith(m[2].trim().toLowerCase())) cn = m[1].trim();
  }

  const key = normalizeName(name) + "||" + src;
  const cls = classification[key] || classification[normalizeName(name) + "||"];
  if (!cls) pending.push(name);
  const year = (String(date).match(/(19|20)\d\d/) || [""])[0];
  const isoDate = toIsoDate(date);

  rows.push({
    id: manualIds[name] || g.Id || "",
    name, source: src, cn,
    date: isoDate || (year ? year + "-01-01" : ""),
    genres, dev, pub, desc,
    cats: cls ? cls.cats : "",
    tags: [
      g.IsInstalled ? "A01 状态-正在玩" : "",
      ...String(g.Series || "").split(";").filter(Boolean).map((s: string) => "B 系列-" + s),
      /Co-Operative/i.test(String(g.Features || "")) ? "F01 特性-多人合作" : "",
      /Multiplayer/i.test(String(g.Features || "")) ? "F02 特性-在线对战" : "",
      /Split Screen/i.test(String(g.Features || "")) ? "F03 特性-本地同屏" : "",
      "S 平台-" + src
    ].filter(Boolean).join(";")
  });
}

// ---------- 名称去重：同名追加（来源） ----------
const used = new Map<string, Row>();
for (const r of rows) {
  const finalName = r.cn || r.name;
  if (!used.has(finalName)) { used.set(finalName, r); continue; }
  const first = used.get(finalName)!;
  if (!/[（(][^)）]*[)）]$/.test(first.cn || first.name)) first.cn = (first.cn || first.name) + "（" + first.source + "）";
  let candidate = finalName + "（" + r.source + "）";
  let k = 2;
  while (used.has(candidate)) { candidate = finalName + "（" + r.source + " " + k + "）"; k++; }
  r.cn = candidate;
  used.set(candidate, r);
}

// ---------- 落盘 ----------
const nameList = rows.filter(r => r.cats);
writeTsv(path.join(outDir, "分类.tsv"), ["Name", "Source", "Categories", "Tags"],
  nameList.map(r => [r.cn || r.name, r.source, r.cats, r.tags]));

writeTsv(path.join(outDir, "游戏数据.tsv"), ["Id", "Name", "Source", "中文名", "ReleaseDate", "Genres", "Developers", "Publishers", "Description"],
  rows.map(r => [r.id, r.name, r.source, r.cn || r.name, r.date, r.genres, r.dev, r.pub, r.desc]));

// 重名自检
const names = new Map<string, number>();
for (const r of rows) names.set(r.cn || r.name, (names.get(r.cn || r.name) || 0) + 1);
const dups = [...names.entries()].filter(([, c]) => c > 1);
console.log(`共 ${rows.length} 条；有中文名 ${rows.filter(r => r.cn).length}；有分类 ${nameList.length}；有简介 ${rows.filter(r => r.desc).length}`);
console.log("重名组:", dups.length, dups.slice(0, 5).map(([n]) => n).join(", "));
if (pending.length) console.log("未分类（前 10）:", pending.slice(0, 10).join(" | "));
console.log(`写出 ${path.join(outDir, "分类.tsv")} 与 ${path.join(outDir, "游戏数据.tsv")}`);
