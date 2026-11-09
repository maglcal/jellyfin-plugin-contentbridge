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

## 7. 实施进度

- **M1 兼容层骨架 ✅**（分支已并入 main 的双渠道更新检测，功能全对齐基线）：
  - `Compat/` 五件套落地：`HostCompat`（运行时宿主探测 + 反射工具 `TrySetOptionalProperty`）、
    `PluginPageCompat`（插件注册：配置页 PageInfo，MenuIcon/EnableInMainMenu 反射可选设置）、
    `ConfigHostCompat`（配置存取：Current/VersionString）、`TaskHostCompat`（任务注册：任务查找 + 调度读写，
    触发器差异仍由 TaskTriggerCompat 反射层消化）、`RouteHostCompat`（路由宿主：宿主无关端点处理器，
    Jellyfin MVC 控制器已薄壳化，DTO 共享至 `Models/ApiContracts.cs`）。
  - `Services/` 四个核心类零改动；web 端 ApiClient 差异封装点在 configPage 的 apiGet/apiPost/normalize 小工具层（已标注）。
  - 宿主探测经验：**Jellyfin 10.10.x 程序集名仍是 MediaBrowser.\***（NuGet 包才叫 Jellyfin.\*），照程序集名探不出宿主；
    可靠标记是 Jellyfin.Data/Jellyfin.Extensions（Jellyfin 独有）与 Emby.Server.Implementations/Emby.Naming（Emby 独有）。
  - cbtest 69/69 全绿（含 9 条 compat 用例）。
- **M2 双包构建 + Emby 绑定层 ✅**：
  - **Emby API 面实测钉死**（apidump 反射实测 4.8.11.0 / 4.10.2.0，落盘 `tools/apidump/out-*.txt`）：
    服务路由 = ServiceStack 风格（请求 DTO 挂 `MediaBrowser.Model.Services.RouteAttribute(path, verbs)` + `IReturn<T>`，服务类实现 `IService`（`IRequiresRequest` 拿 `IRequest`）；
    `IScheduledTask.Execute(CancellationToken, IProgress<double>)`（与 Jellyfin 的 `ExecuteAsync` 签名漂移）；`IScheduledTaskWorker.Triggers` 为 `TaskTriggerInfo[]` 数组 + `ReloadTriggerEvents()`；
    `GetLocalApiUrl` 三套重载按形状反射解析；日志是 Emby 自家 `MediaBrowser.Model.Logging.ILogger`（**ME.Logging.Abstractions 不在 .NET 共享框架、Emby 包也不带**）；
    `BasePlugin<TConfiguration>` ctor 与 Jellyfin 完全同形（`IApplicationPaths, IXmlSerializer`）；404 抛 `MediaBrowser.Common.Extensions.ResourceNotFoundException`；
    ServiceStack.Text 序列化按属性名原样（PascalCase）→ 配置页 JS 无需大小写垫片。
  - **共享层三缝**（差异全部收敛，核心零分叉）：`Compat/ILogSink.cs`（自研日志缝，绕开 ME.Logging 缺席；
    Jellyfin 侧 `PluginServiceRegistrator` 工厂适配 ILoggerFactory，Emby 侧 `emby/EmbyLogSink.cs` 适配自家 ILogger）、
    `Compat/HostApiUrlCompat.cs`（GetLocalApiUrl 重载形状反射缝）、`Compat/ProxyExchange.cs`（代理交换对象缝，
    Stream 逻辑与宿主 HTTP 上下文解耦）；`Tasks/SyncTaskCore.cs` 任务核 + 每宿主薄壳（`Tasks/SyncContentTask.cs` / `emby/EmbyScheduledTask.cs`）。
  - **Emby 绑定层七件套**（`emby/`）：`Emby.Plugin.ContentBridge.csproj`（net6.0，共享 `..\**\*.cs` 源 + Exclude 清单，
    引用 `refs/emby/{4.8|4.10}/` MediaBrowser 三件 + System.IO.Pipelines（Private=false 绝不打包）；`-p:EmbyRefVersion=4.10` 切换验证）、
    `EmbyLogSink`、`ServiceRoot`（Emby 组合根：惰性构建 + `EnsureAttached` 幂等接线，服务/任务由宿主容器自动发现构造）、
    `EmbyScheduledTask`、`EmbyServiceContracts`（6 管理 DTO + 匿名流 DTO，字段名与 ApiContracts 一致）、
    `EmbyContentBridgeService`（六端点）、`EmbyStreamService`（直写 `IResponse` 保 206 透传）。
  - **构建矩阵三路全绿**：Jellyfin net8 Release 0 警告 0 错误；Emby 4.8 Release 0/0；Emby 4.10 验证构建 0/0
    （4.10 引用面的 System.IO.Pipelines 用 NuGet **lib/net6.0** 资产——net8 资产会 CS1705 System.Runtime 8>6）。
  - **cbtest 69/69 全绿**（双宿主不变量零回退；ProxyAsync 换 ProxyExchange 后 cbtest 走 `Api/HttpContextProxyExchange` 适配器，断言零改动）。
- **M3 双包产出 ✅**（构建级；真机冒烟并入 M4 测试审核清单）：
  - `tools/pack.py` 双包打包：`dist/contentbridge_0.3.0.1.zip`（Jellyfin DLL + meta.json + icon.png，md5=d66290da2b26c6d910573aebb961de0d）、
    `dist/contentbridge-emby_0.3.0.1.zip`（Emby DLL + INSTALL.txt，md5=f44e84face52c4b70c71b925990c6ab5）。
  - 版本基线统一 0.3.0.1（csproj AssemblyVersion/FileVersion 对齐）。
