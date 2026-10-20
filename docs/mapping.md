# マッピングの詳細

READMEの使用方法から一歩踏み込んだ、マッピングの挙動をまとめる。
前半でカスタムマッピングの照合と適用のしくみを、後半で自動マッピングの補足を扱う。

## カスタムマッピングの照合と適用

ソースBlendShapeの名前はシェイプキー名との完全一致で照合する（大文字小文字や前後の空白も区別される）。
Sideは適用する頂点の範囲を表し、「左のみ」はアバターから見て左半分（メッシュローカル座標のX < 0）、「右のみ」は右半分（X > 0）に適用される。

`Enable Left Right Split` は生成全体で頂点のX座標による左右分割を行うかどうかのスイッチである。
OFFのときはカスタムマッピングのSideも無視され、ソースが両側に適用される。
無視されていることが分かるよう、OFFのあいだSideのドロップダウンは非活性で表示される（設定値そのものは保持され、ONに戻すとまた効く）。

## 生成させたくないBlendShapeの止め方

生成させたくないBlendShapeがある場合は、マッピングの有効チェックを外す。
外して止まるのはこのカスタム定義だけで、そのARKit名が自動マッピングで対応できる場合は引き続き自動マッピングで生成される。
自動マッピングや手続き的生成にも生成させたくないときは、マッピングを有効にしたままソースのシェイプキー名を未指定にする（ソースを1件以上持つ有効な定義のARKit名は、生成の成否にかかわらず自動マッピングと手続き的生成の対象から外れる）。

Weightはソースごとの変形に掛ける係数のため、0を指定しても無効化にはならない。
そのソースの変形が乗らないBlendShapeが生成される（`Overwrite Existing` 有効時は同名の既存シェイプキーがその生成結果に置き換えられる）。

## 重複の判定

同じARKit名の有効なマッピングが複数あると重複と判定され、生成全体が中止される。
無効なマッピングは重複の対象外なので、定義を差し替えるあいだ、元の定義を無効にしたまま取り置きできる。

## 対応シェイプキー名の一覧

インスペクタの「自動マッピング一覧（参照用）」に、ARKit名ごとの対応シェイプキー名が表示される。
この一覧はマッピング定義から生成しているため、定義を変更すれば表示も追随する。

## Unified Expressions名への対応表

`Output Naming` をUnified Expressionsか両方にすると、ARKit名で計画した生成物を次の対応で出力する。
対応表の実体は `Editor/Domain/OutputBlendShapeNameTable.cs` にある。

表にない名前は、先頭を大文字にするだけで対応する（`jawOpen` → `JawOpen`、`eyeWideLeft` → `EyeWideLeft` など）。
ARKit名として知らない名前（カスタムマッピングで自由に付けた名前）は変換しない。

| ARKit名 | Unified Expressions名 | 分け方 |
|---|---|---|
| eyeBlinkLeft / Right | EyeClosedLeft / Right | 名前のみ |
| mouthClose | MouthClosed | 名前のみ |
| mouthShrugUpper / Lower | MouthRaiserUpper / Lower | 名前のみ |
| mouthRollUpper / Lower | LipSuckUpper / Lower | 名前のみ |
| mouthPucker | LipPucker | 名前のみ |
| browInnerUp | BrowInnerUpLeft + BrowInnerUpRight | 左右分割 |
| cheekPuff | CheekPuffLeft + CheekPuffRight | 左右分割 |
| mouthPress | MouthPressLeft + MouthPressRight | 左右分割 |
| mouthFunnel | LipFunnelUpper + LipFunnelLower | 上下分割 |
| mouthLeft | MouthUpperLeft + MouthLowerLeft | 上下分割 |
| mouthRight | MouthUpperRight + MouthLowerRight | 上下分割 |

`browDownLeft/Right` と `mouthSmileLeft/Right` は、Unified Expressionsでは複数の基底キー（BrowLowerer + BrowPinch、MouthCornerPull + MouthCornerSlant）に分かれているが、同名の統合キー `BrowDownLeft/Right`、`MouthSmileLeft/Right` が定義されているため、先頭の大文字化だけで対応する。
基底キーに分けるには形の情報が足りず、同じ形を両方に出すと同時に動いたときに二重になる。

左右分割は左右分割の設定（`Enable Left Right Split`）に従い、OFFのときは両側に元の形を出す。
上下分割は口の手続き的生成と同じ口領域の検出で唇の境界線を求め、境界線より上を上側、下を下側に分ける。
境界付近はブレンド帯で滑らかに遷移し、上側と下側を足すと元の形に戻る。
口のシェイプキーが無く境界線を求められないメッシュでは、上下どちらにも元の形を半分の強度で出す。

