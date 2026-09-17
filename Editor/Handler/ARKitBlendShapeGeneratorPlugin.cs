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
            // Transforming Phaseで、表情改変で崩れたシェイプキーを直すツールの後に実行する。
            // 生成は既存シェイプキーの変形を写し取るため、修正前のまばたきや口から生成すると
            // 崩れたままのARKit BlendShapeになる。
            // Avatar Blink Fixは打ち消し修正をTransformingで、ベイク修正をGeneratingで行う
            // （Fermata併用時はベイクもTransformingへ遅延する）ため、両方の後ろへ置く。
            // Face BlendShape FixはGeneratingで動くので、フェーズの順序だけで先行が決まる。
            // NDMFの制約はプラグインごとの仮想アンカーへ結び付くため、相手が未導入でも
            // 別フェーズでも指定できる。
            // Modular Avatarより前に置くのは、Jerry's Templates（MAプレハブ）を含む
            // アニメーションの統合より先にシェイプキーを揃えておくため。
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("dev.lemoneru.avatar-blink-fix")
                .AfterPlugin("dev.lemoneru.avatar-blink-fix.redefine")
                .BeforePlugin("nadena.dev.modular-avatar")
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

                    // ビルド時にのみ意味を持つコンポーネントなので、生成の成否にかかわらず取り除く。
                    // ビルド終盤で動作するAvatar Optimizer等から「未知のコンポーネント」として
                    // 検出されないようにするため、Optimizing Phaseより前のここで削除する。
                    RemoveComponents(components);
                })
                .PreviewingWith(new ARKitBlendShapeGeneratorPreview());
        }

        private static void RemoveComponents(ARKitBlendShapeGeneratorComponent[] components)
        {
            if (components == null)
            {
                return;
            }

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                Object.DestroyImmediate(component);
            }
        }
    }
}