- **M4 收尾 ✅**：README 补 Emby 安装与版本适配；测试审核清单（docs/TEST-REVIEW.md）交用户；
  `.gitignore` 隔离 `refs/`（Emby 专有程序集绝不入公开仓库）、`dist/`、apidump 输出。
- **首轮真机测试修复（v0.3.0.2）**：
  - **设置页空白（linuxserver/emby 复现）**：Emby 的配置页机制与 Jellyfin 不同——Emby 把页面 HTML 注入仪表盘后
    **不执行内联 `<script>`**，页面逻辑必须是 `data-controller="__plugin/{name}"` 指向的**独立 AMD 模块**
    （`define(['baseView','loading',...])` 导出 View）。修复：新增 `Configuration/embyConfigPage.html`
    （`emby-scroller` 根 + data-controller）与 `Configuration/embyConfigPage.js`（BaseView 控制器，移植
    Jellyfin 页全部逻辑），`GetPages()` 按 `HostCompat.Detect()` 分支注册（Emby 注册 HTML 页 + js 控制器两页，
    Jellyfin 维持内联脚本页）。双宿主配置页机制不同是宿主差异的一部分，各配一套页面符合"差异收敛"原则。
  - **插件无图标**：Emby 的 "My Plugins" 缩略图自 3.6 起由插件经 **`IHasThumbImage`**（MediaBrowser.Common.dll）
    + 嵌入资源提供（Jellyfin 是 meta.json imagePath 机制，且无此接口）。修复：`emby/Plugin.Emby.cs`
    （partial，仅 Emby 编译）实现 `GetThumbImage()/ThumbImageFormat`，`Configuration/thumb.png`
    （640×360，16:9）以 LogicalName `Jellyfin.Plugin.ContentBridge.Configuration.thumb.png` 嵌入。
  - 版本升 **v0.3.0.2**（分支测试产物递增）；INSTALL.txt 补"容器内换 DLL 重启整个容器"提示
    （原位覆盖可能 BadImageFormatException）。cbtest 69/69、三路构建全绿。
- **第二轮真机测试修复（v0.3.0.3）**：
  - **官方版"同步外部源报错"**：错误语义重做。`SyncService.RunAsync` 改返回 `SyncRunResult`（成功/失败源计数 +
    逐源错误文本）；`Directory.CreateDirectory(LibraryRoot)` 包上带路径与原因的明确异常（"媒体库根目录不可用（路径）：
    原因……请检查是否为绝对路径/磁盘存在/进程有写权限"）；两版计划任务壳在有失败源时抛汇总错误
    （"N 个数据源同步失败：源: 原因；……"），不再假绿。`POST /ContentBridge/sync` 从 fire-and-forget 改为
    **等待完成并返回结果 JSON**，两版配置页「立即同步」直接回显成功/失败与错误原文（旧实现吞掉一切错误只提示"已启动"）。
  - **docker 版设置页仍空白**（v0.3.0.2 的 AMD 页未生效，根因未定——版本差异 / 浏览器或服务端缓存 / 旧 DLL 残留三线并查，
    详见 TEST-REVIEW.md 定位问题区）：加载链全面加固：
    ① 控制器改**零 AMD 依赖** `define([], ...)`——实证 4.8/4.10 的 viewmanager 用 `require([url]).then(...)` 加载控制器，
    任一依赖解析不了就永远不落 → 页面挂死；自定义元素模块（emby-input 等）改绑定期后 best-effort 异步加载（纯样式，缺了不影响功能）；
    ② 页面根同时带 `class="view"` 与 `data-role="page"`（两代 normalizeNewView 的选择器都命中），加稳定 id；
    ③ 内联引导双通道：旧宿主执行内联脚本时动态 `<script>` 加载同一核心（临时隐藏 define 走非 AMD 导出分支），
    新宿主走 data-controller AMD 分支，两通道共用一份 `bind(view)` 核心、`__cbInited` 防双绑；
    ④ 生命周期钩子显式空实现（4.8 无条件调用 onPause、4.10 对缺钩子的控制器自动包 BaseView）；
    ⑤ 全失败路径写页面状态行 + 顶部诊断提示（Ctrl+F5 指引；提示行可见=HTML 已加载，整页空白=资源没加载，二分定位）。
  - **web 机制考古实证**（refs/_downloads 官方包提取，4.8.11 netcore + 4.10.2 freebsd）：
    viewmanager 两代一致——`text!` 取页 HTML → `innerHTML`（内联脚本永不动行，无任何重执行路径）→
    `querySelector('.view,div[data-role="page"]')` 提根（不命中→空白卡住）→ `data-controller="__plugin/x"` →
    `require([getConfigurationResourceUrl(x)])` → `deps[0]` 为控制器工厂（reject→挂死）；
    `allowPluginPages` 的 restrictedplugins 白名单在浏览器 apphost 特性表中**不存在**（仅原生 App 生效）——白名单论排除；
    4.8/4.10 index.html importmap 均含 baseView/loading/emby-* 全套模块名。
  - 版本升 **v0.3.0.3**；cbtest 69/69、三路构建全绿。

---

*很庆幸曾经遇见你，遗憾的是只是遇见你——许诺*
