using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 生成したBlendShapeへ打ち消しデルタを焼き込む機能の検証
    /// </summary>
    public class MouthCancellationTests
    {
        private static Vector3[] TwoVertices() => new[]
        {
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
        };

        private static BlendShapeGenerationOptions CreateOptions(
            string cancellationSourceName,
            float sourceWeight,
            params string[] targets)
        {
            return new BlendShapeGenerationOptions
            {
                IntensityMultiplier = 1.0f,
                EnableLeftRightSplit = false,
                BlendWidth = 0.02f,
                OverwriteExisting = false,
                EnableMouthCancellation = true,
                MouthCancellationStrength = 1.0f,
                MouthCancellationSources = new List<BlendShapeSource>
                {
                    new BlendShapeSource
                    {
                        blendShapeName = cancellationSourceName,
                        weight = sourceWeight,
                        side = BlendShapeSide.Both,
                    },
                },
                MouthCancellationTargets = new HashSet<string>(targets),
            };
        }

        private static List<ARKitMapping> AutoMapping(string arkitName, string sourceName)
        {
            return new List<ARKitMapping>
            {
                new ARKitMapping(arkitName, new List<SourceMapping>
                {
                    new SourceMapping(1.0f, sourceName),
                }),
            };
        }

        [Test]
        public void Generate_BakesCancellationDelta_IntoTargetShape()
        {
            // 打ち消し元「口開き」はweight100で+Y、生成元「あ」も+Y。
            // jawOpenは打ち消し対象なので、生成デルタから打ち消し元の変形が引かれる。
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.25f, 0f));
            var target = new FakeMeshRepository(TwoVertices());

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions("口開き", 1.0f, "jawOpen"), null);

            var shape = target.FindShape("jawOpen");
            Assert.That(shape, Is.Not.Null);
            // 生成デルタ(+1.0) + 打ち消し(-0.25) = +0.75
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void Generate_DoesNotBakeCancellation_IntoNonTargetShape()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.25f, 0f));
            var target = new FakeMeshRepository(TwoVertices());

            // 打ち消し対象はjawOpenのみなので、mouthFunnelには焼き込まれない
            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("mouthFunnel", "あ"),
                CreateOptions("口開き", 1.0f, "jawOpen"), null);

            var shape = target.FindShape("mouthFunnel");
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Generate_InterpolatesCancellationSource_BelowLowestFrameWeight()
        {
            // 打ち消し元のフレームはweight100のみ。source.weight=0.5 → 評価ウェイト50は
            // 変形なし(0)とweight100フレームの線形補間になり、デルタは半分。
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", new Vector3(0f, 0.4f, 0f), new Vector3(0f, 0.4f, 0f));
            var target = new FakeMeshRepository(TwoVertices());

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions("口開き", 0.5f, "jawOpen"), null);

            var shape = target.FindShape("jawOpen");
            // 生成デルタ(+1.0) + 打ち消し(-0.4 * 0.5) = +0.8
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void Generate_InterpolatesCancellationSource_BetweenFrames()
        {
            // 多フレーム（50→+0.2, 100→+1.0）の打ち消し元をweight75で評価すると、
            // フレーム間の線形補間で +0.6 になる。
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShapeFrame("口開き", 50f, new Vector3(0f, 0.2f, 0f), new Vector3(0f, 0.2f, 0f))
                .AddShapeFrame("口開き", 100f, new Vector3(0f, 1.0f, 0f), new Vector3(0f, 1.0f, 0f));
            var target = new FakeMeshRepository(TwoVertices());

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions("口開き", 0.75f, "jawOpen"), null);

            var shape = target.FindShape("jawOpen");
            // 生成デルタ(+1.0) + 打ち消し(-0.6) = +0.4
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(0.4f).Within(0.0001f));
        }

        [Test]
        public void Generate_ExtrapolatesCancellationSource_AboveHighestFrameWeight()
        {
            // 多フレーム（50→+0.2, 100→+1.0）の打ち消し元をweight150で評価すると、
            // 最終フレームでクランプせず外挿して +1.8 になる。
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShapeFrame("口開き", 50f, new Vector3(0f, 0.2f, 0f), new Vector3(0f, 0.2f, 0f))
                .AddShapeFrame("口開き", 100f, new Vector3(0f, 1.0f, 0f), new Vector3(0f, 1.0f, 0f));
            var target = new FakeMeshRepository(TwoVertices());

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions("口開き", 1.5f, "jawOpen"), null);

            var shape = target.FindShape("jawOpen");
            // 生成デルタ(+1.0) + 打ち消し(-1.8) = -0.8
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(-0.8f).Within(0.0001f));
        }

        [Test]
        public void Generate_EvaluatesCancellationSource_AtNegativeWeight()
        {
            // 打ち消し元をマイナス方向に適用しているアバターを想定する。
            // 評価ウェイト-50での変形は-0.2となり、その逆方向（+0.2）が焼き込まれる。
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", new Vector3(0f, 0.4f, 0f), new Vector3(0f, 0.4f, 0f));
            var target = new FakeMeshRepository(TwoVertices());

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions("口開き", -0.5f, "jawOpen"), null);

            var shape = target.FindShape("jawOpen");
            // 生成デルタ(+1.0) + 打ち消し(+0.2) = +1.2
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(1.2f).Within(0.0001f));
        }

        [Test]
        public void Generate_EvaluatesCancellationSource_WithNegativeFrameWeight()
        {
            // フレームウェイトが負のシェイプキー。評価ウェイト-50は
            // 変形なし(0)とweight-100フレームの補間になり、デルタは半分。
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShapeFrame("口開き", -100f, new Vector3(0f, 0.4f, 0f), new Vector3(0f, 0.4f, 0f));
            var target = new FakeMeshRepository(TwoVertices());

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions("口開き", -0.5f, "jawOpen"), null);

            var shape = target.FindShape("jawOpen");
            // 生成デルタ(+1.0) + 打ち消し(-0.4 * 0.5) = +0.8
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void Generate_AppliesCancellationStrength()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", new Vector3(0f, 0.4f, 0f), new Vector3(0f, 0.4f, 0f));
            var target = new FakeMeshRepository(TwoVertices());
            var options = CreateOptions("口開き", 1.0f, "jawOpen");
            options.MouthCancellationStrength = 0.5f;

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"), options, null);

            var shape = target.FindShape("jawOpen");
            // 生成デルタ(+1.0) + 打ち消し(-0.4 * 0.5) = +0.8
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void Generate_DoesNotCreateCancellationOnlyShape_WhenSourceMissing()
        {
            // 生成元「あ」が無いためjawOpenは生成されない。
            // 打ち消しだけのBlendShapeが作られていないこと。
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("口開き", new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.25f, 0f));
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions("口開き", 1.0f, "jawOpen"), null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.FindShape("jawOpen"), Is.Null);
        }

        [Test]
        public void Generate_SkipsCancellation_WhenCancellationSourceMissing()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"),
                CreateOptions("存在しないシェイプ", 1.0f, "jawOpen"), null);

            var shape = target.FindShape("jawOpen");
            // 打ち消し元が見つからない場合は打ち消しなしで生成される
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Generate_SkipsCancellation_WhenNoTargetSelected()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.25f, 0f));
            var target = new FakeMeshRepository(TwoVertices());
            var options = CreateOptions("口開き", 1.0f);

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"), options, null);

            var shape = target.FindShape("jawOpen");
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Generate_SkipsCancellation_WhenDisabled()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.25f, 0f));
            var target = new FakeMeshRepository(TwoVertices());
            var options = CreateOptions("口開き", 1.0f, "jawOpen");
            options.EnableMouthCancellation = false;

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("jawOpen", "あ"), options, null);

            var shape = target.FindShape("jawOpen");
            Assert.That(shape.Frames[0].DeltaVertices[0].y, Is.EqualTo(1.0f).Within(0.0001f));
        }
    }
}
