# Copyright (C) 2026 许诺
# SPDX-License-Identifier: GPL-2.0-only
#!/usr/bin/env python3
"""Pack release/test zips for both hosts.

Usage:
    python tools/pack.py <version> --changelog "..." [--target-abi 10.10.0.0]

Outputs (dist/):
    contentbridge_<version>.zip       Jellyfin: Jellyfin.Plugin.ContentBridge.dll + meta.json + icon.png
    contentbridge-emby_<version>.zip  Emby:    Emby.Plugin.ContentBridge.dll + INSTALL.txt

Prints the MD5 of each zip (used for repository.json checksums at release time).
"""

import argparse
import hashlib
import json
import shutil
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Public metadata (mirror of the catalog entry; keep in sync with repository.json).
META = {
    "category": "General",
    "description": "Inject external content-source feeds into the Jellyfin media library as .strm virtual entries (NFO + poster), with scheduled sync, one-click search-and-add, and MaccCMS/Hailuo-style provide-vod API support.",
    "guid": "b42fcd11-31f8-40fb-b605-77fc7b182edc",
    "name": "ContentBridge",
    "overview": "Inject external content sources into your Jellyfin library",
    "owner": "\u8bb8\u8bfa",
}

EMBY_INSTALL = """ContentBridge for Emby - install
================================

1. Stop the Emby server.
2. Copy Emby.Plugin.ContentBridge.dll into the Emby plugins folder,
   directly (no subfolder):
   - Windows/Linux install: programdata/plugins under the Emby installation
   - Docker (e.g. linuxserver/emby): <config>/plugins
3. Start the Emby server.
4. Open the plugin settings (Plugins -> ContentBridge) and configure
   your content sources, library root and sync schedule.

When upgrading inside a Docker container, restart the whole container
after replacing the DLL (not just the server process) - overwriting the
DLL in place can make the container read the old file length and fail
with BadImageFormatException.

Supported: Emby 4.8.x / 4.10.x. The same plugin also ships for Jellyfin
(contentbridge_<version>.zip).

by \u8bb8\u8bfa
"""


def md5(path: Path) -> str:
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("version", help="four-segment version, e.g. 0.3.0.1")
    ap.add_argument("--changelog", required=True)
    ap.add_argument("--target-abi", default="10.10.0.0")
    args = ap.parse_args()

    jellyfin_dll = ROOT / "bin" / "Release" / "net8.0" / "Jellyfin.Plugin.ContentBridge.dll"
    emby_dll = ROOT / "emby" / "bin" / "Release" / "net6.0" / "Emby.Plugin.ContentBridge.dll"
    icon = ROOT / "docs" / "icon.png"
    for p in (jellyfin_dll, emby_dll, icon):
        if not p.exists():
            print(f"missing build output: {p}", file=sys.stderr)
            return 1

    dist = ROOT / "dist"
    dist.mkdir(exist_ok=True)

    meta = dict(META)
    meta.update(
        {
            "changelog": args.changelog,
            "imagePath": "icon.png",
            "targetAbi": args.target_abi,
            "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "version": args.version,
        }
    )

    jf_zip = dist / f"contentbridge_{args.version}.zip"
    with zipfile.ZipFile(jf_zip, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(jellyfin_dll, "Jellyfin.Plugin.ContentBridge.dll")
        z.writestr("meta.json", json.dumps(meta, ensure_ascii=False, indent=4))
        z.write(icon, "icon.png")

    emby_zip = dist / f"contentbridge-emby_{args.version}.zip"
    with zipfile.ZipFile(emby_zip, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(emby_dll, "Emby.Plugin.ContentBridge.dll")
        z.writestr("INSTALL.txt", EMBY_INSTALL.replace("<version>", args.version))

    for p in (jf_zip, emby_zip):
        print(f"{p.name}  md5={md5(p)}  ({p.stat().st_size} bytes)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
