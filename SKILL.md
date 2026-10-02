---
name: playnite-collection
description: 把 Playnite 游戏库整理成分类+标签，并把游戏名/简介/发行日期/类型/厂商本地化到任意目标语言（默认简体中文，支持中英互换、日韩繁中等），以可回滚的插件写入。当用户说"整理 Playnite 库/给 Playnite 分类/游戏名改中文或改成英文/补全游戏信息"时使用。Organize a Playnite library into categories+tags and localize game metadata to any target language (default zh, supports zh↔en, ja/ko/zh-TW), written via a rollback-safe plugin.
---

# Playnite 游戏库分类与本地化 / Playnite library categorization & localization

把 Playnite 库整理成可筛选的分类+标签，并把元数据本地化/补全到目标语言。
Organize a Playnite library into filterable categories/tags, and localize/enrich metadata into a target language.

## 何时使用 / When to use

- 用户要求"整理 Playnite 库""分类""把游戏名改成中文/英文/某语言""补全发行日期/简介/厂商"
- "library is a mess""names are not in my language""duplicate names""no categories to filter"

## 前置确认 / Before starting (ask what's missing)

1. Playnite 已安装并导入过商店库；Windows + .NET Framework 4.x；Node.js ≥ 23.6。
2. **目标语言**：默认 `schinese`（简体中文）。用户要别的语言就传 `--lang`：
   `tchinese` 繁体 / `japanese` 日文 / `koreana` 韩文 / `english` 英文 / `german` `french` `italian` `spanish` `russian` 德法意西俄。
   Target language: default `schinese`; pass `--lang` for others (`english` converts non-Latin names to English).
3. **是否允许改游戏名**：改名前必须让用户知情（名称是用户最直观的资产）。
   Ask whether renaming games is OK.

## 流程 / Workflow

### 0. 编译安装插件 / Build & install

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-plugin.ps1
```

### 1. 导出库 / Export library

Playnite 运行时独占锁定 `games.db`，脚本会 `--shutdown` 优雅退出后复制并读取。

```powershell
powershell -ExecutionPolicy Bypass -File scripts\export-playnite.ps1 -OutDir .\out
```

汇报：总数、各来源数量、已安装数、有多少条还没有目标语言名。
Report: total, per-source count, installed count, how many lack a target-language name.

### 2. 设计分类体系 / Design categories

维度前缀（默认中文，可在 `build-data.ts` 顶部 `LABELS` 常量改成目标语言）：
Dimension prefixes (default zh; edit `LABELS` in `build-data.ts` for another language):

| 维度 Dim | 落地 How | 例子 Example |
|---|---|---|
| C 类型 Type | 分类 category（1-2 个） | `C01 类型-动作` |
| B 系列 Series | 标签 tag（自动） | `B 系列-Forza Horizon` |
| E 其他 Other | 分类 category | `E01 其他-工具软件` / `E02 其他-演示测试` |
| F 特性 Feature | 标签 tag（自动） | `F01 特性-多人合作` `F02 特性-在线对战` `F03 特性-本地同屏` |
| S 平台 Platform | 标签 tag（自动） | `S 平台-Steam` |
| A 状态 Status | 标签 tag（自动，仅已安装） | `A01 状态-正在玩` |
| D 厂商 Company | 原生字段 native field | 不重复打标签 |

规则 Rules：
- 每款至少一个 C 类型；工具软件只给 E01 不给 C。At least one C type; tools get E01 only.
- Demo/Beta/测试服/特典给 E02。Demos/betas/test servers get E02.
- **重名规则（必须遵守）Duplicate rules (mandatory)**：
  - 同作品不同版本加版本后缀：`示例游戏 第2集`、`示例游戏 技术测试版`、`示例竞速 Demo`
  - 跨平台同游戏加（来源）后缀：`示例竞速（Steam）` / `（Xbox）`
  - 生成后必须校验"最终名称无重复"，重复自动补后缀。

### 3. 本地化元数据 / Localize metadata

优先级 Priority（高→低）：人工表 > 版本标签 > 确认结果 > Steam 目标语言区官方名 > 人工对照 > 保留原名。
manual > version labels > confirmed > Steam target-locale name > manual map > keep original.

- Steam 商店：`https://store.steampowered.com/api/appdetails?appids=<id>&cc=<cc>&l=<lang>`
- **续作误配**：搜前作名会命中续作，用「原名+年份+类型」交叉校验（见 docs/pitfalls.md）。
- 官方名带 `™®©` 和版本尾巴要清洗；英文尾巴只在"确实是原名开头"时才裁掉。
- 简介优先官方目标语言 `short_description`；没有就写一句 ≤40 字的玩法概述。
- **任意语言互换**：目标为某语言时，只有还不是该语言文字的名字才需要翻译；每种语言用各自字符集判断（汉字/假名/谚文/西里尔/拉丁）。

### 4. 写入 / Write（按 ID）

生成 `游戏数据.tsv`（列：`Id, Name, Source, LocalName, ReleaseDate, Genres, Developers, Publishers, Description`）与
`分类.tsv`（`Name, Source, Categories, Tags`），放进：

```
%APPDATA%\Playnite\ExtensionsData\playnite-collection-tool\
```

插件在 `OnApplicationStarted` 自动应用，也可从 **主菜单 → 扩展 → Playnite Collection Tool** 手动触发：
- Apply categories/tags
- Apply localized metadata
- Revert all metadata changes
- Export library data
- Remove categories/tags (rollback)

**关键实现约束（踩过的坑，务必保持）Hard constraints**：
- 必须在 `OnApplicationStarted` 做，不能在构造函数（那时 Database.Games 是空的）。
- **按游戏 ID（GUID）写入**，不要只按名字（改名后名字失配）。
- 插件必须 **AnyCPU**（Playnite 是 32 位进程，x64 报 BadImageFormatException）。
- 本地化映射 `LocMap` 默认简体中文，可用 `loc_map.json` 覆盖成任意语言（键=英文原名，值=目标语言名；重复键后者覆盖前者）。

### 5. 校验 / Verify

```text
GAMES=<总数>  有分类=<全部>  有简介=<全部>  含目标语言名=<尽量高>  重复名称组数=0
```

汇报：分类数、标签数、本地化覆盖率、未翻译清单、回滚方式。
Report: category/tag counts, localization coverage, untranslated list, rollback method.

## 目录约定 / Directory

```
project/
  out/            导出与中间产物（不进版本控制）/ exports & intermediates (git-ignored)
  map/            人工映射表 / manual maps
  backup/         写入前数据库备份 / pre-write DB backups
  scripts/        脚本 / scripts
  plugin/         写入插件源码 / plugin source
```

## 通用守则 / General rules

- 写入前必须备份 `%APPDATA%\Playnite\library\*.db`，并告知回滚方式。
- 不把账号 ID、Cookie、Token 写进产出物或仓库。
- 大库分批处理，每批写入后校验数量；名称去重最后统一做。
- 遇到异常先查 `docs/pitfalls.md`。
