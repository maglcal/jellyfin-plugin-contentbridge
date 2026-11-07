# 采集程序开发指南

> 不会写程序也能看懂的版本。目标：把你自己维护的内容数据（片单、播放地址、元数据）变成 Jellyfin 媒体库里的条目。

本指南是 [FEED-SPEC.md](../FEED-SPEC.md) 的**上手教程**：规范管"格式是什么"，这份管"怎么一步步做出来"。

## 0. 先别急着写代码：三条路线

| 路线 | 适合谁 | 要写代码吗 |
|---|---|---|
| A. 直接用现成内容接口 | 数据源是苹果CMS / 海螺风格接口 | ❌ 不用。插件内置支持，填个地址就行 |
| B. 手写 JSON | 条目少（几十条以内）、变化慢 | ❌ 不用。照 FEED-SPEC 格式手写一个 `.json` 文件 |
| C. 写采集程序 | 数据源是网页/接口，条目多、常更新 | ✅ 本文重点，模板抄去改改就能跑 |

**判断标准**：如果你每月动手改数据的次数少于 5 次，选 B；再多就选 C，一次投入终身省事。

## 1. 采集程序是什么（30 秒版）

采集程序就是一个**小脚本**：定期运行，把数据整理成一个 JSON 文件。没有服务、没有数据库、不用一直挂着——跑完就退出。

```
你的数据源（网页 / 接口 / 表格）
        │  ① 拉数据
        ▼
   采集程序（一个小脚本）
        │  ② 整理成标准格式
        ▼
   feed.json（contentbridge-feed/1）
        │  ③ 插件定时同步
        ▼
   Jellyfin 媒体库（.strm + NFO + 海报）
```

你只负责 ①②，③ 是插件的事。**不需要**提供 HTTP 服务、不需要懂 Jellyfin 内部原理。

## 2. 环境准备（只需一次）

1. 装 **Python 3**：https://www.python.org/downloads/ （Windows 装的时候勾选 "Add to PATH"）
2. 验证：命令行运行 `python --version`，出版本号就行
3. 本模板**只用标准库**，不用装任何第三方包

## 3. 第一个采集程序

仓库里有现成模板：[samples/crawler-template.py](../samples/crawler-template.py)。
它开箱可跑（自带演示数据），跑完会生成一个合法的 feed 文件。下面逐段讲解它在干什么——**你只需要改配置区**。

### 3.1 配置区（只需要改这里）

```python
FEED_OUT   = "feed-output/my-feed.json"   # 输出的 feed 文件放哪
SOURCE_API = ""                           # 你的数据源接口地址（留空 = 用内置演示数据）
CRAWLER_NAME    = "my-crawler"            # 你的采集程序名（会写进 feed 的 generator 字段）
CRAWLER_VERSION = "1.0.0"
DELTA_MODE  = False                       # True = 增量模式（见 §4）
STATE_FILE  = "crawler-state.json"        # 增量模式的状态记忆文件
```

### 3.2 拉数据：拿到"原始条目"

模板演示了最常见的一种：数据源是一个返回 JSON 列表的接口。

```python
def fetch_raw_items():
    if not SOURCE_API:
        return DEMO_ITEMS            # 没配接口就用演示数据，先跑通流程
    req = urllib.request.Request(SOURCE_API, headers={"User-Agent": "my-crawler/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    return data["items"]             # ← 按你数据源的实际结构取列表
```

如果你的数据源是网页而不是接口，就把这一步换成"下载网页 → 从 HTML 里抠数据"。
抠 HTML 推荐装 `requests` + `beautifulsoup4`（`pip install requests beautifulsoup4`），几十行就够。数据源格式五花八门，这一步没法模板化——它是采集程序里唯一"因源而异"的部分。

### 3.3 映射字段：翻译成标准格式

数据源的字段名（`name`/`vod_name`/`title`…）随你叫什么，**统一翻译成 FEED-SPEC 的字段**：

```python
def map_item(raw):
    return {
        "id": str(raw["id"]),                # 全源唯一、每次运行不变（最重要！）
        "title": raw["name"],
        "year": raw.get("year", ""),
        "type": "movie",                     # movie / series，不确定就省略
        "poster": raw.get("cover", ""),
        "summary": raw.get("intro", ""),
        "score": raw.get("score", ""),
        "updated": raw.get("updated", ""),   # 条目级变更时间戳（见 §4）
        "playLines": [
            {
                "name": "m3u8",
                "episodes": [
                    {"name": "正片", "url": raw["play_url"]}
                ],
            }
        ],
    }
```

字段全部含义见 [FEED-SPEC.md §3](../FEED-SPEC.md)。**playLines 是必填的**——没有可播放地址的条目会被插件跳过。

### 3.4 写文件：产出 feed

```python
feed = {
    "schema": "contentbridge-feed/1",        # 照抄，错了插件不认
    "generator": f"{CRAWLER_NAME}/{CRAWLER_VERSION}",
    "updated": datetime.now(timezone.utc).isoformat(),
    "items": items,
}
os.makedirs(os.path.dirname(FEED_OUT), exist_ok=True)
with open(FEED_OUT, "w", encoding="utf-8") as f:
    json.dump(feed, f, ensure_ascii=False, indent=2)
```

### 3.5 运行

```bash
python crawler-template.py
```

输出 `feed-output/my-feed.json`，再在插件配置页把数据源的「接口地址」填这个路径（或它的目录/URL），点「立即同步」就能在媒体库里看到条目。**先跑通这一条链，再往下加功能。**

## 4. 更新机制：让同步变聪明

数据会变：新片上线、集数更新、旧片下架。三种打法对应 FEED-SPEC §5，这里讲怎么落到代码上。

### 4.1 变化检测：`updated` 时间戳

