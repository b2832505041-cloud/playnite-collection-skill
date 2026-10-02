// 通用工具（无第三方依赖）
import * as fs from "node:fs";
import * as path from "node:path";

export function readJson<T = any>(file: string, fallback: T): T {
  try { return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, "")) as T; } catch { return fallback; }
}

export function writeJson(file: string, data: unknown): void {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(data, null, 1), "utf8");
}

export function readTsv(file: string): { header: string[]; rows: string[][] } {
  const lines = fs.readFileSync(file, "utf8").replace(/^\uFEFF/, "").split(/\r?\n/).filter(l => l.trim());
  const header = lines[0].split("\t").map(s => s.trim());
  const rows = lines.slice(1).map(l => l.split("\t"));
  return { header, rows };
}

export function writeTsv(file: string, header: string[], rows: (string | number)[][]): void {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const text = [header.join("\t"), ...rows.map(r => r.map(c => String(c ?? "").replace(/[\t\r\n]+/g, " ").trim()).join("\t"))].join("\n") + "\n";
  fs.writeFileSync(file, text, "utf8");
}

/** 规范化游戏名，用于宽松匹配（忽略商标符号、标点、空格、大小写、版本后缀） */
export function normalizeName(s: string): string {
  return String(s || "")
    .toLowerCase()
    .replace(/[™®©]/g, "")
    .replace(/\b(game of the year|goty|definitive|complete|enhanced|remastered|deluxe|ultimate|legendary|standard|anniversary|edition|remaster|collection|demo|beta|prologue|test|public testing|open beta|tech beta|legacy mode|expansion)\b/g, " ")
    .replace(/[^a-z0-9\u4e00-\u9fff]+/g, "");
}

export function hasCJK(s: string): boolean {
  return /[\u4e00-\u9fff]/.test(s || "");
}

/** 清洗 Steam 返回的官方名：去商标符号、去结尾年份括号、去多余空格 */
export function cleanOfficialName(s: string): string {
  return String(s || "")
    .replace(/[™®©]/g, "")
    .replace(/\s+/g, " ")
    .replace(/\s*[（(](19|20)\d\d[)）]\s*$/, "")
    .trim();
}

/** 简单并发池 */
export async function pool<T, R>(items: T[], concurrency: number, worker: (item: T, index: number) => Promise<R>): Promise<R[]> {
  const results: R[] = new Array(items.length);
  let next = 0;
  const runners = Array.from({ length: Math.max(1, concurrency) }, async () => {
    while (true) {
      const i = next++;
      if (i >= items.length) return;
      results[i] = await worker(items[i], i);
    }
  });
  await Promise.all(runners);
  return results;
}

/** 带重试的 fetch（Steam 商店在高峰期会 429） */
export async function fetchWithRetry(url: string, retries = 3, timeoutMs = 25000): Promise<Response | null> {
  for (let attempt = 0; attempt < retries; attempt++) {
    try {
      const r = await fetch(url, { signal: AbortSignal.timeout(timeoutMs) });
      if (r.status === 429) { await delay(2500 * (attempt + 1)); continue; }
      if (!r.ok) { await delay(1200); continue; }
      return r;
    } catch {
      await delay(1500);
    }
  }
  return null;
}

export function delay(ms: number): Promise<void> {
  return new Promise(r => setTimeout(r, ms));
}

/** 极简参数解析：--key value / --flag */
export function parseArgs(argv: string[] = process.argv.slice(2)): Record<string, string | boolean> {
  const out: Record<string, string | boolean> = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (!a.startsWith("--")) continue;
    const key = a.slice(2);
    const next = argv[i + 1];
    if (next && !next.startsWith("--")) { out[key] = next; i++; }
    else out[key] = true;
  }
  return out;
}
