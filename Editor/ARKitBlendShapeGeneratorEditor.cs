using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace ARKitBlendShapeGenerator
{
    [CustomEditor(typeof(ARKitBlendShapeGeneratorComponent))]
    public class ARKitBlendShapeGeneratorEditor : Editor
    {
        private ARKitBlendShapeGeneratorComponent _component;
        private List<string> _availableBlendShapes = new List<string>();
        private bool _showCustomMappings = true;
        private bool _showAutoMappings = false;
        private bool _showPreview = false;
        private Vector2 _scrollPosition;

        // カテゴリごとの折りたたみ状態
        private bool _foldEye = true;
        private bool _foldEyeLook = true;
        private bool _foldBrow = true;
        private bool _foldMouth = false;
        private bool _foldCheek = false;
        private bool _foldNose = false;
        private bool _foldTongue = false;

        // 検索とフィルタ
        private string _searchFilter = "";
        private int _categoryFilter = 0;
        private static readonly string[] _categoryOptions = {
            "全て", "目 (Eye)", "視線 (Eye Look)", "眉毛 (Brow)",
            "口 (Mouth)", "頬 (Cheek)", "鼻 (Nose)", "舌 (Tongue)"
        };

        // プレビュー用
        private int _previewMappingIndex = -1;
        private float _previewWeight = 1.0f;
        private Dictionary<int, float> _originalWeights = new Dictionary<int, float>();
        private Mesh _originalMesh;
        private Mesh _previewMesh;
        private int _previewBlendShapeIndex = -1;
        private const string PreviewBlendShapeName = "__ARKitGenerator_Preview__";

        private void OnEnable()
        {
            _component = (ARKitBlendShapeGeneratorComponent)target;
            RefreshBlendShapeList();
        }

        private void OnDisable()
        {
            // プレビューをリセット
            ResetPreview();
        }

        private void RefreshBlendShapeList()
        {
            _availableBlendShapes = _component.GetAvailableBlendShapes();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ヘッダー
            EditorGUILayout.LabelField("ARKit BlendShape Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "VRChat/MMDのBlendShapeからARKit用BlendShapeを自動生成します。",
                MessageType.Info);

            EditorGUILayout.Space();

            // 基本設定
            EditorGUILayout.LabelField("基本設定", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetRenderer"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("BlendShapeリストを更新"))
            {
                RefreshBlendShapeList();
            }
            EditorGUILayout.LabelField($"検出: {_availableBlendShapes.Count}個", GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("intensityMultiplier"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableLeftRightSplit"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("blendWidth"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("overwriteExisting"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // カスタムマッピング
            _showCustomMappings = EditorGUILayout.Foldout(_showCustomMappings, "カスタムマッピング", true);
            if (_showCustomMappings)
            {
                EditorGUI.indentLevel++;
                DrawCustomMappingsUI();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // プレビュー
            _showPreview = EditorGUILayout.Foldout(_showPreview, "プレビュー", true);
            if (_showPreview)
            {
                EditorGUI.indentLevel++;
                DrawPreviewUI();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // 自動マッピング情報
            _showAutoMappings = EditorGUILayout.Foldout(_showAutoMappings, "自動マッピング一覧（参照用）", true);
            if (_showAutoMappings)
            {
                EditorGUI.indentLevel++;
                DrawAutoMappingsInfo();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"));

            // デバッグ: 全SkinnedMeshRendererを表示
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全メッシュのBlendShape数を表示", EditorStyles.miniButton))
            {
                var allRenderers = _component.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Debug.Log($"=== 全SkinnedMeshRenderer ({allRenderers.Length}個) ===");
                foreach (var smr in allRenderers)
                {
                    int count = smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0;
                    Debug.Log($"  {smr.gameObject.name}: {count} BlendShapes");
                }
            }
            if (GUILayout.Button("特定メッシュのBlendShape名を表示", EditorStyles.miniButton))
            {
                if (_component.targetRenderer != null && _component.targetRenderer.sharedMesh != null)
                {
                    var mesh = _component.targetRenderer.sharedMesh;
                    Debug.Log($"=== {_component.targetRenderer.gameObject.name} BlendShapes ({mesh.blendShapeCount}個) ===");
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        Debug.Log($"  [{i}] {mesh.GetBlendShapeName(i)}");
                    }
                }
                else
                {
                    Debug.LogWarning("Target Renderer not set");
                }
            }
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawCustomMappingsUI()
        {
            EditorGUILayout.HelpBox(
                "自動マッピングできないBlendShape（視線など）を手動で指定できます。\n" +
                "ソースBlendShapeを複数指定して合成することも可能です。",
                MessageType.Info
            );

            // カテゴリ別クイック追加ボタン
            EditorGUILayout.LabelField("カテゴリ別追加", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("視線", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.EyeLook);
            }
            if (GUILayout.Button("目", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Eye);
            }
            if (GUILayout.Button("眉毛", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Brow);
            }
            if (GUILayout.Button("口", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Mouth);
            }
            if (GUILayout.Button("頬", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Cheek);
            }
            if (GUILayout.Button("鼻", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Nose);
            }
            if (GUILayout.Button("舌", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Tongue);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 検索フィルタ
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("検索:", GUILayout.Width(40));
            _searchFilter = EditorGUILayout.TextField(_searchFilter);
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新規マッピング追加"))
            {
                AddNewMapping();
            }
            if (GUILayout.Button("全て削除", GUILayout.Width(80)))
            {
                if (EditorUtility.DisplayDialog("確認", "全てのカスタムマッピングを削除しますか？", "はい", "いいえ"))
                {
                    _component.customMappings.Clear();
                    EditorUtility.SetDirty(_component);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // マッピング数表示
            int enabledCount = _component.customMappings.Count(m => m.enabled);
            EditorGUILayout.LabelField($"マッピング: {enabledCount}/{_component.customMappings.Count} 有効");

            // マッピングリスト表示
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MaxHeight(400));

            for (int i = 0; i < _component.customMappings.Count; i++)
            {
                var mapping = _component.customMappings[i];

                // 検索フィルタ適用
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    bool matchesArkit = mapping.arkitName.ToLower().Contains(_searchFilter.ToLower());
                    bool matchesSource = mapping.sources.Any(s =>
                        s.blendShapeName != null && s.blendShapeName.ToLower().Contains(_searchFilter.ToLower()));

                    if (!matchesArkit && !matchesSource)
                        continue;
                }

                DrawMappingItem(i);
            }

            EditorGUILayout.EndScrollView();
        }

        private void AddCategoryMappings(string[] arkitNames)
        {
            Undo.RecordObject(_component, "Add Category Mappings");

            foreach (var name in arkitNames)
            {
                if (_component.customMappings.Any(m => m.arkitName == name))
                    continue;

                _component.customMappings.Add(new CustomBlendShapeMapping
                {
                    arkitName = name,
                    enabled = true,
                    sources = new List<BlendShapeSource>()
                });
            }

            EditorUtility.SetDirty(_component);
        }

        private void DrawMappingItem(int index)
        {
            var mapping = _component.customMappings[index];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ヘッダー行
            EditorGUILayout.BeginHorizontal();

            mapping.enabled = EditorGUILayout.Toggle(mapping.enabled, GUILayout.Width(20));

            // ARKit名のドロップダウン
            var arkitNames = ARKitBlendShapeNames.GetAll();
            int currentIndex = System.Array.IndexOf(arkitNames, mapping.arkitName);
            if (currentIndex < 0) currentIndex = 0;

            int newIndex = EditorGUILayout.Popup(currentIndex, arkitNames);
            if (newIndex != currentIndex || string.IsNullOrEmpty(mapping.arkitName))
            {
                mapping.arkitName = arkitNames[newIndex];
                EditorUtility.SetDirty(_component);
            }

            if (GUILayout.Button("+", GUILayout.Width(25)))
            {
                mapping.sources.Add(new BlendShapeSource());
                EditorUtility.SetDirty(_component);
            }

            if (GUILayout.Button("×", GUILayout.Width(25)))
            {
                _component.customMappings.RemoveAt(index);
                EditorUtility.SetDirty(_component);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();

            // ソースBlendShape
            EditorGUI.indentLevel++;
            for (int j = 0; j < mapping.sources.Count; j++)
            {
                DrawSourceItem(mapping, j);
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();
        }

        private void DrawSourceItem(CustomBlendShapeMapping mapping, int sourceIndex)
        {
            var source = mapping.sources[sourceIndex];

            EditorGUILayout.BeginHorizontal();

            // BlendShape名のドロップダウン
            if (_availableBlendShapes.Count > 0)
            {
                var options = new List<string> { "(選択してください)" };
                options.AddRange(_availableBlendShapes);

                int currentIdx = _availableBlendShapes.IndexOf(source.blendShapeName) + 1;
                int newIdx = EditorGUILayout.Popup(currentIdx, options.ToArray());

                if (newIdx > 0 && newIdx <= _availableBlendShapes.Count)
                {
                    source.blendShapeName = _availableBlendShapes[newIdx - 1];
                    EditorUtility.SetDirty(_component);
                }
            }
            else
            {
                source.blendShapeName = EditorGUILayout.TextField(source.blendShapeName);
            }

            // 重み
            EditorGUILayout.LabelField("×", GUILayout.Width(15));
            source.weight = EditorGUILayout.Slider(source.weight, -2f, 2f, GUILayout.Width(100));

            // 左右適用範囲
            source.side = (BlendShapeSide)EditorGUILayout.EnumPopup(source.side, GUILayout.Width(70));

            // 削除ボタン
            if (GUILayout.Button("－", GUILayout.Width(25)))
            {
                mapping.sources.RemoveAt(sourceIndex);
                EditorUtility.SetDirty(_component);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void AddNewMapping()
        {
            var newMapping = new CustomBlendShapeMapping
            {
                arkitName = "eyeBlinkLeft",
                enabled = true,
                sources = new List<BlendShapeSource>()
            };
            _component.customMappings.Add(newMapping);
            EditorUtility.SetDirty(_component);
        }

        private void AddEyeLookMappings()
        {
            AddCategoryMappings(ARKitBlendShapeNames.EyeLook);
        }

        private void DrawPreviewUI()
        {
            if (_component.targetRenderer == null)
            {
                EditorGUILayout.HelpBox(
                    "Target Rendererを設定するとプレビューが利用できます。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                "マッピングを選択してプレビューできます。\n" +
                "左右フィルタリング（LeftOnly/RightOnly）もプレビューに反映されます。",
                MessageType.Info);

            // マッピング選択
            var enabledMappings = _component.customMappings
                .Select((m, i) => new { Mapping = m, Index = i })
                .Where(x => x.Mapping.enabled && x.Mapping.sources.Count > 0)
                .ToList();

            if (enabledMappings.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "プレビュー可能なマッピングがありません。\n" +
                    "有効なマッピングにソースBlendShapeを追加してください。",
                    MessageType.Info);
                return;
            }

            var options = enabledMappings.Select(x => x.Mapping.arkitName).ToArray();
            int currentSelection = enabledMappings.FindIndex(x => x.Index == _previewMappingIndex);
            if (currentSelection < 0) currentSelection = 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("マッピング:", GUILayout.Width(70));
            int newSelection = EditorGUILayout.Popup(currentSelection, options);

            bool mappingChanged = false;
            if (newSelection != currentSelection || _previewMappingIndex < 0)
            {
                ResetPreview();
                _previewMappingIndex = enabledMappings[newSelection].Index;
                // マッピング変更時は自動的に強度を1.0に設定
                _previewWeight = 1.0f;
                mappingChanged = true;
            }
            EditorGUILayout.EndHorizontal();

            // プレビュースライダー
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("強度:", GUILayout.Width(70));
            float newWeight = EditorGUILayout.Slider(_previewWeight, 0f, 1f);

            // 重み変更時のみnewWeightを適用、マッピング変更時は_previewWeightを維持
            if (Mathf.Abs(newWeight - _previewWeight) > 0.001f)
            {
                _previewWeight = newWeight;
                ApplyPreview();
            }
            else if (mappingChanged)
            {
                // _previewWeightは既に1.0に設定済み
                ApplyPreview();
            }
            EditorGUILayout.EndHorizontal();

            // ソースBlendShape情報
            if (_previewMappingIndex >= 0 && _previewMappingIndex < _component.customMappings.Count)
            {
                var mapping = _component.customMappings[_previewMappingIndex];
                EditorGUILayout.LabelField("ソース:", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                foreach (var source in mapping.sources)
                {
                    // 実際の適用重み: source.weight × intensityMultiplier × previewWeight
                    float effectiveWeight = source.weight * _component.intensityMultiplier * _previewWeight;
                    string sideLabel = source.side == BlendShapeSide.Both ? "" : $" [{source.side}]";
                    EditorGUILayout.LabelField(
                        $"{source.blendShapeName}{sideLabel}",
                        $"重み: {source.weight:F2} → 適用: {effectiveWeight * 100:F0}%");
                }
                EditorGUI.indentLevel--;
            }

            // リセットボタン
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("プレビューをリセット"))
            {
                ResetPreview();
                _previewWeight = 0f;
            }
            if (GUILayout.Button("最大 (1.0)"))
            {
                _previewWeight = 1.0f;
                ApplyPreview();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ApplyPreview()
        {
            if (_component.targetRenderer == null)
            {
                Debug.LogWarning("[ARKitGenerator Preview] Target Renderer is null");
                return;
            }
            if (_previewMappingIndex < 0 || _previewMappingIndex >= _component.customMappings.Count)
            {
                Debug.LogWarning("[ARKitGenerator Preview] Invalid preview mapping index");
                return;
            }

            var mapping = _component.customMappings[_previewMappingIndex];

            // 元のメッシュを保存（初回のみ）
            if (_originalMesh == null)
            {
                _originalMesh = _component.targetRenderer.sharedMesh;
                _previewMesh = UnityEngine.Object.Instantiate(_originalMesh);
                _previewMesh.name = _originalMesh.name + "_Preview";

                // 元のウェイトを保存
                for (int i = 0; i < _originalMesh.blendShapeCount; i++)
                {
                    _originalWeights[i] = _component.targetRenderer.GetBlendShapeWeight(i);
                }

                Debug.Log($"[ARKitGenerator Preview] Initialized preview mesh from {_originalMesh.name}");
            }

            // 既存のプレビューBlendShapeがあれば、新しいメッシュを作り直す
            if (_previewBlendShapeIndex >= 0)
            {
                UnityEngine.Object.DestroyImmediate(_previewMesh);
                _previewMesh = UnityEngine.Object.Instantiate(_originalMesh);
                _previewMesh.name = _originalMesh.name + "_Preview";
            }

            // プレビュー用BlendShapeを生成（左右フィルタリング対応）
            GeneratePreviewBlendShape(mapping);

            // プレビューメッシュを適用
            _component.targetRenderer.sharedMesh = _previewMesh;

            // 全てのBlendShapeをリセット
            foreach (var kvp in _originalWeights)
            {
                _component.targetRenderer.SetBlendShapeWeight(kvp.Key, kvp.Value);
            }

            // プレビューBlendShapeを適用
            if (_previewBlendShapeIndex >= 0)
            {
                // intensityMultiplierは既にGeneratePreviewBlendShape内で適用済み
                float weight = _previewWeight * 100f;
                _component.targetRenderer.SetBlendShapeWeight(_previewBlendShapeIndex, weight);

                Debug.Log($"[ARKitGenerator Preview] Applied preview: {mapping.arkitName}, weight={weight}%, sources={mapping.sources.Count}");
            }

            // シーンビューを更新
            SceneView.RepaintAll();
        }

        private void GeneratePreviewBlendShape(CustomBlendShapeMapping mapping)
        {
            int vertexCount = _previewMesh.vertexCount;
            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];

            // メッシュの頂点座標を取得（左右判定用）
            var vertices = _previewMesh.vertices;

            int sourceCount = 0;
            // ソースBlendShapeを合成
            foreach (var source in mapping.sources)
            {
                if (string.IsNullOrEmpty(source.blendShapeName))
                {
                    Debug.LogWarning($"[ARKitGenerator Preview] Empty source blendshape name in {mapping.arkitName}");
                    continue;
                }

                int srcIndex = _previewMesh.GetBlendShapeIndex(source.blendShapeName);
                if (srcIndex < 0)
                {
                    Debug.LogWarning($"[ARKitGenerator Preview] Source blendshape not found: {source.blendShapeName}");
                    continue;
                }

                var srcDeltaV = new Vector3[vertexCount];
                var srcDeltaN = new Vector3[vertexCount];
                var srcDeltaT = new Vector3[vertexCount];

                int frameCount = _previewMesh.GetBlendShapeFrameCount(srcIndex);
                if (frameCount == 0)
                {
                    Debug.LogWarning($"[ARKitGenerator Preview] Source blendshape has no frames: {source.blendShapeName}");
                    continue;
                }

                _previewMesh.GetBlendShapeFrameVertices(srcIndex, 0, srcDeltaV, srcDeltaN, srcDeltaT);
                Debug.Log($"[ARKitGenerator Preview] Got frame vertices for {source.blendShapeName}");

                // intensityMultiplierを適用（本番処理と同じ）
                float adjustedWeight = source.weight * _component.intensityMultiplier;

                for (int i = 0; i < vertexCount; i++)
                {
                    // 左右フィルタリング
                    float vertexX = vertices[i].x;

                    bool shouldApply = source.side switch
                    {
                        BlendShapeSide.LeftOnly => vertexX >= -0.001f,
                        BlendShapeSide.RightOnly => vertexX <= 0.001f,
                        _ => true
                    };

                    if (shouldApply)
                    {
                        deltaVertices[i] += srcDeltaV[i] * adjustedWeight;
                        deltaNormals[i] += srcDeltaN[i] * adjustedWeight;
                        deltaTangents[i] += srcDeltaT[i] * adjustedWeight;
                    }
                }

                sourceCount++;
                Debug.Log($"[ARKitGenerator Preview] Added source: {source.blendShapeName} (weight={adjustedWeight}, side={source.side})");
            }

            if (sourceCount == 0)
            {
                Debug.LogWarning($"[ARKitGenerator Preview] No valid sources found for {mapping.arkitName}");
            }

            // プレビューBlendShapeを追加
            _previewMesh.AddBlendShapeFrame(PreviewBlendShapeName, 100f, deltaVertices, deltaNormals, deltaTangents);
            _previewBlendShapeIndex = _previewMesh.blendShapeCount - 1;

            Debug.Log($"[ARKitGenerator Preview] Generated preview blendshape at index {_previewBlendShapeIndex} from {sourceCount} sources");
        }

        private void ResetPreview()
        {
            if (_component.targetRenderer == null) return;

            // 元のメッシュに戻す
            if (_originalMesh != null)
            {
                _component.targetRenderer.sharedMesh = _originalMesh;

                // 元のウェイトに戻す
                foreach (var kvp in _originalWeights)
                {
                    _component.targetRenderer.SetBlendShapeWeight(kvp.Key, kvp.Value);
                }
            }

            // プレビューメッシュを破棄
            if (_previewMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewMesh);
                _previewMesh = null;
            }

            _originalMesh = null;
            _originalWeights.Clear();
            _previewMappingIndex = -1;
            _previewBlendShapeIndex = -1;
            _previewWeight = 0f;

            SceneView.RepaintAll();
        }

        private void DrawAutoMappingsInfo()
        {
            EditorGUILayout.HelpBox(
                "以下のBlendShapeは自動的にマッピングされます。\n" +
                "カスタムマッピングで同じARKit名を指定した場合、カスタム設定が優先されます。",
                MessageType.Info
            );

            // 目
            _foldEye = EditorGUILayout.Foldout(_foldEye, "目 (Eye)");
            if (_foldEye)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("eyeBlinkLeft/Right", "← vrc.blink_left/right, まばたき, ウィンク");
                EditorGUILayout.LabelField("eyeSquintLeft/Right", "← 笑い, にこり, ><");
                EditorGUILayout.LabelField("eyeWideLeft/Right", "← びっくり, 見開き");
                EditorGUI.indentLevel--;
            }

            // 視線
            _foldEyeLook = EditorGUILayout.Foldout(_foldEyeLook, "視線 (Eye Look) ※通常は手動設定が必要");
            if (_foldEyeLook)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("eyeLookUpLeft/Right", "← (手動設定推奨)");
                EditorGUILayout.LabelField("eyeLookDownLeft/Right", "← (手動設定推奨)");
                EditorGUILayout.LabelField("eyeLookInLeft/Right", "← より目");
                EditorGUILayout.LabelField("eyeLookOutLeft/Right", "← (手動設定推奨)");
                EditorGUI.indentLevel--;
            }

            // 眉毛
            _foldBrow = EditorGUILayout.Foldout(_foldBrow, "眉毛 (Brow)");
            if (_foldBrow)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("browDownLeft/Right", "← 怒り, 真面目, 困る");
                EditorGUILayout.LabelField("browInnerUp", "← 困る, 上, 悲しい");
                EditorGUILayout.LabelField("browOuterUpLeft/Right", "← 上, 驚き");
                EditorGUI.indentLevel--;
            }

            // 口
            _foldMouth = EditorGUILayout.Foldout(_foldMouth, "口 (Mouth)");
            if (_foldMouth)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("jawOpen", "← vrc.v_aa, あ (70%)");
                EditorGUILayout.LabelField("mouthFunnel", "← vrc.v_ou, う");
                EditorGUILayout.LabelField("mouthPucker", "← vrc.v_ou, う, ω (120%)");
                EditorGUILayout.LabelField("mouthSmileLeft/Right", "← にやり, ∧, にっこり");
                EditorGUILayout.LabelField("mouthFrownLeft/Right", "← への字, 悲しみ");
                EditorGUI.indentLevel--;
            }

            // 頬
            _foldCheek = EditorGUILayout.Foldout(_foldCheek, "頬 (Cheek)");
            if (_foldCheek)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("cheekPuff", "← ぷく, 膨らみ");
                EditorGUILayout.LabelField("cheekSquintLeft/Right", "← 笑い, にこり");
                EditorGUI.indentLevel--;
            }

            // 鼻
            _foldNose = EditorGUILayout.Foldout(_foldNose, "鼻 (Nose)");
            if (_foldNose)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("noseSneerLeft/Right", "← 怒り");
                EditorGUI.indentLevel--;
            }

            // 舌
            _foldTongue = EditorGUILayout.Foldout(_foldTongue, "舌 (Tongue)");
            if (_foldTongue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("tongueOut", "← べー, 舌");
                EditorGUI.indentLevel--;
            }
        }
    }
}
