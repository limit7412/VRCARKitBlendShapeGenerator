using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// 生成対象のSkinnedMeshRendererを解決する唯一のロジック
    /// NDMFビルド・NDMFプレビュー・インスペクタUI・シェイプキー一覧・Reset時の自動設定は、すべてここを経由する
    ///
    /// 解決順序:
    /// 1. targetRenderer（Destroy済みの参照は未設定として扱う）
    /// 2. 直下の子のうちPreferredNamesに一致する名前を持つSkinnedMeshRenderer
    /// 3. 自身を含む子孫の最初のSkinnedMeshRenderer（ヒエラルキー順）
    ///
    /// 2と3はいずれも非アクティブなオブジェクトを含める。
    /// NDMFビルドでは非アクティブなメッシュも成果物に残るため、除外するとプレビューとの食い違いが生じる。
    /// </summary>
    internal static class TargetRendererResolver
    {
        /// <summary>
        /// 名前検索で優先するオブジェクト名（配列の並びがそのまま優先順位）
        /// </summary>
        private static readonly string[] PreferredNames = { "Body", "body", "Face", "face", "Head", "head" };

        /// <summary>
        /// コンポーネントから生成対象のSkinnedMeshRendererを解決する
        /// </summary>
        public static SkinnedMeshRenderer Resolve(ARKitBlendShapeGeneratorComponent component)
        {
            if (component == null)
            {
                return null;
            }

            // UnityEngine.Objectの==演算子で判定するため、Destroy済みの参照はフォールバックに回る
            if (component.targetRenderer != null)
            {
                return component.targetRenderer;
            }

            return SelectFallback(
                component.transform,
                component.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        /// <summary>
        /// targetRenderer未設定時のフォールバック探索
        /// レンダラーの列挙を自前で行う経路（ComputeContext経由で取得するNDMFプレビュー）と
        /// 探索順序を共有するために切り出している
        /// candidatesにはrootを含む子孫のSkinnedMeshRendererをヒエラルキー順で渡す
        /// </summary>
        public static SkinnedMeshRenderer SelectFallback(
            Transform root,
            IEnumerable<SkinnedMeshRenderer> candidates)
        {
            if (candidates == null)
            {
                return null;
            }

            SkinnedMeshRenderer firstFound = null;
            SkinnedMeshRenderer preferred = null;
            int preferredRank = int.MaxValue;

            foreach (var candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (firstFound == null)
                {
                    firstFound = candidate;
                }

                if (!IsNameSearchTarget(root, candidate))
                {
                    continue;
                }

                int rank = Array.IndexOf(PreferredNames, candidate.name);
                if (rank >= 0 && rank < preferredRank)
                {
                    preferredRank = rank;
                    preferred = candidate;
                }
            }

            return preferred != null ? preferred : firstFound;
        }

        /// <summary>
        /// 候補が名前検索の対象になるか判定する
        /// 名前検索の対象は直下の子のみ（Reset時の実装がtransform.Findだったため）
        ///
        /// 候補の名前を変更監視の対象に含める必要があるNDMFプレビューと、
        /// 判定条件を共有するために切り出している
        /// </summary>
        public static bool IsNameSearchTarget(Transform root, SkinnedMeshRenderer candidate)
        {
            return root != null
                && candidate != null
                && candidate.transform.parent == root;
        }
    }
}
