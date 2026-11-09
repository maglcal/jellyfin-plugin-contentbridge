# EMBY 兼容支持 · 分支纲领

> 分支：`v0.3.0.1`（分支测试版本）｜基线：main @ `56ba1ef`（v0.3.0 发布后）
> 目标一句话：**同一套核心逻辑，同时产出 Jellyfin 与 Emby 两套插件包；contentbridge-feed/1 标准不变，采集侧零改动。**

## 1. 版本与命名（按《统一版本命名规则》）

| 项 | 值 | 依据 |
|---|---|---|
| 分支名 | `v0.3.0.1` | 分支测试版本规则：当前主版本号 `v0.3.0` + 第 4 段分支序号 `1` |
| 测试产物命名 | `v0.3.0.1`、`v0.3.0.2`… | 分支测试产物不带 `main-` 前缀 |
| 合并递增 | 本分支属"大内容"（新增宿主兼容）→ 合并回 main 时升**次版本号** `v0.4.0` | "大内容升第二位" |
| 正式发布标识 | `main-v0.4.0`（tag / Release / 产物） | 合并并验证通过后才走发版三步 |

拆解：主版本号 `0`、次版本号 `3`、修订号 `0` = 当前主线正式版 v0.3.0；第 4 位 `1` = 分支序号，纯三段是版本号本体、四段专属分支测试线，两条线不混用。

## 2. 功能范围

### 做（范围内）

1. **宿主兼容层**：插件生命周期、配置序列化、HTTP API 路由、计划任务注册、日志——Jellyfin 与 Emby 的差异全部收敛到 compat 层，核心零分叉。
2. **功能完全对齐**（Emby 端不减功能）：
   - 数据源接入：MaccCMS 风格内容接口 + contentbridge-feed/1（单文件/目录/URL 三形态）
   - 同步编排：全量 / 增量 + 墓碑、变更戳跳过、每次同步条数上限、清理已下架条目（delta 硬保护保留）
   - 虚拟库写入：.strm + NFO + 海报，电影/剧集排布，名称清洗与集数解析（main 上已修）
   - 本地代理：UA/Referer 注入、HLS 列表改写、Range 透传、代理 key 防护
   - 计划任务：间隔触发 + 配置页内嵌调度设置
3. **双产物构建**：一份核心源码 → Jellyfin 包、Emby 包两个发布物。
4. **采集标准不动**：`contentbridge-feed/1` 是跨宿主中立契约，字段语义一行不改。

### 不做（范围外）

| 项 | 原因 |
|---|---|
| Jellyfin 侧栏菜单入口（`EnableInMainMenu`/`MenuIcon`） | Emby 无对应消费点；Emby 端走 插件 → 设置 常规入口 |
| Emby 插件商店上架 | Emby 无开放目录机制（对应 Jellyfin 的 repository.json）；交付手动安装 zip + 说明 |
| Emby 专有刮削器 / 元数据对接 | NFO 保持中立，由各媒体服务器自行刮削 |
| Windows/Mac 客户端直连差异 | 播放链路走标准 .strm + HTTP，客户端差异不属插件层 |

## 3. 与现有架构的对应关系

现有分层不动，只在"宿主 API 接触面"包一层：

| 现有组件 | 现状（Jellyfin 10.10/10.11） | Emby 4.x 差异点 | 兼容动作 |
|---|---|---|---|
| `Services/`（FeedClient / SyncService / StrmLibraryWriter / StreamProxyService） | 纯 .NET + 少量宿主接口 | 逻辑同构 | **零改动**，仍是单源 |
| `Tasks/SyncContentTask` + `TaskTriggerCompat` | 反射兼容 10.10 string / 10.11 enum 两种 `TaskTriggerInfo.Type` | Emby 的 Type 是 string 形态 | **现成兼容**——反射层天然覆盖，仅补注册入口 |
| `Api/ContentBridgeController` | ASP.NET Core `[Route]` 控制器 | 路由/宿主注册方式不同 | 路由面适配器化，DTO 不动 |
| `StreamController` + 代理 key 防护 | `[AllowAnonymous]` + key 校验 | 同上 | 随路由层适配 |
| `Plugin.cs`（`PluginPageInfo`、配置页注册） | `EnableInMainMenu`/`MenuIcon` 为 Jellyfin 专有 | 配置页机制同族但字段不同 | 注册代码分支化；web 端 `ApiClient` 调用差异封装进 configPage 小工具层 |
| `repository.json` / 插件目录 | Jellyfin 在线库机制 | 无此机制 | Emby 交付 zip 手动安装包，文档说明 |

## 4. 兼容方案整体思路（三条原则）

1. **核心与宿主解耦，差异收敛到 compat 层。**
   `Services/` 等纯逻辑保持单源；所有 `Jellyfin.*` / `MediaBrowser.*` 命名空间、宿主接口签名差异进 `Compat/`（或延续 `TaskTriggerCompat` 的反射模式——它已经证明了同一招能吃下 10.10/10.11 的类型分裂，Emby 只是再多一种形态）。反射优先于条件编译：一套 IL 能跑的就不产第二套。

2. **构建矩阵出双包，版本线不混。**
   csproj 多目标或薄壳 csproj 引用同一套源文件，产出 `Jellyfin.Plugin.ContentBridge.dll`（Jellyfin）与 Emby 插件 dll 两个包。分支测试期产物叫 `v0.3.0.x`；合并后升 `v0.4.0`、按 `main-v0.4.0` 正式发。

3. **验证双轨：核心回归不变量 + 宿主冒烟清单。**
   cbtest 47 条回归是核心逻辑的不变量（两宿主共用）；Emby 侧补手动冒烟：安装加载 → 配置数据源 → 立即同步 → 媒体库出条目 → 点播 → 代理链路。任何宿主适配改动不得让 cbtest 变红。

## 5. 里程碑（建议切分）

1. **M1 兼容层骨架**：`Compat/` 抽象（插件注册 / 配置存取 / 任务注册 / 路由宿主），Jellyfin 现状回归绿。
2. **M2 Emby 双包构建**：Emby 引用面接入，两套插件包可编译产出。
3. **M3 Emby 冒烟**：Emby 测试服务器上全功能走通（重点：计划任务注册、代理播放）。
4. **M4 收尾**：安装文档、csproj 版本字段对齐（当前 Assembly/FileVersion 停在 0.2.0.0，合并前统一）、cbtest 补 compat 层用例。

## 6. 风险与预案

| 风险 | 预案 |
|---|---|
| Emby 闭源、API 文档少，接口面以反编译/实测为准 | compat 层全部走反射 + 运行时探测，签名不符时降级（如任务注册失败→文档引导手动建任务） |
| Emby 版本碎片（4.7/4.8） | 以 4.8 为基准，兼容性问题按版本记录在本文件 |
| 双包行为漂移 | cbtest 共用 + 同一 feed 样本双宿主比对 .strm/NFO 输出逐字节一致 |

---

*很庆幸曾经遇见你，遗憾的是只是遇见你——许诺*
