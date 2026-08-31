# 配布パッケージ

リリースへ添付するzipは `.github/scripts/build-packages.sh` が作る。
release.ymlとprerelease.ymlの両方がこれを呼ぶため、安定版とプレリリースで中身が食い違うことはない。

| 生成物 | 中身 | 用途 |
| ---- | ---- | ---- |
| `com.qazx7412.kx-vrc-arkit-blendshape-generator-<版>.zip` | `package.json` `Runtime/` `Editor/`（`.meta` を除く） | VCC/ALCOM |
| `VRCARKitBlendShapeGenerator_<版>.zip` | unitypackage 1つ | booth |

unitypackageは `.github/scripts/booth_package.py` がUnityを使わずに組み立てる。
unitypackageの実体はgzip tarで、アセット1件につきGUIDを名前とするディレクトリを持ち、その中に中身とメタと取り込み先のパスを並べた形式である。
リリース経路へUnityライセンスを持ち込まずに済ませるため、Pythonで直接組み立てている。

booth用zipの直下に `package.json` を置かないことが条件になる。
VPMリスティングの生成はリリースのzipをすべて舐めて直下の `package.json` を読むため、置いてしまうとbooth用の配布物がリスティングへ混ざる。
`packaging` ジョブ（[ci.md](ci.md)）がこれを検証する。

同梱対象のGUIDは `.meta` としてリポジトリで固定している。
生成のたびに振り直すと、更新した瞬間に利用者のアバターからコンポーネントの参照が切れる。
**同梱対象の `.meta` のGUIDは変更しないこと。** ファイルの追加時に新しいGUIDを起こすのは問題ない。
