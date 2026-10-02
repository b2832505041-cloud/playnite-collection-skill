/**
 * 汇总所有来源，生成两个给 Playnite 插件用的文件：
 *   - 分类.tsv   Name<TAB>Source<TAB>Categories<TAB>Tags
 *   - 游戏数据.tsv  Id<TAB>Name(原名)<TAB>Source<TAB>LocalName<TAB>ReleaseDate<TAB>Genres<TAB>Developers<TAB>Publishers<TAB>Description
 *
 * 数据来源（都用 --xxx 指定，缺省视为没有）：
 *   --games      out/playnite_games.json   原始库（必须，含 Id/Name/Source）
 *   --steam      out/steam_meta.json       Steam 官方元数据（fetch-steam-meta 产出）
 *   --confirmed  out/confirmed.json        appid 确认结果（fetch-appdetails 产出）
 *   --names      map/manual_names.json     {"原名": "本地化名"} 人工名（优先级最高）
 *   --versions   map/version_labels.json   {"原名": "带版本后缀的本地化名"}
 *   --ids        map/manual_ids.json       {"原名": "游戏GUID"} 用于消歧（重名条目）
 *   --map        map/classification.tsv    人工分类表：Name<TAB>类型1;类型2[<TAB>来源]
 *   --outdir     out
 *   --lang       目标语言（Steam 代码：schinese/tchinese/japanese/koreana/english…，默认 schinese）
 *
 * 规则：
 *   1) 本地化名优先级：人工 > 版本标签 > 确认结果 > Steam 官方名 > 人工表 > 保留原名
 *   2) 同平台不同版本：加版本后缀（由 version_labels 提供）
 *   3) 跨平台同名：自动追加（来源）
 *   4) 输出前校验：最终名称不得重复
 */
import * as fs from "node:fs";
import * as path from "node:path";
import { cleanOfficialName, isTargetLanguage, normalizeName, parseArgs, readJson, writeTsv } from "./lib.ts";

const args = parseArgs();
const outDir = String(args.outdir || "out");
const lang = String(args.lang || "schinese");

const games = readJson<any[]>(path.join(outDir, "playnite_games.json")) as any;
const list: any[] = games?.Games || games;
const steam = readJson<Record<string, any>>(String(args.steam || path.join(outDir, "steam_meta.json")), {});
const confirmed = readJson<Record<string, any>>(String(args.confirmed || path.join(outDir, "confirmed.json")), {});
const manualNames = readJson<Record<string, string>>(String(args.names || "map/manual_names.json"), {});
const versionLabels = readJson<Record<string, string>>(String(args.versions || "map/version_labels.json"), {});
const manualIds = readJson<Record<string, string>>(String(args.ids || "map/manual_ids.json"), {});

const steamByPlaynite: Record<string, any> = {};

/**
 * 分类/标签前缀 —— 按你的目标语言修改这里即可。
 * 例：英文可改成
 *   LABELS.statusPlaying = "A01 Status-Playing"
 *   LABELS.seriesPrefix = "B Series-"
 *   LABELS.featureCoop = "F01 Feature-Coop"
 *   ...
 */
const LABELS = {
  statusPlaying: "A01 状态-正在玩",
  seriesPrefix: "B 系列-",
  featureCoop: "F01 特性-多人合作",
  featureOnline: "F02 特性-在线对战",
  featureSplit: "F03 特性-本地同屏",
  platformPrefix: "S 平台-"
};

/** 把各种日期格式统一成 ISO yyyy-MM-dd（Steam 各语言区返回日期 / ISO / 仅年份） */
function toIsoDate(s: string): string {
  const v = String(s || "").trim();
  const pad = (n: string) => n.padStart(2, "0");
  // 中文/日文：2024 年 11 月 8 日 / 2024年11月8日
  let m = v.match(/(\d{4})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日/);
  if (m) return `${m[1]}-${pad(m[2])}-${pad(m[3])}`;
  // 韩文：2024년 11월 8일
  m = v.match(/(\d{4})\s*년\s*(\d{1,2})\s*월\s*(\d{1,2})\s*일/);
  if (m) return `${m[1]}-${pad(m[2])}-${pad(m[3])}`;
  // ISO：2024-11-08
  m = v.match(/(\d{4})-(\d{1,2})-(\d{1,2})/);
  if (m) return `${m[1]}-${pad(m[2])}-${pad(m[3])}`;
  // 英文等：18 May, 2016 / May 18, 2016 / 18 May 2016
  m = v.match(/(\d{1,2})\s+([A-Za-z]{3,9})[.,]?\s+(\d{4})/);
  if (m) { const mon = monthNum(m[2]); if (mon) return `${m[3]}-${pad(String(mon))}-${pad(m[1])}`; }
  m = v.match(/([A-Za-z]{3,9})\s+(\d{1,2})[.,]?\s+(\d{4})/);
  if (m) { const mon = monthNum(m[1]); if (mon) return `${m[3]}-${pad(String(mon))}-${pad(m[2])}`; }
  return "";
}

