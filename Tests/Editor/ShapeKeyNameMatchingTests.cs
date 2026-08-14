using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// シェイプキー名の照合規則を固定化する。
    ///
    /// 仕様: 名前は一切加工せず完全一致で照合する。空白のみの設定値は未設定として扱う。
    /// メッシュ由来の名前はMMDワールドやアニメーションカーブから参照される識別子であり、
    /// ツールが書き換えてよいものではない。設定値を正規化して救済すると、壊れた設定値が
    /// 気づかれないまま保存され続けるため、不一致は「未検出」として可視化する。
    /// </summary>
    public class ShapeKeyNameMatchingTests
    {
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

        private static CustomBlendShapeMapping CustomMapping(string arkitName, string sourceName)
        {
            return new CustomBlendShapeMapping
            {
                arkitName = arkitName,
                enabled = true,
                sources = new List<BlendShapeSource>
                {
                    new BlendShapeSource { blendShapeName = sourceName, weight = 1.0f, side = BlendShapeSide.Both },
                },
            };
        }

        [Test]
        public void Generate_DoesNotResolveSourceName_WithSurroundingSpace()
        {
            // 設定値を正規化しないため、" あ " はメッシュの "あ" に一致しない。
            // インスペクタも同じ規則で「(未検出)」と表示するため、表示と挙動は一致する
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("jawOpen", " あ "),
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.Shapes, Is.Empty);
        }

        [Test]
        public void Generate_ResolvesMeshShape_WhoseNameItselfHasSurroundingSpace()
        {
            // メッシュ側に前後の空白を含む名前が実在すれば、それを指定して生成できる。
            // メッシュ由来の名前は常に指定可能でなければならない
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape(" あ ", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("jawOpen", " あ "),
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "jawOpen" }));
            Assert.That(target.FindShape("jawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_DistinguishesNames_ThatDifferOnlyBySurroundingSpace()
        {
            // 空白の有無で別のシェイプとして扱われる（片方に寄せない）
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape(" あ ", Vector3.up, Vector3.up)
                .AddShape("あ", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("jawOpen", "あ"),
            };

            BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(target.FindShape("jawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Generate_KeepsInternalSpaces_InMeshSourceName()
        {
            // 名前中間のスペースを含む正当なシェイプキー名はそのまま一致する
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("mouth smile", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("mouthSmileLeft", "mouth smile"),
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "mouthSmileLeft" }));
            Assert.That(target.FindShape("mouthSmileLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_TreatsArkitNamesDifferingBySpace_AsSeparateShapes()
        {
            // 重複判定と生成が同じ規則で動くため、重複中止にはならず別名で2つ生成される
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("い", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("jawOpen", "あ"),
                CustomMapping("jawOpen ", "い"),
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "jawOpen", "jawOpen " }));
            Assert.That(target.FindShape("jawOpen").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            Assert.That(target.FindShape("jawOpen ").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Generate_IgnoresWhitespaceOnlyArkitName()
        {
            // 空白のみは未設定として扱い、そんな名前のシェイプを作らない
            // （重複判定側も空白のみを無視するため、ここで作ると判定を素通りしてしまう）
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("   ", "あ"),
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.Shapes, Is.Empty);
        }

        [Test]
        public void Generate_AppliesCancellation_OnlyToExactlyMatchingTargetName()
        {
            // 打ち消し対象も完全一致。インスペクタのトグルと同じ規則なので、
            // 「チェックが入っていないのに打ち消しが掛かる」状態にならない
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.25f, 0f));
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("jawOpen", "あ"),
            };
            var options = CreateOptions();
            options.EnableMouthCancellation = true;
            options.MouthCancellationStrength = 1.0f;
            options.MouthCancellationSources = new List<BlendShapeSource>
            {
                new BlendShapeSource { blendShapeName = "口開き", weight = 1.0f, side = BlendShapeSide.Both },
            };
            // 空白付きの対象名は生成される "jawOpen" と一致しない
            options.MouthCancellationTargets = new HashSet<string> { " jawOpen " };

            BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, options, null);

            // 打ち消しは掛からず、生成デルタのみ
            Assert.That(
                target.FindShape("jawOpen").Frames[0].DeltaVertices[0].y,
                Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Generate_AppliesCancellation_WhenTargetNameMatchesExactly()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("あ", Vector3.up, Vector3.up)
                .AddShape("口開き", new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.25f, 0f));
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("jawOpen", "あ"),
            };
            var options = CreateOptions();
            options.EnableMouthCancellation = true;
            options.MouthCancellationStrength = 1.0f;
            options.MouthCancellationSources = new List<BlendShapeSource>
            {
                new BlendShapeSource { blendShapeName = "口開き", weight = 1.0f, side = BlendShapeSide.Both },
            };
            options.MouthCancellationTargets = new HashSet<string> { "jawOpen" };

            BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, options, null);

            // 生成デルタ(+1.0) + 打ち消し(-0.25) = +0.75
            Assert.That(
                target.FindShape("jawOpen").Frames[0].DeltaVertices[0].y,
                Is.EqualTo(0.75f).Within(0.0001f));
        }
    }
}
