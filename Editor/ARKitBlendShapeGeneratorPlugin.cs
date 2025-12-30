using System;
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
                        .GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(true);

                    foreach (var component in components)
                    {
                        ProcessComponent(component, ctx);
                    }
                })
                .PreviewingWith(new ARKitBlendShapeGeneratorPreview());
        }

        private void ProcessComponent(ARKitBlendShapeGeneratorComponent component, BuildContext ctx)
        {
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
                component.overwriteExisting,
                component.customMappings,
                component.debugMode
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
        private readonly bool _overwrite;
        private readonly List<CustomBlendShapeMapping> _customMappings;
        private readonly bool _debug;

        private Dictionary<string, int> _existingShapes;
        private List<string> _generatedShapes = new List<string>();
        private HashSet<string> _customMappedNames = new HashSet<string>();

        public BlendShapeProcessor(
            SkinnedMeshRenderer renderer,
            float intensity,
            bool enableSplit,
            bool overwrite,
            List<CustomBlendShapeMapping> customMappings,
            bool debug)
        {
            _renderer = renderer;
            _originalMesh = renderer.sharedMesh;  // 元のメッシュを保持
            _mesh = UnityEngine.Object.Instantiate(renderer.sharedMesh);
            _intensity = intensity;
            _enableSplit = enableSplit;
            _overwrite = overwrite;
            _customMappings = customMappings ?? new List<CustomBlendShapeMapping>();
            _debug = debug;

            // 既存のBlendShapeをインデックス化（元のメッシュから）
            _existingShapes = new Dictionary<string, int>();
            for (int i = 0; i < _originalMesh.blendShapeCount; i++)
            {
                _existingShapes[_originalMesh.GetBlendShapeName(i)] = i;
            }
        }

        public void Process()
        {
            Log($"Processing mesh: {_mesh.name}, existing shapes: {_mesh.blendShapeCount}");
            Log($"Original mesh: {_originalMesh.name}, blendShapeCount: {_originalMesh.blendShapeCount}, isReadable: {_originalMesh.isReadable}");

            // 1. カスタムマッピングを先に処理
            ProcessCustomMappings();

            // 2. 自動マッピングを処理（カスタムで定義されたものはスキップ）
            ProcessAutoMappings();

            // メッシュを適用
            _renderer.sharedMesh = _mesh;

            Log($"Generated {_generatedShapes.Count} BlendShapes: {string.Join(", ", _generatedShapes)}");
        }

        private void ProcessCustomMappings()
        {
            foreach (var mapping in _customMappings)
            {
                if (!mapping.enabled || string.IsNullOrEmpty(mapping.arkitName))
                    continue;

                if (mapping.sources == null || mapping.sources.Count == 0)
                    continue;

                // カスタムマッピングで処理した名前を記録
                _customMappedNames.Add(mapping.arkitName);

                // 既に存在し、上書きしない場合はスキップ
                if (_existingShapes.ContainsKey(mapping.arkitName) && !_overwrite)
                {
                    Log($"Skip custom (exists): {mapping.arkitName}");
                    continue;
                }

                // ソースを検索（side情報付き）
                var sources = new List<(int index, float weight, BlendShapeSide side)>();
                foreach (var src in mapping.sources)
                {
                    if (string.IsNullOrEmpty(src.blendShapeName))
                        continue;

                    if (_existingShapes.TryGetValue(src.blendShapeName, out int index))
                    {
                        sources.Add((index, src.weight, src.side));
                    }
                    else
                    {
                        Log($"Warning: Source not found: {src.blendShapeName} for {mapping.arkitName}");
                    }
                }

                if (sources.Count == 0)
                {
                    Log($"Skip custom (no valid source): {mapping.arkitName}");
                    continue;
                }

                // BlendShapeを生成（side対応版）
                GenerateBlendShapeWithSide(mapping.arkitName, sources);
            }
        }

        private void ProcessAutoMappings()
        {
            var mappings = GetMappingTable();

            // 処理済みのARKit名を追跡（自動マッピング内での重複を管理）
            var processedArkitNames = new HashSet<string>();

            foreach (var mapping in mappings)
            {
                // カスタムマッピングで既に処理済みならスキップ
                if (_customMappedNames.Contains(mapping.arkitName))
                {
                    Log($"Skip auto (custom defined): {mapping.arkitName}");
                    continue;
                }

                // 既にこの自動マッピング処理内で生成済みならスキップ
                if (processedArkitNames.Contains(mapping.arkitName))
                {
                    Log($"Skip auto (already generated in this pass): {mapping.arkitName}");
                    continue;
                }

                // 元のメッシュに既に存在し、上書きしない場合はスキップ
                // 注: _existingShapesは元のメッシュのBlendShapeのみを含む（生成したものは含まない）
                if (_existingShapes.ContainsKey(mapping.arkitName) && !_overwrite)
                {
                    Log($"Skip auto (exists in original): {mapping.arkitName}");
                    continue;
                }

                // ソースBlendShapeを検索
                var sources = FindSources(mapping.sources);
                if (sources.Count == 0)
                {
                    Log($"Skip auto (no source): {mapping.arkitName}");
                    continue;
                }

                // 左右分割が有効で、かつマッピングにside指定がある場合は左右フィルタリング版を使用
                if (_enableSplit && mapping.side != BlendShapeSide.Both)
                {
                    var sourcesWithSide = sources.Select(s => (s.index, s.weight, mapping.side)).ToList();
                    GenerateBlendShapeWithSide(mapping.arkitName, sourcesWithSide);
                }
                else
                {
                    GenerateBlendShape(mapping.arkitName, sources);
                }

                // 処理済みとしてマーク
                processedArkitNames.Add(mapping.arkitName);
            }
        }

        private List<(int index, float weight)> FindSources(List<SourceMapping> mappings)
        {
            var result = new List<(int, float)>();

            foreach (var src in mappings)
            {
                foreach (var name in src.names)
                {
                    if (_existingShapes.TryGetValue(name, out int index))
                    {
                        result.Add((index, src.weight));
                        break; // 最初に見つかったものを使用
                    }
                }
            }

            return result;
        }

        private void GenerateBlendShape(string name, List<(int index, float weight)> sources)
        {
            int vertexCount = _originalMesh.vertexCount;
            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];

            // ソースBlendShapeを合成（元のメッシュからデルタを取得）
            foreach (var (index, weight) in sources)
            {
                var srcDeltaV = new Vector3[vertexCount];
                var srcDeltaN = new Vector3[vertexCount];
                var srcDeltaT = new Vector3[vertexCount];

                // 最後のフレーム（通常100%のフレーム）を使用
                int frameCount = _originalMesh.GetBlendShapeFrameCount(index);
                int targetFrame = frameCount > 0 ? frameCount - 1 : 0;
                _originalMesh.GetBlendShapeFrameVertices(index, targetFrame, srcDeltaV, srcDeltaN, srcDeltaT);

                float adjustedWeight = weight * _intensity;
                for (int i = 0; i < vertexCount; i++)
                {
                    deltaVertices[i] += srcDeltaV[i] * adjustedWeight;
                    deltaNormals[i] += srcDeltaN[i] * adjustedWeight;
                    deltaTangents[i] += srcDeltaT[i] * adjustedWeight;
                }
            }

            // 既存のBlendShapeを上書きする場合は先に削除（Unityは直接削除できないため新規追加のみ）
            // 注: 実際には同名で追加すると後のものが優先される

            // BlendShapeを追加
            _mesh.AddBlendShapeFrame(name, 100f, deltaVertices, deltaNormals, deltaTangents);
            _existingShapes[name] = _mesh.blendShapeCount - 1;
            _generatedShapes.Add(name);

            Log($"Generated: {name} from {sources.Count} source(s)");
        }

        /// <summary>
        /// 左右フィルタリング対応のBlendShape生成
        /// </summary>
        private void GenerateBlendShapeWithSide(string name, List<(int index, float weight, BlendShapeSide side)> sources)
        {
            int vertexCount = _originalMesh.vertexCount;
            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];

            // メッシュの頂点座標を取得（左右判定用）- 元のメッシュから
            var vertices = _originalMesh.vertices;

            // デバッグ用: 頂点のX座標範囲を調査
            if (_debug)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                foreach (var v in vertices)
                {
                    if (v.x < minX) minX = v.x;
                    if (v.x > maxX) maxX = v.x;
                }
                Log($"Mesh vertex X range: {minX:F4} to {maxX:F4}");
            }

            // ソースBlendShapeを合成（元のメッシュからデルタを取得）
            foreach (var (index, weight, side) in sources)
            {
                var srcDeltaV = new Vector3[vertexCount];
                var srcDeltaN = new Vector3[vertexCount];
                var srcDeltaT = new Vector3[vertexCount];

                // フレーム情報を取得（元のメッシュから）
                int frameCount = _originalMesh.GetBlendShapeFrameCount(index);
                string shapeName = _originalMesh.GetBlendShapeName(index);

                if (_debug)
                {
                    Log($"  BlendShape '{shapeName}' (index={index}): frameCount={frameCount}");
                    for (int f = 0; f < frameCount; f++)
                    {
                        float frameWeight = _originalMesh.GetBlendShapeFrameWeight(index, f);
                        Log($"    Frame {f}: weight={frameWeight}");
                    }
                }

                // 最後のフレーム（通常100%のフレーム）を使用
                int targetFrame = frameCount > 0 ? frameCount - 1 : 0;
                _originalMesh.GetBlendShapeFrameVertices(index, targetFrame, srcDeltaV, srcDeltaN, srcDeltaT);

                float adjustedWeight = weight * _intensity;

                // デバッグ用カウンター
                int leftCount = 0, rightCount = 0, centerCount = 0, appliedCount = 0;
                int hasMovementCount = 0;

                for (int i = 0; i < vertexCount; i++)
                {
                    // デルタが存在するか確認
                    bool hasMovement = srcDeltaV[i].sqrMagnitude > 0.0001f;
                    if (hasMovement) hasMovementCount++;

                    // 左右フィルタリング
                    // ARKitは視聴者視点（アバターを見ている人の視点）で左右を定義:
                    // eyeBlinkLeft = 視聴者の左 = アバターの右側 = X < 0
                    // eyeBlinkRight = 視聴者の右 = アバターの左側 = X > 0
                    float vertexX = vertices[i].x;

                    // 中央付近の閾値（顔の中心線付近の頂点は両方に含める）
                    const float CENTER_THRESHOLD = 0.0001f;

                    bool shouldApply = side switch
                    {
                        // ARKit Left = 視聴者の左 = アバターの右側 = X < 0
                        BlendShapeSide.LeftOnly => vertexX < CENTER_THRESHOLD,
                        // ARKit Right = 視聴者の右 = アバターの左側 = X > 0
                        BlendShapeSide.RightOnly => vertexX > -CENTER_THRESHOLD,
                        _ => true  // Both
                    };

                    // デバッグカウント
                    if (vertexX > CENTER_THRESHOLD) leftCount++;
                    else if (vertexX < -CENTER_THRESHOLD) rightCount++;
                    else centerCount++;

                    if (shouldApply && hasMovement) appliedCount++;

                    if (shouldApply)
                    {
                        deltaVertices[i] += srcDeltaV[i] * adjustedWeight;
                        deltaNormals[i] += srcDeltaN[i] * adjustedWeight;
                        deltaTangents[i] += srcDeltaT[i] * adjustedWeight;
                    }
                }

                if (_debug)
                {
                    string sourceName = _mesh.GetBlendShapeName(index);
                    Log($"  Source '{sourceName}' side={side}: vertices with movement={hasMovementCount}, applied={appliedCount}");
                    Log($"    Vertex distribution: left(X>0)={leftCount}, right(X<0)={rightCount}, center={centerCount}");
                }
            }

            // BlendShapeを追加
            _mesh.AddBlendShapeFrame(name, 100f, deltaVertices, deltaNormals, deltaTangents);
            _existingShapes[name] = _mesh.blendShapeCount - 1;
            _generatedShapes.Add(name);

            Log($"Generated (with side filter): {name} from {sources.Count} source(s)");
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
        private List<ARKitMapping> GetMappingTable()
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
