#!/usr/bin/env python3
"""booth配布用の.unitypackageを組み立てる。

unitypackageはgzipで圧縮したtarであり、アセット1件につきGUIDを名前とする
ディレクトリを1つ持つ。ディレクトリの中身は次の3つ。

  asset       アセット本体。フォルダのエントリは持たない
  asset.meta  対応する.metaの内容
  pathname    プロジェクトルートから見た配置先

GUIDはリポジトリの.metaから読み、この場で生成はしない。
GUIDが変わると、booth版を入れている利用者のアバターに付いた
ARKitBlendShapeGeneratorComponentの参照が切れるため、
配布済みのGUIDをそのまま使い続ける必要がある。

同じ入力から同じバイト列が出るよう、mtimeや所有者は固定する。
"""

import argparse
import gzip
import io
import os
import pathlib
import re
import sys
import tarfile

GUID_PATTERN = re.compile(r"^guid: ([0-9a-f]{32})$", re.MULTILINE)


class Entry:
    def __init__(self, guid, pathname, meta, asset_path):
        self.guid = guid
        self.pathname = pathname
        self.meta = meta
        # フォルダのエントリはNone
        self.asset_path = asset_path


def read_guid(meta_path, meta):
    match = GUID_PATTERN.search(meta)
    if match is None:
        raise SystemExit(f"guidを読み取れません: {meta_path}")
    return match.group(1)


def load_meta(meta_path):
    if not meta_path.is_file():
        raise SystemExit(
            f".metaがありません: {meta_path}\n"
            "同梱するファイルとフォルダにはGUIDを固定するため.metaが要ります。"
            "Unityでプロジェクトへ取り込むと生成されます。"
        )
    meta = meta_path.read_text(encoding="utf-8")
    return meta, read_guid(meta_path, meta)


def make_entry(root, path, is_dir):
    """リポジトリ上のパスから、unitypackageのエントリを1件作る。"""
    meta, guid = load_meta(pathlib.Path(str(path) + ".meta"))
    pathname = f"{root}/{path.as_posix()}"
    return Entry(guid, pathname, meta, None if is_dir else path)


def collect(root, root_meta_path, targets):
    """同梱対象を集める。並び順はGUIDで決めるためここでは問わない。"""
    meta, guid = load_meta(root_meta_path)
    entries = [Entry(guid, root, meta, None)]

    for target in targets:
        path = pathlib.Path(target)
        if path.is_file():
            entries.append(make_entry(root, path, is_dir=False))
            continue
        if not path.is_dir():
            raise SystemExit(f"同梱対象が見つかりません: {target}")

        entries.append(make_entry(root, path, is_dir=True))
        # walkの結果は環境によって順序が変わるため、明示的に並べ替える
        for parent, dirs, files in os.walk(path):
            dirs.sort()
            files.sort()
            for name in dirs:
                entries.append(make_entry(root, pathlib.Path(parent, name), is_dir=True))
            for name in files:
                # .meta自体はエントリにならず、対応するアセットのasset.metaになる
                if name.endswith(".meta"):
                    continue
                entries.append(make_entry(root, pathlib.Path(parent, name), is_dir=False))

    return entries


def check_unique(entries):
    seen = {}
    for entry in entries:
        if entry.guid in seen:
            raise SystemExit(
                f"GUIDが重複しています: {entry.guid}\n"
                f"  {seen[entry.guid]}\n  {entry.pathname}"
            )
        seen[entry.guid] = entry.pathname


def add_bytes(tar, name, payload):
    info = tarfile.TarInfo(name)
    info.size = len(payload)
    info.mode = 0o777
    info.mtime = 0
    info.uid = 0
    info.gid = 0
    info.uname = ""
    info.gname = ""
    tar.addfile(info, io.BytesIO(payload))


def add_dir(tar, name):
    info = tarfile.TarInfo(name)
    info.type = tarfile.DIRTYPE
    info.mode = 0o777
    info.mtime = 0
    info.uid = 0
    info.gid = 0
    info.uname = ""
    info.gname = ""
    tar.addfile(info)


def write_package(entries, output):
    output.parent.mkdir(parents=True, exist_ok=True)
    with open(output, "wb") as raw:
        # mtimeを固定しないとgzipヘッダに生成時刻が入り、同じ入力でも別のバイト列になる
        with gzip.GzipFile(fileobj=raw, mode="wb", mtime=0) as gz:
            with tarfile.open(fileobj=gz, mode="w", format=tarfile.GNU_FORMAT) as tar:
                for entry in sorted(entries, key=lambda e: e.guid):
                    add_dir(tar, entry.guid)
                    if entry.asset_path is not None:
                        add_bytes(tar, f"{entry.guid}/asset", entry.asset_path.read_bytes())
                    add_bytes(tar, f"{entry.guid}/asset.meta", entry.meta.encode("utf-8"))
                    # pathnameは末尾に改行を置かない
                    add_bytes(tar, f"{entry.guid}/pathname", entry.pathname.encode("utf-8"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, help="展開先のプロジェクト内パス")
    parser.add_argument("--root-meta", required=True, help="展開先フォルダ自身の.meta")
    parser.add_argument("--output", required=True, help="出力する.unitypackage")
    parser.add_argument("targets", nargs="+", help="同梱するファイルとフォルダ")
    args = parser.parse_args()

    entries = collect(args.root.rstrip("/"), pathlib.Path(args.root_meta), args.targets)
    check_unique(entries)
    write_package(entries, pathlib.Path(args.output))
    print(f"Created: {args.output} ({len(entries)} entries)", file=sys.stderr)


if __name__ == "__main__":
    main()
