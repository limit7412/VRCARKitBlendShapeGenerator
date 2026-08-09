using System.Collections.Generic;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// UnityEngine.Meshを使わずに生成ロジックを検証するためのインメモリIMeshRepository実装
    /// </summary>
    internal sealed class FakeMeshRepository : IMeshRepository
    {
        internal sealed class Frame
        {
            public readonly float Weight;
            public readonly Vector3[] DeltaVertices;
            public readonly Vector3[] DeltaNormals;
            public readonly Vector3[] DeltaTangents;

            public Frame(float weight, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents)
            {
                Weight = weight;
                DeltaVertices = deltaVertices;
                DeltaNormals = deltaNormals;
                DeltaTangents = deltaTangents;
            }
        }

        internal sealed class Shape
        {
            public readonly string Name;
            public readonly List<Frame> Frames = new List<Frame>();

            public Shape(string name)
            {
                Name = name;
            }
        }

        private readonly Vector3[] _vertices;

        public readonly List<Shape> Shapes = new List<Shape>();

        public FakeMeshRepository(Vector3[] vertices)
        {
            _vertices = vertices;
        }

        public int VertexCount => _vertices.Length;

        public int BlendShapeCount => Shapes.Count;

        public string GetBlendShapeName(int shapeIndex) => Shapes[shapeIndex].Name;

        public int GetBlendShapeFrameCount(int shapeIndex) => Shapes[shapeIndex].Frames.Count;

        public float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex) =>
            Shapes[shapeIndex].Frames[frameIndex].Weight;

        public void GetBlendShapeFrameVertices(
            int shapeIndex,
            int frameIndex,
            Vector3[] deltaVertices,
            Vector3[] deltaNormals,
            Vector3[] deltaTangents)
        {
            var frame = Shapes[shapeIndex].Frames[frameIndex];
            CopyTo(frame.DeltaVertices, deltaVertices);
            CopyTo(frame.DeltaNormals, deltaNormals);
            CopyTo(frame.DeltaTangents, deltaTangents);
        }

        public Vector3[] GetVertices() => _vertices;

        public void AddBlendShapeFrame(
            string shapeName,
            float frameWeight,
            Vector3[] deltaVertices,
            Vector3[] deltaNormals,
            Vector3[] deltaTangents)
        {
            // Unityと同様、直前と同名のシェイプへの追加は同一シェイプの追加フレームとして扱う
            Shape shape = Shapes.Count > 0 && Shapes[Shapes.Count - 1].Name == shapeName
                ? Shapes[Shapes.Count - 1]
                : null;
            if (shape == null)
            {
                shape = new Shape(shapeName);
                Shapes.Add(shape);
            }

            shape.Frames.Add(new Frame(frameWeight, Copy(deltaVertices), Copy(deltaNormals), Copy(deltaTangents)));
        }

        public void ClearBlendShapes() => Shapes.Clear();

        /// <summary>1フレーム（weight=100）のシェイプを末尾に追加するテスト用ヘルパー</summary>
        public FakeMeshRepository AddShape(string name, params Vector3[] deltaVertices)
        {
            return AddShapeFrame(name, 100f, deltaVertices);
        }

        /// <summary>
        /// 任意のフレームウェイトでシェイプ（または既存シェイプへの追加フレーム）を末尾に追加する。
        /// 多フレームのシェイプはウェイト昇順で連続して追加する。
        /// </summary>
        public FakeMeshRepository AddShapeFrame(string name, float frameWeight, params Vector3[] deltaVertices)
        {
            AddBlendShapeFrame(name, frameWeight, deltaVertices, new Vector3[VertexCount], new Vector3[VertexCount]);
            return this;
        }

        public Shape FindShape(string name) => Shapes.Find(s => s.Name == name);

        public int CountShapes(string name) => Shapes.FindAll(s => s.Name == name).Count;

        private static Vector3[] Copy(Vector3[] source)
        {
            if (source == null)
            {
                return null;
            }

            var copied = new Vector3[source.Length];
            source.CopyTo(copied, 0);
            return copied;
        }

        private static void CopyTo(Vector3[] source, Vector3[] destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            source.CopyTo(destination, 0);
        }
    }
}