## ネイティブフェイストラッキングのBlendshapes Setの対応表

`Write Native FT Blendshapes Set` を有効にすると、ビルド時にAvatar Descriptorへ書き込むBlendshapes Setの割り当てを、生成後のメッシュにあるシェイプキー名から決める。
対応表の実体は `Editor/Domain/FaceTrackingBlendshapesSetTable.cs` にあり、Descriptorへの書き込みは `Editor/Infra/FaceTrackingDescriptorWriter.cs` が行う。

VRChatのネイティブフェイストラッキングは、Unified Expressionsの基底キー102個を受け取る。
Setはキーごとにシェイプキー名を1つ持ち、空欄のキーは何もしない。
キーと同名のシェイプキーを実行時に自動で結び付ける挙動は無い。
SDKのエディタにある自動入力（Auto Fill）はキー名の完全一致を最優先し、次に候補名を大文字小文字を無視して探す。
本ツールの対応表は、その候補のうち本ツールの出力名に当たるものだけを持ち、同じ優先順位で選ぶ。

キーごとの候補は、キー名そのもの、本ツールのUnified Expressions名、ARKit名の順である。
大半のキーは「キー名 = 本ツールのUnified Expressions名」で、その次がARKit名（`JawOpen` → `jawOpen` など）になる。
次の表は、キー名と本ツールの出力名が異なるものだけを挙げる。

| トラッキングキー | 候補（優先順） | 備考 |
|---|---|---|
| EyeClosedLeft / Right | eyeBlinkLeft / Right | 名前の違い |
| BrowPinchLeft / Right、BrowLowererLeft / Right | BrowDownLeft / Right → browDownLeft / Right | 統合名を分かれた基底キーの両方へ |
| BrowInnerUpLeft / Right | browInnerUp | ARKitは左右に分かれていない |
| CheekPuffLeft / Right | cheekPuff | 同上 |
| MouthClosed | mouthClose | 名前の違い |
| LipSuckUpperLeft / Right | LipSuckUpper → mouthRollUpper | 本ツールは左右に分けずに出す |
| LipSuckLowerLeft / Right | LipSuckLower → mouthRollLower | 同上 |
| LipFunnelUpperLeft / Right | LipFunnelUpper → mouthFunnel | 同上 |
| LipFunnelLowerLeft / Right | LipFunnelLower → mouthFunnel | 同上 |
| LipPuckerUpperLeft / Right、LipPuckerLowerLeft / Right | LipPucker → mouthPucker | 本ツールは上下左右に分けずに出す |
| MouthUpperLeft、MouthLowerLeft | mouthLeft | ARKitは上下に分かれていない |
| MouthUpperRight、MouthLowerRight | mouthRight | 同上 |
| MouthCornerPullLeft / Right、MouthCornerSlantLeft / Right | MouthSmileLeft / Right → mouthSmileLeft / Right | 統合名を分かれた基底キーの両方へ |
| MouthRaiserUpper / Lower | mouthShrugUpper / Lower | 名前の違い |
| MouthPressLeft / Right | mouthPress | ARKitは左右に分かれていない |

本ツールが生成しないキー（EyeDilation、CheekSuck、JawClench、舌の細かい動き、喉など）は、メッシュにキー名そのもののシェイプキーがあればそれを割り当て、無ければ空欄にする。

1つのシェイプキーを複数のキーへ割り当てた場合、それらのキーが同時に動くと変形が重なる。
SDKの自動入力も同じ割り当てを行うため、本ツールの対応表もそれに合わせている。

キーの並びはSDKの列挙体（`VRCAvatarDescriptor.FaceTrackingKey`）に合わせている。
Setはキーの番号を添字にした配列のため、並びがずれると全部の割り当てが別のキーへ行く。
書き込みの直前にSDKの列挙体と突き合わせ、食い違えば書き込まずにエラーを出す。

## mouthPucker を自動生成しない理由

`mouthPucker` は「う」から生成すると口元が破綻しやすいため、自動マッピングとプリセット（「VRChat標準表情のみで設定」）のどちらにも含めていない。
ARKit名としては残してあるので、必要なアバターではカスタムマッピングで手動指定できる。

以前のバージョンでプリセットを適用したアバターでは、`mouthPucker` の定義がカスタムマッピングとして保存されたまま残る。
カスタムマッピングは手動設定として扱うため、更新しても書き換えない。
生成を止めるには、その行を削除するか、プリセットを適用し直す（プリセットは適用前にカスタムマッピングを全て削除する）。
