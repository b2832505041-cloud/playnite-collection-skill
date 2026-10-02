# 踩坑清单（都是实测踩过的）

## Playnite 数据库

**1. 运行中的 Playnite 独占锁定 games.db**
`Copy-Item` / `robocopy /B` / `FileShare.ReadWrite` 全部失败（`ERROR 32`）。
→ 唯一可靠做法：`Playnite.DesktopApp.exe --shutdown` 优雅退出后再复制。
Windows 上被删除但仍被占用的文件句柄会让"提示失败但文件还在"，必要时先结束残留进程再复制。

**2. 字段类型和文档不一致**
`Game.ReleaseDate` 在 LiteDB 里是 **string**，在 SDK 里是 `Playnite.SDK.Models.ReleaseDate?`（结构体）。
按 `DateTime` 读会得到全空——这就是"明明有发行年份却读不出来"的原因。
→ 读库时用 `BsonValue.Type` 判类型；写库用 SDK 的 `new ReleaseDate(DateTime)`。

**3. 关系字段是 ID 数组**
游戏的 Genres/Developers/Series/Categories/Tags 都存 ID，名称在各自的 `genres.db`、`companies.db` … 里。
导出时必须先把各表读成 `id → name` 映射再翻译。

## 插件

**4. 必须是 AnyCPU**
Playnite 是 **32 位进程**（`Playnite.DesktopApp.exe` PE machine = 0x014C）。
用 `/platform:x64` 编译 → `BadImageFormatException: 试图加载格式不正确的程序`，日志里只说"载入失败"。

**5. 生命周期：不要在构造函数里碰数据库**
插件的构造函数执行时 `PlayniteApi.Database.Games` 还是**空的**（库尚未加载），遍历得到 0 条。
→ 用 `OnApplicationStarted`（命名空间 `Playnite.SDK.Events`）。

**6. 按名字匹配会崩，按 ID 才稳**
改名之后再按名字匹配，全部失配（`unmatched=480`）。
→ 生成数据表时带上游戏 GUID，插件用 `Database.Games.Get(guid)` 精确写入。

**7. 静态字段初始化的地雷**
`Dictionary<string,string>`（大小写不敏感）的键重复 → 静态构造函数抛 `ArgumentException: 已添加了具有相同键的项`，
整个插件失效且日志只显示 `TypeInitializationException`。
→ 本地化对照表的键必须先做去重校验。

**8. WPF 引用**
插件用到 `System.Windows.MessageBoxButton`，需要 `WindowsBase / PresentationCore / PresentationFramework / System.Xaml`。
没有 .NET SDK 时从 GAC 解析：`C:\Windows\Microsoft.NET\assembly\GAC_MSIL\<name>\<ver>\<name>.dll`；
注意 `PresentationCore` 在 `GAC_32`。另外**别手动引用 `System.Windows.Forms.dll`**（csc 默认已引用，会 CS1703 重复）。

## 中文化

**9. 「续作劫持」是最常见的误配**
Steam 搜索 `Death Stranding` 会返回《死亡搁浅2》，`Frostpunk` 返回《冰汽时代2》。
→ 命中结果要用「原名 + 年份 + 类型」交叉校验；中文名里出现英文原名没有的续作数字时，一律人工确认。

**10. 官方名带商标和版本尾巴**
`The Sims™ 4`、`Titanfall® 2`、`Life is Strange - Episode 1`。
→ 清洗 `™®©`，并只在英文尾巴确实是原名开头时才裁掉（否则 `杀戮空间2 Beta` 会被裁成 `杀戮空间2`）。

**11. 重名必须显式处理**
把 5 集 `3 out of 10` 全译成「十分之三」会得到 5 个同名条目。
→ 规则：同作品不同版本加版本后缀；跨平台同游戏加（来源）；最后统一做一次唯一性校验。

**12. 本地化来源/完成状态会反弹**
`Sources` 改名成中文后，插件下一轮按英文来源匹配就会失败。
→ 匹配时同时接受原名与译名（`Tr(SourceLabel(g))`）。

## 其他

**13. PowerShell 5.1 读 UTF-8 脚本**
无 BOM 的 UTF-8 `.ps1` 含中文时会被按 ANSI 解码，可能破坏字符串导致语法错误。
→ 生成 `.ps1` 时写入 UTF-8 **BOM**。

**14. Steam 商店接口限流**
高频请求会 429。并发 4~6、带退避重试即可稳定跑完几百条。
