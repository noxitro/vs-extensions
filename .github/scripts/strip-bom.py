#!/usr/bin/env python3
"""テキストファイルの先頭の UTF-8 BOM を外す。内容と改行コードには触らない。

Visual Studio がプロジェクトファイルを保存し直したときや、テンプレートから
ファイルを作ったときなどに BOM が付くことがある。File format の検査で
「BOM が付いた」と言われたら、これで外す。

使い方:
    python3 .github/scripts/strip-bom.py <パス>...   # 指定したファイルの BOM を外す
    python3 .github/scripts/strip-bom.py --all       # git 管理下の全ファイルを対象にする

どちらの場合も、BOM が必要なファイル (bom_policy.py の REQUIRED) と、どちらでもよい
ファイル (ANY) には触らない。
"""

import argparse
import os
import subprocess
import sys

import bom_policy

UTF8_BOM = b"\xef\xbb\xbf"


def repo_relative(path, root):
    return os.path.relpath(os.path.abspath(path), root).replace(os.sep, "/")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="*", help="BOM を外すファイル")
    ap.add_argument("--all", action="store_true", help="git 管理下の全ファイルを対象にする")
    args = ap.parse_args()
    if not args.paths and not args.all:
        ap.error("パスか --all を指定すること")

    root = subprocess.run(["git", "rev-parse", "--show-toplevel"], check=True, stdout=subprocess.PIPE).stdout.decode().strip()
    if args.all:
        out = subprocess.run(["git", "ls-files", "-z"], check=True, stdout=subprocess.PIPE, cwd=root).stdout
        targets = [os.path.join(root, p) for p in out.decode("utf-8").split("\0") if p]
    else:
        targets = args.paths

    stripped = 0
    for path in targets:
        if os.path.islink(path) or not os.path.isfile(path):
            continue
        rel = repo_relative(path, root)
        if bom_policy.policy(rel) != bom_policy.FORBIDDEN_POLICY:
            if not args.all:
                print(f"skip: {rel} (BOM を残す決まりのファイル)")
            continue
        with open(path, "rb") as f:
            data = f.read()
        if not data.startswith(UTF8_BOM):
            continue
        with open(path, "wb") as f:
            f.write(data[len(UTF8_BOM):])
        stripped += 1
        if not args.all:
            print(f"stripped: {rel}")
    print(f"{stripped} 本の BOM を外した")
    return 0


if __name__ == "__main__":
    sys.exit(main())
