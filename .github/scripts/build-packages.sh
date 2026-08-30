#!/usr/bin/env bash
# リリースへ添付するパッケージを作る。
#
# release.ymlとprerelease.ymlの両方から呼ばれる。
# 以前は同じzip生成ステップが両方に書かれており、片方だけ変更するとプレリリースと
# 安定版で中身の違うパッケージができる状態だった。ここへ集約してその余地を無くす。
#
# 出力は2つで、どちらもリリースの`files: "*.zip"`がそのまま拾う。
#   com.qazx7412.kx-vrc-arkit-blendshape-generator-<version>.zip  VCC/ALCOM向け
#   VRCARKitBlendShapeGenerator_<version>.zip                     booth向け(unitypackage 1つ)
set -euo pipefail

VERSION="${1:?バージョンを指定すること}"
OUTPUT_DIR="${2:-.}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

cd "$REPO_ROOT"
mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR="$(cd "$OUTPUT_DIR" && pwd)"

PACKAGE_NAME="com.qazx7412.kx-vrc-arkit-blendshape-generator"

# package.jsonのversionはリリース時にタグから上書きする運用のため、
# リポジトリ上の値ではなく渡されたバージョンを書き込む
jq --arg version "$VERSION" '.version = $version' package.json > package.json.tmp
mv package.json.tmp package.json

# VPM用。`.meta`は入れない。
# VCC/ALCOMはPackages/配下へ展開し、そこではUnityが`.meta`を作らないため不要であり、
# 既存の配布物と中身を揃える意味でも除外を続ける
VPM_ZIP="$OUTPUT_DIR/${PACKAGE_NAME}-${VERSION}.zip"
rm -f "$VPM_ZIP"
zip -q -r "$VPM_ZIP" \
  package.json \
  Runtime/ \
  Editor/ \
  -x "*.meta"

echo "Created: $(basename "$VPM_ZIP")"
ls -la "$VPM_ZIP"

# booth用。中身はunitypackage 1つで、zipの直下に`package.json`を置かない。
# 置くとVPMリスティングの生成がこれをパッケージとして拾ってしまう
python3 "$SCRIPT_DIR/booth_package.py" build --version "$VERSION" --output-dir "$OUTPUT_DIR"
ls -la "$OUTPUT_DIR/VRCARKitBlendShapeGenerator_${VERSION}.zip"
