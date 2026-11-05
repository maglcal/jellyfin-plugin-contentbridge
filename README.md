# Jellyfin.Plugin.ContentBridge

把外部内容接口的条目**注入 Jellyfin 媒体库**的插件——fnmedia-ext 思路的 Jellyfin 实现。
条目以 `.strm` 虚拟条目 + NFO + 海报的形式落到媒体库目录，Jellyfin 原生刮削、搜索、播放，
播放走外部链路不占盘，插件版本升级免疫（不碰内核 API）。

**状态**：已在真实 Jellyfin 环境实测（同步入库、刮削、播放均正常）。

## 工作原理

```
内容接口（api.php/provide/vod/ 兼容）
   │  同步任务（默认 6 小时）/ 配置页"立即同步" / 搜索一键入库
   ▼
{媒体库根目录}/{数据源名}/{片名 (年份)}/
   ├── 片名 (年份).strm        ← 电影：一行播放地址
   ├── movie.nfo / poster.jpg
   └── 剧名 (年份)/            ← 剧集
        ├── tvshow.nfo / poster.jpg
        └── Season 01/剧名 - S01E01.strm ...
   ▼
Jellyfin 混合媒体库（Movies + Shows）→ 海报墙 / 搜索 / 全客户端播放
```

## 安装

### 方式一：插件存储库安装（推荐）

1. Jellyfin 控制台 → 插件 → 存储库 → 添加，填入：

   ```
   https://raw.githubusercontent.com/maglcal/jellyfin-plugin-contentbridge/main/repository.json
   ```

2. 插件 → 目录 → 找到 ContentBridge → 安装，重启 Jellyfin。

后续发新版本时目录里会直接提示更新，不用手动换 DLL。

### 方式二：手动安装

1. 从 [Releases](https://github.com/maglcal/jellyfin-plugin-contentbridge/releases) 下载 `contentbridge_x.y.z.zip`（或直接拿 `Jellyfin.Plugin.ContentBridge.dll`）：

   ```bash
   dotnet build -c Release
   ```

2. 把 `Jellyfin.Plugin.ContentBridge.dll` 拷到 Jellyfin 插件目录下的 `ContentBridge/` 子目录：
   - Docker：`<config>/plugins/ContentBridge/`
   - Windows 安装版：`%ProgramData%\Jellyfin\Server\plugins\ContentBridge\`
   - Linux 包安装：`/var/lib/jellyfin/plugins/ContentBridge/`

3. 重启 Jellyfin，在 控制台 → 插件 里能看到 ContentBridge。

## 配置

1. **媒体库根目录**：生成文件的目录。保存后，在 控制台 → 媒体库 添加该目录，类型选「混合电影与剧集」。
   开启实时监控的话，入库的条目几秒内自动出现。
2. **数据源**：填接口地址（形如 `https://host/api.php/provide/vod/`），可配分类 ID 过滤和每次同步条数。
3. **播放线路偏好**：多条线路时优先匹配关键词（如 `m3u8`）。
4. **搜索并入库**：配置页底部按关键词搜数据源，点「入库」立刻生成条目（免等同步）。

同步进度看 控制台 → 计划任务 → 同步外部内容源（默认每 6 小时，可改）。

## 接口协议

两种数据源类型（数据源配置里选）：

**1. 自定义 Feed（自己写采集程序推荐）**——按 [FEED-SPEC.md](FEED-SPEC.md) 规范产出 JSON 文件/目录/URL 即可，
插件负责解析入库。采集程序只管落数据，不用提供任何 HTTP 服务。规范含字段表、合并规则、
自测清单和[样例](samples/feed-sample.json)。

**2. 标准接口**——适配 **MaccCMS（苹果CMS）/ 海螺** 系 provide vod 接口：

- 列表：`?ac=detail&at=json&pg=1&t=分类ID`（JSON 优先，XML 自动兜底）
- 搜索：`?ac=detail&at=json&wd=关键词`
- 详情：`?ac=detail&at=json&ids=ID`
- 播放地址：`vod_play_from` / `vod_play_url`，线路用 `$$$` 分隔、分集用 `#`、`名称$地址` 成对

其他格式的接口包一层转换即可，协议解析都在 `Services/ContentApiClient.cs`。

## 版本适配

默认目标 **Jellyfin 10.11.x（.NET 9）**。适配 10.10 只改 csproj 两处：

```xml
<TargetFramework>net8.0</TargetFramework>
<PackageReference Include="Jellyfin.Controller" Version="10.10.7" />
<PackageReference Include="Jellyfin.Common" Version="10.10.7" />
```

另注意：10.10 及以下计划任务触发器用 `TaskTriggerInfo.TriggerInterval`（字符串常量），
10.11 用 `TaskTriggerInfoType.IntervalTrigger`（枚举），改一下 `Tasks/SyncContentTask.cs` 一行。

## 已知限制

- ~~播放地址需要可直连~~ **v0.2 已解决**：数据源开启「走本地代理」后，.strm 走本地代理端点，自动带源的 User-Agent/Referer 请求头；HLS（m3u8）会改写播放列表，分片/密钥同样走代理。开启代理的源改配置后需重新同步刷新 .strm。
- 代理链接的对外地址默认取局域网地址；用域名/公网访问 Jellyfin 的，在配置页填「代理基础地址」。
- 链接有时效的源靠定期同步刷新；已入库条目地址失效时重新同步即可覆盖。
- 分集编号从分集名解析（`第N集` / `EP N` / 数字），解析不到按顺序编号。
- 「清理已下架条目」默认关闭；开启后才删除源上已不存在的条目文件。

## 目录结构

```
Jellyfin.Plugin.ContentBridge/
├── Plugin.cs                    # 插件入口 + 配置页注册
├── PluginServiceRegistrator.cs  # DI 注册（同步服务 / 计划任务）
├── Configuration/
│   ├── PluginConfiguration.cs   # 配置项 + 数据源模型
│   └── configPage.html          # 配置页（数据源编辑 / 搜索入库 / 立即同步）
├── Models/Vod.cs                # 内容接口数据模型
├── Services/
│   ├── ContentApiClient.cs      # 接口协议解析（JSON / XML）
│   ├── StrmLibraryWriter.cs     # .strm / NFO / 海报写入
│   └── SyncService.cs           # 同步编排 + 清单管理
├── Tasks/SyncContentTask.cs     # 计划任务
└── Api/ContentBridgeController.cs  # search / add / sync 接口
```

## License

[GPL-2.0](LICENSE) © 2026 丶许诺 (maglcal)

与 Jellyfin 本体的 GPL-2.0 许可保持一致，可自由使用、修改、分发，衍生作品需同样以 GPL-2.0 发布。