每条数据给一个**变更戳**（源数据自带 `updated` 就用它；没有就用内容算一个，模板里有示例）。
规则很简单：**内容变了才动时间戳**。插件记住每条的时间戳，没变的条目直接跳过，不重写文件——同步日志里的 `unchanged` 就是这么来的。

### 4.2 增量模式（DELTA_MODE = True）

条目几百上千、每次只有几条变化时用。feed 里**只放变化的条目**：

```json
{
  "schema": "contentbridge-feed/1",
  "mode": "delta",
  "items": [
    { "id": "new-001", "title": "新片", "updated": "2026-11-07T02:50:00Z", "playLines": [ ... ] },
    { "id": "old-002", "deleted": true }
  ]
}
```

模板的增量逻辑就三步（`build_items()` 里）：

1. 读上次的状态（`crawler-state.json`，记录 id → 变更戳）；
2. 新数据里变更戳变了的 → 输出条目；新出现的 → 输出条目；
3. 上次有、这次没了 → 输出**墓碑** `{"id": "...", "deleted": true}`，插件删除对应文件。

注意：

- **delta 源别开「清理已下架条目」**——插件有硬保护会自动忽略，但语义上要知道：清理是全量对账专用的。
- 墓碑永远生效，不受「每次同步条数」限制。
- 状态文件丢了也别慌：删掉它重新全量跑一次即可（`DELTA_MODE = False` 跑一遍，再切回来）。

### 4.3 版本声明（可选但推荐）

feed 头部的 `generator` / `version` 字段照实填（模板自动写入）。它们不参与同步逻辑，纯粹是"这批数据是谁、哪一版产出的"，出问题对账用。

## 5. 定时运行

采集程序自己定时（插件那边另有 6 小时一次的同步任务，两层配合：采集更新数据，插件更新媒体库）。

**Windows（任务计划程序）**：

```powershell
schtasks /create /tn "ContentBridge-Crawler" /tr "python C:\crawler\crawler-template.py" /sc hourly /mo 6
```

**Linux / NAS（cron）**：

```bash
# crontab -e，每 6 小时跑一次
0 */6 * * * /usr/bin/python3 /volume1/crawler/crawler-template.py >> /volume1/crawler/crawler.log 2>&1
```

## 6. 三种对接方式怎么选

| 形态 | 做法 | 适合 |
|---|---|---|
| 单文件 | 采集程序写一个 `.json`，插件填文件路径 | 绝大多数场景（首选） |
| 目录 | 按分类/批次写多个 `.json` 到同一目录，插件填目录路径 | 条目多，想分文件管理 |
| URL | 文件/目录托管成 HTTP 可访问（NAS 的 Web 目录、对象存储） | 采集程序和 Jellyfin 不在同一台机器 |

同机部署优先用**文件路径**，少一层网络依赖。多文件目录注意：按文件名顺序合并（`01-`、`02-` 这样编号），同 id 后者覆盖前者。

## 7. 常见坑（踩过的都在这）

| 坑 | 症状 | 解法 |
|---|---|---|
| 编码不是 UTF-8 | 中文乱码、JSON 解析报错 | 所有文件读写显式 `encoding="utf-8"` |
| id 不稳定（每次运行都变） | 清理/墓碑对不上，条目重复入库 | id 用数据源的固定主键，别用随机数/时间戳 |
| url 有时效 | 几天后播放 403/404 | 定期重跑采集更新 url；或在数据源开「走本地代理」 |
| 播放要 Referer/UA | Jellyfin 直接播报错 | 不用改 url，插件数据源开「走本地代理」填请求头 |
| delta 源开了清理 | 正常条目被批量误删（不会发生：插件有硬保护） | 知道这层语义即可，插件自动忽略 |
| JSON 逗号/引号手写出错 | 同步日志报解析失败 | 用 Python 的 `json.dump` 写文件，别手拼字符串 |
| 分集编号乱 | `S01E01` 排序错乱 | episodes 里给 `number` 整数字段 |

## 8. 自测清单

照 [FEED-SPEC.md §7](../FEED-SPEC.md) 的清单过一遍，另外确认：

- [ ] 模板演示模式跑通（`python crawler-template.py` 出文件）
- [ ] 换上真实数据源后，抽查 3 条的 id / title / url 正确
- [ ] 连续跑两次，第二次的 `updated` 没变化（不会触发无谓重写）
- [ ] 删掉一条数据再跑，feed 里出现它的墓碑（增量模式）或它从清单消失（全量模式）
- [ ] 插件「立即同步」后媒体库条目正确，Jellyfin 日志无报错

## 9. FAQ

**Q：采集程序会被封吗？要爬多少数据？**
采集程序访问的是你自己的数据源/授权接口，拉取频率自己控制（模板默认不并发、单线程）。对自己的数据源友好点：加 `timeout`、失败就跳过别死循环。

**Q：能多进程/并发跑吗？**
几百条的规模完全没必要，单线程足够。真要提速优先"只拉有变化的分类"，而不是开线程。

**Q：数据源要登录/Cookie 怎么办？**
`fetch_raw_items()` 里把 Cookie 放进请求头即可：`headers={"User-Agent": "...", "Cookie": "..."}`。注意 Cookie 会过期，过期的表现是突然拿不到数据——加个"返回为空就报警告日志"的判断好排查。

**Q：规范以后会升级吗？**
破坏性变更会换代号（`contentbridge-feed/2`）；`/1` 内只做向后兼容的字段追加，老采集程序不用改。

**Q：还是看不太懂？**
从路线 B 开始：照 FEED-SPEC §6 的最小示例手写一个 2 条目的 JSON，插件同步成功、媒体库出现条目——建立体感后再回来写程序，会顺很多。
