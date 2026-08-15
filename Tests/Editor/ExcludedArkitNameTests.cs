using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 生成対象から除外したARKit名の扱いの検証。
    ///
    /// 除外は自動マッピング・カスタムマッピング・口の手続き的生成の3経路すべてで効く必要がある。
    /// どれか1つでも判定が漏れると、除外したはずの名前がその経路だけ生成される。
    /// また、除外は「生成しない」であって「消す」ではないため、既存シェイプへは触れない。
    /// </summary>
    public class ExcludedArkitNameTests
    {
        // 左(X<0)・右(X>0)に1頂点ずつ持つ最小構成
        private static Vector3[] TwoVertices() => new[]
        {
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
        };

        // 口領域を模した直方体（左右2 × 上下2 × 前後2）に、口から離れた頂点を1つ足した構成
        private const float SideX = 0.05f;
        private const float UpperY = 0.01f;
        private const float LowerY = -0.01f;
        private const float FrontZ = 0.05f;
        private const float BackZ = 0f;

        private const int RegionVertexCount = 8;
        private const float MouthSourceDelta = 0.02f;

        private const string MouthSourceShapeName = "vrc.v_aa";

        private static Vector3[] MouthVertices() => new[]
        {
            new Vector3(-SideX, UpperY, FrontZ),
            new Vector3(SideX, UpperY, FrontZ),
            new Vector3(-SideX, LowerY, FrontZ),
            new Vector3(SideX, LowerY, FrontZ),
            new Vector3(-SideX, UpperY, BackZ),
            new Vector3(SideX, UpperY, BackZ),
            new Vector3(-SideX, LowerY, BackZ),
            new Vector3(SideX, LowerY, BackZ),
            new Vector3(0f, 0.5f, 0f),
        };

        private static Vector3[] RegionDeltas()
        {
            var deltas = new Vector3[MouthVertices().Length];
            for (int i = 0; i < RegionVertexCount; i++)
            {
                deltas[i] = Vector3.down * MouthSourceDelta;
            }

            return deltas;
        }

        /// <summary>口領域を検出できるシェイプキーを1つ持つメッシュ</summary>
        private static FakeMeshRepository MouthMesh()
        {
            return new FakeMeshRepository(MouthVertices())
                .AddShape(MouthSourceShapeName, RegionDeltas());
        }

        private static BlendShapeGenerationOptions CreateOptions(params string[] excludedArkitNames)
        {
            return new BlendShapeGenerationOptions
            {
                IntensityMultiplier = 1.0f,
                EnableLeftRightSplit = false,
                BlendWidth = 0.02f,
                OverwriteExisting = false,
                ExcludedArkitNames = new HashSet<string>(excludedArkitNames),
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
                        new BlendShapeSource { blendShapeName = sourceName, weight = 1.0f, side = BlendShapeSide.Both },
                    },
                },
            };
        }

        [Test]
        public void Generate_SkipsExcludedName_FromAutoMapping()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source,
                target,
                null,
                AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOptions("eyeBlinkLeft"),
                null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.FindShape("eyeBlinkLeft"), Is.Null);
        }

        [Test]
        public void Generate_SkipsExcludedName_FromCustomMapping()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            var result = BlendShapeGenerationEngine.Generate(
                source,
                target,
                CustomMapping("eyeBlinkLeft", "vrc.blink"),
                null,
                CreateOptions("eyeBlinkLeft"),
                null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.FindShape("eyeBlinkLeft"), Is.Null);
        }

        [Test]
        public void Generate_SkipsExcludedName_FromProceduralMouthGeneration()
        {
            var target = MouthMesh();
            var options = CreateOptions("mouthLeft");
            options.EnableProceduralMouthShapes = true;
            options.ProceduralMouthIntensity = 1.0f;

            var result = BlendShapeGenerationEngine.Generate(
                MouthMesh(), target, null, null, options, null);

            Assert.That(result.GeneratedShapes, Does.Not.Contain("mouthLeft"));
            Assert.That(target.FindShape("mouthLeft"), Is.Null);

            // 除外していない名前は手続き的生成で作られる
            Assert.That(result.GeneratedShapes, Contains.Item("mouthRight"));
        }

        [Test]
        public void Generate_DoesNotFallBackToProcedural_WhenExcludedNameHasCustomMapping()
        {
            // 除外はカスタムマッピングより優先する。
            // カスタム定義を弾いた結果として手続き的生成へ流れてしまうと、除外が骨抜きになる
            var target = MouthMesh();
            var options = CreateOptions("mouthLeft");
            options.EnableProceduralMouthShapes = true;
            options.ProceduralMouthIntensity = 1.0f;

            var result = BlendShapeGenerationEngine.Generate(
                MouthMesh(),
                target,
                CustomMapping("mouthLeft", MouthSourceShapeName),
                null,
                options,
                null);

            Assert.That(result.GeneratedShapes, Does.Not.Contain("mouthLeft"));
            Assert.That(target.FindShape("mouthLeft"), Is.Null);
        }

        [Test]
        public void Generate_KeepsExistingShape_WhenExcludedNameWouldBeOverwritten()
        {
            // 除外は「生成しない」であって「消す」ではない。
            // 上書きONでも、除外した名前の既存シェイプは元のデルタのまま残る
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("eyeBlinkLeft", Vector3.left, Vector3.left);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("eyeBlinkLeft", Vector3.left, Vector3.left);

            var options = CreateOptions("eyeBlinkLeft");
            options.OverwriteExisting = true;

            var result = BlendShapeGenerationEngine.Generate(
                source,
                target,
                null,
                AutoMapping("eyeBlinkLeft", "vrc.blink"),
                options,
                null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.CountShapes("eyeBlinkLeft"), Is.EqualTo(1));

            var shape = target.FindShape("eyeBlinkLeft");
            Assert.That(shape.Frames[0].DeltaVertices, Is.EqualTo(new[] { Vector3.left, Vector3.left }));
        }

        [Test]
        public void Generate_OnlySkipsExcludedNames()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            var autoMappings = AutoMapping("eyeBlinkLeft", "vrc.blink");
            autoMappings.AddRange(AutoMapping("eyeBlinkRight", "vrc.blink"));

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, autoMappings, CreateOptions("eyeBlinkLeft"), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkRight" }));
        }

        [Test]
        public void Generate_IgnoresExclusions_WhenNoneAreConfigured()
        {
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices());

            // 除外集合が未設定（null）でも生成は従来どおり動く
            var options = CreateOptions();
            options.ExcludedArkitNames = null;

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"), options, null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
        }
    }
}
