using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    public class BlendShapeGenerationEngineTests
    {
        // 左(X<0)・右(X>0)に1頂点ずつ持つ最小構成
        private static Vector3[] TwoVertices() => new[]
        {
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
        };

        private static BlendShapeGenerationOptions CreateOptions()
        {
            return new BlendShapeGenerationOptions
            {
                IntensityMultiplier = 1.0f,
                EnableLeftRightSplit = false,
                BlendWidth = 0.02f,
                OverwriteExisting = false,
            };
        }

        private static List<ARKitMapping> AutoMapping(string arkitName, float weight, string sourceName, BlendShapeSide side = BlendShapeSide.Both)
        {
            return new List<ARKitMapping>
            {
                new ARKitMapping(arkitName, new List<SourceMapping>
                {
                    new SourceMapping(weight, sourceName),
                }, side),
            };
        }

        private static CustomBlendShapeMapping CustomMapping(
            string arkitName,
            string sourceName,
            float weight,
            BlendShapeSide side = BlendShapeSide.Both)
        {
            return new CustomBlendShapeMapping
            {
                arkitName = arkitName,
                enabled = true,
                sources = new List<BlendShapeSource>
                {
                    new BlendShapeSource { blendShapeName = sourceName, weight = weight, side = side },
                },
            };
        }

        [Test]
        public void Generate_CreatesShapeFromAutoMapping()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", 1.0f, "vrc.blink"), CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            var shape = target.FindShape("eyeBlinkLeft");
            Assert.That(shape, Is.Not.Null);
            Assert.That(shape.Frames[0].DeltaVertices, Is.EqualTo(new[] { Vector3.up, Vector3.up }));
        }

        [Test]
        public void Generate_AppliesSourceWeightAndIntensityMultiplier()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());
            var options = CreateOptions();
            options.IntensityMultiplier = 1.0f;

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", 0.5f, "vrc.blink"), options, null);

            var shape = target.FindShape("eyeBlinkLeft");
            Assert.That(shape.Frames[0].DeltaVertices[0], Is.EqualTo(new Vector3(0f, 0.5f, 0f)));
        }

        [Test]
        public void Generate_SplitsLeftSide_WhenLeftRightSplitEnabled()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());
            var options = CreateOptions();
            options.EnableLeftRightSplit = true;

            BlendShapeGenerationEngine.Generate(
                source, target, null,
                AutoMapping("eyeBlinkLeft", 1.0f, "vrc.blink", BlendShapeSide.LeftOnly), options, null);

            var shape = target.FindShape("eyeBlinkLeft");
            // LeftOnlyはX<0側にのみ適用される（X=-1は全適用、X=+1は適用なし）
            Assert.That(shape.Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            Assert.That(shape.Frames[0].DeltaVertices[1], Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Generate_AppliesCustomMappingSide_WhenLeftRightSplitEnabled()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("wink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());
            var options = CreateOptions();
            options.EnableLeftRightSplit = true;

            BlendShapeGenerationEngine.Generate(
                source,
                target,
                new List<CustomBlendShapeMapping>
                {
                    CustomMapping("eyeBlinkLeft", "wink", 1.0f, BlendShapeSide.LeftOnly),
                },
                null,
                options,
                null);

            var shape = target.FindShape("eyeBlinkLeft");
            Assert.That(shape.Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            Assert.That(shape.Frames[0].DeltaVertices[1], Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Generate_IgnoresCustomMappingSide_WhenLeftRightSplitDisabled()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("wink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());
            var options = CreateOptions();
            options.EnableLeftRightSplit = false;

            BlendShapeGenerationEngine.Generate(
                source,
                target,
                new List<CustomBlendShapeMapping>
                {
                    CustomMapping("eyeBlinkLeft", "wink", 1.0f, BlendShapeSide.LeftOnly),
                },
                null,
                options,
                null);

            // 左右分割は生成全体のスイッチで、カスタムマッピングのSideもここで止まる
            // （インスペクタ側はこの状態のときSideのドロップダウンを非活性にして無視されることを示す）
            var shape = target.FindShape("eyeBlinkLeft");
            Assert.That(shape.Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            Assert.That(shape.Frames[0].DeltaVertices[1], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_CustomMappingTakesPriorityOverAutoMapping()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("wink", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("eyeBlinkLeft", "wink", 1.0f),
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, AutoMapping("eyeBlinkLeft", 1.0f, "vrc.blink"), CreateOptions(), null);

            // カスタム定義済みのARKit名は自動マッピングをスキップし、カスタムのソースが使われる
            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(target.CountShapes("eyeBlinkLeft"), Is.EqualTo(1));
            Assert.That(target.FindShape("eyeBlinkLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Generate_UsesEnabledMapping_WhenDisabledMappingHasSameArkitName()
        {
            // 無効な同名マッピングは重複中止の対象にならず、取り置きとして残せる
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices());
            var disabled = CustomMapping("jawOpen", "口開き", 1.0f);
            disabled.enabled = false;
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("jawOpen", "あ", 1.0f),
                disabled,
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "jawOpen" }));
            Assert.That(target.CountShapes("jawOpen"), Is.EqualTo(1));
            Assert.That(target.FindShape("jawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_AbortsOnDuplicate_WhenBothMappingsAreEnabled()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("jawOpen", "あ", 1.0f),
                CustomMapping("jawOpen", "口開き", 1.0f),
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.Shapes, Is.Empty);
        }

        [Test]
        public void Generate_SkipsExistingShape_WhenOverwriteDisabled()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right);

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", 1.0f, "vrc.blink"), CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.CountShapes("eyeBlinkLeft"), Is.EqualTo(1));
            // 既存のデルタが維持される
            Assert.That(target.FindShape("eyeBlinkLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.right));
        }

        [Test]
        public void Generate_ReplacesExistingShape_WhenOverwriteEnabled()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right);
            var options = CreateOptions();
            options.OverwriteExisting = true;

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", 1.0f, "vrc.blink"), options, null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(target.CountShapes("eyeBlinkLeft"), Is.EqualTo(1));
            // 新しく生成されたデルタで置き換わる
            Assert.That(target.FindShape("eyeBlinkLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_PreservesMultiFrameShape_WhenOverwriteEnabled()
        {
            // 上書きは削除・再構築で行うため、置き換え対象でないシェイプのフレーム構成が
            // 崩れていないことを確認する
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShapeFrame("表情", 50f, new Vector3(0f, 0.2f, 0f), new Vector3(0f, 0.2f, 0f))
                .AddShapeFrame("表情", 100f, new Vector3(0f, 1.0f, 0f), new Vector3(0f, 1.0f, 0f))
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right);
            var options = CreateOptions();
            options.OverwriteExisting = true;

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", 1.0f, "vrc.blink"), options, null);

            var preserved = target.FindShape("表情");
            Assert.That(target.CountShapes("表情"), Is.EqualTo(1));
            Assert.That(preserved.Frames.Count, Is.EqualTo(2));
            Assert.That(preserved.Frames[0].Weight, Is.EqualTo(50f));
            Assert.That(preserved.Frames[1].Weight, Is.EqualTo(100f));
            Assert.That(preserved.Frames[0].DeltaVertices[0], Is.EqualTo(new Vector3(0f, 0.2f, 0f)));
            Assert.That(preserved.Frames[1].DeltaVertices[0], Is.EqualTo(new Vector3(0f, 1.0f, 0f)));
        }

        [Test]
        public void Generate_KeepsShapeOrder_WhenOverwriteEnabled()
        {
            // 上書きは削除+末尾追加ではなく元の位置での差し替えで行うため、置き換えた
            // シェイプは末尾へ移動せず元の位置に留まる。並びを変えないことが、同名シェイプを
            // 隣接させずに済ませる仕組みそのものになっている（#50）
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("笑い", Vector3.up, Vector3.up)
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right)
                .AddShape("怒り", Vector3.forward, Vector3.forward);
            var options = CreateOptions();
            options.OverwriteExisting = true;

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", 1.0f, "vrc.blink"), options, null);

            Assert.That(result.ShapeIndices["笑い"], Is.EqualTo(0));
            Assert.That(result.ShapeIndices["eyeBlinkLeft"], Is.EqualTo(1));
            Assert.That(result.ShapeIndices["怒り"], Is.EqualTo(2));
            // 差し替えなので中身は新しいデルタになっている
            Assert.That(target.FindShape("eyeBlinkLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_SkipsAutoMapping_WhenSourceShapeMissing()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("unrelated", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", 1.0f, "vrc.blink"), CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.Shapes, Is.Empty);
        }

        [Test]
        public void Generate_ReturnsShapeIndicesOfFinalTargetState()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", 1.0f, "vrc.blink"), CreateOptions(), null);

            Assert.That(result.ShapeIndices["vrc.blink"], Is.EqualTo(0));
            Assert.That(result.ShapeIndices["eyeBlinkLeft"], Is.EqualTo(1));
        }

        [TestCase(-1f, 1f)]
        [TestCase(1f, 0f)]
        [TestCase(0f, 0.5f)]
        public void CalculateSideMultiplier_LeftOnly(float vertexX, float expected)
        {
            float actual = BlendShapeGenerationEngine.CalculateSideMultiplier(vertexX, BlendShapeSide.LeftOnly, 0.02f);
            Assert.That(actual, Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(-1f, 0f)]
        [TestCase(1f, 1f)]
        [TestCase(0f, 0.5f)]
        public void CalculateSideMultiplier_RightOnly(float vertexX, float expected)
        {
            float actual = BlendShapeGenerationEngine.CalculateSideMultiplier(vertexX, BlendShapeSide.RightOnly, 0.02f);
            Assert.That(actual, Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(-1f)]
        [TestCase(0f)]
        [TestCase(1f)]
        public void CalculateSideMultiplier_Both_AlwaysReturnsOne(float vertexX)
        {
            float actual = BlendShapeGenerationEngine.CalculateSideMultiplier(vertexX, BlendShapeSide.Both, 0.02f);
            Assert.That(actual, Is.EqualTo(1f));
        }
    }
}
