---
name: playnite-collection
description: 把 Playnite 游戏库整理成分类 + 标签，并把游戏名/简介/发行日期/类型/厂商中文化与补全（官方中文名优先取 Steam 国区），以可回滚的 Playnite 插件写入。当用户说“整理 Playnite 库/给 Playnite 分类/游戏名改中文/补全游戏信息”时使用。
---

# Playnite 游戏库分类与中文化

把 Playnite 库整理成一套可筛选的**分类 + 标签**，并把元数据中文化 / 补全。写入通过自带的 Playnite 插件完成，
**每批写入前备份、可一键回滚**，不修改游戏安装目录、启动动作、ID 等影响运行逻辑的字段。

## 何时使用

- 用户要求「整理 Playnite 游戏库」「按类型/系列分类」「把游戏名改成中文」「补全发行日期/简介/厂商」
- 用户抱怨「库里一堆英文名」「有重复的名字」「筛选器里没有分类可用」

## 前置条件（开工前确认，缺什么问什么）

1. **Playnite 已安装**且至少导入过一个商店库（Steam/Epic/GOG/Xbox/…）。
2. **Windows + .NET Framework 4.x**：插件用系统自带 `csc` 编译，不需要 .NET SDK。
3. **Node.js 18+**：跑数据脚本（无第三方依赖，只用内置模块）。
4. 询问用户：**是否允许改游戏名称**（会把英文名改成中文）、**分类精细度**、**是否要隐藏工具/Demo 条目**。
   —— 改名前必须让用户知情，因为名称是用户最直观的资产。

## 流程

### 步骤 0：准备

```powershell
# 编译 + 安装插件
powershell -ExecutionPolicy Bypass -File scripts\build-plugin.ps1
```

### 步骤 1：导出游戏库

Playnite 运行时会**独占锁定** `games.db`，普通复制会失败。脚本会：

1. 调 `Playnite.DesktopApp.exe --shutdown` 优雅退出（必要时等待/结束进程）；
2. 复制 `%APPDATA%\Playnite\library\*.db` 到临时目录；
3. 用 Playnite 自带的 `LiteDB.dll` 只读打开，导出 `playnite_games.json`（含 Id、Name、Source、Genres、Developers、Publishers、Series、Features、Platforms、ReleaseDate、Playtime、IsInstalled…）。

```powershell
powershell -ExecutionPolicy Bypass -File scripts\export-playnite.ps1 -OutDir .\out
```

**汇报**：总数、各来源数量、已安装数量、有多少条没有中文名。等待确认。

### 步骤 2：设计分类体系

沿用「维度前缀」命名，便于在 Playnite 筛选器里排序：

| 维度 | 落地方式 | 例子 |
|---|---|---|
| C 类型 | 标签（每款 1-2 个） | `C01 类型-动作`、`C08 类型-解谜` |
| B 系列 | 标签 | `B 系列-Forza Horizon` |
| E 其他 | 标签 | `E01 其他-工具软件`、`E02 其他-演示测试` |
| F 特性 | 标签 | `F01 特性-多人合作`、`F02 特性-在线对战` |
| S 平台 | 标签 | `S 平台-Steam` |
| A 状态 | Playnite「完成状态」 | `正在玩`（只给已安装的） |
| D 厂商 | Playnite 原生「开发商/发行商」 | 不重复打标签 |

规则：
- **每款游戏至少一个 C 类型**；工具软件（OBS、ShareX、3DMark…）只给 `E01`，不给 C。
- Demo / Beta / 测试服 / 特典给 `E02 其他-演示测试`。
- 编号在类目文档里唯一、连续；兜底类用最大号（`C99`）。
- **重名规则**（必须遵守）：
  - 同一作品的版本条目加**版本后缀**：`示例游戏 第2集`、`示例游戏 技术测试版`、`示例竞速 Demo`；
  - 跨平台同一游戏加**（来源）后缀**：`示例竞速（Steam）` / `（Xbox）`、`示例网游（Epic）`；
  - 生成后必须校验「最终名称无重复」，重复的自动补后缀。

### 步骤 3：中文化元数据

优先级（从高到低）：

1. 用户在人工表里指定的名字（`map/manual_names.json`）
2. **Steam 国区商店页官方中文名**：`https://store.steampowered.com/api/appdetails?appids=<id>&cc=cn&l=schinese`
3. Steam 搜索建议（`/search/suggest?...&cc=cn&l=schinese`）命中同名游戏时取其中文名
4. 知名游戏的人工对照表
5. 都没有 → **保留原名**（不要机翻生造，不要给工具类硬翻）

注意：
- Steam 官方名会带 `™®©` 和版本后缀，落库前要清洗（去商标符号、去「- Episode 1」这类非本体后缀）。
- **续作误配**是最常见的坑：搜前作名会命中续作（如 `Sample Game` → 《Sample Game 2》），要用「原名 + 年份 + 类型」交叉校验，见 `docs/pitfalls.md`。
- 中文简介优先用 Steam 国区的 `short_description`；没有官方中文的写一句不超过 40 字的中文玩法概述。

### 步骤 4：写入 Playnite（按 ID）

生成 `游戏数据.tsv`（列：`Id, Name(原名), Source, 中文名, ReleaseDate, Genres, Developers, Publishers, Description`）与
`分类.tsv`（列：`Name, Source, Categories, Tags`），放进：

```
%APPDATA%\Playnite\ExtensionsData\playnite-collection-tool\
```

插件会在 Playnite 启动时（`OnApplicationStarted`）自动应用，也可以从
**主菜单 → 扩展 → Playnite 库整理** 手动触发：

- 应用分类/标签
- 应用元数据（名称/日期/类型/厂商/简介）
- 撤销元数据改动（回滚到应用前）
- 清除分类/标签（回滚）

**关键实现约束**（踩过的坑，务必保持）：
- 必须在 `OnApplicationStarted` 里做，**不能在插件构造函数里**——构造函数阶段 `Database.Games` 还是空的。
- **按游戏 ID（GUID）匹配写入**，不要只按名字匹配：改名后名字对不上会整批失配。
- 插件程序集必须 **AnyCPU**（Playnite 是 32 位进程，x64 DLL 会报 `BadImageFormatException`）。
- 本地化对照表（来源/完成状态/平台）是 `Dictionary<string,string>` 且大小写不敏感，**键不能重复**，否则静态构造函数抛异常导致整个插件失效。

### 步骤 5：校验（必做）

重新读库检查：

```text
GAMES=<总数>  有分类=<全部>  有简介=<全部>  含中文名=<尽量高>  重复名称组数=0
```

任何一项不达标都要排查后再汇报。汇报内容：分类数、标签数、中文名覆盖率、未翻译清单（哪些官方确实没有中文名）、回滚方式。

## 目录约定

```
project/
  out/            导出与中间产物（不进版本控制）
  map/            人工维护的映射表：classification.tsv / manual_names.json / manual_ids.json
  backup/         每次写入前的 Playnite 数据库备份
  scripts/        本 Skill 的脚本
  plugin/         写入插件源码
```

## 通用守则

- **写入前必须备份** `%APPDATA%\Playnite\library\*.db`，并告知用户回滚方式。
- 不把任何账号 ID、Cookie、Token 写进产出物或仓库。
- 大库（500+）分批处理，每批写入后校验数量；名称去重必须在最后统一做。
- 遇到异常先查 `docs/pitfalls.md`。
