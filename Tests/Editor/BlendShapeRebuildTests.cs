using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 上書き生成の再構築（ClearBlendShapes → 積み直し）を検証する。
    ///
    /// 再構築は、差し替えないシェイプのデルタをどこから読み直すかで2通りある。
    /// 対象メッシュが複製元の写しである場合は複製元から1シェイプずつ読み直し、
    /// そうでない場合は対象メッシュの内容をメモリへ退避してから積み直す。
    /// どちらを通っても結果が変わらないこと、複製元から読む経路がシェイプ数分の
    /// メモリを確保しないことを固定化する（#40）。
    /// </summary>
    public class BlendShapeRebuildTests
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

        /// <summary>
        /// 差し替え対象・空名シェイプ・多フレームシェイプを含むメッシュ。
        /// 生成前の複製を模すため、同じ内容のものを2つ作って複製元／対象にする
        /// </summary>
        private static FakeMeshRepository CreateMesh(int fillerShapeCount = 0)
        {
            var mesh = new FakeMeshRepository(TwoVertices())
                .AddShape("vrc.blink", Vector3.up, Vector3.up)
                .AddShape("", Vector3.forward, Vector3.forward)
                .AddShapeFrame("表情", 50f, new Vector3(0f, 0.2f, 0f), new Vector3(0f, 0.2f, 0f))
                .AddShapeFrame("表情", 100f, new Vector3(0f, 1.0f, 0f), new Vector3(0f, 1.0f, 0f))
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right);

            for (int i = 0; i < fillerShapeCount; i++)
            {
                mesh.AddShape($"filler{i}", Vector3.back, Vector3.back);
            }

            return mesh;
        }

        private static FakeMeshRepository Generate(bool targetIsSourceCopy, int fillerShapeCount = 0)
        {
            var target = CreateMesh(fillerShapeCount);
            BlendShapeGenerationEngine.Generate(
                CreateMesh(fillerShapeCount),
                target,
                null,
                AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOverwriteOptions(),
                null,
                targetIsSourceCopy);
            return target;
        }

        [Test]
        public void Generate_ProducesSameMesh_WhicheverRebuildPathIsUsed()
        {
            var fromSourceCopy = Generate(targetIsSourceCopy: true);
            var fromTargetItself = Generate(targetIsSourceCopy: false);

            Assert.That(fromSourceCopy.Shapes.Count, Is.EqualTo(fromTargetItself.Shapes.Count));
            for (int shapeIndex = 0; shapeIndex < fromSourceCopy.Shapes.Count; shapeIndex++)
            {
                var expected = fromTargetItself.Shapes[shapeIndex];
                var actual = fromSourceCopy.Shapes[shapeIndex];

                Assert.That(actual.Name, Is.EqualTo(expected.Name), $"{shapeIndex}番目のシェイプ名");
                Assert.That(actual.Frames.Count, Is.EqualTo(expected.Frames.Count), $"{expected.Name}のフレーム数");

                for (int frameIndex = 0; frameIndex < expected.Frames.Count; frameIndex++)
                {
                    Assert.That(
                        actual.Frames[frameIndex].Weight,
                        Is.EqualTo(expected.Frames[frameIndex].Weight),
                        $"{expected.Name}の{frameIndex}番目のフレームウェイト");
                    Assert.That(
                        actual.Frames[frameIndex].DeltaVertices,
                        Is.EqualTo(expected.Frames[frameIndex].DeltaVertices),
                        $"{expected.Name}の{frameIndex}番目のデルタ");
                }
            }
        }

        [Test]
        public void Generate_ReplacesInPlace_WhenSourceAndTargetAreTheSameMesh()
        {
            // 複製ではなくメッシュそのものを両方に渡された場合、再構築でソースのデルタも消える。
            // 合成元（vrc.blink）を差し替え対象より後ろに置き、積み直しの途中では
            // まだ読み直せない並びにしてある
            var mesh = new FakeMeshRepository(TwoVertices())
                .AddShape("eyeBlinkLeft", Vector3.right, Vector3.right)
                .AddShape("", Vector3.forward, Vector3.forward)
                .AddShape("vrc.blink", Vector3.up, Vector3.up);

            var result = BlendShapeGenerationEngine.Generate(
                mesh,
                mesh,
                null,
                AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOverwriteOptions(),
                null);

            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(mesh.Shapes[0].Name, Is.EqualTo("eyeBlinkLeft"));
            Assert.That(mesh.Shapes[0].Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));

            // 差し替え対象以外も失われない
            Assert.That(mesh.Shapes.Count, Is.EqualTo(3));
            Assert.That(mesh.FindShape("").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.forward));
            Assert.That(mesh.FindShape("vrc.blink").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Generate_RebuildsFromSourceCopy_WithoutBufferingEveryShape()
        {
            // 保持するシェイプを1つずつ読み直して書き戻していれば、確保するデルタ配列の本数は
            // シェイプ数に依存しない。全シェイプを一度にメモリへ展開すると本数が比例して増える
            int fewShapes = CountDeltaBuffers(fillerShapeCount: 2);
            int manyShapes = CountDeltaBuffers(fillerShapeCount: 32);

            Assert.That(manyShapes, Is.EqualTo(fewShapes));
        }

        private static int CountDeltaBuffers(int fillerShapeCount)
        {
            var target = new DeltaBufferCountingMeshRepository(CreateMesh(fillerShapeCount));

            var result = BlendShapeGenerationEngine.Generate(
                CreateMesh(fillerShapeCount),
                target,
                null,
                AutoMapping("eyeBlinkLeft", "vrc.blink"),
                CreateOverwriteOptions(),
                null,
                targetIsSourceCopy: true);

            // 差し替えが起きていなければ再構築自体が走らず、本数を数える意味がなくなる
            Assert.That(result.GeneratedShapes, Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(target.Inner.FindShape("eyeBlinkLeft").Frames[0].DeltaVertices[0], Is.EqualTo(Vector3.up));

            return target.DistinctDeltaBufferCount;
        }

        /// <summary>
        /// 受け取ったデルタ配列を参照で数えるIMeshRepositoryデコレータ。
        /// 生成処理がバッファを使い回しているか（＝確保量がシェイプ数に比例しないか）を観測する
        /// </summary>
        private sealed class DeltaBufferCountingMeshRepository : IMeshRepository
        {
            // 配列は参照で等価比較されるため、使い回されたバッファは1本として数えられる
            private readonly HashSet<Vector3[]> _buffers = new HashSet<Vector3[]>();

            public DeltaBufferCountingMeshRepository(FakeMeshRepository inner)
            {
                Inner = inner;
            }

            public FakeMeshRepository Inner { get; }

            /// <summary>読み書きで受け取ったデルタ配列の本数</summary>
            public int DistinctDeltaBufferCount => _buffers.Count;

            public int VertexCount => Inner.VertexCount;

            public int BlendShapeCount => Inner.BlendShapeCount;

            public string GetBlendShapeName(int shapeIndex) => Inner.GetBlendShapeName(shapeIndex);

            public int GetBlendShapeFrameCount(int shapeIndex) => Inner.GetBlendShapeFrameCount(shapeIndex);

            public float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex) =>
                Inner.GetBlendShapeFrameWeight(shapeIndex, frameIndex);

            public Vector3[] GetVertices() => Inner.GetVertices();

            public void ClearBlendShapes() => Inner.ClearBlendShapes();

            public void GetBlendShapeFrameVertices(
                int shapeIndex,
                int frameIndex,
                Vector3[] deltaVertices,
                Vector3[] deltaNormals,
                Vector3[] deltaTangents)
            {
                Record(deltaVertices, deltaNormals, deltaTangents);
                Inner.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);
            }

            public void AddBlendShapeFrame(
                string shapeName,
                float frameWeight,
                Vector3[] deltaVertices,
                Vector3[] deltaNormals,
                Vector3[] deltaTangents)
            {
                Record(deltaVertices, deltaNormals, deltaTangents);
                Inner.AddBlendShapeFrame(shapeName, frameWeight, deltaVertices, deltaNormals, deltaTangents);
            }

            private void Record(Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents)
            {
                Add(deltaVertices);
                Add(deltaNormals);
                Add(deltaTangents);
            }

            private void Add(Vector3[] buffer)
            {
                if (buffer != null)
                {
                    _buffers.Add(buffer);
                }
            }
        }
    }
}
