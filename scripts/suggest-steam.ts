/**
 * 用 Steam 搜索建议接口为「没有中文名」的游戏找候选中文名。
 * 用法: node scripts/suggest-steam.ts --games out/playnite_games.json --skip-names out/steam_meta.json --out out/steam_suggest.json
 * 输出: { "<原始名>": { source, results: [{appid, name}] } }
 */
import * as fs from "node:fs";
import { delay, fetchWithRetry, hasCJK, parseArgs, pool, writeJson } from "./lib.ts";

const args = parseArgs();
const gamesFile = String(args.games || "out/playnite_games.json");
const skipFile = String(args["skip-names"] || "out/steam_meta.json");
const outFile = String(args.out || "out/steam_suggest.json");
const concurrency = Number(args.concurrency || 4);

const games = JSON.parse(fs.readFileSync(gamesFile, "utf8").replace(/^\uFEFF/, "")).Games || [];
let skippedNames: string[] = [];
try {
  const meta = JSON.parse(fs.readFileSync(skipFile, "utf8"));
  skippedNames = Object.values(meta).map((v: any) => v.playniteName);
} catch { /* 没有就全量处理 */ }
const skip = new Set<string>([...skippedNames, ...games.filter((g: any) => hasCJK(g.Name)).map((g: any) => g.Name)]);
const need = games.filter((g: any) => !skip.has(g.Name));
console.log("待搜索:", need.length);

async function suggest(q: string): Promise<{ appid: string; name: string }[]> {
  const url = "https://store.steampowered.com/search/suggest?term=" + encodeURIComponent(q) + "&f=games&cc=cn&l=schinese";
  const r = await fetchWithRetry(url);
  if (!r) return [];
  const t = await r.text();
  const items: { appid: string; name: string }[] = [];
  const re = /<a[^>]*data-ds-appid="(\d+)"[\s\S]*?<div class="match_name">([\s\S]*?)<\/div>/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(t))) items.push({ appid: m[1], name: m[2].replace(/<[^>]+>/g, "").trim() });
  return items;
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
