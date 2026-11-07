#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ContentBridge 采集程序模板（contentbridge-feed/1）

开箱可跑：python crawler-template.py
  → 用内置演示数据生成 feed-output/my-feed.json，可直接配到插件里试同步。

接真实数据源：改下面【配置区】和 fetch_raw_items() 即可，其余不用动。
字段映射自带兜底（name/title/vod_name 都认），播放地址支持三种形态：
完整直链 / 相对地址（自动拼 BASE_URL）/ 复合播放串（名称$url#...$$$...）。
只依赖 Python 标准库，不需要 pip install 任何东西。

字段规范见仓库 FEED-SPEC.md，手把手教程见 docs/CRAWLER-GUIDE.md。
"""

import hashlib
import json
import os
import re
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timezone

# ============================ 配置区（只需要改这里） ============================

FEED_OUT = os.path.join("feed-output", "my-feed.json")  # 输出的 feed 文件
SOURCE_API = ""            # 你的数据源接口地址；留空 = 用内置演示数据先跑通流程
BASE_URL = ""              # 播放地址是相对路径时的拼接基地址；留空 = 用 SOURCE_API 的地址
CRAWLER_NAME = "my-crawler"
CRAWLER_VERSION = "1.1.0"
DELTA_MODE = False         # True = 增量模式：只输出变化条目 + 下架墓碑（详见指南 §4.2）
STATE_FILE = "crawler-state.json"   # 增量模式的状态记忆（丢了就删掉重跑，不值钱）

# ============================ 演示数据（接真实源后可删） ============================
# 演示地址用的是 W3C 公开测试视频（长期可播），跑通流程后换成你自己数据源的地址。

DEMO_ITEMS = [
    {
        "id": "demo-001",
        "name": "示例电影",
        "year": "2025",
        "cover": "https://picsum.photos/seed/cbdemo1/300/450",
        "intro": "一部用来演示采集程序输出的电影。",
        "score": "8.1",
        "updated": "2026-11-07T02:00:00Z",
        "play_url": "https://media.w3.org/2010/05/sintel/trailer.mp4",
    },
    {
        "id": "demo-002",
        "name": "示例剧集",
        "year": "2026",
        "cover": "https://picsum.photos/seed/cbdemo2/300/450",
        "intro": "演示剧集，两集。",
        "score": "7.5",
        "updated": "2026-11-07T02:30:00Z",
        "episodes": [
            {"name": "第1集", "url": "https://media.w3.org/2010/05/bunny/trailer.mp4", "number": 1},
            {"name": "第2集", "url": "https://media.w3.org/2010/05/video/movie_300.mp4", "number": 2},
        ],
    },
]


# ============================ ① 拉数据 ============================

def fetch_raw_items():
    """从数据源拉原始条目列表。接真实源只改这一个函数。"""
    if not SOURCE_API:
        print("未配置 SOURCE_API，使用内置演示数据")
        return DEMO_ITEMS

    req = urllib.request.Request(
        SOURCE_API,
        headers={"User-Agent": f"{CRAWLER_NAME}/{CRAWLER_VERSION}"},
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        data = json.loads(resp.read().decode("utf-8"))

    # ↓ 按你数据源的实际结构取列表，比如 data["data"]["list"]
    return data["items"]


# ============================ ② 映射成标准格式 ============================

def pick(raw, *keys, default=""):
    """按顺序取第一个非空字段。数据源字段名五花八门（name/title/vod_name…），
    把候选都列出来，字段名对不上也不会取错或报 KeyError。"""
    for key in keys:
        value = raw.get(key)
        if value is not None and str(value).strip():
            return str(value).strip()
    return default


def to_int(value):
    """数字字段容错：int、数字字符串都收（"3" -> 3），其余返回 None。"""
    if value is None or isinstance(value, bool):
        return None
    try:
        return int(str(value).strip())
    except ValueError:
        return None


def absolute_url(url):
    """播放地址规范化：完整 http(s) 直链原样返回；相对路径按 BASE_URL 拼成完整地址；
    拼不出来 / 不是 http(s) 的返回空串（宁可跳过，也不往 .strm 里写播不了的地址）。"""
    url = (url or "").strip()
    if not url:
        return ""
    if re.match(r"^https?://", url, re.I):
        return url
    base = (BASE_URL or SOURCE_API or "").strip()
    if not base:
        print(f"  警告：相对地址 {url!r} 没有 BASE_URL/SOURCE_API 可拼接，已跳过")
        return ""
    joined = urllib.parse.urljoin(base, url)
    return joined if re.match(r"^https?://", joined, re.I) else ""


def parse_play_lines(play_value, from_value=""):
    """解析播放数据，三种形态通吃，返回 contentbridge-feed/1 的 playLines：
    ① 单条直链        https://cdn.x.com/index.m3u8
    ② 相对地址        /upload/index.m3u8（自动拼 BASE_URL）
    ③ 复合播放串      分集名$url#分集名$url$$$线路2#分集名$url
                      （苹果CMS vod_play_url 风格：$ 分名址、# 分集、$$$ 分线路）
    线路名取 vod_play_from（$$$ 分隔）；解析不出任何可播地址时返回 []。
    """
    play_value = (play_value or "").strip()
    if not play_value:
        return []

    blocks = play_value.split("$$$")
    line_names = [f.strip() for f in (from_value or "").split("$$$")]
    lines = []

    for index, block in enumerate(blocks):
        name = line_names[index] if index < len(line_names) and line_names[index] else f"线路{index + 1}"
        episodes = []
        for part in block.split("#"):
            part = part.strip()
            if not part:
                continue
            if "$" in part:
                ep_name, ep_url = part.split("$", 1)
            else:
                ep_name, ep_url = "", part
            url = absolute_url(ep_url)
            if not url:
                continue
            ep_name = ep_name.strip()
            if not ep_name:
                ep_name = f"第{len(episodes) + 1}集" if ("#" in block or "$" in block) else "正片"
            episodes.append({"name": ep_name, "url": url})
        if episodes:
            lines.append({"name": name, "episodes": episodes})

    return lines


def change_stamp(raw, mapped):
    """条目级变更戳：源自带更新时间就用它；没有就对内容做指纹。
    规则：内容变了戳才变，插件靠它跳过没变化的条目。"""
    source_stamp = pick(raw, "updated", "vod_time", "update_time")
    if source_stamp:
        return source_stamp
    fingerprint = json.dumps(mapped, ensure_ascii=False, sort_keys=True)
    return hashlib.md5(fingerprint.encode("utf-8")).hexdigest()


def map_item(raw):
    """把数据源的字段翻译成 contentbridge-feed/1 的条目。
    字段含义见 FEED-SPEC.md §3。字段名对不上时改 pick() 里的候选列表即可。
    支持两种输入形态：结构化 episodes 列表，或单条/复合播放串（vod_play_url）。"""
    item_id = pick(raw, "id", "vod_id")
    title = pick(raw, "title", "name", "vod_name", "vod_title")
    if not item_id or not title:
        print(f"  警告：条目缺 id 或标题（id={item_id!r} title={title!r}），已跳过")
        return None

    item = {
        "id": item_id,
        "title": title,
        "year": pick(raw, "year", "vod_year"),
        "poster": pick(raw, "poster", "cover", "pic", "vod_pic"),
        "summary": pick(raw, "summary", "intro", "content", "vod_content"),
        "score": pick(raw, "score", "vod_score"),
    }

    if raw.get("episodes"):                      # 形态 A：结构化分集列表
        item["type"] = "series"
        episodes = []
        for position, ep in enumerate(raw["episodes"], start=1):
            url = absolute_url(pick(ep, "url", "play_url", "vod_url"))
            if not url:
                continue
            ep_item = {
                "name": pick(ep, "name", "title", "episode_name") or f"第{position}集",
                "url": url,
            }
            number = to_int(ep.get("number")) or to_int(ep.get("episode"))
            if number:                            # 没给序号就不写，让插件从分集名解析
                ep_item["number"] = number
            episodes.append(ep_item)
        if episodes:
            item["playLines"] = [
                {"name": pick(raw, "play_line_name") or "默认", "episodes": episodes}
            ]
    else:                                        # 形态 B：单条直链 / 复合播放串
        lines = parse_play_lines(
            pick(raw, "play_url", "vod_play_url", "url", "vod_url"),
            pick(raw, "play_from", "vod_play_from"),
        )
        if lines:
            item["playLines"] = lines
            multi = any(len(line["episodes"]) > 1 for line in lines)
            item["type"] = "series" if multi else "movie"

    if not item.get("playLines"):
        print(f"  警告：条目 {item['id']}（{title}）没有可播放地址，已跳过")
        return None

    item["updated"] = change_stamp(raw, item)
    return item


# ============================ ③ 组装输出（全量 / 增量） ============================

def load_state():
    if not os.path.exists(STATE_FILE):
        return {}
    with open(STATE_FILE, encoding="utf-8") as f:
        return json.load(f)


def save_state(state):
    with open(STATE_FILE, "w", encoding="utf-8") as f:
        json.dump(state, f, ensure_ascii=False, indent=2)


def build_items(raw_items):
    mapped = [m for m in (map_item(raw) for raw in raw_items) if m]   # 映射失败的条目已跳过
    if not DELTA_MODE:
        # 全量模式：每次输出全部条目
        return mapped, {m["id"]: m["updated"] for m in mapped}

    # 增量模式：只输出变化条目 + 下架墓碑（指南 §4.2）
    state = load_state()
    stamps = state.get("stamps", {})
    out, new_stamps = [], {}

    for m in mapped:
        new_stamps[m["id"]] = m["updated"]
        if stamps.get(m["id"]) != m["updated"]:    # 新增 / 内容变化
            out.append(m)

    for gone_id in stamps:                          # 上次有、这次没了 → 墓碑
        if gone_id not in new_stamps:
            out.append({"id": gone_id, "deleted": True})

    return out, new_stamps


# ============================ ④ 写文件 ============================

def main():
    raw_items = fetch_raw_items()
    items, stamps = build_items(raw_items)

    feed = {
        "schema": "contentbridge-feed/1",            # 照抄，错了插件不认
        "generator": f"{CRAWLER_NAME}/{CRAWLER_VERSION}",
        "version": datetime.now(timezone.utc).strftime("%Y-%m-%d.%H%M"),
        "mode": "delta" if DELTA_MODE else "full",
        "updated": datetime.now(timezone.utc).isoformat(),
        "items": items,
    }

    out_dir = os.path.dirname(FEED_OUT)
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)
    tmp_path = FEED_OUT + ".tmp"
    with open(tmp_path, "w", encoding="utf-8") as f:
        json.dump(feed, f, ensure_ascii=False, indent=2)
    os.replace(tmp_path, FEED_OUT)                   # 原子替换，插件不会读到半截文件

    if DELTA_MODE:
        save_state({"stamps": stamps})
        removed = sum(1 for i in items if i.get("deleted"))
        print(f"增量输出 {len(items) - removed} 条变更 + {removed} 条墓碑 -> {FEED_OUT}")
    else:
        print(f"全量输出 {len(items)} 条 -> {FEED_OUT}")

    print(f"generator={feed['generator']}  mode={feed['mode']}  updated={feed['updated']}")


if __name__ == "__main__":
    if "--reset" in sys.argv and os.path.exists(STATE_FILE):
        os.remove(STATE_FILE)
        print(f"已清空状态文件 {STATE_FILE}，下次增量运行将视为全量")
    main()
