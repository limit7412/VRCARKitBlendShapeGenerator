using UnityEngine;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// BlendShape生成処理が必要とするメッシュ操作の抽象
    /// 実装はInfra層（UnityMeshRepository）が提供し、テストではインメモリ実装に差し替える
    /// </summary>
    internal interface IMeshRepository
    {
        int VertexCount { get; }
        int BlendShapeCount { get; }

        string GetBlendShapeName(int shapeIndex);
        int GetBlendShapeFrameCount(int shapeIndex);
        float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex);

        /// <summary>
        /// 指定フレームのデルタを取得する。不要な配列はnullで省略可能。
        /// 渡した配列は頂点数分すべて書き潰される（前の内容が残らない）ため、
        /// 呼び出し側は同じ配列を読み出しごとに使い回してよい。
        /// </summary>
        void GetBlendShapeFrameVertices(
            int shapeIndex,
            int frameIndex,
            Vector3[] deltaVertices,
            Vector3[] deltaNormals,
            Vector3[] deltaTangents);

        /// <summary>
        /// 頂点座標を取得する。生成処理中は不変として扱ってよい。
        /// </summary>
        Vector3[] GetVertices();

        /// <summary>
        /// フレームを追加する。渡した配列の内容は実装側へ複製されるため、
        /// 呼び出し側は追加後に同じ配列を書き換えてよい。
        /// </summary>
        void AddBlendShapeFrame(
            string shapeName,
            float frameWeight,
            Vector3[] deltaVertices,
            Vector3[] deltaNormals,
            Vector3[] deltaTangents);

        void ClearBlendShapes();
    }
}
