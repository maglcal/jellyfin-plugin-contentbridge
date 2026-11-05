# ContentBridge Feed 规范 v1（自定义采集器输出标准）

本规范定义采集程序产出数据的格式。只要按这个格式落数据，ContentBridge 插件就能解析、入库、生成海报墙和播放条目。**采集侧只需产出数据，不需要提供任何 HTTP 服务。**

格式代号：`contentbridge-feed/1`

## 1. 数据形态（三选一）

| 形态 | 说明 |
|---|---|
| 单个 JSON 文件 | 采集程序输出一个 `.json` 文件 |
| JSON 文件目录 | 采集程序按批次/分类输出多个 `.json` 文件到同一目录（插件只扫一层，不递归） |
| HTTP URL | 以上任一种托管成可访问的 URL（本地路径直接写路径，不用 URL） |

- 编码一律 **UTF-8**（带 BOM 也接受）。
- 目录多文件合并规则：按文件名排序依次加载，**同 id 后加载的覆盖先加载的**（确定性行为，方便增量更新）。

## 2. 文档结构

```json
{
  "schema": "contentbridge-feed/1",
  "updated": "2026-11-05T12:00:00Z",
  "items": [ ... ]
}
```

| 字段 | 必填 | 说明 |
|---|---|---|
| schema | ✅ | 固定 `contentbridge-feed/1`，不匹配的文档会被拒绝 |
| updated | | 数据时间戳，ISO 8601，仅展示用 |
| items | ✅ | 条目数组，可以为空 |

## 3. 条目字段（items[]）

```json
{
  "id": "my-movie-0001",
  "title": "片名",
  "year": "2025",
  "type": "movie",
  "poster": "https://img.example.com/poster.jpg",
  "summary": "简介，纯文本或 HTML 均可（HTML 会被剥标签）",
  "category": "动作片",
  "area": "美国",
  "lang": "英语",
  "actors": ["演员甲", "演员乙"],
  "directors": ["导演甲"],
  "score": 7.8,
  "remarks": "更新至 12 集",
  "playLines": [
    {
      "name": "m3u8",
      "episodes": [
        { "name": "第1集", "url": "https://cdn.example.com/ep1.m3u8", "number": 1 },
        { "name": "第2集", "url": "https://cdn.example.com/ep2.m3u8", "number": 2 }
      ]
    }
  ]
}
```

| 字段 | 必填 | 说明 |
|---|---|---|
| id | ✅ | 全源唯一且**跨同步稳定**（清理已下架条目靠它对账）。没 id 的条目会被跳过 |
| title | ✅ | 标题 |
| year | | 年份，数字或字符串都行 |
| type | | `movie` / `series`。省略则自动判断：1 集→电影，多集→剧集。`series` 可强制单集也按剧集排布 |
| poster | | 海报图 URL |
| summary | | 简介 |
| category / area / lang | | 分类 / 地区 / 语言（NFO 的 genre/country 字段） |
| actors / directors | | 数组或单个字符串均可 |
| score | | 评分，数字或字符串 |
| remarks | | 备注（如"更新至 12 集"） |
| playLines | ✅ | 播放线路数组，至少 1 条且含至少 1 个分集 |

### playLines[] / episodes[]

| 字段 | 必填 | 说明 |
|---|---|---|
| name（线路） | | 线路名（如 `m3u8`/`4K`），配合插件的「播放线路偏好」做线路选择 |
| name（分集） | | 分集显示名（如 `第1集` / `正片`） |
| url | ✅ | 播放地址，http(s) 直链（m3u8/mp4 均可） |
| number | | 分集序号（整数）。给了就用它命名 `S01E02`；不给则从 name 解析（`第N集`/`EP N`/数字），解析不到按顺序编号 |

## 4. 采集侧行为约定

1. **id 稳定**：同一部片子每次产出保持同一个 id，否则清理和增量更新会对不上。
2. **url 失效**：链接有时效就定期重新产出，插件重新同步时会覆盖旧 .strm。
3. **请求头校验的地址**（要 Referer/UA 才能播）：照常写进 url，在插件数据源里开「走本地代理」并填 UA/Referer，插件代理会带上。
4. **下架**：不再产出的条目，开启插件的「清理已下架条目」后会自动删除对应文件（按 id 对账）。
5. **增量**：允许只产出变化的条目（目录形态下新文件覆盖旧文件即可）；但开启清理时，每个文档里的 items 必须是**全量**——清理是拿整个 Feed 的 id 集合对账的。

## 5. 最小可用示例

```json
{
  "schema": "contentbridge-feed/1",
  "items": [
    {
      "id": "demo-001",
      "title": "示例电影",
      "playLines": [
        { "episodes": [ { "name": "正片", "url": "https://cdn.example.com/demo.mp4" } ] }
      ]
    }
  ]
}
```

## 6. 校验清单（采集程序自测）

- [ ] JSON 合法，UTF-8 编码
- [ ] 顶层 `schema` = `contentbridge-feed/1`
- [ ] 每条有 `id` + `title` + 至少一条含 url 的分集
- [ ] id 跨次运行不变
- [ ] url 在 Jellyfin 所在网络可达

## 7. FAQ

**Q：数据量大怎么办？**
目录形态分文件放（如按分类一个文件），插件按文件名顺序合并。单文档不建议超过几千条。

**Q：能给字幕/多版本吗？**
v1 不支持外挂字幕字段；多版本靠多条 playLines（线路）表达，插件按「播放线路偏好」选。

**Q：标准会变吗？**
破坏性变更会换代号（`contentbridge-feed/2`），`/1` 内只做向后兼容的字段追加。
