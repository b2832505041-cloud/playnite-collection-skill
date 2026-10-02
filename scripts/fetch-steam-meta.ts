/**
 * 从 Steam 国区商店抓官方中文元数据。
 * 用法: node scripts/fetch-steam-meta.ts --ids out/playnite_ids.json --out out/steam_meta.json [--lang schinese] [--cc cn]
 * 输入 JSON: [{ Name, Source, GameId }]，只处理 Source=Steam 且 GameId 是数字的条目。
 */
import * as fs from "node:fs";
import { fetchWithRetry, parseArgs, pool, writeJson } from "./lib.ts";

const args = parseArgs();
const idsFile = String(args.ids || "out/playnite_ids.json");
const outFile = String(args.out || "out/steam_meta.json");
const cc = String(args.cc || "cn");
const lang = String(args.lang || "schinese");
const concurrency = Number(args.concurrency || 6);

type IdRow = { Name: string; Source: string; GameId: string };
const rows: IdRow[] = JSON.parse(fs.readFileSync(idsFile, "utf8").replace(/^\uFEFF/, ""));
const targets = rows.filter(r => r.Source === "Steam" && /^\d+$/.test(String(r.GameId || "").trim()));
console.log("待抓取 Steam 条目:", targets.length);

type Meta = {
  appid: string; playniteName: string; cnName: string; type: string;
  developers: string[]; publishers: string[]; releaseDate: string;
  genres: string[]; categories: string[]; desc: string;
};

async function fetchOne(row: IdRow): Promise<Meta | null> {
  const appid = String(row.GameId).trim();
  const url = `https://store.steampowered.com/api/appdetails?appids=${appid}&cc=${cc}&l=${lang}`;
  const r = await fetchWithRetry(url);
  if (!r) return null;
  const j: any = await r.json();
  const d = j[appid];
  if (!d || !d.success) return null;
  const x = d.data;
  return {
    appid,
    playniteName: row.Name,
    cnName: x.name || "",
    type: x.type || "",
    developers: x.developers || [],
    publishers: x.publishers || [],
    releaseDate: (x.release_date && x.release_date.date) || "",
    genres: (x.genres || []).map((g: any) => g.description),
    categories: (x.categories || []).map((c: any) => c.description),
    desc: x.short_description || ""
  };
}

const out: Record<string, Meta> = {};
await pool(targets, concurrency, async (row, i) => {
  const meta = await fetchOne(row);
  if (meta) out[meta.appid] = meta;
  if ((i + 1) % 25 === 0) console.log(`进度 ${i + 1}/${targets.length}`);
});

writeJson(outFile, out);
const ok = Object.keys(out).length;
console.log(`完成：成功 ${ok}/${targets.length}，写出 ${outFile}`);
