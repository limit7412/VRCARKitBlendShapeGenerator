using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Infra
{
    /// <summary>
    /// UnityEngine.Meshに対するIMeshRepository実装
    /// </summary>
    internal sealed class UnityMeshRepository : IMeshRepository
    {
        private readonly Mesh _mesh;
        private Vector3[] _cachedVertices;

        public UnityMeshRepository(Mesh mesh)
        {
            _mesh = mesh;
        }

        public int VertexCount => _mesh.vertexCount;

        public int BlendShapeCount => _mesh.blendShapeCount;

        public string GetBlendShapeName(int shapeIndex)
        {
            return _mesh.GetBlendShapeName(shapeIndex);
        }

        public int GetBlendShapeFrameCount(int shapeIndex)
        {
            return _mesh.GetBlendShapeFrameCount(shapeIndex);
        }

        public float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex)
        {
            return _mesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
        }

        public void GetBlendShapeFrameVertices(
            int shapeIndex,
            int frameIndex,
            Vector3[] deltaVertices,
            Vector3[] deltaNormals,
            Vector3[] deltaTangents)
        {
            _mesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);
        }

        public Vector3[] GetVertices()
        {
            // Mesh.verticesはアクセスごとに配列を複製するためキャッシュする
            // （生成処理は頂点座標自体を変更しない前提）
            if (_cachedVertices == null)
            {
                _cachedVertices = _mesh.vertices;
            }

            return _cachedVertices;
        }

        public void AddBlendShapeFrame(
            string shapeName,
            float frameWeight,
            Vector3[] deltaVertices,
            Vector3[] deltaNormals,
            Vector3[] deltaTangents)
        {
            _mesh.AddBlendShapeFrame(shapeName, frameWeight, deltaVertices, deltaNormals, deltaTangents);
        }

        public void ClearBlendShapes()
        {
            _mesh.ClearBlendShapes();
        }
    }
}
