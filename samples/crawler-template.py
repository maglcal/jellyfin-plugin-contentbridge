#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ContentBridge 采集程序模板（contentbridge-feed/1）

开箱可跑：python crawler-template.py
  → 用内置演示数据生成 feed-output/my-feed.json，可直接配到插件里试同步。

接真实数据源：改下面【配置区】和 fetch_raw_items() 即可，其余不用动。
只依赖 Python 标准库，不需要 pip install 任何东西。

字段规范见仓库 FEED-SPEC.md，手把手教程见 docs/CRAWLER-GUIDE.md。
"""

import hashlib
import json
import os
import sys
import urllib.request
from datetime import datetime, timezone

# ============================ 配置区（只需要改这里） ============================

FEED_OUT = os.path.join("feed-output", "my-feed.json")  # 输出的 feed 文件
SOURCE_API = ""            # 你的数据源接口地址；留空 = 用内置演示数据先跑通流程
CRAWLER_NAME = "my-crawler"
CRAWLER_VERSION = "1.0.0"
DELTA_MODE = False         # True = 增量模式：只输出变化条目 + 下架墓碑（详见指南 §4.2）
STATE_FILE = "crawler-state.json"   # 增量模式的状态记忆（丢了就删掉重跑，不值钱）

# ============================ 演示数据（接真实源后可删） ============================

DEMO_ITEMS = [
    {
        "id": "demo-001",
        "name": "示例电影",
        "year": "2025",
        "cover": "https://img.example.com/demo-001.jpg",
        "intro": "一部用来演示采集程序输出的电影。",
        "score": "8.1",
        "updated": "2026-11-07T02:00:00Z",
        "play_url": "https://cdn.example.com/demo-001/index.m3u8",
    },
    {
        "id": "demo-002",
        "name": "示例剧集",
        "year": "2026",
        "cover": "https://img.example.com/demo-002.jpg",
        "intro": "演示剧集，两集。",
        "score": "7.5",
        "updated": "2026-11-07T02:30:00Z",
        "episodes": [
            {"name": "第1集", "url": "https://cdn.example.com/demo-002/s01e01.m3u8", "number": 1},
            {"name": "第2集", "url": "https://cdn.example.com/demo-002/s01e02.m3u8", "number": 2},
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

def change_stamp(raw, mapped):
    """条目级变更戳：源自带 updated 就用它；没有就对内容做指纹。
    规则：内容变了戳才变，插件靠它跳过没变化的条目。"""
    if raw.get("updated"):
        return str(raw["updated"])
    fingerprint = json.dumps(mapped, ensure_ascii=False, sort_keys=True)
    return hashlib.md5(fingerprint.encode("utf-8")).hexdigest()


def map_item(raw):
    """把数据源的字段翻译成 contentbridge-feed/1 的条目。
    字段含义见 FEED-SPEC.md §3。"""
    item = {
        "id": str(raw["id"]),                 # 全源唯一且每次运行不变（最重要）
        "title": raw["name"],
        "year": str(raw.get("year", "")),
        "poster": raw.get("cover", ""),
        "summary": raw.get("intro", ""),
        "score": str(raw.get("score", "")),
    }

    if "episodes" in raw:                     # 剧集
        item["type"] = "series"
        item["playLines"] = [
            {"name": "m3u8", "episodes": [
                {"name": ep["name"], "url": ep["url"], "number": ep.get("number")}
                for ep in raw["episodes"]
            ]}
        ]
    else:                                     # 电影
        item["type"] = "movie"
        item["playLines"] = [
            {"name": "m3u8", "episodes": [{"name": "正片", "url": raw["play_url"]}]}
        ]

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
    mapped = [map_item(raw) for raw in raw_items]
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
