using System.Collections.Generic;
using System.Linq;
using ARKitBlendShapeGenerator.Infra;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ARKitBlendShapeGenerator.AAOCompat
{
    /// <summary>
    /// AAOの処理後のBlendshapes Setの解決の結果
    /// </summary>
    internal sealed class FaceTrackingSetResolveResult
    {
        /// <summary>名前を書き換えた割り当ての数</summary>
        public int Renamed;

        /// <summary>AAOが取り除いたため空にした割り当ての、元の名前（重複なし）</summary>
        public readonly List<string> Removed = new List<string>();

        /// <summary>Face Tracking Meshesへ加えたメッシュの数</summary>
        public int MeshesAdded;

        /// <summary>Face Tracking Meshesが上限で、加えられなかったメッシュ</summary>
        public readonly List<SkinnedMeshRenderer> MeshesOverLimit = new List<SkinnedMeshRenderer>();
    }

    /// <summary>
    /// AAOの処理後に、Avatar DescriptorのBlendshapes SetをAAOの結果に合わせて書き直す。
    ///
    /// AAOはBlendShapeを別名へ統合したり（Auto Merge Blend Shape）、別メッシュへ移したり
    /// （Merge Skinned Mesh）する。Setは名前の文字列しか持たないので、AAOの参照の置き換えでは追従しない。
    /// 目印に記録された移動先で名前を書き換え、移動先のメッシュをFace Tracking Meshesへ加える。
    /// AAOが取り除いたBlendShapeの割り当ては空にする。空欄のキーは何もしないので、
    /// 存在しない名前を残してアップロード時の検証に引っかかるよりよい
    /// </summary>
    internal static class FaceTrackingSetResolver
    {
        /// <summary>
        /// AAOの処理後の、あるBlendShapeの行き先
        /// </summary>
        private sealed class Target
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly string Name;

            public Target(SkinnedMeshRenderer renderer, string name)
            {
                Renderer = renderer;
                Name = name;
            }
        }

        public static FaceTrackingSetResolveResult Apply(
            VRCAvatarDescriptor descriptor,
            NativeFaceTrackingBlendShapeUsage usage)
        {
            var result = new FaceTrackingSetResolveResult();
            if (descriptor == null || usage == null || usage.entries == null)
            {
                return result;
            }

            var resolvedByName = BuildLookup(usage);

            if (descriptor.faceTrackingBlendshapesSetList != null)
            {
                foreach (var set in descriptor.faceTrackingBlendshapesSetList)
                {
                    if (set == null)
                    {
                        continue;
                    }

                    Rewrite(set.faceTrackingBlendshapeNames, resolvedByName, result);
                    Rewrite(set.visemeBlendshapeNames, resolvedByName, result);
                }
            }

            UpdateMeshes(descriptor, resolvedByName, result);
            return result;
        }

        /// <summary>
        /// 元の名前から移動先の一覧を作る。
        /// 目印にAAOの結果が書かれていないとき（mappingAppliedがfalse）は、AAOが参照を置き換えたあとの
        /// メッシュに同じ名前が残っているかで判断する。移動先が無い（取り除かれた）名前は空の一覧になる
        /// </summary>
        private static Dictionary<string, List<Target>> BuildLookup(
            NativeFaceTrackingBlendShapeUsage usage)
        {
            var lookup = new Dictionary<string, List<Target>>();
            foreach (var entry in usage.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.blendShapeName))
                {
                    continue;
                }

                if (!lookup.TryGetValue(entry.blendShapeName, out var targets))
                {
                    targets = new List<Target>();
                    lookup.Add(entry.blendShapeName, targets);
                }

                if (usage.mappingApplied)
                {
                    if (entry.resolved && entry.resolvedRenderer != null
                        && !string.IsNullOrEmpty(entry.resolvedBlendShapeName))
                    {
                        targets.Add(new Target(entry.resolvedRenderer, entry.resolvedBlendShapeName));
                    }
                }
                else if (entry.renderer != null && entry.renderer.sharedMesh != null
                         && entry.renderer.sharedMesh.GetBlendShapeIndex(entry.blendShapeName) >= 0)
                {
                    targets.Add(new Target(entry.renderer, entry.blendShapeName));
                }
            }

            return lookup;
        }

        private static void Rewrite(
            string[] names,
            Dictionary<string, List<Target>> resolvedByName,
            FaceTrackingSetResolveResult result)
        {
            if (names == null)
            {
                return;
            }

            for (int i = 0; i < names.Length; i++)
            {
                var name = names[i];
                if (string.IsNullOrEmpty(name) || !resolvedByName.TryGetValue(name, out var targets))
                {
                    // 目印に無い名前は、AAOの前からどのFTメッシュにも無かったもの。手を付けない
                    continue;
                }

                if (targets.Count == 0)
                {
                    names[i] = string.Empty;
                    if (!result.Removed.Contains(name))
                    {
                        result.Removed.Add(name);
                    }

                    continue;
                }

                // 複数のメッシュが同じ名前を持ち、別々の名前へ移った場合は先頭を採る。
                // Setは1つの名前しか持てないので、どれかを選ぶしかない
                var newName = targets[0].Name;
                if (newName != name)
                {
                    names[i] = newName;
                    result.Renamed++;
                }
            }
        }

        /// <summary>
        /// AAOが取り除いたメッシュの参照（null）を外し、移動先のメッシュを加える
        /// </summary>
        private static void UpdateMeshes(
            VRCAvatarDescriptor descriptor,
            Dictionary<string, List<Target>> resolvedByName,
            FaceTrackingSetResolveResult result)
        {
            if (descriptor.faceTrackingMeshes == null)
            {
                descriptor.faceTrackingMeshes = new List<SkinnedMeshRenderer>();
            }

            descriptor.faceTrackingMeshes.RemoveAll(mesh => mesh == null);

            var renderers = resolvedByName.Values
                .SelectMany(targets => targets)
                .Select(target => target.Renderer)
                .Where(renderer => renderer != null)
                .Distinct();

            foreach (var renderer in renderers)
            {
                if (descriptor.faceTrackingMeshes.Contains(renderer))
                {
                    continue;
                }

                if (descriptor.faceTrackingMeshes.Count >= FaceTrackingDescriptorWriter.FaceTrackingMeshLimit)
                {
                    result.MeshesOverLimit.Add(renderer);
                    continue;
                }

                descriptor.faceTrackingMeshes.Add(renderer);
                result.MeshesAdded++;
            }
        }
    }
}
