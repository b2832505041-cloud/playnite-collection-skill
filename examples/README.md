# 示例数据（虚构）

这里的数据**全部是编造的**，只用来演示列格式，不含任何真实游戏库内容。

| 文件 | 对应真实数据的 | 说明 |
|---|---|---|
| `classification.tsv` | `map/classification.tsv` | 人工分类表：Name / Categories / Source |
| `游戏数据.tsv` | `out/游戏数据.tsv` | 插件读取的写入数据（含游戏 GUID） |
| `manual_names.json` | `map/manual_names.json` | 人工指定中文名 |
| `version_labels.json` | `map/version_labels.json` | 版本类条目的带后缀名称 |
| `manual_ids.json` | `map/manual_ids.json` | 重名条目的 GUID 消歧 |
| `playnite_ids.json` | `out/playnite_ids.json` | 从库导出的 Id/名称/来源，供抓元数据 |

真实使用时：

```powershell
mkdir map
copy examples\manual_names.json   map\
copy examples\version_labels.json  map\
copy examples\manual_ids.json      map\
# classification.tsv 需要按自己的库来写
```
