# 示例数据（虚构）/ Example data (fictional)

这里的数据**全部是编造的**，只用来演示列格式，不含任何真实游戏库内容。
All data here is **fictional**, used only to demonstrate the column format. It contains no real library content.

| 文件 / File | 对应真实数据 / Real counterpart | 说明 / Notes |
|---|---|---|
| `classification.tsv` | `map/classification.tsv` | 人工分类表：Name / Categories / Source<br>Manual classification map |
| `游戏数据.tsv` | `out/游戏数据.tsv` | 插件读取的写入数据（含游戏 GUID）<br>Write data the plugin reads (with game GUID) |
| `loc_map.json` | `ExtensionsData\playnite-collection-tool\loc_map.json` | 本地化覆盖表（默认中文，可改成任意语言）<br>Locale override (default zh, switch to any language) |
| `manual_names.json` | `map/manual_names.json` | 人工指定本地化名<br>Manual localized names |
| `version_labels.json` | `map/version_labels.json` | 版本类条目的带后缀名称<br>Version-label names with suffixes |
| `manual_ids.json` | `map/manual_ids.json` | 重名条目的 GUID 消歧<br>GUID disambiguation for duplicates |
| `playnite_ids.json` | `out/playnite_ids.json` | 从库导出的 Id/名称/来源<br>Id/name/source exported from the library |

真实使用时 / Real usage:

```powershell
mkdir map
copy examples\manual_names.json   map\
copy examples\version_labels.json  map\
copy examples\manual_ids.json      map\
# classification.tsv 需要按自己的库来写 / write classification.tsv for your own library
```

## 目标语言 / Target language

把抓取和生成脚本的 `--lang` 传成你的语言即可，例如：
Pass `--lang` to the fetch/generate scripts to pick your language, e.g.:

- 简体中文 / Simplified Chinese: `--lang schinese`
- 繁体中文 / Traditional Chinese: `--lang tchinese`
- 日文 / Japanese: `--lang japanese`
- 韩文 / Korean: `--lang koreana`
- 英文（反向，把中文名换成英文）/ English (reverse, zh → en): `--lang english`

界面字段名（来源/完成状态/平台）由 `loc_map.json` 控制。
UI field names (source/status/platform) are controlled by `loc_map.json`.
