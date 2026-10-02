# 设计说明 / Design Notes

## 为什么用「插件写入」 / Why write via a plugin而不是直接改数据库

| 方案 | 风险 |
|---|---|
| 直接写 LiteDB | 需要复刻 Playnite 的 schema；写错会损坏库；无法利用 Playnite 的缓存刷新 |
| Playnite SDK 插件 | 走官方 API（`Categories.Add` / `Tags.Add` / `Games.Update`），自动处理缓存与索引 |

插件是幂等的：数据文件没变、版本号没变时不会重复写入（`applied_version.txt` 标记）。

## 数据流 / Data flow

```
games.db ──(export-playnite.ps1)──▶ playnite_games.json
                                        │
playnite_ids.json ──(fetch-steam-meta)──▶ steam_meta.json ─┐
                                                            ├─(build-data.ts)─▶ 分类.tsv / 游戏数据.tsv
candidates ──(fetch-appdetails)──────▶ confirmed.json ──────┘        │
map/manual_names.json / version_labels.json / manual_ids.json        │
                                                                     ▼
                                        ExtensionsData\playnite-collection-tool\
                                                                     │
                                                     Playnite 启动/手动触发 → 写入库
```

## 分类维度约定 / Category dimension conventions

统一用「维度前缀 + 编号 + 名称」，好处是在 Playnite 筛选器里按字母排序天然分组。
落地位置：C（类型）与 E（其他）写入 Playnite 的**分类 Categories**；B（系列）、F（特性）、S（平台）、A（状态）写入**标签 Tags**。

- `C01 类型-动作` … 类型（每款 1-2 个）→ 分类
- `E01 其他-工具软件` / `E02 其他-演示测试` → 分类
- `B 系列-xxx` 系列 → 标签（由 Series 字段自动生成）
- `F01 特性-多人合作` / `F02 特性-在线对战` / `F03 特性-本地同屏` → 标签（由 Features 自动生成）
- `S 平台-Steam` → 标签（自动）
- `A01 状态-正在玩` → 标签（仅已安装；完成状态「未游玩/已通关」的本地化由 `loc_map.json` 处理，与此无关）

## 名称唯一化算法 / Name dedup algorithm

1. 先算出每条的「期望名称」（中文名，或原名）
2. 版本类条目（Demo/Beta/测试服/分集）由 `version_labels.json` 给出带后缀的名字
3. 剩下的重复：第一个出现的加 `（来源）` 保持可辨识，后续逐一追加 `（来源）`，仍冲突则加序号
4. 输出前再跑一次唯一性断言，有任何重复直接报错

## 可回滚设计 / Rollback design

- 写元数据前把每条游戏的旧值写入 `meta_backup.tsv`（Id + 名称 + 日期 + 各类 ID 串），且多次应用也只保留「首次改动前」的原始值
- 来源/完成状态/平台的本地化改动记录在 `lookup_backup.tsv`（Type + Id + 原名）
- 菜单「撤销元数据改动」按 `meta_backup.tsv` + `lookup_backup.tsv` 一并还原
- 菜单「清除分类/标签」只删除 `import_result.json` 里记录过的分类/标签 ID
- 整库备份保留在 `backup/`（`export-playnite.ps1` 导出时自动复制）
