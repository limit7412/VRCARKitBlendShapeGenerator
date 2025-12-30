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
| **Target Renderer** | 対象のSkinnedMeshRenderer（空の場合はBody/Faceを自動検出） |
| **Intensity Multiplier** | 生成時の強度係数（0.5〜1.5推奨） |
| **Enable Left Right Split** | 左右分割を有効化（まばたき等を左右別々に生成） |
| **Blend Width** | 左右分割時のグラデーション幅（中央付近で左右をブレンドする範囲、0.001〜0.1） |
| **Overwrite Existing** | 既存のARKit BlendShapeを上書きする |
| **Custom Mappings** | 自動マッピングできないBlendShapeを手動で指定 |
| **Debug Mode** | デバッグログを出力する |

### カスタムマッピング

自動マッピングで対応できない場合は、カスタムマッピングを使用して手動でBlendShapeを指定できます。

1. Custom Mappingsセクションを展開
2. 「+」ボタンで新しいマッピングを追加
3. ARKit名（例: `eyeBlinkLeft`）とソースBlendShapeを指定
4. 必要に応じてWeight（重み）とSide（左右フィルタ）を調整

### NDMFプレビュー

NDMFのプレビュー機能を使用して、生成結果をリアルタイムで確認できます。

1. Unityメニュー > Tools > NDMFを開く
2. 「Kx VRC ARKit BlendShape Generator」のプレビューを有効化
3. 生成されるBlendShapeをシーンビューで確認

## 対応BlendShape

以下のARKit BlendShapeを自動生成します：

- **目**: eyeBlinkLeft/Right, eyeSquintLeft/Right, eyeWideLeft/Right
- **眉**: browDownLeft/Right, browInnerUp, browOuterUpLeft/Right
- **口**: jawOpen, mouthFunnel, mouthPucker, mouthSmileLeft/Right, etc.
- **頬**: cheekPuff, cheekSquintLeft/Right
- **鼻**: noseSneerLeft/Right
- **舌**: tongueOut

VRChat/MMDの標準的なBlendShape名（vrc.blink, まばたき, あ, い, う等）から自動的にマッピングされます。

## ライセンス

MIT License

## 作者

kairox ([@limit7412](https://github.com/limit7412))
