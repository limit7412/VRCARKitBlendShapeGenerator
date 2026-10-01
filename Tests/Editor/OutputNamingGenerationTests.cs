using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 出力名の種別（ARKit / Unified Expressions / 両方）が生成結果へどう反映されるかの検証。
    ///
    /// 名前の変換そのものはOutputBlendShapeNameTableTestsで確認しているため、ここでは
    /// 既存ARKitシェイプキーの変換、左右・上下の分割、上書きと手続き的生成との組み合わせを対象にする
    /// </summary>
    public class OutputNamingGenerationTests
    {
        private static Vector3[] TwoVertices() => new[]
        {
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
        };

        // 口領域を模した直方体（左右2 × 上下2 × 前後2）に、口から離れた頂点を1つ足した構成。
        // 唇の境界線は口領域の重心Y=0に決まり、上側（Y=+0.01）と下側（Y=-0.01）はブレンド帯の外に出る。
        // 分割するシェイプのソース名は口領域の検出候補（う、お 等）と重ならないものにする。
        // 候補に当たると口から離れた頂点まで口領域に含まれ、境界線がずれる
        private const float SideX = 0.05f;
        private const float UpperY = 0.01f;
        private const float LowerY = -0.01f;
        private const int RegionVertexCount = 8;

        private static Vector3[] MouthVertices() => new[]
        {
            new Vector3(-SideX, UpperY, 0.05f),
            new Vector3(SideX, UpperY, 0.05f),
            new Vector3(-SideX, LowerY, 0.05f),
            new Vector3(SideX, LowerY, 0.05f),
            new Vector3(-SideX, UpperY, 0f),
            new Vector3(SideX, UpperY, 0f),
            new Vector3(-SideX, LowerY, 0f),
            new Vector3(SideX, LowerY, 0f),
            new Vector3(0f, 0.5f, 0f),
        };

        private static Vector3[] RegionDeltas(Vector3 delta)
        {
            var deltas = new Vector3[MouthVertices().Length];
            for (int i = 0; i < RegionVertexCount; i++)
            {
                deltas[i] = delta;
            }

            return deltas;
        }

        private static Vector3[] UniformDeltas(int count, Vector3 delta)
        {
            var deltas = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                deltas[i] = delta;
            }

            return deltas;
        }

        private static BlendShapeGenerationOptions CreateOptions(
            BlendShapeNaming naming,
            bool overwriteExisting = false,
            bool enableLeftRightSplit = false)
        {
            return new BlendShapeGenerationOptions
            {
                IntensityMultiplier = 1.0f,
                EnableLeftRightSplit = enableLeftRightSplit,
                BlendWidth = 0.02f,
                OverwriteExisting = overwriteExisting,
                OutputNaming = naming,
            };
        }

        private static List<ARKitMapping> AutoMapping(string arkitName, string sourceName, BlendShapeSide side = BlendShapeSide.Both)
        {
            return new List<ARKitMapping>
            {
                new ARKitMapping(arkitName, new List<SourceMapping>
                {
                    new SourceMapping(1.0f, sourceName),
                }, side),
            };
        }

        private static List<CustomBlendShapeMapping> CustomMapping(string arkitName, string sourceName)
        {
            return new List<CustomBlendShapeMapping>
            {
                new CustomBlendShapeMapping
                {
                    arkitName = arkitName,
                    enabled = true,
                    sources = new List<BlendShapeSource>
                    {
                        new BlendShapeSource { blendShapeName = sourceName, weight = 1.0f },
                    },
                },
            };
        }

        [Test]
        public void Generate_WritesUnifiedExpressionsName_InsteadOfArkitName()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOptions(BlendShapeNaming.UnifiedExpressions), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "EyeClosedLeft" }));
            Assert.That(target.FindShape("eyeBlinkLeft"), Is.Null);
            Assert.That(target.FindShape("EyeClosedLeft").Frames[0].DeltaVertices, Is.EqualTo(new[] { Vector3.up, Vector3.up }));
        }

        [Test]
        public void Generate_WritesBothNames_InBothMode()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions(BlendShapeNaming.Both), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "jawOpen", "JawOpen" }));
            Assert.That(target.FindShape("jawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            Assert.That(target.FindShape("JawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_KeepsCustomNameUnchanged_WhenItIsNotAnArkitName()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, CustomMapping("MyCustomShape", "あ"), null,
                CreateOptions(BlendShapeNaming.Both), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "MyCustomShape" }));
        }

        [Test]
        public void Generate_SplitsSides_ForUnifiedExpressionsNamesThatHaveBothSides()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("眉上", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("browInnerUp", "眉上"),
                CreateOptions(BlendShapeNaming.UnifiedExpressions, enableLeftRightSplit: true), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "BrowInnerUpLeft", "BrowInnerUpRight" }));
            // LeftOnlyはX<0側、RightOnlyはX>0側にだけ残る
            Assert.That(target.FindShape("BrowInnerUpLeft").Frames[0].DeltaVertices, Is.EqualTo(new[] { Vector3.up, Vector3.zero }));
            Assert.That(target.FindShape("BrowInnerUpRight").Frames[0].DeltaVertices, Is.EqualTo(new[] { Vector3.zero, Vector3.up }));
        }

        [Test]
        public void Generate_AppliesWholeShapeToBothSides_WhenLeftRightSplitIsOff()
        {
            // 左右分割がOFFのときは、カスタムマッピングのSideと同じく出力先の左右指定も無視する
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("眉上", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("browInnerUp", "眉上"),
                CreateOptions(BlendShapeNaming.UnifiedExpressions, enableLeftRightSplit: false), null);

            Assert.That(target.FindShape("BrowInnerUpLeft").Frames[0].DeltaVertices, Is.EqualTo(new[] { Vector3.up, Vector3.up }));
            Assert.That(target.FindShape("BrowInnerUpRight").Frames[0].DeltaVertices, Is.EqualTo(new[] { Vector3.up, Vector3.up }));
        }

        [Test]
        public void Generate_SplitsUpperAndLowerLip_AtTheDetectedLipLine()
        {
            var source = new FakeMeshRepository(MouthVertices())
                .AddShape("vrc.v_aa", RegionDeltas(Vector3.down * 0.02f))
                .AddShape("すぼめ", UniformDeltas(MouthVertices().Length, Vector3.forward));
            var target = new FakeMeshRepository(MouthVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("mouthFunnel", "すぼめ"),
                CreateOptions(BlendShapeNaming.UnifiedExpressions), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "LipFunnelUpper", "LipFunnelLower" }));

            var upper = target.FindShape("LipFunnelUpper").Frames[0].DeltaVertices;
            var lower = target.FindShape("LipFunnelLower").Frames[0].DeltaVertices;
            var vertices = MouthVertices();
            for (int i = 0; i < vertices.Length; i++)
            {
                // 境界線より上の頂点は上唇側へ、下の頂点は下唇側へ入り、足すと元の形に戻る
                var expectedUpper = vertices[i].y > 0f ? Vector3.forward : Vector3.zero;
                Assert.That(upper[i], Is.EqualTo(expectedUpper), $"upper[{i}]");
                Assert.That(lower[i], Is.EqualTo(Vector3.forward - expectedUpper), $"lower[{i}]");
            }
        }

        [Test]
        public void Generate_EmitsHalfStrengthToBothLips_WhenLipLineCannotBeDetected()
        {
            // 口領域を検出できるシェイプキーが無いメッシュでは、上下どちらにも元の形を半分の強度で出す
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("すぼめ", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices());

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("mouthFunnel", "すぼめ"),
                CreateOptions(BlendShapeNaming.UnifiedExpressions), null);

            Assert.That(target.FindShape("LipFunnelUpper").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward * 0.5f));
            Assert.That(target.FindShape("LipFunnelLower").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward * 0.5f));
        }

        [Test]
        public void Generate_SplitsCancellationTogetherWithTheShape()
        {
            // 打ち消しを足し終えた形を分割するので、上下を足すと打ち消し込みの形に戻る
            var source = new FakeMeshRepository(MouthVertices())
                .AddShape("vrc.v_aa", RegionDeltas(Vector3.down * 0.02f))
                .AddShape("すぼめ", UniformDeltas(MouthVertices().Length, Vector3.forward))
                .AddShape("口角", UniformDeltas(MouthVertices().Length, Vector3.forward * 0.25f));
            var target = new FakeMeshRepository(MouthVertices());
            var options = CreateOptions(BlendShapeNaming.UnifiedExpressions);
            options.EnableMouthCancellation = true;
            options.MouthCancellationStrength = 1.0f;
            options.MouthCancellationSources = new List<BlendShapeSource>
            {
                new BlendShapeSource { blendShapeName = "口角", weight = 1.0f },
            };
            options.MouthCancellationTargets = new HashSet<string> { "mouthFunnel" };

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("mouthFunnel", "すぼめ"), options, null);

            var upper = target.FindShape("LipFunnelUpper").Frames[0].DeltaVertices;
            var lower = target.FindShape("LipFunnelLower").Frames[0].DeltaVertices;
            for (int i = 0; i < upper.Length; i++)
            {
                Assert.That((upper[i] + lower[i]).z, Is.EqualTo(0.75f).Within(0.0001f), $"sum[{i}]");
            }
        }

        [Test]
        public void Generate_ConvertsExistingArkitShape_InsteadOfRegenerating()
        {
            // メッシュに既にあるARKitシェイプキーは、VRChat/MMDのシェイプキーより形として信頼できる変換元
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("jawOpen", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("jawOpen", Vector3.forward, Vector3.forward);
            var options = CreateOptions(BlendShapeNaming.UnifiedExpressions, overwriteExisting: true);
            options.IntensityMultiplier = 2.0f;

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"), options, null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "JawOpen" }));
            // 変換は形を変えない複写なので、強度係数も掛からない
            Assert.That(target.FindShape("JawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
            Assert.That(target.FindShape("jawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
            Assert.That(target.CountShapes("jawOpen"), Is.EqualTo(1));
        }

        [Test]
        public void Generate_ConvertsExistingArkitShape_EvenWithoutAnyMapping()
        {
            // 自動マッピングの対象外でも、ARKit名のシェイプキーがあればUE名へ変換する
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("eyeBlinkRight", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("eyeBlinkRight", Vector3.up, Vector3.up);

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, null, CreateOptions(BlendShapeNaming.UnifiedExpressions), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "EyeClosedRight" }));
        }

        [Test]
        public void Generate_DoesNotDuplicateArkitShape_WhenConvertingInBothMode()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("jawOpen", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("jawOpen", Vector3.forward, Vector3.forward);

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, null, CreateOptions(BlendShapeNaming.Both), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "JawOpen" }));
            Assert.That(target.CountShapes("jawOpen"), Is.EqualTo(1));
        }

        [Test]
        public void Generate_DoesNotConvertExistingArkitShape_InArkitMode()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("jawOpen", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("jawOpen", Vector3.forward, Vector3.forward);

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, null, CreateOptions(BlendShapeNaming.ARKit), null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.Shapes.Count, Is.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Generate_PrefersCustomMapping_OverExistingArkitShape(bool overwriteExisting)
        {
            // カスタムマッピングは利用者の明示的な指定なので、既存ARKitシェイプキーより優先する。
            // UE名の出力先は既存のjawOpenと重ならないため、上書きがOFFでもカスタム定義から生成される
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("jawOpen", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("jawOpen", Vector3.forward, Vector3.forward);

            BlendShapeGenerationEngine.Generate(
                source, target, CustomMapping("jawOpen", "あ"), null,
                CreateOptions(BlendShapeNaming.UnifiedExpressions, overwriteExisting: overwriteExisting), null);

            Assert.That(target.FindShape("JawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            Assert.That(target.FindShape("jawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Generate_WritesArkitCopy_WhenConvertingInBothModeAndTargetLacksIt()
        {
            // 対象がソースの複製でなくARKit名を持たないときは、変換元の複写としてARKit名も書く
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("jawOpen", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, null, CreateOptions(BlendShapeNaming.Both), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "jawOpen", "JawOpen" }));
            Assert.That(target.FindShape("jawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Generate_CopiesEveryFrameAndWeight_WhenConvertingExistingArkitShape()
        {
            // 変換は形を変えない複写なので、中間フレームとそのウェイトも保つ
            var source = new FakeMeshRepository(TwoVertices())
                .AddShapeFrame("jawOpen", 50f, Vector3.forward * 0.2f, Vector3.forward * 0.2f)
                .AddShapeFrame("jawOpen", 100f, Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShapeFrame("jawOpen", 50f, Vector3.forward * 0.2f, Vector3.forward * 0.2f)
                .AddShapeFrame("jawOpen", 100f, Vector3.forward, Vector3.forward);

            BlendShapeGenerationEngine.Generate(
                source, target, null, null, CreateOptions(BlendShapeNaming.UnifiedExpressions), null);

            var converted = target.FindShape("JawOpen");
            Assert.That(converted.Frames.Count, Is.EqualTo(2));
            Assert.That(converted.Frames[0].Weight, Is.EqualTo(50f));
            Assert.That(converted.Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward * 0.2f));
            Assert.That(converted.Frames[1].Weight, Is.EqualTo(100f));
            Assert.That(converted.Frames[1].DeltaVertices[0], Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Generate_KeepsCustomDefinition_WhenAutoMappingConvertsToTheSameOutputName()
        {
            // UE名を直接付けたカスタム定義と、同じUE名へ変換される自動マッピングが共存しても、出力は1件でカスタムが残る
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("wink", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, CustomMapping("EyeClosedLeft", "wink"), AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOptions(BlendShapeNaming.UnifiedExpressions), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "EyeClosedLeft" }));
            Assert.That(target.CountShapes("EyeClosedLeft"), Is.EqualTo(1));
            Assert.That(target.FindShape("EyeClosedLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Generate_ReplacesExistingUnifiedExpressionsShapeInPlace_WhenOverwriteEnabled()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("JawOpen", Vector3.zero, Vector3.zero)
                .AddShape("あ", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("JawOpen", Vector3.zero, Vector3.zero)
                .AddShape("あ", Vector3.up, Vector3.up);

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions(BlendShapeNaming.UnifiedExpressions, overwriteExisting: true), null,
                targetIsSourceCopy: true);

            Assert.That(target.CountShapes("JawOpen"), Is.EqualTo(1));
            Assert.That(target.Shapes[0].Name, Is.EqualTo("JawOpen"));
            Assert.That(target.Shapes[0].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_SkipsExistingUnifiedExpressionsShape_WhenOverwriteDisabled()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("JawOpen", Vector3.zero, Vector3.zero)
                .AddShape("あ", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("JawOpen", Vector3.zero, Vector3.zero)
                .AddShape("あ", Vector3.up, Vector3.up);

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions(BlendShapeNaming.UnifiedExpressions), null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.CountShapes("JawOpen"), Is.EqualTo(1));
        }

        [Test]
        public void Generate_WritesProceduralShapes_UnderUnifiedExpressionsNames()
        {
            var options = CreateOptions(BlendShapeNaming.UnifiedExpressions);
            options.EnableProceduralMouthShapes = true;
            options.ProceduralMouthIntensity = 1.0f;
            var source = new FakeMeshRepository(MouthVertices())
                .AddShape("vrc.v_aa", RegionDeltas(Vector3.down * 0.02f));
            var target = new FakeMeshRepository(MouthVertices())
                .AddShape("vrc.v_aa", RegionDeltas(Vector3.down * 0.02f));

            var result = BlendShapeGenerationEngine.Generate(source, target, null, null, options, null);

            Assert.That(result.GeneratedShapes, Contains.Item("MouthUpperLeft"));
            Assert.That(result.GeneratedShapes, Contains.Item("MouthLowerLeft"));
            Assert.That(result.GeneratedShapes, Contains.Item("JawForward"));
            Assert.That(target.FindShape("mouthLeft"), Is.Null);

            // 上下に分けた手続き的生成を足すと、ARKit名で生成したmouthLeftと同じ形になる
            var arkitTarget = new FakeMeshRepository(MouthVertices())
                .AddShape("vrc.v_aa", RegionDeltas(Vector3.down * 0.02f));
            var arkitOptions = CreateOptions(BlendShapeNaming.ARKit);
            arkitOptions.EnableProceduralMouthShapes = true;
            arkitOptions.ProceduralMouthIntensity = 1.0f;
            BlendShapeGenerationEngine.Generate(source, arkitTarget, null, null, arkitOptions, null);

            var upper = target.FindShape("MouthUpperLeft").Frames[0].DeltaVertices;
            var lower = target.FindShape("MouthLowerLeft").Frames[0].DeltaVertices;
            var whole = arkitTarget.FindShape("mouthLeft").Frames[0].DeltaVertices;
            for (int i = 0; i < whole.Length; i++)
            {
                Assert.That((upper[i] + lower[i] - whole[i]).magnitude, Is.LessThan(0.0001f), $"vertex {i}");
            }
        }

        [Test]
        public void Generate_SkipsProcedural_WhenExistingArkitShapeIsConverted()
        {
            // 既存ARKitシェイプキーの変換が成立した名前は、手続き的生成で作り直さない
            var options = CreateOptions(BlendShapeNaming.UnifiedExpressions);
            options.EnableProceduralMouthShapes = true;
            var source = new FakeMeshRepository(MouthVertices())
                .AddShape("vrc.v_aa", RegionDeltas(Vector3.down * 0.02f))
                .AddShape("mouthLeft", UniformDeltas(MouthVertices().Length, Vector3.left));
            var target = new FakeMeshRepository(MouthVertices())
                .AddShape("vrc.v_aa", RegionDeltas(Vector3.down * 0.02f))
                .AddShape("mouthLeft", UniformDeltas(MouthVertices().Length, Vector3.left));

            BlendShapeGenerationEngine.Generate(source, target, null, null, options, null);

            Assert.That(target.CountShapes("MouthUpperLeft"), Is.EqualTo(1));
            // 口から離れた頂点（上側）にも元の形が残っており、手続き的生成の口領域マスクではなく変換である
            Assert.That(target.FindShape("MouthUpperLeft").Frames[0].DeltaVertices[8], Is.EqualTo(Vector3.left));
        }
    }
}