/** 英文月份缩写/全称 → 数字 */
function monthNum(s: string): number {
  const m = String(s || "").toLowerCase().slice(0, 3);
  const map: Record<string, number> = { jan: 1, feb: 2, mar: 3, apr: 4, may: 5, jun: 6, jul: 7, aug: 8, sep: 9, oct: 10, nov: 11, dec: 12 };
  return map[m] || 0;
}
for (const v of Object.values(steam)) steamByPlaynite[v.playniteName] = v;

// ---------- 分类 ----------
let classification: Record<string, { cats: string; src: string }> = {};
if (args.map && fs.existsSync(String(args.map))) {
  const lines = fs.readFileSync(String(args.map), "utf8").replace(/^\uFEFF/, "").split(/\r?\n/).filter(l => l.trim() && !l.startsWith("#"));
  const header = lines[0].split("\t");
  const iName = Math.max(header.indexOf("Name"), header.indexOf("名称"));
  const iCat = Math.max(header.indexOf("Categories"), header.indexOf("类型"));
  const iSrc = Math.max(header.indexOf("Source"), header.indexOf("来源"));
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
  // 库自带发行日期作为兜底：非 Steam（Epic/GOG/Xbox…）游戏的日期不会被丢掉
  let date = g.ReleaseDate || "";
  let desc = "";
  let genres = "";
  let dev = "";
  let pub = "";

  // 优先级：人工表 > 版本标签 > 确认结果 > Steam 官方名（与 SKILL.md 一致）
  if (manualNames[name]) cn = cleanOfficialName(manualNames[name]);
  if (!cn && versionLabels[name]) cn = versionLabels[name];
  if (!cn && cf && cf.localName && isTargetLanguage(cf.localName, lang) && normalizeName(cf.localName) !== normalizeName(name)) cn = cleanOfficialName(cf.localName);
  if (!cn && sm && sm.localName && isTargetLanguage(sm.localName, lang) && normalizeName(sm.localName) !== normalizeName(name)) cn = cleanOfficialName(sm.localName);

  if (sm) {
    if (sm.releaseDate) date = sm.releaseDate;
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

  // 去掉「本地化名 + 重复英文」里的英文尾巴（仅当英文确实是原名开头）
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
      g.IsInstalled ? LABELS.statusPlaying : "",
      ...String(g.Series || "").split(";").filter(Boolean).map((s: string) => LABELS.seriesPrefix + s),
      /Co-Operative/i.test(String(g.Features || "")) ? LABELS.featureCoop : "",
      /Multiplayer/i.test(String(g.Features || "")) ? LABELS.featureOnline : "",
      /Split Screen/i.test(String(g.Features || "")) ? LABELS.featureSplit : "",
      LABELS.platformPrefix + src
    ].filter(Boolean).join(";")
  });
}

// ---------- 名称去重：同名追加（来源） ----------
// 先按「期望名」分组；任一组内出现多条时，所有成员统一追加（来源）后缀，仍冲突再加序号。
const byBase = new Map<string, Row[]>();
for (const r of rows) {
  const base = r.cn || r.name;
  const group = byBase.get(base);
  if (group) group.push(r); else byBase.set(base, [r]);
}
for (const [base, group] of byBase) {
  if (group.length <= 1) continue;
  const seen = new Set<string>();
  for (const r of group) {
    let candidate = base + "（" + r.source + "）";
    let k = 2;
    while (seen.has(candidate)) { candidate = base + "（" + r.source + " " + k + "）"; k++; }
    seen.add(candidate);
    r.cn = candidate;
  }
}

// ---------- 落盘 ----------
const nameList = rows.filter(r => r.cats);
writeTsv(path.join(outDir, "分类.tsv"), ["Name", "Source", "Categories", "Tags"],
  nameList.map(r => [r.cn || r.name, r.source, r.cats, r.tags]));

writeTsv(path.join(outDir, "游戏数据.tsv"), ["Id", "Name", "Source", "LocalName", "ReleaseDate", "Genres", "Developers", "Publishers", "Description"],
  rows.map(r => [r.id, r.name, r.source, r.cn || r.name, r.date, r.genres, r.dev, r.pub, r.desc]));

// 重名自检
const names = new Map<string, number>();
for (const r of rows) names.set(r.cn || r.name, (names.get(r.cn || r.name) || 0) + 1);
const dups = [...names.entries()].filter(([, c]) => c > 1);
console.log(`共 ${rows.length} 条；有本地化名 ${rows.filter(r => r.cn).length}；有分类 ${nameList.length}；有简介 ${rows.filter(r => r.desc).length}`);
console.log("重名组:", dups.length, dups.slice(0, 5).map(([n]) => n).join(", "));
if (pending.length) console.log("未分类（前 10）:", pending.slice(0, 10).join(" | "));
console.log(`写出 ${path.join(outDir, "分类.tsv")} 与 ${path.join(outDir, "游戏数据.tsv")}`);
