/**
 * 用 Steam 搜索建议接口，为「还没有目标语言名称」的游戏找候选名。
 * 用法: node scripts/suggest-steam.ts --games out/playnite_games.json --skip-names out/steam_meta.json --out out/steam_suggest.json --lang schinese
 * 支持 --lang（Steam 语言代码）：schinese/tchinese/japanese/koreana/english 等；--cc 地区代码（默认 cn）。
 * 输出: { "<原始名>": { source, results: [{appid, name}] } }
 */
import * as fs from "node:fs";
import { delay, fetchWithRetry, isTargetLanguage, parseArgs, pool, writeJson } from "./lib.ts";

const args = parseArgs();
const gamesFile = String(args.games || "out/playnite_games.json");
const skipFile = String(args["skip-names"] || "out/steam_meta.json");
const outFile = String(args.out || "out/steam_suggest.json");
const concurrency = Number(args.concurrency || 4);
const lang = String(args.lang || "schinese");
const cc = String(args.cc || "cn");


const games = JSON.parse(fs.readFileSync(gamesFile, "utf8").replace(/^\uFEFF/, "")).Games || [];
let skippedNames: string[] = [];
try {
  const meta = JSON.parse(fs.readFileSync(skipFile, "utf8"));
  skippedNames = Object.values(meta).map((v: any) => v.playniteName);
} catch { /* 没有就全量处理 */ }
const skip = new Set<string>([...skippedNames, ...games.filter((g: any) => isTargetLanguage(g.Name, lang)).map((g: any) => g.Name)]);
const need = games.filter((g: any) => !skip.has(g.Name));
console.log("待搜索 (lang=" + lang + "):", need.length);

async function suggest(q: string): Promise<{ appid: string; name: string }[]> {
  const url = "https://store.steampowered.com/search/suggest?term=" + encodeURIComponent(q) + "&f=games&cc=" + cc + "&l=" + lang;
  const r = await fetchWithRetry(url);
  if (!r) return [];
  const t = await r.text();
  const items: { appid: string; name: string }[] = [];
  // 兼容 Steam 改版：match_name 允许带额外 class；名字做 HTML 实体反转义
  const re = /<a[^>]*data-ds-appid="(\d+)"[\s\S]*?<div[^>]*class="[^"]*match_name[^"]*"[^>]*>([\s\S]*?)<\/div>/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(t))) items.push({ appid: m[1], name: decodeHtml(m[2].replace(/<[^>]+>/g, "").trim()) });
  return items;
}

function decodeHtml(s: string): string {
  return s.replace(/&amp;/g, "&").replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&#39;|&apos;/g, "'");
}

const out: Record<string, any> = {};
await pool(need, concurrency, async (g: any, i) => {
  const items = await suggest(g.Name);
  if (items.length) out[g.Name] = { source: g.Source, results: items.slice(0, 4) };
  if ((i + 1) % 25 === 0) console.log(`进度 ${i + 1}/${need.length}`);
  await delay(200);
});

writeJson(outFile, out);
console.log("有候选:", Object.keys(out).length, "/", need.length);
