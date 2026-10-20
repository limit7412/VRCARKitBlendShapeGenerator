using System.Linq;
using UnityEngine;
using nadena.dev.ndmf;
using VRC.SDK3.Avatars.Components;
using static ARKitBlendShapeGenerator.Localization;

[assembly: ExportsPlugin(typeof(ARKitBlendShapeGenerator.AAOCompat.AAOCompatPlugin))]

namespace ARKitBlendShapeGenerator.AAOCompat
{
    /// <summary>
    /// AAO: Avatar Optimizer がネイティブフェイストラッキングのBlendshapes Setを参照と見なすまでの仮対応。
    ///
    /// AAOはBlendShapeが使われているかをアニメーターと各コンポーネントの申告だけで判定するため、
    /// Setからしか参照されないBlendShapeを未使用として固定・統合してしまう（AAO 1.9.20で確認）。
    /// この仮対応は、AAOの直前にSetが動かすBlendShapeを目印へ写し取ってAAOへ申告し、
    /// AAOの直後に目印の記録でSetを書き直す。
    ///
    /// このアセンブリはAAOとネイティブFT対応のSDKが両方入っているときだけコンパイルされる
    /// （asmdefのversionDefinesとdefineConstraints）。AAOが対応したら、versionDefinesの範囲を
    /// 対応版より前に絞れば自動で無効になる。
    /// 本体の生成処理には依存せず、Descriptorの設定だけを見るので、手で設定したSetも対象になる
    /// </summary>
    public class AAOCompatPlugin : Plugin<AAOCompatPlugin>
    {
        private const string AvatarOptimizerPlugin = "com.anatawa12.avatar-optimizer";

        public override string QualifiedName => "com.qazx7412.kx-vrc-arkit-blendshape-generator.aao-compat";
        public override string DisplayName => "Kx VRC ARKit BlendShape Generator (AAO)";

        protected override void Configure()
        {
            // AAOはOptimizing Phaseで動く。本体の生成とSetの書き込みはTransforming Phaseなので、
            // ここで見るDescriptorには本体が書いたSetが入っている
            InPhase(BuildPhase.Optimizing)
                .BeforePlugin(AvatarOptimizerPlugin)
                .Run("Mark native face tracking blend shapes for Avatar Optimizer", MarkUsage);

            InPhase(BuildPhase.Optimizing)
                .AfterPlugin(AvatarOptimizerPlugin)
                .Run("Resolve native face tracking blendshapes set after Avatar Optimizer", ResolveSet);
        }

        private static void MarkUsage(BuildContext context)
        {
            var root = context.AvatarRootObject;
            RemoveUsages(root);

            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            var entries = FaceTrackingUsageCollector.Collect(descriptor);
            if (entries.Count == 0)
            {
                return;
            }

            var usage = root.AddComponent<NativeFaceTrackingBlendShapeUsage>();
            usage.entries = entries;

            var meshCount = entries.Select(entry => entry.renderer).Distinct().Count();
            Debug.Log("[ARKitGenerator] " + S("log.aao_compat.marked", entries.Count, meshCount), descriptor);
        }

        private static void ResolveSet(BuildContext context)
        {
            var root = context.AvatarRootObject;
            var usages = root.GetComponents<NativeFaceTrackingBlendShapeUsage>();
            if (usages.Length == 0)
            {
                return;
            }

            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            foreach (var usage in usages)
            {
                var result = FaceTrackingSetResolver.Apply(descriptor, usage);

                Debug.Log("[ARKitGenerator] " + S("log.aao_compat.resolved", result.Renamed, result.MeshesAdded), descriptor);

                if (result.Removed.Count > 0)
                {
                    Debug.LogWarning(
                        "[ARKitGenerator] " + S(
                            "log.aao_compat.removed",
                            result.Removed.Count,
                            string.Join(", ", result.Removed)),
                        descriptor);
                }

                foreach (var renderer in result.MeshesOverLimit)
                {
                    Debug.LogWarning(
                        "[ARKitGenerator] " + S("log.face_tracking_set.mesh_limit", renderer.name),
                        descriptor);
                }
            }

            // 目印はビルド中にだけ意味を持つ。成果物に残さない
            RemoveUsages(root);
        }

        private static void RemoveUsages(GameObject root)
        {
            foreach (var usage in root.GetComponentsInChildren<NativeFaceTrackingBlendShapeUsage>(true))
            {
                if (usage != null)
                {
                    Object.DestroyImmediate(usage);
                }
            }
        }
    }
}
