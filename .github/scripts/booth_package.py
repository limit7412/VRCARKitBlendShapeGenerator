#!/usr/bin/env python3
"""booth配布用のunitypackageとzipを、Unityを使わずに組み立てる。

unitypackageはgzip tarで、アセット1件につきGUIDを名前とするディレクトリを持ち、
その中に`asset`(中身)、`asset.meta`(メタ)、`pathname`(取り込み先のパス)を並べた形式である。
Unityのライセンスをリリース経路へ持ち込まずに済むよう、ここではtarfileで直接組み立てる。

GUIDはリポジトリの`.meta`が持つものをそのまま使う。
配布済みのGUIDは利用者のアバターから参照されているため、生成のたびに振り直すわけにはいかない。
"""

import argparse
import gzip
import hashlib
import io
import json
import pathlib
import re
import subprocess
import sys
import tarfile
import tempfile
import zipfile

# unitypackageの取り込み先。booth版はここへ展開される
UNITY_ROOT = "Assets/AtelierKairox/VRCARKitBlendShapeGenerator"

# 取り込み先のルートフォルダのGUID。
# リポジトリのルートがそのままパッケージのルートであり、`.meta`を隣へ置く場所が無いため、
# ここを唯一の置き場所とする。一度配布したら変更しない
ROOT_FOLDER_GUID = "22ced6d9ff0ddbbbe2290f9feca0e2ec"

# 同梱するもの。VPM zipの中身に`LICENSE`を加えたものにあたる。
# テスト(`Tests/`)とドキュメントは配布物へ入れない
INCLUDED_TOP_LEVEL = ["package.json", "LICENSE", "Runtime", "Editor"]

# booth 0.1.9で配布済みのGUID。
# アバターに置かれたコンポーネントがこのGUIDでスクリプトを参照しているため、
# 変えると更新した瞬間にコンポーネントがMissingになる
PINNED_GUIDS = {
    "Runtime/ARKitBlendShapeGeneratorComponent.cs": "cfe038bef4164364db38e499eaeb6e10",
}

PACKAGE_NAME = "com.qazx7412.kx-vrc-arkit-blendshape-generator"
BOOTH_BASENAME = "VRCARKitBlendShapeGenerator"

GUID_PATTERN = re.compile(r"^guid: ([0-9a-f]{32})$", re.MULTILINE)

# 同じ入力から同じバイト列を出すための固定値。
# tarとzipはそのままだとmtimeや所有者を書き込み、実行のたびに違うバイト列になる
FIXED_MTIME = 0
FIXED_ZIP_DATE = (1980, 1, 1, 0, 0, 0)


class PackagingError(Exception):
    pass


def collect_entries(repo_root):
    """同梱するファイルとフォルダを、リポジトリからの相対パスで集める。

    返すのは(files, folders)で、どちらもソート済み。
    順序を固定するのは、生成物を再現可能にするため。
    """
    files, folders = [], []

    for top in INCLUDED_TOP_LEVEL:
        path = repo_root / top
        if not path.exists():
            raise PackagingError(f"同梱対象が見つからない: {top}")

        if path.is_file():
            files.append(top)
            continue

        folders.append(top)
        for child in path.rglob("*"):
            rel = child.relative_to(repo_root).as_posix()
            if child.is_dir():
                folders.append(rel)
            elif not rel.endswith(".meta"):
                files.append(rel)

    return sorted(files), sorted(folders)


def read_guid(meta_path):
    """`.meta`からGUIDを取り出す"""
    if not meta_path.exists():
        raise PackagingError(f"`.meta`が無い: {meta_path}")

    match = GUID_PATTERN.search(meta_path.read_text(encoding="utf-8"))
    if not match:
        raise PackagingError(f"GUIDを読み取れない: {meta_path}")

    return match.group(1)


