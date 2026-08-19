using System.Collections.Generic;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// VRChat/MMD → ARKit の自動マッピングテーブル（ドメイン知識）
    /// </summary>
    internal static class ARKitMappingTable
    {
        public static List<ARKitMapping> GetMappings()
        {
            return new List<ARKitMapping>
            {
                // === 目 (Eye) ===
                // 注: 左右別のソースが存在する場合はそちらを優先（最初にマッチしたものを使用）
                // 優先順位: 1. 左右別ソース(vrc.blink_left等), 2. 両目用を左右分割(vrc.blink, まばたき等)
                new ARKitMapping("eyeBlinkLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.blink_left", "blink_left", "Blink_L"),
                }),
                new ARKitMapping("eyeBlinkRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.blink_right", "blink_right", "Blink_R"),
                }),
                // 左右別ソースが見つからない場合のフォールバック: 両目用から左右別に生成
                // vrc.blinkを追加（VRChatの標準的な両目まばたき）
                new ARKitMapping("eyeBlinkLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.blink", "まばたき", "ウィンク", "blink"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeBlinkRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.blink", "まばたき", "ウィンク右", "blink"),
                }, BlendShapeSide.RightOnly),
                // eyeSquint - 左右別のソースがある場合
                new ARKitMapping("eyeSquintLeft", new List<SourceMapping> {
                    new SourceMapping(0.7f, "Squint_L", "squint_left"),
                }),
                new ARKitMapping("eyeSquintRight", new List<SourceMapping> {
                    new SourceMapping(0.7f, "Squint_R", "squint_right"),
                }),
                // eyeSquint - 両目用から左右分割
                new ARKitMapping("eyeSquintLeft", new List<SourceMapping> {
                    new SourceMapping(0.7f, "笑い", "にこり", "><", "笑い目"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeSquintRight", new List<SourceMapping> {
                    new SourceMapping(0.7f, "笑い", "にこり", "><", "笑い目"),
                }, BlendShapeSide.RightOnly),
                // eyeWide - 左右別のソースがある場合
                new ARKitMapping("eyeWideLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Wide_L", "wide_left"),
                }),
                new ARKitMapping("eyeWideRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Wide_R", "wide_right"),
                }),
                // eyeWide - 両目用から左右分割
                new ARKitMapping("eyeWideLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "びっくり", "見開き", "驚き"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeWideRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "びっくり", "見開き", "驚き"),
                }, BlendShapeSide.RightOnly),

                // === 視線 (Eye Look) - 通常は手動設定が必要 ===
                // 既に左右別のBlendShapeがある場合
                new ARKitMapping("eyeLookUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeUp_L", "eye_up_L"),
                }),
                new ARKitMapping("eyeLookUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeUp_R", "eye_up_R"),
                }),
                new ARKitMapping("eyeLookDownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeDown_L", "eye_down_L"),
                }),
                new ARKitMapping("eyeLookDownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeDown_R", "eye_down_R"),
                }),
                new ARKitMapping("eyeLookInLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeIn_L", "eye_in_L"),
                }),
                new ARKitMapping("eyeLookInRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeIn_R", "eye_in_R"),
                }),
                new ARKitMapping("eyeLookOutLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeOut_L", "eye_out_L"),
                }),
                new ARKitMapping("eyeLookOutRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeOut_R", "eye_out_R"),
                }),
                // 両目用のBlendShapeから左右分割
                new ARKitMapping("eyeLookUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "目上"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeLookUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "目上"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("eyeLookDownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "目下"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeLookDownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "目下"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("eyeLookInLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "より目"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeLookInRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "より目"),
                }, BlendShapeSide.RightOnly),

                // === 眉毛 (Brow) ===
                // 既に左右別のBlendShapeがある場合
                new ARKitMapping("browDownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "BrowDown_L"),
                }),
                new ARKitMapping("browDownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "BrowDown_R"),
                }),
                // 両眉用から左右分割
                new ARKitMapping("browDownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "怒り", "真面目", "困る"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("browDownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "怒り", "真面目", "困る"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("browInnerUp", new List<SourceMapping> {
                    new SourceMapping(1.0f, "困る", "上", "悲しい", "BrowInnerUp"),
                }),
                new ARKitMapping("browOuterUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "BrowOuterUp_L"),
                }),
                new ARKitMapping("browOuterUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "BrowOuterUp_R"),
                }),
                new ARKitMapping("browOuterUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "上", "驚き"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("browOuterUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "上", "驚き"),
                }, BlendShapeSide.RightOnly),

                // === 口 - 母音 (Mouth Vowels) ===
                new ARKitMapping("jawOpen", new List<SourceMapping> {
                    new SourceMapping(0.7f, "vrc.v_aa", "あ", "a", "A"),
                }),
                new ARKitMapping("mouthFunnel", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ou", "う", "u", "U"),
                }),
                // mouthPuckerは「う」から生成すると口元が破綻しやすいため自動マッピングを持たない。
                // 必要な場合はカスタムマッピングで明示的に指定する

                // === 口 - 表情 (Mouth Expressions) ===
                // 既に左右別のBlendShapeがある場合
                new ARKitMapping("mouthSmileLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Smile_L"),
                }),
                new ARKitMapping("mouthSmileRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Smile_R"),
                }),
                // 両側用から左右分割
                new ARKitMapping("mouthSmileLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "にやり", "∧", "にっこり"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthSmileRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "にやり", "∧", "にっこり"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("mouthFrownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Frown_L"),
                }),
                new ARKitMapping("mouthFrownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Frown_R"),
                }),
                new ARKitMapping("mouthFrownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "への字", "悲しみ"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthFrownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "への字", "悲しみ"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("mouthLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "口左", "MouthLeft"),
                }),
                new ARKitMapping("mouthRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "口右", "MouthRight"),
                }),
                // mouthUpperUp/LowerDown - 左右分割
                new ARKitMapping("mouthUpperUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ih", "い", "i", "I"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthUpperUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ih", "い", "i", "I"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("mouthLowerDownLeft", new List<SourceMapping> {
                    new SourceMapping(0.6f, "vrc.v_aa", "あ", "a", "A"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthLowerDownRight", new List<SourceMapping> {
                    new SourceMapping(0.6f, "vrc.v_aa", "あ", "a", "A"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("mouthClose", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_nn", "ん", "n", "N"),
                }),
                new ARKitMapping("mouthShrugUpper", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ch", "え", "e", "E"),
                }),
                new ARKitMapping("mouthShrugLower", new List<SourceMapping> {
                    new SourceMapping(0.5f, "vrc.v_oh", "お", "o", "O"),
                }),
                new ARKitMapping("mouthPress", new List<SourceMapping> {
                    new SourceMapping(1.0f, "むっ", "MouthPress"),
                }),
                new ARKitMapping("mouthStretchLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ih", "い", "i"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthStretchRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ih", "い", "i"),
                }, BlendShapeSide.RightOnly),

                // === 頬 (Cheek) ===
                new ARKitMapping("cheekPuff", new List<SourceMapping> {
                    new SourceMapping(1.0f, "ぷく", "膨らみ", "CheekPuff"),
                }),
                // 既に左右別がある場合
                new ARKitMapping("cheekSquintLeft", new List<SourceMapping> {
                    new SourceMapping(0.8f, "CheekSquint_L"),
                }),
                new ARKitMapping("cheekSquintRight", new List<SourceMapping> {
                    new SourceMapping(0.8f, "CheekSquint_R"),
                }),
                // 両側用から左右分割
                new ARKitMapping("cheekSquintLeft", new List<SourceMapping> {
                    new SourceMapping(0.8f, "笑い", "にこり"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("cheekSquintRight", new List<SourceMapping> {
                    new SourceMapping(0.8f, "笑い", "にこり"),
                }, BlendShapeSide.RightOnly),

                // === 鼻 (Nose) ===
                new ARKitMapping("noseSneerLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "NoseSneer_L"),
                }),
                new ARKitMapping("noseSneerRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "NoseSneer_R"),
                }),
                new ARKitMapping("noseSneerLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "怒り"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("noseSneerRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "怒り"),
                }, BlendShapeSide.RightOnly),

                // === 顎 (Jaw) ===
                new ARKitMapping("jawForward", new List<SourceMapping> {
                    new SourceMapping(1.0f, "JawForward"),
                }),
                new ARKitMapping("jawLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "JawLeft"),
                }),
                new ARKitMapping("jawRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "JawRight"),
                }),

                // === 舌 (Tongue) ===
                new ARKitMapping("tongueOut", new List<SourceMapping> {
                    new SourceMapping(1.0f, "べー", "舌", "TongueOut"),
                }),
            };
        }
    }
}
