using System.Collections.Generic;
using UnityEngine;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// 既存シェイプキーの変形データに依存せず、頂点の平行移動で
    /// 口周りのARKit BlendShape（mouthLeft/Right、jaw系等）を生成する。
    /// 既存シェイプキーは口領域の検出（マスク）にのみ使用する。
    /// </summary>
    internal static class ProceduralMouthShapeGenerator
    {
        /// <summary>手続き的生成の対象となるARKit BlendShape名</summary>
        public static readonly string[] TargetShapeNames =
        {
            "mouthLeft",
            "mouthRight",
            "jawLeft",
            "jawRight",
            "jawForward",
            "mouthShrugUpper",
            "mouthShrugLower",
            "mouthUpperUpLeft",
            "mouthUpperUpRight",
            "mouthLowerDownLeft",
            "mouthLowerDownRight",
        };

        // 口領域検出に使用するシェイプキー候補（グループごとに最初に見つかった名前を使用）
        private static readonly string[][] MouthSourceCandidates =
        {
            new[] { "vrc.v_aa", "vrc_v_aa", "あ", "a", "A" },
            new[] { "vrc.v_ou", "vrc_v_ou", "う", "u", "U" },
            new[] { "vrc.v_ih", "vrc_v_ih", "い", "i", "I" },
            new[] { "vrc.v_oh", "vrc_v_oh", "お", "o", "O" },
            new[] { "vrc.v_ch", "vrc_v_ch", "え", "e", "E" },
            new[] { "vrc.v_nn", "vrc_v_nn", "ん", "n", "N" },
        };

        // 口領域ウェイトの立ち上がり範囲（全頂点中の最大デルタ量に対する比率）
        private const float RegionWeightLowRatio = 0.03f;
        private const float RegionWeightHighRatio = 0.25f;

        // コア頂点（スケール算出・誤検出除去の基準）とみなすウェイト閾値
        private const float CoreWeightThreshold = 0.5f;

        // コア頂点のバウンディングボックスをこの倍率に拡張し、外側の頂点を誤検出として除外する
        private const float RegionBoundsExpansion = 1.5f;

        // 上唇側/下唇側を分けるブレンド帯の幅（口領域の高さに対する比率）
        private const float LipBlendBandRatio = 0.2f;

        // 奥行き減衰: 口領域の最奥の頂点に適用する移動量の割合（前面=1.0から奥に向かって減衰）
        private const float DepthFalloffMinScale = 0.3f;

        // 各シェイプの移動量（口領域の幅に対する比率、ウェイト100適用時）
        private const float MouthTranslateRatio = 0.12f;
        private const float JawTranslateRatio = 0.20f;
        private const float JawForwardRatio = 0.20f;
        private const float ShrugForwardRatio = 0.12f;
        private const float UpperUpRatio = 0.12f;
        private const float LowerDownRatio = 0.15f;

        private enum LipMask
        {
            All,
            Upper,
            Lower,
        }

        internal sealed class MouthRegionContext
        {
            public readonly Vector3[] Vertices;
            public readonly float[] Weights;
            public readonly float[] LowerRatios;
            public readonly float[] DepthFactors;
            public readonly float MouthWidth;

            public MouthRegionContext(
                Vector3[] vertices,
                float[] weights,
                float[] lowerRatios,
                float[] depthFactors,
                float mouthWidth)
            {
                Vertices = vertices;
                Weights = weights;
                LowerRatios = lowerRatios;
                DepthFactors = depthFactors;
                MouthWidth = mouthWidth;
            }
        }

        /// <summary>
        /// 口領域検出に使えるシェイプキーが存在するか
        /// </summary>
        public static bool HasMouthSource(ICollection<string> shapeNames)
        {
            if (shapeNames == null || shapeNames.Count == 0)
            {
                return false;
            }

            foreach (var group in MouthSourceCandidates)
            {
                foreach (var name in group)
                {
                    if (shapeNames.Contains(name))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 口領域を検出してコンテキストを作成する。検出できない場合はfalse。
        /// </summary>
        public static bool TryCreateContext(Mesh sourceMesh, out MouthRegionContext context)
        {
            context = null;
            if (sourceMesh == null || sourceMesh.vertexCount == 0)
            {
                return false;
            }

            var sourceIndices = FindMouthSourceIndices(sourceMesh);
            if (sourceIndices.Count == 0)
            {
                return false;
            }

            int vertexCount = sourceMesh.vertexCount;
            var maxDeltas = new float[vertexCount];
            var deltaVertices = new Vector3[vertexCount];

            foreach (int shapeIndex in sourceIndices)
            {
                int frameCount = sourceMesh.GetBlendShapeFrameCount(shapeIndex);
                if (frameCount == 0)
                {
                    continue;
                }

                // 法線・接線のデルタは使用しないためnullを渡して取得をスキップする
                sourceMesh.GetBlendShapeFrameVertices(
                    shapeIndex,
                    frameCount - 1,
                    deltaVertices,
                    null,
                    null);

                for (int i = 0; i < vertexCount; i++)
                {
                    float magnitude = deltaVertices[i].magnitude;
                    if (magnitude > maxDeltas[i])
                    {
                        maxDeltas[i] = magnitude;
                    }
                }
            }

            float globalMax = 0f;
            for (int i = 0; i < vertexCount; i++)
            {
                if (maxDeltas[i] > globalMax)
                {
                    globalMax = maxDeltas[i];
                }
            }

            if (globalMax <= Mathf.Epsilon)
            {
                return false;
            }

            // デルタ量を正規化して口領域ウェイトを算出（境界はsmoothstepで滑らかに減衰）
            var weights = new float[vertexCount];
            float low = globalMax * RegionWeightLowRatio;
            float high = globalMax * RegionWeightHighRatio;
            float range = Mathf.Max(high - low, Mathf.Epsilon);
            for (int i = 0; i < vertexCount; i++)
            {
                float t = Mathf.Clamp01((maxDeltas[i] - low) / range);
                weights[i] = t * t * (3f - 2f * t);
            }

            var vertices = sourceMesh.vertices;
            bool hasCore = false;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            for (int i = 0; i < vertexCount; i++)
            {
                if (weights[i] < CoreWeightThreshold)
                {
                    continue;
                }

                min = Vector3.Min(min, vertices[i]);
                max = Vector3.Max(max, vertices[i]);
                hasCore = true;
            }

            if (!hasCore)
            {
                return false;
            }

            var coreBounds = new Bounds();
            coreBounds.SetMinMax(min, max);

            float mouthWidth = coreBounds.size.x;
            if (mouthWidth <= Mathf.Epsilon)
            {
                return false;
            }

            // コア領域から離れた頂点（シェイプキーが顔全体を動かすアバター等での誤検出）を除外
            var expandedBounds = new Bounds(
                coreBounds.center,
                coreBounds.size * RegionBoundsExpansion + Vector3.one * (mouthWidth * 0.1f));
            for (int i = 0; i < vertexCount; i++)
            {
                if (weights[i] > 0f && !expandedBounds.Contains(vertices[i]))
                {
                    weights[i] = 0f;
                }
            }

            // 口領域のウェイト付き重心Yを唇の境界線とみなし、上唇側/下唇側の割合を算出
            float weightSum = 0f;
            float weightedYSum = 0f;
            for (int i = 0; i < vertexCount; i++)
            {
                if (weights[i] <= 0f)
                {
                    continue;
                }

                weightSum += weights[i];
                weightedYSum += weights[i] * vertices[i].y;
            }

            if (weightSum <= Mathf.Epsilon)
            {
                return false;
            }

            float lipLineY = weightedYSum / weightSum;
            float blendBand = Mathf.Max(coreBounds.size.y * LipBlendBandRatio, mouthWidth * 0.01f);
            var lowerRatios = new float[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                lowerRatios[i] = Mathf.Clamp01((lipLineY - vertices[i].y) / blendBand * 0.5f + 0.5f);
            }

            // 奥行き減衰係数を算出（口の前面ほど大きく、奥にかけて動きを減らして自然な変形にする）
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            for (int i = 0; i < vertexCount; i++)
            {
                if (weights[i] <= 0f)
                {
                    continue;
                }

                float z = vertices[i].z;
                if (z < minZ)
                {
                    minZ = z;
                }

                if (z > maxZ)
                {
                    maxZ = z;
                }
            }

            var depthFactors = new float[vertexCount];
            float depthRange = maxZ - minZ;
            if (depthRange <= Mathf.Epsilon)
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    depthFactors[i] = 1f;
                }
            }
            else
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    float depthT = Mathf.Clamp01((vertices[i].z - minZ) / depthRange);
                    float smoothDepth = depthT * depthT * (3f - 2f * depthT);
                    depthFactors[i] = Mathf.Lerp(DepthFalloffMinScale, 1f, smoothDepth);
                }
            }

            context = new MouthRegionContext(vertices, weights, lowerRatios, depthFactors, mouthWidth);
            return true;
        }

        /// <summary>
        /// 指定したARKitシェイプのデルタ頂点を生成する。
        /// 対象外の名前や変形が発生しない場合はfalse。
        /// </summary>
        public static bool TryBuildDeltaVertices(
            MouthRegionContext context,
            string arkitName,
            BlendShapeGenerationOptions options,
            out Vector3[] deltaVertices)
        {
            deltaVertices = null;
            if (context == null || options == null)
            {
                return false;
            }

            if (!TryGetShapeDefinition(arkitName, out var direction, out float ratio, out var lipMask, out var side))
            {
                return false;
            }

            float amount = context.MouthWidth * ratio * options.IntensityMultiplier * options.ProceduralMouthIntensity;
            if (amount <= Mathf.Epsilon)
            {
                return false;
            }

            int vertexCount = context.Vertices.Length;
            float blendWidth = Mathf.Max(0.0001f, options.BlendWidth);
            var deltas = new Vector3[vertexCount];
            bool hasDelta = false;

            for (int i = 0; i < vertexCount; i++)
            {
                float weight = context.Weights[i];
                if (weight <= 0f)
                {
                    continue;
                }

                if (lipMask == LipMask.Upper)
                {
                    weight *= 1f - context.LowerRatios[i];
                }
                else if (lipMask == LipMask.Lower)
                {
                    weight *= context.LowerRatios[i];
                }

                // 奥の頂点ほど動きを減らし、単純な平行移動にならないようにする
                weight *= context.DepthFactors[i];

                if (options.EnableLeftRightSplit && side != BlendShapeSide.Both)
                {
                    weight *= BlendShapeGenerationEngine.CalculateSideMultiplier(
                        context.Vertices[i].x,
                        side,
                        blendWidth);
                }

                if (weight <= 0.0001f)
                {
                    continue;
                }

                deltas[i] = direction * (amount * weight);
                hasDelta = true;
            }

            if (!hasDelta)
            {
                return false;
            }

            deltaVertices = deltas;
            return true;
        }

        private static List<int> FindMouthSourceIndices(Mesh sourceMesh)
        {
            var nameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < sourceMesh.blendShapeCount; i++)
            {
                var shapeName = sourceMesh.GetBlendShapeName(i);
                if (!string.IsNullOrEmpty(shapeName) && !nameToIndex.ContainsKey(shapeName))
                {
                    nameToIndex[shapeName] = i;
                }
            }

            var result = new List<int>();
            foreach (var group in MouthSourceCandidates)
            {
                foreach (var name in group)
                {
                    if (nameToIndex.TryGetValue(name, out int index))
                    {
                        if (!result.Contains(index))
                        {
                            result.Add(index);
                        }

                        break;
                    }
                }
            }

            return result;
        }

        // 左右の向きは既存の左右分割の規約（LeftOnly = X < 0 側を残す）に合わせる。
        // アバターは+Zを向いている前提のため、前方は+Z。
        private static bool TryGetShapeDefinition(
            string arkitName,
            out Vector3 direction,
            out float ratio,
            out LipMask lipMask,
            out BlendShapeSide side)
        {
            direction = Vector3.zero;
            ratio = 0f;
            lipMask = LipMask.All;
            side = BlendShapeSide.Both;

            switch (arkitName)
            {
                case "mouthLeft":
                    direction = Vector3.left;
                    ratio = MouthTranslateRatio;
                    return true;
                case "mouthRight":
                    direction = Vector3.right;
                    ratio = MouthTranslateRatio;
                    return true;
                case "jawLeft":
                    direction = Vector3.left;
                    ratio = JawTranslateRatio;
                    lipMask = LipMask.Lower;
                    return true;
                case "jawRight":
                    direction = Vector3.right;
                    ratio = JawTranslateRatio;
                    lipMask = LipMask.Lower;
                    return true;
                case "jawForward":
                    direction = Vector3.forward;
                    ratio = JawForwardRatio;
                    lipMask = LipMask.Lower;
                    return true;
                case "mouthShrugUpper":
                    direction = Vector3.forward;
                    ratio = ShrugForwardRatio;
                    lipMask = LipMask.Upper;
                    return true;
                case "mouthShrugLower":
                    direction = Vector3.forward;
                    ratio = ShrugForwardRatio;
                    lipMask = LipMask.Lower;
                    return true;
                case "mouthUpperUpLeft":
                    direction = Vector3.up;
                    ratio = UpperUpRatio;
                    lipMask = LipMask.Upper;
                    side = BlendShapeSide.LeftOnly;
                    return true;
                case "mouthUpperUpRight":
                    direction = Vector3.up;
                    ratio = UpperUpRatio;
                    lipMask = LipMask.Upper;
                    side = BlendShapeSide.RightOnly;
                    return true;
                case "mouthLowerDownLeft":
                    direction = Vector3.down;
                    ratio = LowerDownRatio;
                    lipMask = LipMask.Lower;
                    side = BlendShapeSide.LeftOnly;
                    return true;
                case "mouthLowerDownRight":
                    direction = Vector3.down;
                    ratio = LowerDownRatio;
                    lipMask = LipMask.Lower;
                    side = BlendShapeSide.RightOnly;
                    return true;
                default:
                    return false;
            }
        }
    }
}
