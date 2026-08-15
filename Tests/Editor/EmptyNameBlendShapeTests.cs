using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 名前が空のシェイプキーを含むメッシュでの挙動を固定化する。
    ///
    /// 仕様: 空名シェイプは「そのまま保持するが、生成・照合・プレビューの対象にはしない」。
    /// </summary>
    public class EmptyNameBlendShapeTests
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

        /// <summary>
        /// 空名シェイプを中間に挟んだメッシュ。上書き生成では "eyeBlinkLeft" が削除・再生成される。
        /// </summary>
        private static FakeMeshRepository MeshWithEmptyNamedShape()
        {
            return new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("", Vector3.forward, Vector3.forward)
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right);
        }

        [Test]
        public void Generate_PreservesEmptyNamedShape_WhenOverwritingExistingShape()
        {
            // 上書き時の削除・再構築（ClearBlendShapes → 再追加）で空名シェイプが黙って消えないこと
            var source = MeshWithEmptyNamedShape();
            var target = MeshWithEmptyNamedShape();
            var options = CreateOptions();
            options.OverwriteExisting = true;

            BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"), options, null);

            Assert.That(target.CountShapes(""), Is.EqualTo(1));
            // デルタも失われていないこと
            Assert.That(target.FindShape("").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
            Assert.That(target.Shapes.Count, Is.EqualTo(3));
        }

        [Test]
        public void Generate_KeepsShapeIndicesConsistent_WhenEmptyNamedShapeExists()
        {
            // 空名シェイプが保持される結果、インデックス表が実メッシュ上の位置と一致すること
            var source = MeshWithEmptyNamedShape();
            var target = MeshWithEmptyNamedShape();
            var options = CreateOptions();
            options.OverwriteExisting = true;

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"), options, null);

            // 再構築後の並びは ["vrc.blink", "", "eyeBlinkLeft"]
            Assert.That(target.Shapes[0].Name, Is.EqualTo("vrc.blink"));
            Assert.That(target.Shapes[1].Name, Is.EqualTo(""));
            Assert.That(target.Shapes[2].Name, Is.EqualTo("eyeBlinkLeft"));

            Assert.That(result.ShapeIndices["vrc.blink"], Is.EqualTo(0));
            Assert.That(result.ShapeIndices["eyeBlinkLeft"], Is.EqualTo(2));
            // 空名は名前で引けないためエントリを持たない
            Assert.That(result.ShapeIndices.ContainsKey(""), Is.False);
        }

        [Test]
        public void Generate_PreservesEmptyNamedShape_WhenNothingIsRemoved()
        {
            // 削除が発生しない通常生成でも空名シェイプはそのまま残る
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("", Vector3.forward, Vector3.forward)
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("", Vector3.forward, Vector3.forward)
                .AddShape("vrc.blink", Vector3.up, Vector3.up);

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"), CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(target.CountShapes(""), Is.EqualTo(1));
            Assert.That(target.FindShape("").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Generate_KeepsBothEmptyNamedShapes_WhenOverwritingTheShapeBetweenThem()
        {
            // 空名シェイプに挟まれた対象を上書きするケース。
            // 削除して末尾へ追加し直すと、空いた隙間で空名シェイプが隣接して統合されるため、
            // その場で差し替えて並びを保つ（同名シェイプの扱いは #50 / DuplicateShapeNameTests）
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("", Vector3.forward, Vector3.forward)
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right)
                .AddShape("", Vector3.back, Vector3.back);
            var options = CreateOptions();
            options.OverwriteExisting = true;

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"), options, null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));

            // 空名シェイプは2つとも、それぞれのデルタを保ったまま残る
            Assert.That(target.Shapes.Count, Is.EqualTo(3));
            Assert.That(target.CountShapes(""), Is.EqualTo(2));
            Assert.That(target.Shapes[0].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
            Assert.That(target.Shapes[2].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.back));
            // 対象は元の位置のまま新しいデルタへ差し替わる
            Assert.That(target.Shapes[1].Name, Is.EqualTo("eyeBlinkLeft"));
            Assert.That(target.Shapes[1].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_ProceedsWithOverwrite_WhenShapeNamesAreUnique()
        {
            // 同名シェイプが無い通常のケースでも従来どおり上書きされる
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("", Vector3.forward, Vector3.forward)
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right);
            var options = CreateOptions();
            options.OverwriteExisting = true;

            var result = BlendShapeGenerationEngine.Generate(
                source, target, null, AutoMapping("eyeBlinkLeft", "vrc.blink"), options, null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(target.CountShapes("eyeBlinkLeft"), Is.EqualTo(1));
            Assert.That(target.FindShape("eyeBlinkLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            Assert.That(target.CountShapes(""), Is.EqualTo(1));
        }

        [Test]
        public void Generate_DoesNotUseEmptyNamedShape_AsSource()
        {
            // 空名は照合キーにしないため、ソース名が空の設定は「ソースなし」として扱われる
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("", Vector3.forward, Vector3.forward);
            var target = new FakeMeshRepository(TwoVertices());
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("jawOpen", ""),
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.Empty);
            Assert.That(target.Shapes, Is.Empty);
        }

        [Test]
        public void Generate_DoesNotTreatEmptyNamedShape_AsExistingTarget()
        {
            // 空名シェイプが「既存の生成先」と誤認されて生成がスキップされないこと。
            // 対象メッシュにも空名シェイプを置き、保持と生成結果の両方を検証する
            var source = new FakeMeshRepository(TwoVertices())
                .AddShape("", Vector3.forward, Vector3.forward)
                .AddShape("vrc.blink", Vector3.up, Vector3.up);
            var target = new FakeMeshRepository(TwoVertices())
                .AddShape("", Vector3.forward, Vector3.forward);
            var customMappings = new List<CustomBlendShapeMapping>
            {
                CustomMapping("eyeBlinkLeft", "vrc.blink"),
            };

            var result = BlendShapeGenerationEngine.Generate(
                source, target, customMappings, null, CreateOptions(), null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(target.FindShape("eyeBlinkLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
            // 対象メッシュの空名シェイプはそのまま残り、生成分が末尾に追加される
            Assert.That(target.Shapes.Count, Is.EqualTo(2));
            Assert.That(target.CountShapes(""), Is.EqualTo(1));
            Assert.That(target.FindShape("").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
            Assert.That(result.ShapeIndices["eyeBlinkLeft"], Is.EqualTo(1));
        }
    }
}
