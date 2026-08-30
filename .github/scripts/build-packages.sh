#!/usr/bin/env bash
# リリースへ添付する2つのzipを作る。
#
#   com.qazx7412.kx-vrc-arkit-blendshape-generator-<version>.zip
#     VCC/ALCOM向けのVPMパッケージ。中身は従来どおりで.metaを含まない。
#
#   VRCARKitBlendShapeGenerator_<version>.zip
#     booth向け。中身は同名の.unitypackage 1つだけ。
#
# booth用zipの直下に`package.json`を置いてはならない。
# VPMリスティングの生成(vrchat-community/package-list-action)はリリースの全zipを
# 見に行き、zip直下の`package.json`を読めたものをパッケージとして登録する。
# 直下に置くと、boothの配布物がVPMパッケージとしてリスティングへ載ってしまう。
#
# release.ymlとprerelease.ymlの両方から呼ばれる。
# 生成手順をここへ集めているのは、以前のように同じ手順を2つのワークフローへ
# 書き写すと、片方だけ更新したときに安定版とプレリリースで中身の違うパッケージが
# できるため。
set -euo pipefail

if [ $# -ne 1 ]; then
  echo "usage: $0 <version>" >&2
  exit 1
fi

VERSION="$1"
cd "$(dirname "$0")/../.."

PACKAGE_NAME="com.qazx7412.kx-vrc-arkit-blendshape-generator"
BOOTH_NAME="VRCARKitBlendShapeGenerator"
# unitypackageの展開先。boothで配布済みのものと揃える
UNITY_ROOT="Assets/AtelierKairox/$BOOTH_NAME"

VPM_ZIP="${PACKAGE_NAME}-${VERSION}.zip"
BOOTH_ZIP="${BOOTH_NAME}_${VERSION}.zip"
UNITYPACKAGE="${BOOTH_NAME}_${VERSION}.unitypackage"

# リリースのバージョンをパッケージへ書き込む。
# package.jsonのversionはタグから上書きする運用のため、リポジトリ上の値は使わない
jq ".version = \"${VERSION}\"" package.json > package.json.tmp
mv package.json.tmp package.json

rm -f "$VPM_ZIP" "$BOOTH_ZIP" "$UNITYPACKAGE"

zip -r "$VPM_ZIP" \
  package.json \
  Runtime/ \
  Editor/ \
  -x "*.meta"

# booth用はVPMパッケージと同じ内容にLICENSEを加える。
# .metaはunitypackageの組み立てに使われ、エントリのasset.metaになる
python3 .github/scripts/build_unitypackage.py \
  --root "$UNITY_ROOT" \
  --root-meta .github/packaging/package-root.meta \
  --output "$UNITYPACKAGE" \
  package.json \
  Runtime \
  Editor \
  LICENSE

# `-j`でパスを落とし、zip直下にunitypackageだけが入るようにする
zip -j "$BOOTH_ZIP" "$UNITYPACKAGE"
rm -f "$UNITYPACKAGE"

echo "Created: $VPM_ZIP"
echo "Created: $BOOTH_ZIP"
ls -la "$VPM_ZIP" "$BOOTH_ZIP"
