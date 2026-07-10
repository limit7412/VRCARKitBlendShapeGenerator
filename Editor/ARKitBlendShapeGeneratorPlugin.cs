using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using nadena.dev.ndmf;

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
                            $"[ARKitGenerator] 同一アバター内で複数のARKitBlendShapeGeneratorComponentが検出されました。\"{primaryComponent.name}\" のみ処理します。",
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
                    "[ARKitGenerator] カスタムマッピングで同一ARKit名が重複しているため、生成を中止しました。\n" +
                    $"重複: {string.Join(", ", duplicateArkitNames)}",
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
                Debug.LogWarning("[ARKitGenerator] SkinnedMeshRenderer not found");
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
            bool enableProceduralMouthShapes = true,
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
                GetMappingTable(),
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

        /// <summary>
        /// VRChat/MMD → ARKit マッピングテーブル
        /// </summary>
        internal static List<ARKitMapping> GetMappingTable()
        {
            return new List<ARKitMapping>
            {
                // === 目 (Eye) ===
                // 注: 左右別のソースが存在する場合はそちらを優先（最初にマッチしたものを使用）
                // 優先順位: 1. 左右別ソース(vrc.blink_left等), 2. 両目用を左右分割(vrc.blink, まばたき等)
                new ARKitMapping("eyeBlinkLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.blink_left", "blink_left", "Blink_L"),
                }),
                new ARKitMapping("eyeBlinkRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.blink_right", "blink_right", "Blink_R"),
                }),
                // 左右別ソースが見つからない場合のフォールバック: 両目用から左右別に生成
                // vrc.blinkを追加（VRChatの標準的な両目まばたき）
                new ARKitMapping("eyeBlinkLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.blink", "まばたき", "ウィンク", "blink"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeBlinkRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.blink", "まばたき", "ウィンク右", "blink"),
                }, BlendShapeSide.RightOnly),
                // eyeSquint - 左右別のソースがある場合
                new ARKitMapping("eyeSquintLeft", new List<SourceMapping> {
                    new SourceMapping(0.7f, "Squint_L", "squint_left"),
                }),
                new ARKitMapping("eyeSquintRight", new List<SourceMapping> {
                    new SourceMapping(0.7f, "Squint_R", "squint_right"),
                }),
                // eyeSquint - 両目用から左右分割
                new ARKitMapping("eyeSquintLeft", new List<SourceMapping> {
                    new SourceMapping(0.7f, "笑い", "にこり", "><", "笑い目"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeSquintRight", new List<SourceMapping> {
                    new SourceMapping(0.7f, "笑い", "にこり", "><", "笑い目"),
                }, BlendShapeSide.RightOnly),
                // eyeWide - 左右別のソースがある場合
                new ARKitMapping("eyeWideLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Wide_L", "wide_left"),
                }),
                new ARKitMapping("eyeWideRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Wide_R", "wide_right"),
                }),
                // eyeWide - 両目用から左右分割
                new ARKitMapping("eyeWideLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "びっくり", "見開き", "驚き"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeWideRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "びっくり", "見開き", "驚き"),
                }, BlendShapeSide.RightOnly),

                // === 視線 (Eye Look) - 通常は手動設定が必要 ===
                // 既に左右別のBlendShapeがある場合
                new ARKitMapping("eyeLookUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeUp_L", "eye_up_L"),
                }),
                new ARKitMapping("eyeLookUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeUp_R", "eye_up_R"),
                }),
                new ARKitMapping("eyeLookDownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeDown_L", "eye_down_L"),
                }),
                new ARKitMapping("eyeLookDownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeDown_R", "eye_down_R"),
                }),
                new ARKitMapping("eyeLookInLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeIn_L", "eye_in_L"),
                }),
                new ARKitMapping("eyeLookInRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeIn_R", "eye_in_R"),
                }),
                new ARKitMapping("eyeLookOutLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeOut_L", "eye_out_L"),
                }),
                new ARKitMapping("eyeLookOutRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "EyeOut_R", "eye_out_R"),
                }),
                // 両目用のBlendShapeから左右分割
                new ARKitMapping("eyeLookUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "目上"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeLookUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "目上"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("eyeLookDownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "目下"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeLookDownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "目下"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("eyeLookInLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "より目"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("eyeLookInRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "より目"),
                }, BlendShapeSide.RightOnly),

                // === 眉毛 (Brow) ===
                // 既に左右別のBlendShapeがある場合
                new ARKitMapping("browDownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "BrowDown_L"),
                }),
                new ARKitMapping("browDownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "BrowDown_R"),
                }),
                // 両眉用から左右分割
                new ARKitMapping("browDownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "怒り", "真面目", "困る"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("browDownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "怒り", "真面目", "困る"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("browInnerUp", new List<SourceMapping> {
                    new SourceMapping(1.0f, "困る", "上", "悲しい", "BrowInnerUp"),
                }),
                new ARKitMapping("browOuterUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "BrowOuterUp_L"),
                }),
                new ARKitMapping("browOuterUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "BrowOuterUp_R"),
                }),
                new ARKitMapping("browOuterUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "上", "驚き"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("browOuterUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "上", "驚き"),
                }, BlendShapeSide.RightOnly),

                // === 口 - 母音 (Mouth Vowels) ===
                new ARKitMapping("jawOpen", new List<SourceMapping> {
                    new SourceMapping(0.7f, "vrc.v_aa", "あ", "a", "A"),
                }),
                new ARKitMapping("mouthFunnel", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ou", "う", "u", "U"),
                }),
                new ARKitMapping("mouthPucker", new List<SourceMapping> {
                    new SourceMapping(1.2f, "vrc.v_ou", "う", "ω", "u", "U"),
                }),

                // === 口 - 表情 (Mouth Expressions) ===
                // 既に左右別のBlendShapeがある場合
                new ARKitMapping("mouthSmileLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Smile_L"),
                }),
                new ARKitMapping("mouthSmileRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Smile_R"),
                }),
                // 両側用から左右分割
                new ARKitMapping("mouthSmileLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "にやり", "∧", "にっこり"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthSmileRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "にやり", "∧", "にっこり"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("mouthFrownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Frown_L"),
                }),
                new ARKitMapping("mouthFrownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "Frown_R"),
                }),
                new ARKitMapping("mouthFrownLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "への字", "悲しみ"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthFrownRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "への字", "悲しみ"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("mouthLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "口左", "MouthLeft"),
                }),
                new ARKitMapping("mouthRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "口右", "MouthRight"),
                }),
                // mouthUpperUp/LowerDown - 左右分割
                new ARKitMapping("mouthUpperUpLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ih", "い", "i", "I"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthUpperUpRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ih", "い", "i", "I"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("mouthLowerDownLeft", new List<SourceMapping> {
                    new SourceMapping(0.6f, "vrc.v_aa", "あ", "a", "A"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthLowerDownRight", new List<SourceMapping> {
                    new SourceMapping(0.6f, "vrc.v_aa", "あ", "a", "A"),
                }, BlendShapeSide.RightOnly),
                new ARKitMapping("mouthClose", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_nn", "ん", "n", "N"),
                }),
                new ARKitMapping("mouthShrugUpper", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ch", "え", "e", "E"),
                }),
                new ARKitMapping("mouthShrugLower", new List<SourceMapping> {
                    new SourceMapping(0.5f, "vrc.v_oh", "お", "o", "O"),
                }),
                new ARKitMapping("mouthPress", new List<SourceMapping> {
                    new SourceMapping(1.0f, "むっ", "MouthPress"),
                }),
                new ARKitMapping("mouthStretchLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ih", "い", "i"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("mouthStretchRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "vrc.v_ih", "い", "i"),
                }, BlendShapeSide.RightOnly),

                // === 頬 (Cheek) ===
                new ARKitMapping("cheekPuff", new List<SourceMapping> {
                    new SourceMapping(1.0f, "ぷく", "膨らみ", "CheekPuff"),
                }),
                // 既に左右別がある場合
                new ARKitMapping("cheekSquintLeft", new List<SourceMapping> {
                    new SourceMapping(0.8f, "CheekSquint_L"),
                }),
                new ARKitMapping("cheekSquintRight", new List<SourceMapping> {
                    new SourceMapping(0.8f, "CheekSquint_R"),
                }),
                // 両側用から左右分割
                new ARKitMapping("cheekSquintLeft", new List<SourceMapping> {
                    new SourceMapping(0.8f, "笑い", "にこり"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("cheekSquintRight", new List<SourceMapping> {
                    new SourceMapping(0.8f, "笑い", "にこり"),
                }, BlendShapeSide.RightOnly),

                // === 鼻 (Nose) ===
                new ARKitMapping("noseSneerLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "NoseSneer_L"),
                }),
                new ARKitMapping("noseSneerRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "NoseSneer_R"),
                }),
                new ARKitMapping("noseSneerLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "怒り"),
                }, BlendShapeSide.LeftOnly),
                new ARKitMapping("noseSneerRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "怒り"),
                }, BlendShapeSide.RightOnly),

                // === 顎 (Jaw) ===
                new ARKitMapping("jawForward", new List<SourceMapping> {
                    new SourceMapping(1.0f, "JawForward"),
                }),
                new ARKitMapping("jawLeft", new List<SourceMapping> {
                    new SourceMapping(1.0f, "JawLeft"),
                }),
                new ARKitMapping("jawRight", new List<SourceMapping> {
                    new SourceMapping(1.0f, "JawRight"),
                }),

                // === 舌 (Tongue) ===
                new ARKitMapping("tongueOut", new List<SourceMapping> {
                    new SourceMapping(1.0f, "べー", "舌", "TongueOut"),
                }),
            };
        }
    }

    /// <summary>
    /// ARKitマッピング定義
    /// </summary>
    public class ARKitMapping
    {
        public string arkitName;
        public List<SourceMapping> sources;
        public BlendShapeSide side;  // 左右フィルタリング用

        public ARKitMapping(string name, List<SourceMapping> sources, BlendShapeSide side = BlendShapeSide.Both)
        {
            this.arkitName = name;
            this.sources = sources;
            this.side = side;
        }
    }

    /// <summary>
    /// ソースBlendShapeマッピング
    /// </summary>
    public class SourceMapping
    {
        public float weight;
        public string[] names;

        public SourceMapping(float weight, params string[] names)
        {
            this.weight = weight;
            this.names = names;
        }
    }
}
