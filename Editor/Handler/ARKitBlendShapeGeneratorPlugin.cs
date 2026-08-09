using System.Linq;
using UnityEngine;
using nadena.dev.ndmf;
using ARKitBlendShapeGenerator.UseCase;
using static ARKitBlendShapeGenerator.Localization;

[assembly: ExportsPlugin(typeof(ARKitBlendShapeGenerator.Handler.ARKitBlendShapeGeneratorPlugin))]

namespace ARKitBlendShapeGenerator.Handler
{
    /// <summary>
    /// NDMFビルドのエントリポイント
    /// コンポーネントの収集と選定を行い、生成処理はUseCase層へ委譲する
    /// </summary>
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
                    var primaryComponent = GenerateBlendShapesUseCase.SelectPrimaryComponent(
                        ctx.AvatarRootObject, components);

                    if (components.Length > 1 && primaryComponent != null)
                    {
                        Debug.LogWarning(
                            "[ARKitGenerator] " + S("log.multiple_components", primaryComponent.name),
                            primaryComponent);
                    }

                    if (primaryComponent != null)
                    {
                        GenerateBlendShapesUseCase.ExecuteForBuild(primaryComponent);
                    }
                })
                .PreviewingWith(new ARKitBlendShapeGeneratorPreview());
        }
    }
}
