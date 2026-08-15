using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 同名のシェイプキーを複数持つメッシュでの上書き生成を検証する。
    ///
    /// AddBlendShapeFrame は名前をキーにするうえ、同名シェイプのフレームウェイトは昇順を
    /// 要求するため、同名シェイプが隣接するとデルタが失われる。そこで上書きは削除+末尾追加
    /// ではなく元の位置での差し替えで行い、並びを変えないことで隣接を新たに生じさせない。
    /// </summary>
    public class DuplicateShapeNameTests
    {
        private static Vector3[] TwoVertices() => new[]
        {
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
        };

        private static BlendShapeGenerationOptions CreateOverwriteOptions()
        {
            return new BlendShapeGenerationOptions
            {
                IntensityMultiplier = 1.0f,
                EnableLeftRightSplit = false,
                BlendWidth = 0.02f,
                OverwriteExisting = true,
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

        private static FakeMeshRepository SourceMesh()
        {
            return new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
        }

        [Test]
        public void Generate_KeepsSameNamedShapesSeparate_WhenOverwritingTheShapeBetweenThem()
        {
            // 同名シェイプに挟まれた対象を上書きする。削除すると両者が隣接して統合されるが、
            // その場で差し替えるため並びが変わらず、どちらも元のデルタを保ったまま残る
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("dup", Vector3.forward, Vector3.forward)
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right)
                .AddShape("dup", Vector3.back, Vector3.back);

            var result = BlendShapeGenerationEngine.Generate(
                SourceMesh(), target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOverwriteOptions(), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(target.Shapes.Count, Is.EqualTo(3));
            Assert.That(target.CountShapes("dup"), Is.EqualTo(2));
            Assert.That(target.Shapes[0].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
            Assert.That(target.Shapes[2].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.back));
        }

        [Test]
        public void Generate_KeepsShapeOrder_WhenOverwriting()
        {
            // 置き換えた対象は末尾へ移動せず元の位置に留まる。
            // 並びが変わらないことが、同名シェイプを隣接させない仕組みそのものになっている
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right)
                .AddShape("other", Vector3.forward, Vector3.forward);

            var result = BlendShapeGenerationEngine.Generate(
                SourceMesh(), target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOverwriteOptions(), null);

            Assert.That(target.Shapes[0].Name, Is.EqualTo("eyeBlinkLeft"));
            Assert.That(target.Shapes[0].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            Assert.That(target.Shapes[1].Name, Is.EqualTo("other"));
            Assert.That(result.ShapeIndices["eyeBlinkLeft"], Is.EqualTo(0));
            Assert.That(result.ShapeIndices["other"], Is.EqualTo(1));
        }

        [Test]
        public void Generate_ReplacesEveryOccurrence_OfTheSameName()
        {
            // 同じ名前のシェイプが複数ある場合はすべて差し替える。
            // 片方だけ残すと「上書きしたのに古いデータが残る」状態になる
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right)
                .AddShape("other", Vector3.forward, Vector3.forward)
                .AddShape("eyeBlinkLeft", Vector3.back, Vector3.back);

            BlendShapeGenerationEngine.Generate(
                SourceMesh(), target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOverwriteOptions(), null);

            Assert.That(target.Shapes.Count, Is.EqualTo(3));
            Assert.That(target.CountShapes("eyeBlinkLeft"), Is.EqualTo(2));
            Assert.That(target.Shapes[0].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            Assert.That(target.Shapes[2].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_DoesNotRebuild_WhenNothingIsReplaced()
        {
            // 置き換えが無ければ再構築しないので、元から同名が隣接していても影響を受けない
            var target = new FakeMeshRepository(TwoVertices())
                .AddDistinctShape("dup", Vector3.forward)
                .AddDistinctShape("dup", Vector3.back);

            var result = BlendShapeGenerationEngine.Generate(
                SourceMesh(), target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOverwriteOptions(), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(target.CountShapes("dup"), Is.EqualTo(2));
            Assert.That(target.Shapes[0].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
            Assert.That(target.Shapes[1].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.back));
        }

        [Test]
        public void Generate_AbortsOverwrite_WhenMeshAlreadyHasAdjacentDuplicates()
        {
            // 元から同名シェイプが隣接しているメッシュ（FBXインポート等）は、再構築すると
            // 統合されてデルタを失う。差し替えでは解消できないため、メッシュへ触れずに中止する
            var target = new FakeMeshRepository(TwoVertices())
                .AddDistinctShape("dup", Vector3.forward)
                .AddDistinctShape("dup", Vector3.back)
                .AddDistinctShape("eyeBlinkLeft", Vector3.right);

            var result = BlendShapeGenerationEngine.Generate(
                SourceMesh(), target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOverwriteOptions(), null);

            Assert.That(result.GeneratedShapes, Is.Empty);

            // メッシュは一切変更されない
            Assert.That(target.Shapes.Count, Is.EqualTo(3));
            Assert.That(target.CountShapes("dup"), Is.EqualTo(2));
            Assert.That(target.Shapes[0].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
            Assert.That(target.Shapes[1].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.back));
            Assert.That(target.FindShape("eyeBlinkLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.right));
        }
    }
}
