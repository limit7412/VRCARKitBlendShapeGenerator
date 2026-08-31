# Kx VRC ARKit BlendShape Generator

顔トラ機材を投入するのにBlendShapeが無いアバターをどうにかするために既存表情からARKit用BlendShapeを生成するやつをClaude Codeに作らせました
あくまで自分用の簡易的なもので100%生成したコードなのでメンテは期待しないでください

以下生成した説明文

VRChat/MMDのBlendShapeからARKit用BlendShapeを自動生成するNDMFプラグインです。
Jerry's Templatesと組み合わせて使用することで、フェイストラッキング非対応アバターを簡単に対応させることができます。

## 必要要件

- Unity 2022.3.6f1以降
- [NDMF](https://ndmf.nadena.dev/) 1.5.0以降（プレビュー機構がNDMF 1.5.0で追加されたため、1.4系ではコンパイルできません）
- VRChat SDK (Avatars)

## インストール

### VCC/ALCOM経由（推奨）

1. [VPMリポジトリ](https://limit7412.github.io/vcc-vpm/)をVCC/ALCOMに追加
2. プロジェクトに「Kx VRC ARKit BlendShape Generator」を追加

### 手動インストール

1. Releasesからzipファイルをダウンロード
2. VCCのプロジェクト管理画面で「Add Package」→「Add from Archive」を選択
3. ダウンロードしたzipファイルを選択

### booth版（unitypackage）

Releasesの `VRCARKitBlendShapeGenerator_<バージョン>.zip` に、unitypackageが1つ入っています。
展開してUnityへドラッグすると `Assets/AtelierKairox/VRCARKitBlendShapeGenerator/` へ取り込まれます。

**VPM版と同時に入れないでください。**
どちらもアセンブリ名が同じため、1つのプロジェクトへ両方を入れるとコンパイルが通りません。

更新は下の「更新の確認」の通知から行えます。
手で入れ直す場合は `Assets/AtelierKairox/VRCARKitBlendShapeGenerator/` をフォルダごと削除してから、新しいunitypackageを取り込んでください。
unitypackageの取り込みはファイルの追加と上書きだけを行うため、上書きするだけでは、そのバージョンで削除されたファイルが残ります。

アバターに追加済みのコンポーネントは、削除して入れ直しても失われません。
配布物のGUIDをリポジトリで固定しており、バージョンが変わっても参照が切れないようにしています。

### 更新の確認

新しいバージョンが出ているかを[Releases](https://github.com/limit7412/VRCARKitBlendShapeGenerator/releases/latest)へ問い合わせます。
問い合わせはエディタの読み込み時に自動で走るため、インスペクタを開く必要はありません。

エディタから外部へ通信しますが、送るのはリリース情報の取得要求だけで、1日1回までです。

通信を望まない場合は Preferences > ARKit BlendShape Generator から止められます。
止めれば問い合わせを行わず、更新の通知も出ません。

知らせるのは安定版が出たときだけです。
プレリリース（`0.1.10-test4` など）を入れている場合も、同じバージョンの安定版が出た時点で更新として扱います。

更新の手段はインストール方法で違うため、扱いもそれに合わせて変わります。

`Packages/` 配下（VCC/ALCOM経由）の場合は、新しいバージョンが出ていることをインスペクタで知らせるだけにとどめます。
バージョンはVCC/ALCOMが `vpm-manifest.json` で管理しているため、こちらでファイルを置き換えると管理側の記録と実態がずれます。

`Assets/` 配下（unitypackageから導入）の場合は、そのまま更新できます。
新しいバージョンを見つけると、そのバージョンについて一度だけダイアログで知らせます。
そこで「あとで」を選んだ場合は、インスペクタの先頭に出る「更新する」ボタンからいつでも実行できます。

更新は、Releasesからbooth用zipを取得し、GitHubが示すダイジェスト（sha256）と照合してから取り込みます。
取り込みの前に、新しいバージョンに無くなったファイルを削除し、更新前のフォルダを `Temp/` へ控えます。
取り込みは取り消せないため、実行前に確認を挟みます。
フォルダの中を自分で書き換えている場合は、先に控えを取ってください。

フォルダを `Assets/AtelierKairox/VRCARKitBlendShapeGenerator/` から動かしている場合、この更新は行えません。
取り込み先はunitypackageの側で決まっており、手元の位置へは追従しないため、実行すると同じアセンブリがプロジェクトに二組できてしまいます。
この場合は上の「booth版」の手順で入れ直してください。

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
| **Enable Left Right Split** | 頂点のX座標による左右分割を行う（まばたき等を左右別々に生成）。OFFにするとカスタムマッピングのSide指定も無視され、両側に適用される |
| **Blend Width** | 左右分割時のグラデーション幅（中央付近で左右をブレンドする範囲、0.001〜0.1。メッシュローカル座標で中心から片側への幅を指定するため、グラデーション全体は指定値の2倍） |
| **Overwrite Existing** | 既存のARKit BlendShapeを上書きする |
| **Enable Procedural Mouth Shapes** | 既存シェイプキーから生成できない口周りのBlendShapeを頂点移動で自動生成する（デフォルト: 無効） |
| **Procedural Mouth Intensity** | 手続き的生成の変形量係数（0.1〜2.0） |
| **Enable Mouth Cancellation** | 生成した口関連BlendShapeに、指定したBlendShapeの打ち消し成分を焼き込む（デフォルト: 無効） |
| **Mouth Cancellation Strength** | 打ち消し量の全体係数（0.0〜1.0） |
| **Custom Mappings** | 自動マッピングできないBlendShapeを手動で指定 |
| **Debug Mode** | デバッグログを出力する |

Target Renderer が空のときは、コンポーネントの直下にある `Body` / `body` / `Face` / `face` / `Head` / `head` のいずれかの名前を持つSkinnedMeshRendererを優先し、見つからなければ自身を含む子孫の最初のSkinnedMeshRendererを対象にします。
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
`Enable Left Right Split` は生成全体で頂点のX座標による左右分割を行うかどうかのスイッチです。
OFFのときはカスタムマッピングのSideも無視され、ソースが両側に適用されます。
無視されていることが分かるよう、OFFのあいだSideのドロップダウンは非活性で表示されます（設定値そのものは保持され、ONに戻すとまた効きます）。

生成させたくないBlendShapeがある場合は、マッピングの有効チェックを外します。
外して止まるのはこのカスタム定義だけで、そのARKit名が自動マッピングで対応できる場合は引き続き自動マッピングで生成されます。
自動マッピングや手続き的生成にも生成させたくないときは、マッピングを有効にしたままソースのシェイプキー名を未指定にします（ソースを1件以上持つ有効な定義のARKit名は、生成の成否にかかわらず自動マッピングと手続き的生成の対象から外れます）。
Weightはソースごとの変形に掛ける係数のため、0を指定しても無効化にはならず、そのソースの変形が乗らないBlendShapeが生成されます（`Overwrite Existing` 有効時は同名の既存シェイプキーがその生成結果に置き換えられます）。
なお、同じARKit名の有効なマッピングが複数あると重複と判定され、生成全体が中止されます。
無効なマッピングは重複の対象外なので、定義を差し替えるあいだ、元の定義を無効にしたまま取り置きできます。

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
- **口**: jawOpen, jawLeft/Right/Forward, mouthFunnel, mouthSmileLeft/Right, mouthFrownLeft/Right, mouthLeft/Right, mouthUpperUpLeft/Right, mouthLowerDownLeft/Right, mouthStretchLeft/Right, mouthClose, mouthShrugUpper/Lower, mouthPress
- **頬**: cheekPuff, cheekSquintLeft/Right
- **鼻**: noseSneerLeft/Right
- **舌**: tongueOut

VRChat/MMDの標準的なBlendShape名（vrc.blink, まばたき, あ, い, う等）から自動的にマッピングされます。
自動マッピングでは、対応するシェイプキーがアバターに無いものは生成されません（口の一部は後述の手続き的生成で補えます）。
`mouthPucker` は「う」から生成すると口元が破綻しやすいため、自動マッピングとプリセット（「VRChat標準表情のみで設定」）のどちらにも含めていません。
ARKit名としては残してあるので、必要なアバターではカスタムマッピングで手動指定できます。
以前のバージョンでプリセットを適用したアバターでは、`mouthPucker` の定義がカスタムマッピングとして保存されたまま残ります。
カスタムマッピングは手動設定として扱うため、更新しても書き換えません。
生成を止めるには、その行を削除するか、プリセットを適用し直してください（プリセットは適用前にカスタムマッピングを全て削除します）。
視線（Eye Look）は `EyeUp_L` や `目上` といったシェイプキーを持つアバターが少ないため、多くの場合はカスタムマッピングでの手動設定が必要です。
インスペクタの「自動マッピング一覧（参照用）」に、ARKit名ごとの対応シェイプキー名が表示されます。
この一覧はマッピング定義から生成しているため、定義を変更すれば表示も追随します。

### 口の手続き的生成

`Enable Procedural Mouth Shapes` を有効にすると、対応するシェイプキーがアバターに存在しない場合でも、以下の口周りBlendShapeが頂点の移動により自動生成されます（既存シェイプキーは口領域の検出にのみ使用されます）：

- mouthLeft / mouthRight（口の左右移動）
- jawLeft / jawRight / jawForward（顎の左右・前方移動）
- mouthShrugUpper / mouthShrugLower
- mouthUpperUpLeft/Right, mouthLowerDownLeft/Right

口領域の検出には `vrc.v_aa`、`あ`、`vrc.v_nn`、`ん` などの口のシェイプキーが必要です。
いずれも見つからないアバターでは、有効にしても何も生成されません。

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

開発者向けの情報は `docs/` にまとめています。

- [コードリーディングガイド](docs/code-reading.md)：コード全体の構造と読み始めの入口
- [開発環境とテストの実行](docs/development.md)：テストの置き場所と手元での実行手順
- [配布パッケージ](docs/packaging.md)：リリースzipの構成とGUIDを固定している理由
- [CIでのテスト実行](docs/ci.md)：PRごとのテストと検証、Unityライセンスの設定

## ライセンス

MIT License

## 作者

kairox ([@limit7412](https://github.com/limit7412))
