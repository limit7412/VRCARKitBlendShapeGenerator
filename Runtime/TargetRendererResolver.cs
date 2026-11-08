using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// 生成対象のSkinnedMeshRendererを解決する唯一のロジック
    /// NDMFビルド・NDMFプレビュー・インスペクタUI・シェイプキー一覧・Reset時の自動設定は、すべてここを経由する
    ///
    /// 解決順序:
    /// 1. targetRenderer（Destroy済みの参照は未設定として扱う）
    /// 2. 探索の起点の直下の子のうちPreferredNamesに一致する名前を持つSkinnedMeshRenderer
    /// 3. 探索の起点を含む子孫の最初のSkinnedMeshRenderer（ヒエラルキー順）
    ///
    /// 探索の起点は、まずコンポーネント自身のオブジェクトとし、そこで見つからなければアバタールートとする（GetSearchRoots）。
    /// コンポーネントを空のオブジェクトに付けてアバターの子に置いたとき、アバタールートに付けたときと同じレンダラーを選ぶためである。
    /// 自身の配下を先に見るのは、顔メッシュに直接付けた場合に、アバタールートの直下にあるBodyへ乗り換えないようにするためである。
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

            foreach (var root in GetSearchRoots(component.transform))
            {
                var found = SelectFallback(root, root.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// targetRenderer未設定時に探す起点を、探す順に返す。
        /// 1つ目はコンポーネント自身のオブジェクトで、2つ目はアバタールートである。
        /// アバタールートが自身と同じとき、またはアバターの外に置かれたときは、自身だけを返す
        ///
        /// レンダラーの列挙を自前で行うNDMFプレビューと、探す順序を共有するために切り出している
        /// </summary>
        public static IEnumerable<Transform> GetSearchRoots(Transform componentTransform)
        {
            if (componentTransform == null)
            {
                yield break;
            }

            yield return componentTransform;

            var avatarRoot = FindAvatarRoot(componentTransform);
            if (avatarRoot != null && avatarRoot != componentTransform)
            {
                yield return avatarRoot;
            }
        }

        /// <summary>
        /// 祖先をたどり、最も上にあるAvatar Descriptorのオブジェクトを返す。見つからなければnull。
        /// 重複コンポーネントガードがアバターの範囲を決める方法と揃えている
        /// </summary>
        public static Transform FindAvatarRoot(Transform transform)
        {
            Transform found = null;
            for (var cursor = transform; cursor != null; cursor = cursor.parent)
            {
                if (cursor.GetComponent<VRC_AvatarDescriptor>() != null)
                {
                    found = cursor;
                }
            }

            return found;
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
