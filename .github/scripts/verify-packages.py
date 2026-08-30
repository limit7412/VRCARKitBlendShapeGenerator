#!/usr/bin/env python3
"""build-packages.shが作ったzipを検証する。

リリースへ添付してから気付くと差し替えが効かないため、
中身の前提が崩れていないかをここで落とす。
"""

import argparse
import io
import pathlib
import re
import sys
import tarfile
import zipfile

PACKAGE_NAME = "com.qazx7412.kx-vrc-arkit-blendshape-generator"
BOOTH_NAME = "VRCARKitBlendShapeGenerator"
UNITY_ROOT = f"Assets/AtelierKairox/{BOOTH_NAME}"
GUID_PATTERN = re.compile(r"^guid: ([0-9a-f]{32})$", re.MULTILINE)

errors = []


def check(condition, message):
    if not condition:
        errors.append(message)


def expected_assets(targets):
    """リポジトリ上で同梱対象となるファイルの一覧。"""
    found = set()
    for target in targets:
        path = pathlib.Path(target)
        if path.is_file():
            found.add(path.as_posix())
            continue
        for child in path.rglob("*"):
            if child.is_file() and child.suffix != ".meta":
                found.add(child.as_posix())
    return found


def verify_vpm_zip(version):
    name = f"{PACKAGE_NAME}-{version}.zip"
    if not pathlib.Path(name).is_file():
        errors.append(f"VPM用zipがありません: {name}")
        return

    with zipfile.ZipFile(name) as zf:
        names = zf.namelist()

    check("package.json" in names, f"{name}: 直下にpackage.jsonがありません")
    metas = [n for n in names if n.endswith(".meta")]
    check(not metas, f"{name}: .metaが含まれています: {metas[:3]}")


def verify_booth_zip(version, targets):
    name = f"{BOOTH_NAME}_{version}.zip"
    if not pathlib.Path(name).is_file():
        errors.append(f"booth用zipがありません: {name}")
        return

    with zipfile.ZipFile(name) as zf:
        names = zf.namelist()
        expected_member = f"{BOOTH_NAME}_{version}.unitypackage"
        if names != [expected_member]:
            errors.append(f"{name}: 直下は{expected_member}のみであるべきです: {names}")
            return
        payload = zf.read(expected_member)

    # zip直下のpackage.jsonはVPMリスティングへの誤登録を招く。
    # 上の一致判定で弾かれるが、意図を残すために明示しておく
    check("package.json" not in names, f"{name}: 直下にpackage.jsonを置いてはいけません")

    verify_unitypackage(expected_member, payload, version, targets)


def verify_unitypackage(name, payload, version, targets):
    entries = {}
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:gz") as tar:
        for member in tar.getmembers():
            if member.isdir():
                continue
            guid, _, kind = member.name.partition("/")
            content = tar.extractfile(member)
            entries.setdefault(guid, {})[kind] = content.read() if content else b""

    check(bool(entries), f"{name}: エントリがありません")

    pathnames = {}
    for guid, files in sorted(entries.items()):
        if "pathname" not in files or "asset.meta" not in files:
            errors.append(f"{name}: {guid} にpathnameかasset.metaがありません")
            continue

        pathname = files["pathname"].decode("utf-8")
        meta = files["asset.meta"].decode("utf-8")
        match = GUID_PATTERN.search(meta)

        check(match is not None and match.group(1) == guid,
              f"{name}: {guid} のasset.metaのguidが一致しません")
        check(pathname == UNITY_ROOT or pathname.startswith(UNITY_ROOT + "/"),
              f"{name}: 展開先が想定と違います: {pathname}")
        check(pathname not in pathnames,
              f"{name}: pathnameが重複しています: {pathname}")

        pathnames[pathname] = files

    packaged = {
        p[len(UNITY_ROOT) + 1:]: files
        for p, files in pathnames.items()
        if p != UNITY_ROOT and "asset" in files
    }
    expected = expected_assets(targets)

    for missing in sorted(expected - packaged.keys()):
        errors.append(f"{name}: 同梱されていません: {missing}")
    for extra in sorted(packaged.keys() - expected):
        errors.append(f"{name}: 同梱対象ではありません: {extra}")

    # 中間のフォルダにエントリが無いと、展開先でフォルダのGUIDが振り直される
    for relative in expected:
        parent = pathlib.PurePosixPath(relative).parent
        while str(parent) != ".":
            check(f"{UNITY_ROOT}/{parent}" in pathnames,
                  f"{name}: フォルダのエントリがありません: {parent}")
            parent = parent.parent

    if "package.json" in packaged:
        manifest = packaged["package.json"]["asset"].decode("utf-8")
        check(f'"version": "{version}"' in manifest,
              f"{name}: package.jsonのversionが{version}ではありません")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version")
    parser.add_argument("targets", nargs="+", help="同梱するファイルとフォルダ")
    args = parser.parse_args()

    verify_vpm_zip(args.version)
    verify_booth_zip(args.version, args.targets)

    if errors:
        for message in errors:
            print(f"::error::{message}", file=sys.stderr)
        raise SystemExit(1)

    print("パッケージの検証を通過しました")


if __name__ == "__main__":
    main()
