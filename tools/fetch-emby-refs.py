#!/usr/bin/env python3
"""Fetch Emby reference assemblies for the dual-host build (EMBY-COMPAT.md M2).

Downloads the smallest official Emby server packages and extracts only the managed
plugin-API assemblies into refs/emby/<ver>/ (gitignored). The DLLs stay local — they
are proprietary Emby assemblies used as compile references only and must never be
committed or redistributed.

Usage: python tools/fetch-emby-refs.py [--all]
  default: extract the baseline refs (4.8) and the current-version refs (4.10)
"""
import io
import pathlib
import sys
import tarfile
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
REFS = ROOT / "refs" / "emby"
DOWNLOADS = ROOT / "refs" / "_downloads"
UA = "contentbridge-build/1.0"

# (version label, package url, note)
PACKAGES = [
    ("4.8", "https://github.com/MediaBrowser/Emby.Releases/releases/download/4.8.11.0/embyserver-netcore_4.8.11.0.zip",
     "baseline per docs/EMBY-COMPAT.md (4.8 series, .NET runtime build)"),
    ("4.10", "https://github.com/MediaBrowser/Emby.Releases/releases/download/4.10.2.0/emby-server-freebsd13_4.10.2.0_amd64.tar.xz",
     "current release (smallest package carrying the same managed assemblies)"),
]

# managed assemblies that form the plugin API surface (plus service-stack bits if present)
WANTED = ("MediaBrowser", "ServiceStack", "Emby.")


def extract_members(names, opener):
    """opener(name) -> bytes for one archive member; yields (basename, data)."""
    for name in names:
        base = name.replace("\\", "/").rsplit("/", 1)[-1]
        if not base.lower().endswith(".dll"):
            continue
        if not base.startswith(WANTED):
            continue
        yield base, opener(name)


def main():
    DOWNLOADS.mkdir(parents=True, exist_ok=True)
    for label, url, note in PACKAGES:
        pkg_name = url.rsplit("/", 1)[-1]
        pkg_path = DOWNLOADS / pkg_name
        if not pkg_path.exists():
            print(f"[refs] downloading {pkg_name}  ({note})")
            req = urllib.request.Request(url, headers={"user-agent": UA})
            with urllib.request.urlopen(req, timeout=600) as resp, open(pkg_path, "wb") as out:
                while True:
                    chunk = resp.read(1 << 20)
                    if not chunk:
                        break
                    out.write(chunk)
        else:
            print(f"[refs] cached {pkg_name}")

        out_dir = REFS / label
        out_dir.mkdir(parents=True, exist_ok=True)
        if pkg_name.endswith(".zip"):
            with zipfile.ZipFile(pkg_path) as zf:
                members = [n for n in zf.namelist()]
                for base, data in extract_members(members, lambda n: zipfile.ZipFile(pkg_path).read(n)):
                    (out_dir / base).write_bytes(data)
                    print(f"[refs]   {label}/{base}  ({len(data)} 字节)")
        else:
            with tarfile.open(pkg_path, "r:xz") as tf:
                members = [m.name for m in tf.getmembers() if m.isfile()]
                for base, data in extract_members(members, lambda n: tf.extractfile(n).read()):
                    (out_dir / base).write_bytes(data)
                    print(f"[refs]   {label}/{base}  ({len(data)} 字节)")

        have = sorted(p.name for p in out_dir.glob("*.dll"))
        print(f"[refs] {label}: {len(have)} 个程序集 -> {out_dir}")
        if not any(h.startswith("MediaBrowser") for h in have):
            print(f"[refs] ! {label}: 没抓到 MediaBrowser.*，包布局变了？检查 {pkg_path}", file=sys.stderr)
            return 1
    print("[refs] done")
    return 0


if __name__ == "__main__":
    sys.exit(main())
