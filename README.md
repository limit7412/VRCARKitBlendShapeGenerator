# Kx VRC ARKit BlendShape Generator

顔トラ機材を投入するのにBlendShapeが無いアバターをどうにかするために既存表情からARKit用BlendShapeを生成するやつをClaude Codeに作らせました
あくまで自分用の簡易的なもので100%生成したコードなのでメンテは期待しないでください

以下生成した説明文

VRChat/MMDのBlendShapeからARKit用BlendShapeを自動生成するNDMFプラグインです。
Jerry's Templatesと組み合わせて使用することで、フェイストラッキング非対応アバターを簡単に対応させることができます。

## 必要要件

- Unity 2022.3.6f1以降
- [NDMF](https://ndmf.nadena.dev/) 1.4.0以降
- VRChat SDK (Avatars)

## インストール

### VCC/ALCOM経由（推奨）

1. [VPMリポジトリ](https://limit7412.github.io/vcc-vpm/)をVCC/ALCOMに追加
2. プロジェクトに「Kx VRC ARKit BlendShape Generator」を追加

### 手動インストール

1. Releasesからzipファイルをダウンロード
2. VCCのプロジェクト管理画面で「Add Package」→「Add from Archive」を選択
3. ダウンロードしたzipファイルを選択

## 使用方法

### 基本的な使い方

1. アバターのルートオブジェクトまたは顔メッシュを持つオブジェクトを選択
2. Inspector > Add Component > **KxVRCARKitBlendShapeGenerator** > **Kx VRC ARKit BlendShape Generator** を追加
3. [Jerry's Templates (MA版)](https://github.com/Adjerry91/VRCFaceTracking-Templates) をアバターに追加
4. アップロード時に自動的にARKit BlendShapeが生成されます

### 設定項目

| 項目 | 説明 |
| ---- | ---- |
| **Target Renderer** | 対象のSkinnedMeshRenderer（空の場合はBody/Face/Headを自動検出） |
| **Intensity Multiplier** | 生成時の強度係数（0.5〜1.5推奨） |
| **Enable Left Right Split** | 左右分割を有効化（まばたき等を左右別々に生成）。OFFにするとカスタムマッピングのSide指定も無視され、両側に適用される |
| **Blend Width** | 左右分割時のグラデーション幅（中央付近で左右をブレンドする範囲、0.001〜0.1。メッシュローカル座標のX幅） |
| **Overwrite Existing** | 既存のARKit BlendShapeを上書きする |
| **Enable Procedural Mouth Shapes** | 既存シェイプキーから生成できない口周りのBlendShapeを頂点移動で自動生成する（デフォルト: 無効） |
| **Procedural Mouth Intensity** | 手続き的生成の変形量係数（0.1〜2.0） |
| **Enable Mouth Cancellation** | 生成した口関連BlendShapeに、指定したBlendShapeの打ち消し成分を焼き込む（デフォルト: 無効） |
| **Mouth Cancellation Strength** | 打ち消し量の全体係数（0.0〜1.0） |
| **Custom Mappings** | 自動マッピングできないBlendShapeを手動で指定 |
| **Debug Mode** | デバッグログを出力する |

Target Renderer が空のときは、コンポーネントの直下にある `Body` / `body` / `Face` / `face` / `Head` / `head` のいずれかの名前を持つSkinnedMeshRendererを優先し、見つからなければ子孫の最初のSkinnedMeshRendererを対象にします。
どちらの検索も非アクティブなオブジェクトを含みます。
この探索順序はビルド、プレビュー、インスペクタ表示のすべてで共通です。

### カスタムマッピング

自動マッピングで対応できない場合は、カスタムマッピングを使用して手動でBlendShapeを指定できます。

1. Custom Mappingsセクションを展開
2. 「+」ボタンで新しいマッピングを追加
3. ARKit名（例: `eyeBlinkLeft`）とソースBlendShapeを指定
4. 必要に応じてWeight（重み）とSide（左右フィルタ）を調整

ソースBlendShapeの名前はシェイプキー名との完全一致で照合します（大文字小文字や前後の空白も区別されます）。
Sideは適用する頂点の範囲を表し、「左のみ」はアバターから見て左半分（メッシュローカル座標のX < 0）、「右のみ」は右半分（X > 0）に適用されます。
`Enable Left Right Split` がOFFのときはSideの指定は無視され、ソースが両側に適用されます。

### NDMFプレビュー

NDMFのプレビュー機能を使用して、生成結果をリアルタイムで確認できます。

1. Unityメニュー > Tools > NDM Framework > Configure Previews を開く
2. 「Kx VRC ARKit BlendShape Generator」のプレビューを有効化
3. 生成されるBlendShapeをシーンビューで確認

コンポーネントのインスペクタにある「NDMF Preview」のON/OFFボタンからも同じ設定を切り替えられます。

## 対応BlendShape

以下のARKit BlendShapeを自動生成します：

- **目**: eyeBlinkLeft/Right, eyeSquintLeft/Right, eyeWideLeft/Right
- **視線**: eyeLookUpLeft/Right, eyeLookDownLeft/Right, eyeLookInLeft/Right, eyeLookOutLeft/Right
- **眉**: browDownLeft/Right, browInnerUp, browOuterUpLeft/Right
- **口**: jawOpen, jawLeft/Right/Forward, mouthFunnel, mouthPucker, mouthSmileLeft/Right, mouthFrownLeft/Right, mouthLeft/Right, mouthUpperUpLeft/Right, mouthLowerDownLeft/Right, mouthStretchLeft/Right, mouthClose, mouthShrugUpper/Lower, mouthPress
- **頬**: cheekPuff, cheekSquintLeft/Right
- **鼻**: noseSneerLeft/Right
- **舌**: tongueOut

VRChat/MMDの標準的なBlendShape名（vrc.blink, まばたき, あ, い, う等）から自動的にマッピングされます。
対応するシェイプキーがアバターに無いものは生成されません。視線（Eye Look）は `EyeUp_L` や `目上` といったシェイプキーを持つアバターが少ないため、多くの場合はカスタムマッピングでの手動設定が必要です。
インスペクタの「自動マッピング一覧（参照用）」に、ARKit名ごとの対応シェイプキー名が表示されます。

### 口の手続き的生成

`Enable Procedural Mouth Shapes` を有効にすると、対応するシェイプキーがアバターに存在しない場合でも、以下の口周りBlendShapeが頂点の移動により自動生成されます（既存シェイプキーは口領域の検出にのみ使用されます）：

- mouthLeft / mouthRight（口の左右移動）
- jawLeft / jawRight / jawForward（顎の左右・前方移動）
- mouthShrugUpper / mouthShrugLower
- mouthUpperUpLeft/Right, mouthLowerDownLeft/Right

シェイプキーから生成できた場合はそちらが優先され、手続き的生成はフォールバックとして動作します。デフォルトでは無効です。変形量は `Procedural Mouth Intensity` で調整できます。変形は口の前面（唇側）が最も大きく、奥の頂点ほど減衰するため、単純な平行移動よりも自然な動きになります。

### 口の打ち消し

`Enable Mouth Cancellation` を有効にすると、生成した口関連BlendShapeに、指定したBlendShapeの変形を打ち消す成分（逆方向のデルタ）を焼き込みます。
口角調整などのBlendShapeを常時適用しているアバターで、その変形とフェイストラッキングによる口の動きが二重に適用されるのを防ぐ用途を想定しています。

**生成元のBlendShapeの側で既に修正済み（打ち消したい変形を含まない形で作られている）の場合、この設定は不要です。**

設定手順:

1. `Enable Mouth Cancellation` を有効化
2. 「打ち消すBlendShape」に対象のBlendShapeを追加し、重みにアバター側での適用量を指定（1.0 = ウェイト100）
    - Sideはアバター側での適用範囲を表すため、`Enable Left Right Split` がOFFでも指定どおりに適用されます（カスタムマッピングのSideとは扱いが異なります）
3. 「焼き込み先のARKit BlendShape」で、打ち消しを入れるARKit名を選択（デフォルト: `jawOpen`）

BlendShapeは線形合成されるため、焼き込み先を複数選ぶと、それらが同時に適用されたときに打ち消しが重なって対象の変形を通り越し、逆向きに変形します。焼き込み先は必要最小限（多くの場合は `jawOpen` のみ）に絞ってください。打ち消し量は `Mouth Cancellation Strength` で弱めることもできます。

## 開発

### テスト

生成ロジックのテストは `Tests/Editor` にEditModeテストとして置いています。
メッシュの読み書きは `IMeshRepository` 越しに行っているため、テストでは `UnityEngine.Mesh` を使わずインメモリ実装（`FakeMeshRepository`）へ差し替えて検証します。

テストはCIでは実行していません。
`Editor/` `Runtime/` を変更したときは、PRを出す前に手元で実行してください。

1. このリポジトリをUnityプロジェクトの `Packages/` 以下へ配置する（VCC/ALCOM経由で導入したものは書き換えられないため、開発時はクローンを直接置く）
2. `Packages/manifest.json` の `testables` にパッケージ名を追加する

    ```json
    {
      "dependencies": { },
      "testables": [ "com.qazx7412.kx-vrc-arkit-blendshape-generator" ]
    }
    ```

3. Unityメニュー > Window > General > Test Runner を開く
4. EditModeタブで `ARKitBlendShapeGenerator.Editor.Tests` を実行する

`testables` へ追加しないとテストアセンブリがコンパイルされず、Test Runnerの一覧にも現れません。
テストは配布物には含みません（リリース用のzipへ入れるのは `package.json` と `Runtime/` `Editor/` だけです）。

## ライセンス

MIT License

## 作者

kairox ([@limit7412](https://github.com/limit7412))
