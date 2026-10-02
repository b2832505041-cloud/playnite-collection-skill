/**
 * 用候选 appid 调 Steam 商店接口，确认「是不是游戏本体」并取官方目标语言名/日期/简介。
 * 两种输入模式：
 *   1) --candidates out/steam_suggest.json   从 suggest-steam 的输出里挑候选
 *   2) --appids "1245620,383270"             直接指定 appid（适合少数需要人工补的条目）
 * 用法: node scripts/fetch-appdetails.ts --candidates out/steam_suggest.json --out out/confirmed.json
 */
import * as fs from "node:fs";
import { fetchWithRetry, isTargetLanguage, parseArgs, pool, writeJson } from "./lib.ts";

const args = parseArgs();
const outFile = String(args.out || "out/confirmed.json");
const cc = String(args.cc || "cn");
const lang = String(args.lang || "schinese");
const concurrency = Number(args.concurrency || 4);
const BAD = /(soundtrack|ost|art ?book|bundle|pack|dlc|expansion|season pass|demo|wallpaper|原声|扩展|合集|服装|皮肤|editor|server|工具|tool|bonus)/i;

const appidOnly: string[] = args.appids ? String(args.appids).split(",").map(s => s.trim()).filter(Boolean) : [];

type Job = { key: string; source?: string; appids: string[] };
const jobs: Job[] = [];

if (appidOnly.length) {
  for (const id of appidOnly) jobs.push({ key: id, appids: [id] });
} else {
  const candFile = String(args.candidates || "out/steam_suggest.json");
  const cand = JSON.parse(fs.readFileSync(candFile, "utf8").replace(/^\uFEFF/, ""));
  for (const [name, info] of Object.entries<any>(cand)) {
    const picked = (info.results || []).filter((r: any) => !BAD.test(r.name)).slice(0, 2).map((r: any) => r.appid);
    jobs.push({ key: name, source: info.source, appids: picked });
  }
}
console.log("待确认条目:", jobs.length);

async function details(appid: string) {
  const url = `https://store.steampowered.com/api/appdetails?appids=${appid}&cc=${cc}&l=${lang}`;
  const r = await fetchWithRetry(url);
  if (!r) return null;
  const j: any = await r.json();
  const d = j[appid];
  if (!d || !d.success) return null;
  return d.data;
}

const out: Record<string, any> = {};
await pool(jobs, concurrency, async (job, i) => {
  for (const appid of job.appids) {
    const d = await details(appid);
    if (d && d.type === "game") {
      out[job.key] = {
        source: job.source || "",
        appid,
        localName: isTargetLanguage(d.name, lang) ? d.name : "",
        steamName: d.name || "",
        release: (d.release_date && d.release_date.date) || "",
        desc: d.short_description || "",
        genres: (d.genres || []).map((g: any) => g.description),
        dev: d.developers || [],
        pub: d.publishers || []
      };
      break;
    }
  }
  if ((i + 1) % 25 === 0) console.log(`进度 ${i + 1}/${jobs.length}`);
});

writeJson(outFile, out);
const withLocal = Object.values(out).filter((v: any) => v.localName).length;
console.log(`完成：${Object.keys(out).length} 条确认，其中带目标语言名 ${withLocal} 条 -> ${outFile}`);
