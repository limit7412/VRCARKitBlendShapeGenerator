using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 手続き的に生成した口周りBlendShapeが、生成全体の中でどうメッシュへ書かれるかを検証する。
    ///
    /// デルタの中身そのものはProceduralMouthShapeGeneratorTestsで確認しているため、
    /// ここでは対象の選定（既存シェイプキーからの生成やカスタム定義との優先関係）と、
    /// 差し替え・追加の書き分けを対象にする。
    /// </summary>
    public class ProceduralMouthShapeWriteTests
    {
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

        private static BlendShapeGenerationOptions CreateOptions(bool overwriteExisting = false)
        {
            return new BlendShapeGenerationOptions
            {
                IntensityMultiplier = 1.0f,
                EnableLeftRightSplit = false,
                BlendWidth = 0.02f,
                OverwriteExisting = overwriteExisting,
                EnableProceduralMouthShapes = true,
                ProceduralMouthIntensity = 1.0f,
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
        public void Generate_AppendsProceduralShapes_WhenTheyDoNotExist()
        {
            var target = MouthMesh();

            var result = BlendShapeGenerationEngine.Generate(
                MouthMesh(), target, null, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Contains.Item("mouthLeft"));
            Assert.That(result.GeneratedShapes, Contains.Item("jawForward"));

            // 既存シェイプキーはそのまま残り、生成分が末尾に追加される
            Assert.That(target.Shapes[0].Name, Is.EqualTo(MouthSourceShapeName));
            Assert.That(target.FindShape("mouthLeft"), Is.Not.Null);
            Assert.That(result.ShapeIndices["mouthLeft"], Is.GreaterThan(0));
        }

        [Test]
        public void Generate_SkipsProcedural_WhenShapeIsGeneratedFromExistingShapeKeys()
        {
            // 既存シェイプキーからの生成が成立している名前は、手続き的生成では作り直さない
            var target = MouthMesh();

            var result = BlendShapeGenerationEngine.Generate(
                MouthMesh(),
                target,
                null,
                AutoMapping("mouthLeft", MouthSourceShapeName),
                CreateOptions(),
                null);

            Assert.That(target.CountShapes("mouthLeft"), Is.EqualTo(1));
            Assert.That(result.GeneratedShapes, Contains.Item("mouthLeft"));

            // 自動マッピング由来なので、口領域の平行移動ではなくソースのデルタがそのまま入る
            Assert.That(
                target.FindShape("mouthLeft").Frames[0].DeltaVertices[0],
                Is.EqualTo(Vector3.down * MouthSourceDelta));
        }

        [Test]
        public void Generate_ReplacesProceduralShapeInPlace_WhenOverwriteEnabled()
        {
            var target = MouthMesh().AddShape("mouthLeft", new Vector3[MouthVertices().Length]);
            target.AddShape("末尾", new Vector3[MouthVertices().Length]);

            var result = BlendShapeGenerationEngine.Generate(
                MouthMesh(), target, null, null, CreateOptions(overwriteExisting: true), null);

            // 差し替えなので末尾へ移動せず、元の位置のまま中身だけ入れ替わる
            Assert.That(target.CountShapes("mouthLeft"), Is.EqualTo(1));
            Assert.That(result.ShapeIndices["mouthLeft"], Is.EqualTo(1));
            Assert.That(target.Shapes[2].Name, Is.EqualTo("末尾"));
            Assert.That(
                target.FindShape("mouthLeft").Frames[0].DeltaVertices[0],
                Is.Not.EqualTo(Vector3.zero));
        }

        [Test]
        public void Generate_SkipsProceduralOnly_WhenReplacementWouldMergeAdjacentDuplicates()
        {
            // 元から同名シェイプが隣接しているメッシュは再構築でデータを失うため、
            // 差し替えを伴う手続き的生成だけを見送る。差し替えを伴わない生成はそのまま続ける
            var target = MouthMesh()
                .AddDistinctShape("dup", new Vector3[MouthVertices().Length])
                .AddDistinctShape("dup", new Vector3[MouthVertices().Length])
                .AddShape("mouthLeft", new Vector3[MouthVertices().Length]);

            var result = BlendShapeGenerationEngine.Generate(
                MouthMesh(),
                target,
                null,
                AutoMapping("eyeBlinkLeft", MouthSourceShapeName),
                CreateOptions(overwriteExisting: true),
                null);

            // 既存シェイプキーからの生成は成立する
            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));

            // 手続き的生成は1つも書かれず、メッシュは差し替え前のまま
            Assert.That(target.CountShapes("dup"), Is.EqualTo(2));
            Assert.That(target.FindShape("mouthLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.zero));
            Assert.That(target.FindShape("jawForward"), Is.Null);
        }
    }
}
