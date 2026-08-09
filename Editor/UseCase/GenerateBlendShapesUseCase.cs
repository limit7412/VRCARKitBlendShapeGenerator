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
        /// </summary>
        public static SkinnedMeshRenderer ResolveTargetRenderer(ARKitBlendShapeGeneratorComponent component)
        {
            if (component == null)
            {
                return null;
            }

            var renderer = component.targetRenderer;
            if (renderer == null)
            {
                renderer = component.GetComponentInChildren<SkinnedMeshRenderer>();
            }

            return renderer;
        }

        /// <summary>
        /// NDMFビルド時の生成処理
        /// 対象レンダラーのメッシュを複製してBlendShapeを生成し、レンダラーへ差し替える
        /// </summary>
        public static void ExecuteForBuild(ARKitBlendShapeGeneratorComponent component)
        {
            if (component == null)
            {
                return;
            }

            if (CustomMappingValidation.HasDuplicateArkitNames(component.customMappings, out var duplicateArkitNames))
            {
                Debug.LogError(
                    "[ARKitGenerator] " + S("log.duplicate_abort", string.Join(", ", duplicateArkitNames)),
                    component);
                return;
            }

            var renderer = ResolveTargetRenderer(component);
            if (renderer == null || renderer.sharedMesh == null)
            {
                Debug.LogWarning("[ARKitGenerator] " + S("log.renderer_not_found"));
                return;
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
        }

        /// <summary>
        /// sourceMeshのBlendShapeを元に、targetMeshへARKit BlendShapeを生成する
        /// （プレビューはこのメソッドを直接使用してプロキシメッシュへ生成する）
        /// Infra実装（UnityMeshRepository / UnityDebugLogger）の組み立てはここで行う
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
                UnityDebugLogger.Instance);
        }
    }
}