def build_unitypackage(repo_root):
    """unitypackage(gzip tar)のバイト列を組み立てる"""
    files, folders = collect_entries(repo_root)

    # ルートフォルダは`.meta`の置き場所が無いため、ここで組み立てる
    entries = [(
        ROOT_FOLDER_GUID,
        UNITY_ROOT,
        None,
        f"fileFormatVersion: 2\nguid: {ROOT_FOLDER_GUID}\nfolderAsset: yes\n"
        "DefaultImporter:\n  externalObjects: {}\n"
        "  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    )]

    for rel in folders:
        meta = repo_root / (rel + ".meta")
        entries.append((read_guid(meta), f"{UNITY_ROOT}/{rel}", None, meta.read_text(encoding="utf-8")))

    for rel in files:
        meta = repo_root / (rel + ".meta")
        entries.append((
            read_guid(meta),
            f"{UNITY_ROOT}/{rel}",
            (repo_root / rel).read_bytes(),
            meta.read_text(encoding="utf-8"),
        ))

    seen = {}
    for guid, pathname, _, _ in entries:
        if guid in seen:
            raise PackagingError(f"GUIDが重複している: {guid} ({seen[guid]} と {pathname})")
        seen[guid] = pathname

    entries.sort(key=lambda entry: entry[1])

    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.GNU_FORMAT) as tar:
        for guid, pathname, content, meta_text in entries:
            _add_dir(tar, guid)
            _add_file(tar, f"{guid}/pathname", pathname.encode("utf-8"))
            _add_file(tar, f"{guid}/asset.meta", meta_text.encode("utf-8"))
            if content is not None:
                _add_file(tar, f"{guid}/asset", content)

    compressed = io.BytesIO()
    # mtimeを明示しないとgzipヘッダへ現在時刻が入り、実行のたびにバイト列が変わる
    with gzip.GzipFile(fileobj=compressed, mode="wb", compresslevel=9, mtime=FIXED_MTIME) as gz:
        gz.write(raw.getvalue())

    return compressed.getvalue(), len(entries)


def _tarinfo(name, size, mode, typeflag):
    info = tarfile.TarInfo(name)
    info.size = size
    info.mode = mode
    info.type = typeflag
    info.mtime = FIXED_MTIME
    info.uid = info.gid = 0
    info.uname = info.gname = ""
    return info


def _add_dir(tar, name):
    tar.addfile(_tarinfo(name, 0, 0o755, tarfile.DIRTYPE))


def _add_file(tar, name, payload):
    tar.addfile(_tarinfo(name, len(payload), 0o644, tarfile.REGTYPE), io.BytesIO(payload))


def build_booth_zip(repo_root, version, output_dir):
    """unitypackageを1つだけ収めたzipを書き出し、そのパスを返す。

    zipの直下へ`package.json`を置かないことが条件になる。
    VPMリスティングの生成はリリースのzipを全て舐めて直下の`package.json`を読むため、
    置いてしまうとbooth用の配布物がリスティングへ混ざる
    """
    payload, entry_count = build_unitypackage(repo_root)

    unitypackage_name = f"{BOOTH_BASENAME}_{version}.unitypackage"
    zip_path = output_dir / f"{BOOTH_BASENAME}_{version}.zip"

    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        info = zipfile.ZipInfo(unitypackage_name, date_time=FIXED_ZIP_DATE)
        info.external_attr = 0o644 << 16
        info.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(info, payload)

    return zip_path, entry_count


def read_version(repo_root):
    return json.loads((repo_root / "package.json").read_text(encoding="utf-8"))["version"]


def command_build(args):
    repo_root = pathlib.Path(args.repo_root).resolve()
    output_dir = pathlib.Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    manifest_version = read_version(repo_root)
    if args.version != manifest_version:
        raise PackagingError(
            f"指定されたバージョン({args.version})とpackage.json({manifest_version})が食い違っている"
        )

    zip_path, entry_count = build_booth_zip(repo_root, args.version, output_dir)
    digest = hashlib.sha256(zip_path.read_bytes()).hexdigest()

    print(f"Created: {zip_path.name} ({zip_path.stat().st_size} bytes, {entry_count} entries)")
    print(f"sha256: {digest}")
    return 0


def command_verify(args):
    repo_root = pathlib.Path(args.repo_root).resolve()
    failures = []

    def check(condition, message):
        if not condition:
            failures.append(message)

    # 同梱対象に`.meta`が揃っているか
    try:
        files, folders = collect_entries(repo_root)
    except PackagingError as error:
        print(f"NG: {error}")
        return 1

    guids = {UNITY_ROOT: ROOT_FOLDER_GUID}
    for rel in files + folders:
        meta = repo_root / (rel + ".meta")
        if not meta.exists():
            failures.append(f"`.meta`が無い: {rel}")
            continue
        try:
            guids[rel] = read_guid(meta)
        except PackagingError as error:
            failures.append(str(error))

    # GUIDの重複
    by_guid = {}
    for rel, guid in guids.items():
        by_guid.setdefault(guid, []).append(rel)
    for guid, owners in sorted(by_guid.items()):
        check(len(owners) == 1, f"GUIDが重複している: {guid} ({', '.join(sorted(owners))})")

    # 配布済みGUIDの固定
    for rel, expected in PINNED_GUIDS.items():
        check(
            guids.get(rel) == expected,
            f"配布済みのGUIDが変わっている: {rel} は {expected} でなければならない(現在 {guids.get(rel)})",
        )

    if failures:
        _report(failures)
        return 1

    # 生成物の検証
    version = read_version(repo_root)
    with tempfile.TemporaryDirectory() as tmp:
        first_dir = pathlib.Path(tmp) / "first"
        second_dir = pathlib.Path(tmp) / "second"
        first_dir.mkdir()
        second_dir.mkdir()

        first, entry_count = build_booth_zip(repo_root, version, first_dir)
        second, _ = build_booth_zip(repo_root, version, second_dir)

        # 2回作って同一バイトになるか
        check(
            first.read_bytes() == second.read_bytes(),
            "2回作った生成物のバイト列が一致しない",
        )

        with zipfile.ZipFile(first) as archive:
            names = archive.namelist()
            check(len(names) == 1, f"zipの直下に複数のファイルがある: {names}")
            check(
                all(name.endswith(".unitypackage") for name in names),
                f"zipの中身が`.unitypackage`ではない: {names}",
            )
            # リスティングへの誤混入の防止。
            # vcc-vpmはリリースのzipを全て舐めて直下の`package.json`を読む
            check(
                not any(name == "package.json" for name in names),
                "zipの直下に`package.json`が居るためVPMリスティングへ混ざる",
            )
            payload = archive.read(names[0]) if names else b""

        if payload:
            with tarfile.open(fileobj=io.BytesIO(gzip.decompress(payload))) as tar:
                pathnames = [
                    tar.extractfile(member).read().decode("utf-8")
                    for member in tar.getmembers()
                    if member.name.endswith("/pathname")
                ]

            check(
                len(pathnames) == entry_count,
                f"エントリ数が合わない: pathname {len(pathnames)}件、想定 {entry_count}件",
            )
            for pathname in pathnames:
                check(
                    pathname == UNITY_ROOT or pathname.startswith(UNITY_ROOT + "/"),
                    f"取り込み先が`{UNITY_ROOT}`の外を指している: {pathname}",
                )

    # 既存の`.meta`のGUIDが変わっていないか
    if args.base_ref:
        failures.extend(_diff_guids_against(repo_root, args.base_ref))

    if failures:
        _report(failures)
        return 1

    print(f"OK: ファイル{len(files)}件、フォルダ{len(folders) + 1}件、エントリ{entry_count}件")
    return 0


def _diff_guids_against(repo_root, base_ref):
    """`base_ref`が配っていたGUIDが今も同じ意味で残っているか調べる。

    GUIDは一度配布したら変えられない。
    利用者のアバターやプレハブはこの値でアセットを指しているため、振り直すと参照が切れる。

    見るのは3つ。
    パスが残っているのにGUIDが変わっていないか、
    配っていたGUIDそのものが一覧から消えていないか（リネームで振り直すとこうなる）、
    取り込み先ルートフォルダのGUIDが変わっていないか。
    """
    try:
        # `-z`でNUL区切りにする。
        # 既定の出力を空白で分割すると`Editor/Foo Bar.cs.meta`のような名前が分断され、
        # 実在しないパスを見に行った末に「変化なし」として通してしまう
        listing = subprocess.run(
            ["git", "ls-tree", "-r", "-z", "--name-only", base_ref],
            cwd=repo_root, capture_output=True, text=True, check=True,
        ).stdout.split("\0")
    except (subprocess.CalledProcessError, FileNotFoundError) as error:
        return [f"`{base_ref}`の内容を取得できない: {error}"]

    # 現在のリポジトリが持つGUIDの全体。
    # 同梱対象に限らず拾うのは、対象から外れただけのアセットを「消えた」と誤って責めないため
    live_guids = set()
    for meta in repo_root.rglob("*.meta"):
        match = GUID_PATTERN.search(meta.read_text(encoding="utf-8"))
        if match:
            live_guids.add(match.group(1))

    problems = []
    for name in listing:
        if not name or not name.endswith(".meta"):
            continue

        old = subprocess.run(
            ["git", "show", f"{base_ref}:{name}"],
            cwd=repo_root, capture_output=True, text=True, check=False,
        )
        if old.returncode != 0:
            continue

        old_match = GUID_PATTERN.search(old.stdout)
        if not old_match:
            continue
        old_guid = old_match.group(1)

        current = repo_root / name
        if not current.exists():
            # リネームは`.meta`ごと動かすのが正しく、その場合GUIDは新しいパスで生き残る。
            # 消えているなら、振り直したか、配っていたアセットを消したかのどちらか
            if old_guid not in live_guids:
                problems.append(
                    f"配っていたGUIDが失われている: {name} ({old_guid})。"
                    "リネームなら`.meta`も一緒に動かし、削除なら参照が切れることを承知のうえで行うこと"
                )
            continue

        new_match = GUID_PATTERN.search(current.read_text(encoding="utf-8"))
        if new_match and old_guid != new_match.group(1):
            problems.append(
                f"既存の`.meta`のGUIDが変わっている: {name} "
                f"({old_guid} → {new_match.group(1)})"
            )

    problems.extend(_diff_root_folder_guid(repo_root, base_ref))
    return problems


def _diff_root_folder_guid(repo_root, base_ref):
    """取り込み先ルートフォルダのGUIDが変わっていないか調べる。

    この値だけはリポジトリのルートに`.meta`を置く場所が無く、このスクリプトの定数として
    持っている。`.meta`と同じく配布済みの値なので、同じように変化を止める
    """
    relative = f".github/scripts/{pathlib.Path(__file__).name}"

    old = subprocess.run(
        ["git", "show", f"{base_ref}:{relative}"],
        cwd=repo_root, capture_output=True, text=True, check=False,
    )
    if old.returncode != 0:
        # このスクリプト自体を導入した変更では、比較対象が無い
        return []

    match = re.search(r'ROOT_FOLDER_GUID = "([0-9a-f]{32})"', old.stdout)
    if not match or match.group(1) == ROOT_FOLDER_GUID:
        return []

    return [
        "取り込み先ルートフォルダのGUIDが変わっている: "
        f"{match.group(1)} → {ROOT_FOLDER_GUID}"
    ]


def _report(failures):
    print("NG:")
    for failure in failures:
        print(f"  - {failure}")


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=".")
    subparsers = parser.add_subparsers(dest="command", required=True)

    build = subparsers.add_parser("build", help="booth用のzipを作る")
    build.add_argument("--version", required=True)
    build.add_argument("--output-dir", default=".")
    build.set_defaults(func=command_build)

    verify = subparsers.add_parser("verify", help="同梱対象と生成物を検証する")
    verify.add_argument("--base-ref", default=None, help="GUIDの変化を突き合わせる基準(例: origin/main)")
    verify.set_defaults(func=command_verify)

    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except PackagingError as error:
        print(f"NG: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
