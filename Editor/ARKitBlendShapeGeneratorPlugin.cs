using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using nadena.dev.ndmf;
using ARKitBlendShapeGenerator.Domain;
using static ARKitBlendShapeGenerator.Localization;

[assembly: ExportsPlugin(typeof(ARKitBlendShapeGenerator.ARKitBlendShapeGeneratorPlugin))]

namespace ARKitBlendShapeGenerator
{
    public class ARKitBlendShapeGeneratorPlugin : Plugin<ARKitBlendShapeGeneratorPlugin>
    {
        public override string QualifiedName => "com.qazx7412.kx-vrc-arkit-blendshape-generator";
        public override string DisplayName => "Kx VRC ARKit BlendShape Generator";

        protected override void Configure()
        {
            // Generating Phaseで実行（Jerry's Templatesより先に動作）
            InPhase(BuildPhase.Generating)
                .BeforePlugin("com.adjerry91.vrcft-templates")
                .Run("Generate ARKit BlendShapes", ctx =>
                {
                    var components = ctx.AvatarRootObject
                        .GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(true)
                        .Where(c => c != null)
                        .ToArray();
                    var primaryComponent = SelectPrimaryComponent(ctx.AvatarRootObject, components);

                    if (components.Length > 1 && primaryComponent != null)
                    {
                        Debug.LogWarning(
                            "[ARKitGenerator] " + S("log.multiple_components", primaryComponent.name),
                            primaryComponent);
                    }

                    if (primaryComponent != null)
                    {
                        ProcessComponent(primaryComponent, ctx);
                    }
                })
                .PreviewingWith(new ARKitBlendShapeGeneratorPreview());
        }

        private static ARKitBlendShapeGeneratorComponent SelectPrimaryComponent(
            GameObject avatarRoot,
            ARKitBlendShapeGeneratorComponent[] components)
        {
            if (components == null || components.Length == 0)
            {
                return null;
            }

            var onRoot = components.FirstOrDefault(c => c != null && c.gameObject == avatarRoot);
            if (onRoot != null)
            {
                return onRoot;
            }

            return components[0];
        }

        private void ProcessComponent(ARKitBlendShapeGeneratorComponent component, BuildContext ctx)
        {
            if (CustomMappingValidation.HasDuplicateArkitNames(component.customMappings, out var duplicateArkitNames))
            {
                Debug.LogError(
                    "[ARKitGenerator] " + S("log.duplicate_abort", string.Join(", ", duplicateArkitNames)),
                    component);
                return;
            }

            var renderer = component.targetRenderer;
            if (renderer == null)
            {
                renderer = component.GetComponentInChildren<SkinnedMeshRenderer>();
            }

            if (renderer == null || renderer.sharedMesh == null)
            {
                Debug.LogWarning("[ARKitGenerator] " + S("log.renderer_not_found"));
                return;
            }

            var generator = new BlendShapeProcessor(
                renderer,
                component.intensityMultiplier,
                component.enableLeftRightSplit,
                component.blendWidth,
                component.overwriteExisting,
                component.customMappings,
                component.debugMode,
                component.enableProceduralMouthShapes,
                component.proceduralMouthIntensity
            );

            generator.Process();
        }
    }

    /// <summary>
    /// BlendShape生成処理
    /// </summary>
    public class BlendShapeProcessor
    {
        private readonly SkinnedMeshRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly Mesh _originalMesh;  // 元のメッシュ（BlendShapeデータ取得用）
        private readonly float _intensity;
        private readonly bool _enableSplit;
        private readonly float _blendWidth;
        private readonly bool _overwrite;
        private readonly List<CustomBlendShapeMapping> _customMappings;
        private readonly bool _debug;
        private readonly bool _enableProceduralMouthShapes;
        private readonly float _proceduralMouthIntensity;

        private List<string> _generatedShapes = new List<string>();

        public BlendShapeProcessor(
            SkinnedMeshRenderer renderer,
            float intensity,
            bool enableSplit,
            float blendWidth,
            bool overwrite,
            List<CustomBlendShapeMapping> customMappings,
            bool debug,
            bool enableProceduralMouthShapes = false,
            float proceduralMouthIntensity = 1.0f)
        {
            _renderer = renderer;
            _originalMesh = renderer.sharedMesh;  // 元のメッシュを保持
            _mesh = UnityEngine.Object.Instantiate(renderer.sharedMesh);
            _intensity = intensity;
            _enableSplit = enableSplit;
            _blendWidth = blendWidth;
            _overwrite = overwrite;
            _customMappings = customMappings ?? new List<CustomBlendShapeMapping>();
            _debug = debug;
            _enableProceduralMouthShapes = enableProceduralMouthShapes;
            _proceduralMouthIntensity = proceduralMouthIntensity;
        }

        public void Process()
        {
            Log($"Processing mesh: {_mesh.name}, existing shapes: {_mesh.blendShapeCount}");
            Log($"Original mesh: {_originalMesh.name}, blendShapeCount: {_originalMesh.blendShapeCount}, isReadable: {_originalMesh.isReadable}");

            var result = BlendShapeGenerationEngine.Generate(
                _originalMesh,
                _mesh,
                _customMappings,
                ARKitMappingTable.GetMappings(),
                new BlendShapeGenerationOptions
                {
                    IntensityMultiplier = _intensity,
                    EnableLeftRightSplit = _enableSplit,
                    BlendWidth = _blendWidth,
                    OverwriteExisting = _overwrite,
                    EnableProceduralMouthShapes = _enableProceduralMouthShapes,
                    ProceduralMouthIntensity = _proceduralMouthIntensity,
                    Debug = _debug
                });
            _generatedShapes = result.GeneratedShapes.ToList();

            // メッシュを適用
            _renderer.sharedMesh = _mesh;

            Log($"Generated {_generatedShapes.Count} BlendShapes: {string.Join(", ", _generatedShapes)}");
        }

        private void Log(string message)
        {
            if (_debug)
            {
                Debug.Log($"[ARKitGenerator] {message}");
            }
        }
    }
}
