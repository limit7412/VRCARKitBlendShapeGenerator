using System.Collections.Generic;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// 生成するBlendShape1件の出力先。
    /// 名前に加えて、正準名の変形から切り出す範囲（左右・上下）を持つ
    /// </summary>
    internal sealed class OutputBlendShape
    {
        public readonly string Name;
        public readonly BlendShapeSide Side;
        public readonly BlendShapeLipMask LipMask;

        public OutputBlendShape(
            string name,
            BlendShapeSide side = BlendShapeSide.Both,
            BlendShapeLipMask lipMask = BlendShapeLipMask.All)
        {
            Name = name;
            Side = side;
            LipMask = lipMask;
        }
    }

    /// <summary>
    /// 正準名（ARKit名）から、出力名の種別ごとの出力先を決める変換表。
    ///
    /// 生成の計画と合成はすべてARKit名で行い、名前の切り替えはメッシュへ書く直前のここだけで行う。
    /// カスタムマッピングと打ち消しの焼き込み先はARKit名で保存されているため、
    /// 正準名を固定しておけば既存のコンポーネント設定は種別を変えてもそのまま使える。
    ///
    /// Unified Expressions（UE）の名前はVRCFTが定めた体系に従う。
    /// ARKitの1シェイプがUEでは左右や上下に分かれているものは、変形を分割して複数の出力先へ出す。
    /// 統合名（BrowDownLeft、MouthSmileLeft）は、UEが「複数の基底キーをまとめた名前」として
    /// 定義しているもので、ARKitの形がそのまま対応する。
    /// </summary>
    internal static class OutputBlendShapeNameTable
    {
        /// <summary>
        /// 先頭の大文字化だけでは対応しないARKit名の、UEでの出力先
        /// </summary>
        private static readonly Dictionary<string, OutputBlendShape[]> UnifiedExpressionsOverrides =
            new Dictionary<string, OutputBlendShape[]>
            {
                // 1対1だが単語が違う
                { "eyeBlinkLeft", new[] { new OutputBlendShape("EyeClosedLeft") } },
                { "eyeBlinkRight", new[] { new OutputBlendShape("EyeClosedRight") } },
                { "mouthClose", new[] { new OutputBlendShape("MouthClosed") } },
                { "mouthShrugUpper", new[] { new OutputBlendShape("MouthRaiserUpper") } },
                { "mouthShrugLower", new[] { new OutputBlendShape("MouthRaiserLower") } },
                { "mouthRollUpper", new[] { new OutputBlendShape("LipSuckUpper") } },
                { "mouthRollLower", new[] { new OutputBlendShape("LipSuckLower") } },
                { "mouthPucker", new[] { new OutputBlendShape("LipPucker") } },

                // 左右分割
                {
                    "browInnerUp", new[]
                    {
                        new OutputBlendShape("BrowInnerUpLeft", BlendShapeSide.LeftOnly),
                        new OutputBlendShape("BrowInnerUpRight", BlendShapeSide.RightOnly),
                    }
                },
                {
                    "cheekPuff", new[]
                    {
                        new OutputBlendShape("CheekPuffLeft", BlendShapeSide.LeftOnly),
                        new OutputBlendShape("CheekPuffRight", BlendShapeSide.RightOnly),
                    }
                },
                {
                    "mouthPress", new[]
                    {
                        new OutputBlendShape("MouthPressLeft", BlendShapeSide.LeftOnly),
                        new OutputBlendShape("MouthPressRight", BlendShapeSide.RightOnly),
                    }
                },

                // 上下分割
                {
                    "mouthFunnel", new[]
                    {
                        new OutputBlendShape("LipFunnelUpper", BlendShapeSide.Both, BlendShapeLipMask.Upper),
                        new OutputBlendShape("LipFunnelLower", BlendShapeSide.Both, BlendShapeLipMask.Lower),
                    }
                },
                {
                    "mouthLeft", new[]
                    {
                        new OutputBlendShape("MouthUpperLeft", BlendShapeSide.Both, BlendShapeLipMask.Upper),
                        new OutputBlendShape("MouthLowerLeft", BlendShapeSide.Both, BlendShapeLipMask.Lower),
                    }
                },
                {
                    "mouthRight", new[]
                    {
                        new OutputBlendShape("MouthUpperRight", BlendShapeSide.Both, BlendShapeLipMask.Upper),
                        new OutputBlendShape("MouthLowerRight", BlendShapeSide.Both, BlendShapeLipMask.Lower),
                    }
                },
            };

        private static readonly HashSet<string> KnownArkitNames = new HashSet<string>(ARKitBlendShapeNames.GetAll());

        /// <summary>
        /// 正準名を出力先へ展開する。
        ///
        /// ARKit名として知らない名前（カスタムマッピングで自由に付けた名前）は変換せずそのまま出す。
        /// UE名を直接書いたカスタムマッピングを、UEモードで二重に変換しないためである。
        /// 「両方」でも知らない名前は1件だけ出す
        /// </summary>
        public static IReadOnlyList<OutputBlendShape> Resolve(string arkitName, BlendShapeNaming naming)
        {
            if (string.IsNullOrEmpty(arkitName))
            {
                return new OutputBlendShape[0];
            }

            var arkit = new OutputBlendShape(arkitName);
            if (naming == BlendShapeNaming.ARKit || !KnownArkitNames.Contains(arkitName))
            {
                return new[] { arkit };
            }

            var unified = ResolveUnifiedExpressions(arkitName);
            if (naming == BlendShapeNaming.UnifiedExpressions)
            {
                return unified;
            }

            var both = new List<OutputBlendShape>(unified.Length + 1) { arkit };
            both.AddRange(unified);
            return both;
        }

        /// <summary>
        /// 出力名の種別で、上下分割の出力先が生じうるか。
        /// 唇の境界線の検出を、必要なときだけ走らせるための判定
        /// </summary>
        public static bool RequiresLipMask(BlendShapeNaming naming)
        {
            return naming != BlendShapeNaming.ARKit;
        }

        private static OutputBlendShape[] ResolveUnifiedExpressions(string arkitName)
        {
            if (UnifiedExpressionsOverrides.TryGetValue(arkitName, out var outputs))
            {
                return outputs;
            }

            return new[] { new OutputBlendShape(CapitalizeFirst(arkitName)) };
        }

        private static string CapitalizeFirst(string name)
        {
            if (char.IsUpper(name[0]))
            {
                return name;
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
    }
}
