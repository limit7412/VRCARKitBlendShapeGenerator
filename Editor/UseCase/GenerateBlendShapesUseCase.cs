using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;
using ARKitBlendShapeGenerator.Infra;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.UseCase
{
    /// <summary>
    /// ARKit BlendShape生成のユースケース
    /// NDMFビルド・プレビュー・エディタ拡張の各エントリポイントから共通で使用する
    /// </summary>
    internal static class GenerateBlendShapesUseCase
    {
        /// <summary>
        /// アバター内の処理対象コンポーネントを選定する（アバタールート直付けを優先）
        /// </summary>
        public static ARKitBlendShapeGeneratorComponent SelectPrimaryComponent(
            Transform avatarRoot,
            IReadOnlyList<ARKitBlendShapeGeneratorComponent> components)
        {
            if (components == null || components.Count == 0)
            {
                return null;
            }

            if (avatarRoot != null)
            {
                var onRoot = components.FirstOrDefault(c => c != null && c.transform == avatarRoot);
                if (onRoot != null)
                {
                    return onRoot;
                }
            }

            return components[0];
        }

        public static ARKitBlendShapeGeneratorComponent SelectPrimaryComponent(
            GameObject avatarRoot,
            IReadOnlyList<ARKitBlendShapeGeneratorComponent> components)
        {
            return SelectPrimaryComponent(avatarRoot != null ? avatarRoot.transform : null, components);
        }

        /// <summary>
        /// 生成対象のSkinnedMeshRendererを解決する（未設定時は子要素から検索）
        /// 解決順序の実体はTargetRendererResolverにあり、全経路でこれを共有する
        /// </summary>
        public static SkinnedMeshRenderer ResolveTargetRenderer(ARKitBlendShapeGeneratorComponent component)
        {
            return TargetRendererResolver.Resolve(component);
        }

        /// <summary>
        /// NDMFビルド時の生成処理
        /// 対象レンダラーのメッシュを複製してBlendShapeを生成し、レンダラーへ差し替える
        /// </summary>
        public static BuildOutcome ExecuteForBuild(ARKitBlendShapeGeneratorComponent component)
        {
            if (component == null)
            {
                return null;
            }

            if (CustomMappingValidation.HasDuplicateArkitNames(component.customMappings, out var duplicateArkitNames))
            {
                Debug.LogError(
                    "[ARKitGenerator] " + S("log.duplicate_abort", string.Join(", ", duplicateArkitNames)),
                    component);
                return null;
            }

            var renderer = ResolveTargetRenderer(component);
            if (renderer == null || renderer.sharedMesh == null)
            {
                Debug.LogWarning("[ARKitGenerator] " + S("log.renderer_not_found"));
                return null;
            }

            var sourceMesh = renderer.sharedMesh;
            var generatedMesh = Object.Instantiate(sourceMesh);
            var options = BlendShapeGenerationOptions.FromComponent(component);

            if (options.Debug)
            {
                Debug.Log($"[ARKitGenerator] Processing mesh: {generatedMesh.name}, existing shapes: {generatedMesh.blendShapeCount}");
                Debug.Log($"[ARKitGenerator] Original mesh: {sourceMesh.name}, blendShapeCount: {sourceMesh.blendShapeCount}, isReadable: {sourceMesh.isReadable}");
            }

            var result = GenerateInto(sourceMesh, generatedMesh, component.customMappings, options);

            // メッシュを適用
            renderer.sharedMesh = generatedMesh;

            if (options.Debug)
            {
                Debug.Log($"[ARKitGenerator] Generated {result.GeneratedShapes.Count} BlendShapes: {string.Join(", ", result.GeneratedShapes)}");
            }

            return new BuildOutcome(renderer, result);
        }

        /// <summary>
        /// NDMFビルドでの生成結果。差し替え後のレンダラーと、そのメッシュのシェイプキー一覧を持つ
        /// </summary>
        internal sealed class BuildOutcome
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly BlendShapeGenerationResult Result;

            public BuildOutcome(SkinnedMeshRenderer renderer, BlendShapeGenerationResult result)
            {
                Renderer = renderer;
                Result = result;
            }
        }

        /// <summary>
        /// ネイティブフェイストラッキングのBlendshapes SetをAvatar Descriptorへ書き込む。
        ///
        /// 生成後のメッシュにあるシェイプキー名から、トラッキングキーごとの割り当てを決めて書く。
        /// 生成物だけでなくメッシュにもとからあるシェイプキーも候補に含めるのは、
        /// SDKの自動入力をビルド時に行うのと同じ結果にするため
        /// </summary>
        public static void WriteFaceTrackingBlendshapesSet(
            GameObject avatarRoot,
            ARKitBlendShapeGeneratorComponent component,
            BuildOutcome outcome)
        {
            if (component == null || !component.writeFaceTrackingBlendshapesSet || outcome == null)
            {
                return;
            }

            if (!FaceTrackingDescriptorWriter.IsSupported)
            {
                Debug.LogWarning("[ARKitGenerator] " + S("log.face_tracking_set.unsupported"), component);
                return;
            }

            var plan = FaceTrackingBlendshapesSetTable.Build(outcome.Result.ShapeIndices.Keys);
            var written = FaceTrackingDescriptorWriter.Write(avatarRoot, outcome.Renderer, plan);

            switch (written.Status)
            {
                case FaceTrackingWriteStatus.Written:
                    Debug.Log(
                        "[ARKitGenerator] " + S(
                            "log.face_tracking_set.written",
                            FaceTrackingBlendshapesSetTable.SetName,
                            plan.AssignedCount,
                            plan.BlendShapeNames.Length),
                        component);
                    if (written.MeshLimitReached)
                    {
                        Debug.LogWarning(
                            "[ARKitGenerator] " + S("log.face_tracking_set.mesh_limit", outcome.Renderer.name),
                            component);
                    }
                    break;
                case FaceTrackingWriteStatus.DescriptorNotFound:
                    Debug.LogWarning("[ARKitGenerator] " + S("log.face_tracking_set.descriptor_not_found"), component);
                    break;
                case FaceTrackingWriteStatus.KeyMismatch:
                    Debug.LogError(
                        "[ARKitGenerator] " + S("log.face_tracking_set.key_mismatch", written.MismatchDetail),
                        component);
                    break;
                case FaceTrackingWriteStatus.Unsupported:
                    Debug.LogWarning("[ARKitGenerator] " + S("log.face_tracking_set.unsupported"), component);
                    break;
            }
        }

        /// <summary>
        /// sourceMeshのBlendShapeを元に、targetMeshへARKit BlendShapeを生成する
        /// （プレビューはこのメソッドを直接使用してプロキシメッシュへ生成する）
        /// Infra実装（UnityMeshRepository / UnityDebugLogger）の組み立てはここで行う
        ///
        /// targetMeshにはsourceMeshの複製（Object.Instantiate）を渡すこと。
        /// 上書き生成時の再構築をsourceMeshからの読み直しで済ませられるようになり、
        /// 対象メッシュの全BlendShapeをメモリへ展開せずに済む
        /// </summary>
        public static BlendShapeGenerationResult GenerateInto(
            Mesh sourceMesh,
            Mesh targetMesh,
            List<CustomBlendShapeMapping> customMappings,
            BlendShapeGenerationOptions options)
        {
            if (sourceMesh == null || targetMesh == null)
            {
                return new BlendShapeGenerationResult(
                    new List<string>(),
                    new Dictionary<string, int>());
            }

            return BlendShapeGenerationEngine.Generate(
                new UnityMeshRepository(sourceMesh),
                new UnityMeshRepository(targetMesh),
                customMappings,
                ARKitMappingTable.GetMappings(),
                options,
                UnityDebugLogger.Instance,
                targetIsSourceCopy: true);
        }
    }
}
