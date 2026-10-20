using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ARKitBlendShapeGenerator.AAOCompat
{
    /// <summary>
    /// Avatar Descriptorのフェイストラッキング設定から、ネイティブFTが動かす（メッシュ, BlendShape名）の組を集める。
    ///
    /// Blendshapes Setは名前だけを持ち、Face Tracking Meshesに登録された全メッシュへ同じ名前で適用される。
    /// そのため、Setの名前とメッシュの総当たりで、実際にそのBlendShapeを持つメッシュだけを拾う。
    /// 本ツールが書いたSetに限らず、手で設定したSetや他のツールが書いたSetも対象にする。
    /// 守るべきBlendShapeは「どのSetが書いたか」ではなく「FTが動かすか」で決まるため
    /// </summary>
    internal static class FaceTrackingUsageCollector
    {
        public static List<NativeFaceTrackingBlendShapeUsage.Entry> Collect(VRCAvatarDescriptor descriptor)
        {
            var entries = new List<NativeFaceTrackingBlendShapeUsage.Entry>();
            if (descriptor == null || !descriptor.enableFaceTracking)
            {
                return entries;
            }

            var names = CollectNames(descriptor.faceTrackingBlendshapesSetList);
            if (names.Count == 0 || descriptor.faceTrackingMeshes == null)
            {
                return entries;
            }

            var seenRenderers = new HashSet<SkinnedMeshRenderer>();
            foreach (var renderer in descriptor.faceTrackingMeshes)
            {
                if (renderer == null || renderer.sharedMesh == null || !seenRenderers.Add(renderer))
                {
                    continue;
                }

                foreach (var name in names)
                {
                    if (renderer.sharedMesh.GetBlendShapeIndex(name) < 0)
                    {
                        continue;
                    }

                    entries.Add(new NativeFaceTrackingBlendShapeUsage.Entry
                    {
                        renderer = renderer,
                        blendShapeName = name,
                    });
                }
            }

            return entries;
        }

        /// <summary>
        /// 全Setのトラッキングキーとビセームの割り当てから、空でない名前を重複なく集める。
        /// ビセームの割り当て（Viseme Dampening）もFTが動かすので対象に含める
        /// </summary>
        private static List<string> CollectNames(List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet> sets)
        {
            var names = new List<string>();
            if (sets == null)
            {
                return names;
            }

            var seen = new HashSet<string>();
            foreach (var set in sets)
            {
                if (set == null)
                {
                    continue;
                }

                AddNames(set.faceTrackingBlendshapeNames, seen, names);
                AddNames(set.visemeBlendshapeNames, seen, names);
            }

            return names;
        }

        private static void AddNames(string[] source, HashSet<string> seen, List<string> names)
        {
            if (source == null)
            {
                return;
            }

            foreach (var name in source)
            {
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                {
                    names.Add(name);
                }
            }
        }
    }
}
