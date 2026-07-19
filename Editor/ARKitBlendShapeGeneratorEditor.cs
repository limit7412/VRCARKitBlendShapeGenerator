using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using nadena.dev.ndmf.ui;
using static ARKitBlendShapeGenerator.Localization;

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
        private bool _showNdmfOffWarning;
        private bool _showPreviewCategoryCustom = true;
        private bool _showPreviewCategoryAuto = true;
        private bool _showPreviewCategoryOriginal = true;
        private Vector2 _scrollPosition;
        private Vector2 _previewScrollPosition;
        private int _cachedPreviewConfigRevision = -1;
        private int _cachedPreviewRendererInstanceId;
        private int _cachedPreviewMeshInstanceId;
        private PreviewShapeCategories _cachedPreviewCategories;

        // カテゴリごとの折りたたみ状態
        private bool _foldEye = true;
        private bool _foldEyeLook = true;
        private bool _foldBrow = true;
        private bool _foldMouth = false;
        private bool _foldCheek = false;
        private bool _foldNose = false;
        private bool _foldTongue = false;

        // 検索
        private string _searchFilter = "";

        private void OnEnable()
        {
            _component = (ARKitBlendShapeGeneratorComponent)target;
            InvalidatePreviewCategoryCache();
            RefreshBlendShapeList();
        }

        private void OnDisable()
        {
            int componentId = _component != null ? _component.GetInstanceID() : 0;
            ARKitBlendShapeGeneratorPreviewState.ReleaseIfActive(componentId);
            InvalidatePreviewCategoryCache();
        }

        private void RefreshBlendShapeList()
        {
            _availableBlendShapes = _component.GetAvailableBlendShapes();
            InvalidatePreviewCategoryCache();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 言語切替 + ヘッダー
            LanguageSwitcher.DrawImmediate();
            EditorGUILayout.LabelField("ARKit BlendShape Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(S("inspector.description"), MessageType.Info);

            EditorGUILayout.Space();

            // 基本設定
            EditorGUILayout.LabelField(S("inspector.section.basic"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetRenderer"), G("prop.target_renderer"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S("inspector.refresh_list")))
            {
                RefreshBlendShapeList();
            }
            EditorGUILayout.LabelField(S("inspector.detected_count", _availableBlendShapes.Count), GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("intensityMultiplier"), G("prop.intensity_multiplier"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableLeftRightSplit"), G("prop.enable_left_right_split"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("blendWidth"), G("prop.blend_width"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("overwriteExisting"), G("prop.overwrite_existing"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(S("inspector.section.procedural_mouth"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(S("inspector.procedural_mouth.description"), MessageType.Info);
            var enableProceduralProperty = serializedObject.FindProperty("enableProceduralMouthShapes");
            EditorGUILayout.PropertyField(enableProceduralProperty, G("prop.enable_procedural_mouth"));
            using (new EditorGUI.DisabledScope(!enableProceduralProperty.boolValue))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("proceduralMouthIntensity"), G("prop.procedural_mouth_intensity"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // カスタムマッピング
            _showCustomMappings = EditorGUILayout.Foldout(_showCustomMappings, S("inspector.section.custom_mappings"), true);
            if (_showCustomMappings)
            {
                EditorGUI.indentLevel++;
                DrawCustomMappingsUI();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // プレビュー
            _showPreview = EditorGUILayout.Foldout(_showPreview, S("inspector.section.preview"), true);
            if (_showPreview)
            {
                EditorGUI.indentLevel++;
                DrawPreviewUI();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // 自動マッピング情報
            _showAutoMappings = EditorGUILayout.Foldout(_showAutoMappings, S("inspector.section.auto_mappings"), true);
            if (_showAutoMappings)
            {
                EditorGUI.indentLevel++;
                DrawAutoMappingsInfo();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"), G("prop.debug_mode"));

            // デバッグ: 全SkinnedMeshRendererを表示
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S("debug.show_all_meshes"), EditorStyles.miniButton))
            {
                var allRenderers = _component.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Debug.Log($"=== 全SkinnedMeshRenderer ({allRenderers.Length}個) ===");
                foreach (var smr in allRenderers)
                {
                    int count = smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0;
                    Debug.Log($"  {smr.gameObject.name}: {count} BlendShapes");
                }
            }
            if (GUILayout.Button(S("debug.show_target_shapes"), EditorStyles.miniButton))
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
                    Debug.LogWarning("[ARKitGenerator] " + S("log.target_renderer_not_set"));
                }
            }
            EditorGUILayout.EndHorizontal();

            bool didApply = serializedObject.ApplyModifiedProperties();
            if (didApply)
            {
                ARKitBlendShapeGeneratorPreviewState.NotifyComponentConfigurationChanged();
            }
        }

        private void DrawNdmfPreviewToggle()
        {
            var isEnabled = ARKitBlendShapeGeneratorPreview.EnableNode.IsEnabled.Value;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("NDMF Preview", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            // プレビュー状態に応じてボタンの色を変更
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = isEnabled ? new Color(0.6f, 1.0f, 0.6f) : new Color(1.0f, 0.6f, 0.6f);

            string buttonText = isEnabled ? "ON" : "OFF";
            if (GUILayout.Button(buttonText, GUILayout.MinWidth(50), GUILayout.MaxWidth(70)))
            {
                ARKitBlendShapeGeneratorPreview.EnableNode.IsEnabled.Value = !isEnabled;
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = originalColor;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCustomMappingsUI()
        {
            EditorGUILayout.HelpBox(S("custom_mappings.description"), MessageType.Info);

            var duplicateArkitNames = CustomMappingValidation.GetDuplicateArkitNames(_component.customMappings);
            if (duplicateArkitNames.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    CustomMappingValidation.BuildDuplicateMessage(duplicateArkitNames) +
                    "\n" + S("custom_mappings.duplicate_blocked"),
                    MessageType.Error);
            }

            // VRChat標準表情のみを使用したプリセット
            EditorGUILayout.LabelField(S("presets.label"), EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S("presets.vrchat_standard")))
            {
                if (EditorUtility.DisplayDialog(
                    S("dialog.title"),
                    S("dialog.vrchat_preset.message"),
                    S("dialog.vrchat_preset.apply"),
                    S("common.cancel")))
                {
                    ApplyVRChatStandardPreset();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // カテゴリ別クイック追加ボタン
            EditorGUILayout.LabelField(S("category_add.label"), EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S("category.eye_look"), EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.EyeLook);
            }
            if (GUILayout.Button(S("category.eye"), EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Eye);
            }
            if (GUILayout.Button(S("category.brow"), EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Brow);
            }
            if (GUILayout.Button(S("category.mouth"), EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Mouth);
            }
            if (GUILayout.Button(S("category.cheek"), EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Cheek);
            }
            if (GUILayout.Button(S("category.nose"), EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Nose);
            }
            if (GUILayout.Button(S("category.tongue"), EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Tongue);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 検索フィルタ
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(S("inspector.search"), GUILayout.Width(60));
            _searchFilter = EditorGUILayout.TextField(_searchFilter);
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S("custom_mappings.add_new")))
            {
                AddNewMapping();
            }
            if (GUILayout.Button(S("custom_mappings.delete_all"), GUILayout.Width(80)))
            {
                if (EditorUtility.DisplayDialog(S("dialog.title"), S("dialog.delete_all.message"), S("common.yes"), S("common.no")))
                {
                    _component.customMappings.Clear();
                    MarkComponentChanged();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // マッピング数表示
            int enabledCount = _component.customMappings.Count(m => m.enabled);
            EditorGUILayout.LabelField(S("custom_mappings.count", enabledCount, _component.customMappings.Count));

            // マッピングリスト表示
            string normalizedFilter = string.IsNullOrWhiteSpace(_searchFilter)
                ? string.Empty
                : _searchFilter.Trim().ToLowerInvariant();

            int visibleCount = 0;
            float estimatedContentHeight = 0f;
            for (int i = 0; i < _component.customMappings.Count; i++)
            {
                var mapping = _component.customMappings[i];
                if (!IsMappingVisible(mapping, normalizedFilter))
                {
                    continue;
                }

                visibleCount++;
                estimatedContentHeight += EstimateCustomMappingItemHeight(mapping);
            }

            if (visibleCount == 0)
            {
                EditorGUILayout.LabelField(S("custom_mappings.none_matching"), EditorStyles.miniLabel);
                return;
            }

            bool useScrollView = estimatedContentHeight > 400f;
            if (useScrollView)
            {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(400f));
            }
            else
            {
                _scrollPosition = Vector2.zero;
            }

            for (int i = 0; i < _component.customMappings.Count; i++)
            {
                var mapping = _component.customMappings[i];
                if (!IsMappingVisible(mapping, normalizedFilter))
                {
                    continue;
                }

                DrawMappingItem(i);
            }

            if (useScrollView)
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private bool IsMappingVisible(CustomBlendShapeMapping mapping, string normalizedFilter)
        {
            if (mapping == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(normalizedFilter))
            {
                return true;
            }

            bool matchesArkit = !string.IsNullOrEmpty(mapping.arkitName) &&
                                mapping.arkitName.ToLowerInvariant().Contains(normalizedFilter);
            bool matchesSource = mapping.sources != null && mapping.sources.Any(source =>
                !string.IsNullOrEmpty(source.blendShapeName) &&
                source.blendShapeName.ToLowerInvariant().Contains(normalizedFilter));

            return matchesArkit || matchesSource;
        }

        private float EstimateCustomMappingItemHeight(CustomBlendShapeMapping mapping)
        {
            int sourceCount = mapping != null && mapping.sources != null ? mapping.sources.Count : 0;
            // helpBoxのヘッダー1行 + source行 + 余白の概算
            return 46f + (sourceCount * 22f);
        }

        private List<string> GetSelectableArkitNames(int mappingIndex)
        {
            var allArkitNames = ARKitBlendShapeNames.GetAll();
            var customMappings = _component.customMappings ?? new List<CustomBlendShapeMapping>();
            var usedByOthers = new HashSet<string>(
                customMappings
                    .Where((mapping, index) =>
                        index != mappingIndex &&
                        mapping != null &&
                        !string.IsNullOrWhiteSpace(mapping.arkitName))
                    .Select(mapping => mapping.arkitName.Trim()));

            var options = allArkitNames
                .Where(name => !usedByOthers.Contains(name))
                .ToList();

            if (mappingIndex < 0 || mappingIndex >= customMappings.Count)
            {
                return options;
            }

            var currentName = customMappings[mappingIndex]?.arkitName;
            if (!string.IsNullOrWhiteSpace(currentName))
            {
                var trimmedCurrentName = currentName.Trim();
                if (!options.Contains(trimmedCurrentName))
                {
                    options.Insert(0, trimmedCurrentName);
                }
            }

            return options;
        }

        private string GetFirstUnusedArkitName()
        {
            var customMappings = _component.customMappings ?? new List<CustomBlendShapeMapping>();
            var usedNames = new HashSet<string>(
                customMappings
                    .Where(mapping => mapping != null && !string.IsNullOrWhiteSpace(mapping.arkitName))
                    .Select(mapping => mapping.arkitName.Trim()));

            foreach (var arkitName in ARKitBlendShapeNames.GetAll())
            {
                if (!usedNames.Contains(arkitName))
                {
                    return arkitName;
                }
            }

            return null;
        }

        private void AddCategoryMappings(string[] arkitNames)
        {
            Undo.RecordObject(_component, "Add Category Mappings");

            foreach (var name in arkitNames)
            {
                if (_component.customMappings.Any(m => m != null && m.arkitName == name))
                    continue;

                _component.customMappings.Add(new CustomBlendShapeMapping
                {
                    arkitName = name,
                    enabled = true,
                    sources = new List<BlendShapeSource>()
                });
            }

            MarkComponentChanged();
        }

        private void DrawMappingItem(int index)
        {
            var mapping = _component.customMappings[index];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ヘッダー行
            EditorGUILayout.BeginHorizontal();

            bool newEnabled = EditorGUILayout.Toggle(mapping.enabled, GUILayout.Width(20));
            if (newEnabled != mapping.enabled)
            {
                mapping.enabled = newEnabled;
                MarkComponentChanged();
            }

            // ARKit名のドロップダウン（同一ARKit名の重複は選択不可）
            var selectableArkitNames = GetSelectableArkitNames(index);
            if (selectableArkitNames.Count == 0)
            {
                EditorGUILayout.LabelField(S("custom_mappings.no_available_names"));
            }
            else
            {
                string currentArkitName = string.IsNullOrWhiteSpace(mapping.arkitName)
                    ? null
                    : mapping.arkitName.Trim();
                int currentIndex = selectableArkitNames.IndexOf(currentArkitName);
                if (currentIndex < 0) currentIndex = 0;

                int newIndex = EditorGUILayout.Popup(currentIndex, selectableArkitNames.ToArray());
                string selectedArkitName = selectableArkitNames[newIndex];
                if (!string.Equals(mapping.arkitName, selectedArkitName))
                {
                    mapping.arkitName = selectedArkitName;
                    MarkComponentChanged();
                }
            }

            if (GUILayout.Button("+", GUILayout.Width(25)))
            {
                mapping.sources.Add(new BlendShapeSource());
                MarkComponentChanged();
            }

            if (GUILayout.Button("×", GUILayout.Width(25)))
            {
                _component.customMappings.RemoveAt(index);
                MarkComponentChanged();
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
                string notFoundSuffix = S("source.not_found_suffix");
                var options = new List<string> { S("source.placeholder") };
                options.AddRange(_availableBlendShapes);

                int currentIdx = _availableBlendShapes.IndexOf(source.blendShapeName) + 1;

                // リストにない名前が設定されている場合は末尾に追加して表示
                if (currentIdx == 0 && !string.IsNullOrEmpty(source.blendShapeName))
                {
                    options.Add(source.blendShapeName + notFoundSuffix);
                    currentIdx = options.Count - 1;
                }

                int newIdx = EditorGUILayout.Popup(currentIdx, options.ToArray());

                if (newIdx > 0 && newIdx < options.Count)
                {
                    // 「(未検出)」付きの項目が選択された場合は元の名前を維持
                    string selectedName = options[newIdx];
                    if (selectedName.EndsWith(notFoundSuffix))
                    {
                        selectedName = selectedName.Replace(notFoundSuffix, "");
                    }

                    if (source.blendShapeName != selectedName)
                    {
                        source.blendShapeName = selectedName;
                        MarkComponentChanged();
                    }
                }
            }
            else
            {
                string newName = EditorGUILayout.TextField(source.blendShapeName);
                if (newName != source.blendShapeName)
                {
                    source.blendShapeName = newName;
                    MarkComponentChanged();
                }
            }

            // 重み
            EditorGUILayout.LabelField("×", GUILayout.Width(15));
            float newWeight = EditorGUILayout.Slider(source.weight, -2f, 2f, GUILayout.Width(100));
            if (Mathf.Abs(newWeight - source.weight) > 0.0001f)
            {
                source.weight = newWeight;
                MarkComponentChanged();
            }

            // 左右適用範囲
            var sideLabels = new[] { S("enum.side.both"), S("enum.side.left_only"), S("enum.side.right_only") };
            BlendShapeSide newSide = (BlendShapeSide)EditorGUILayout.Popup((int)source.side, sideLabels, GUILayout.Width(70));
            if (newSide != source.side)
            {
                source.side = newSide;
                MarkComponentChanged();
            }

            // 削除ボタン
            if (GUILayout.Button("－", GUILayout.Width(25)))
            {
                mapping.sources.RemoveAt(sourceIndex);
                MarkComponentChanged();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void AddNewMapping()
        {
            string firstUnusedArkitName = GetFirstUnusedArkitName();
            if (string.IsNullOrEmpty(firstUnusedArkitName))
            {
                EditorUtility.DisplayDialog(
                    S("dialog.title"),
                    S("dialog.no_available_name.message"),
                    S("common.ok"));
                return;
            }

            var newMapping = new CustomBlendShapeMapping
            {
                arkitName = firstUnusedArkitName,
                enabled = true,
                sources = new List<BlendShapeSource>()
            };
            _component.customMappings.Add(newMapping);
            MarkComponentChanged();
        }

        private void AddEyeLookMappings()
        {
            AddCategoryMappings(ARKitBlendShapeNames.EyeLook);
        }

        /// <summary>
        /// VRChat標準の表情のみを使用したカスタムマッピングを設定
        /// MMD用シェイプキーが存在しないアバター向け
        /// </summary>
        private void ApplyVRChatStandardPreset()
        {
            // BlendShapeリストを先に更新
            RefreshBlendShapeList();

            // SerializedObjectを使用して変更を適用
            serializedObject.Update();

            var customMappingsProperty = serializedObject.FindProperty("customMappings");
            customMappingsProperty.ClearArray();

            // === 目 (Eye) ===
            // vrc.blink または vrc_blink を自動検出
            string blinkName = FindVrcBlendShape("vrc.blink", "vrc_blink");
            AddPresetMappingSerialized(customMappingsProperty, "eyeBlinkLeft", blinkName, 1.0f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "eyeBlinkRight", blinkName, 1.0f, BlendShapeSide.RightOnly);

            // === 口 - 母音系 (Mouth Vowels) ===
            string vAa = FindVrcBlendShape("vrc.v_aa", "vrc_v_aa");
            string vOu = FindVrcBlendShape("vrc.v_ou", "vrc_v_ou");
            string vIh = FindVrcBlendShape("vrc.v_ih", "vrc_v_ih");
            string vNn = FindVrcBlendShape("vrc.v_nn", "vrc_v_nn");
            string vCh = FindVrcBlendShape("vrc.v_ch", "vrc_v_ch");
            string vOh = FindVrcBlendShape("vrc.v_oh", "vrc_v_oh");

            AddPresetMappingSerialized(customMappingsProperty, "jawOpen", vAa, 0.7f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthFunnel", vOu, 1.0f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthPucker", vOu, 1.2f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthUpperUpLeft", vIh, 1.0f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthUpperUpRight", vIh, 1.0f, BlendShapeSide.RightOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthLowerDownLeft", vAa, 0.6f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthLowerDownRight", vAa, 0.6f, BlendShapeSide.RightOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthClose", vNn, 1.0f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthShrugUpper", vCh, 1.0f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthShrugLower", vOh, 0.5f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthStretchLeft", vIh, 1.0f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthStretchRight", vIh, 1.0f, BlendShapeSide.RightOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthSmileLeft", vIh, 0.7f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthSmileRight", vIh, 0.7f, BlendShapeSide.RightOnly);

            // === 視線系 (Eye Look) - 無効で追加（通常は手動設定が必要） ===
            foreach (var eyeLookName in ARKitBlendShapeNames.EyeLook)
            {
                AddPresetMappingSerialized(customMappingsProperty, eyeLookName, null, 0f, BlendShapeSide.Both, false);
            }

            serializedObject.ApplyModifiedProperties();
            ARKitBlendShapeGeneratorPreviewState.NotifyComponentConfigurationChanged();

            if (_component.debugMode)
            {
                Debug.Log("[ARKitGenerator] " + S("log.preset_applied"));
            }
        }

        private void MarkComponentChanged()
        {
            EditorUtility.SetDirty(_component);
            ARKitBlendShapeGeneratorPreviewState.NotifyComponentConfigurationChanged();
        }

        /// <summary>
        /// 複数の候補名からメッシュに存在するBlendShape名を検索
        /// </summary>
        private string FindVrcBlendShape(params string[] candidates)
        {
            foreach (var name in candidates)
            {
                if (_availableBlendShapes.Contains(name))
                {
                    return name;
                }
            }
            // 見つからない場合は最初の候補を返す
            return candidates.Length > 0 ? candidates[0] : null;
        }

        /// <summary>
        /// SerializedPropertyを使用してプリセットマッピングを追加
        /// </summary>
        private void AddPresetMappingSerialized(SerializedProperty customMappingsProperty, string arkitName, string sourceName, float weight, BlendShapeSide side, bool enabled = true)
        {
            int index = customMappingsProperty.arraySize;
            customMappingsProperty.InsertArrayElementAtIndex(index);
            var mappingProperty = customMappingsProperty.GetArrayElementAtIndex(index);

            var arkitNameProp = mappingProperty.FindPropertyRelative("arkitName");
            var enabledProp = mappingProperty.FindPropertyRelative("enabled");
            var sourcesProperty = mappingProperty.FindPropertyRelative("sources");

            arkitNameProp.stringValue = arkitName;
            enabledProp.boolValue = enabled;
            sourcesProperty.ClearArray();

            if (!string.IsNullOrEmpty(sourceName))
            {
                sourcesProperty.InsertArrayElementAtIndex(0);
                var sourceProperty = sourcesProperty.GetArrayElementAtIndex(0);

                var blendShapeNameProp = sourceProperty.FindPropertyRelative("blendShapeName");
                var weightProp = sourceProperty.FindPropertyRelative("weight");
                var sideProp = sourceProperty.FindPropertyRelative("side");

                blendShapeNameProp.stringValue = sourceName;
                weightProp.floatValue = weight;
                sideProp.enumValueIndex = (int)side;
            }
        }

        private void DrawPreviewUI()
        {
            // NDMFプレビュー ON/OFF ボタン
            DrawNdmfPreviewToggle();
            EditorGUILayout.Space(5);
            var targetRenderer = GetPreviewTargetRenderer();
            var isNdmfPreviewEnabled = ARKitBlendShapeGeneratorPreview.EnableNode.IsEnabled.Value;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(S("preview.realtime"), EditorStyles.boldLabel);

            int componentId = _component.GetInstanceID();
            ARKitBlendShapeGeneratorPreviewState.BeginEdit(componentId);
            var previewState = ARKitBlendShapeGeneratorPreviewState.Current;
            bool isActive = previewState.InteractiveEnabled && previewState.ActiveComponentInstanceId == componentId;

            if (!isActive)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = isActive;
            if (GUILayout.Button(S("preview.reset")))
            {
                ARKitBlendShapeGeneratorPreviewState.SetAllWeights(componentId, new string[0], 0f);
                previewState = ARKitBlendShapeGeneratorPreviewState.Current;
                SceneView.RepaintAll();
            }
            if (GUILayout.Button(S("preview.categories"), GUILayout.Width(90)))
            {
                ShowPreviewCategoryDropdown();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            var previewCategories = GetPreviewCategoriesWithCache(targetRenderer);
            if (previewCategories.TotalCount == 0)
            {
                return;
            }

            if (_showNdmfOffWarning && !isNdmfPreviewEnabled)
            {
                EditorGUILayout.HelpBox(S("preview.ndmf_off_warning"), MessageType.Warning);
            }

            _previewScrollPosition = EditorGUILayout.BeginScrollView(_previewScrollPosition, GUILayout.MaxHeight(260));
            if (_showPreviewCategoryCustom)
            {
                DrawPreviewCategory(
                    S("preview.category.custom"),
                    previewCategories.Custom,
                    componentId,
                    ref previewState,
                    isNdmfPreviewEnabled);
            }
            if (_showPreviewCategoryAuto)
            {
                DrawPreviewCategory(
                    S("preview.category.auto"),
                    previewCategories.AutoGenerated,
                    componentId,
                    ref previewState,
                    isNdmfPreviewEnabled);
            }
            if (_showPreviewCategoryOriginal)
            {
                DrawPreviewCategory(
                    S("preview.category.original"),
                    previewCategories.Original,
                    componentId,
                    ref previewState,
                    isNdmfPreviewEnabled);
            }
            EditorGUILayout.EndScrollView();
        }

        private void ShowPreviewCategoryDropdown()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent(S("preview.category.custom")), _showPreviewCategoryCustom, () =>
            {
                _showPreviewCategoryCustom = !_showPreviewCategoryCustom;
                Repaint();
            });
            menu.AddItem(new GUIContent(S("preview.category.auto")), _showPreviewCategoryAuto, () =>
            {
                _showPreviewCategoryAuto = !_showPreviewCategoryAuto;
                Repaint();
            });
            menu.AddItem(new GUIContent(S("preview.category.original")), _showPreviewCategoryOriginal, () =>
            {
                _showPreviewCategoryOriginal = !_showPreviewCategoryOriginal;
                Repaint();
            });
            menu.ShowAsContext();
        }

        private SkinnedMeshRenderer GetPreviewTargetRenderer()
        {
            if (_component.targetRenderer != null)
            {
                return _component.targetRenderer;
            }

            return _component.GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        private sealed class PreviewShapeCategories
        {
            public readonly List<string> Custom = new List<string>();
            public readonly List<string> AutoGenerated = new List<string>();
            public readonly List<string> Original = new List<string>();

            public int TotalCount => Custom.Count + AutoGenerated.Count + Original.Count;
        }

        private void InvalidatePreviewCategoryCache()
        {
            _cachedPreviewCategories = null;
            _cachedPreviewConfigRevision = -1;
            _cachedPreviewRendererInstanceId = 0;
            _cachedPreviewMeshInstanceId = 0;
        }

        private PreviewShapeCategories GetPreviewCategoriesWithCache(SkinnedMeshRenderer targetRenderer)
        {
            int configRevision = ARKitBlendShapeGeneratorPreviewState.ComponentConfigRevision.Value;
            int rendererId = targetRenderer != null ? targetRenderer.GetInstanceID() : 0;
            int meshId = targetRenderer != null && targetRenderer.sharedMesh != null
                ? targetRenderer.sharedMesh.GetInstanceID()
                : 0;

            bool isCacheValid = _cachedPreviewCategories != null &&
                                _cachedPreviewConfigRevision == configRevision &&
                                _cachedPreviewRendererInstanceId == rendererId &&
                                _cachedPreviewMeshInstanceId == meshId;

            if (!isCacheValid)
            {
                _cachedPreviewCategories = BuildRealtimePreviewCategories(targetRenderer);
                _cachedPreviewConfigRevision = configRevision;
                _cachedPreviewRendererInstanceId = rendererId;
                _cachedPreviewMeshInstanceId = meshId;
            }

            return _cachedPreviewCategories;
        }

        private void DrawPreviewCategory(
            string title,
            List<string> shapeNames,
            int componentId,
            ref ARKitBlendShapeGeneratorPreviewState.Snapshot previewState,
            bool isNdmfPreviewEnabled)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);

            if (shapeNames == null || shapeNames.Count == 0)
            {
                EditorGUILayout.LabelField(S("preview.category.empty"), EditorStyles.miniLabel);
                return;
            }

            foreach (var shapeName in shapeNames)
            {
                float current = previewState.GetWeight(shapeName);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(shapeName, GUILayout.Width(180));
                float next = EditorGUILayout.Slider(current, 0f, 1f);
                EditorGUILayout.EndHorizontal();

                if (Mathf.Abs(next - current) <= 0.0001f)
                {
                    continue;
                }

                ARKitBlendShapeGeneratorPreviewState.SetWeight(componentId, shapeName, next);
                previewState = ARKitBlendShapeGeneratorPreviewState.Current;
                if (!isNdmfPreviewEnabled)
                {
                    _showNdmfOffWarning = true;
                }
                else
                {
                    _showNdmfOffWarning = false;
                }
                SceneView.RepaintAll();
            }
        }

        private PreviewShapeCategories BuildRealtimePreviewCategories(SkinnedMeshRenderer targetRenderer)
        {
            var categories = new PreviewShapeCategories();
            var customSet = new HashSet<string>();
            var autoSet = new HashSet<string>();
            var customMappedNames = new HashSet<string>();
            var customMappings = _component.customMappings ?? new List<CustomBlendShapeMapping>();

            foreach (var mapping in customMappings)
            {
                if (mapping == null || !mapping.enabled || string.IsNullOrEmpty(mapping.arkitName))
                {
                    continue;
                }

                if (mapping.sources == null || mapping.sources.Count == 0)
                {
                    continue;
                }

                customSet.Add(mapping.arkitName);
                customMappedNames.Add(mapping.arkitName);
            }

            if (targetRenderer == null || targetRenderer.sharedMesh == null)
            {
                categories.Custom.AddRange(customSet.OrderBy(name => name));
                return categories;
            }

            var sourceShapeNames = new HashSet<string>();
            for (int i = 0; i < targetRenderer.sharedMesh.blendShapeCount; i++)
            {
                sourceShapeNames.Add(targetRenderer.sharedMesh.GetBlendShapeName(i));
            }

            var processedAutoNames = new HashSet<string>();
            foreach (var mapping in BlendShapeProcessor.GetMappingTable())
            {
                if (mapping == null || string.IsNullOrEmpty(mapping.arkitName) || mapping.sources == null)
                {
                    continue;
                }

                if (customMappedNames.Contains(mapping.arkitName))
                {
                    continue;
                }

                if (processedAutoNames.Contains(mapping.arkitName))
                {
                    continue;
                }

                bool hasAnySource = false;
                foreach (var source in mapping.sources)
                {
                    if (source == null || source.names == null)
                    {
                        continue;
                    }

                    foreach (var sourceName in source.names)
                    {
                        if (!string.IsNullOrEmpty(sourceName) && sourceShapeNames.Contains(sourceName))
                        {
                            hasAnySource = true;
                            break;
                        }
                    }

                    if (hasAnySource)
                    {
                        break;
                    }
                }

                if (!hasAnySource)
                {
                    continue;
                }

                autoSet.Add(mapping.arkitName);
                processedAutoNames.Add(mapping.arkitName);
            }

            if (_component.enableProceduralMouthShapes &&
                ProceduralMouthShapeGenerator.HasMouthSource(sourceShapeNames))
            {
                foreach (var arkitName in ProceduralMouthShapeGenerator.TargetShapeNames)
                {
                    if (customMappedNames.Contains(arkitName) || autoSet.Contains(arkitName))
                    {
                        continue;
                    }

                    autoSet.Add(arkitName);
                }
            }

            var originalSet = new HashSet<string>();
            foreach (var shapeName in sourceShapeNames)
            {
                if (customSet.Contains(shapeName) || autoSet.Contains(shapeName))
                {
                    continue;
                }

                originalSet.Add(shapeName);
            }

            categories.Custom.AddRange(customSet.OrderBy(name => name));
            categories.AutoGenerated.AddRange(autoSet.OrderBy(name => name));
            categories.Original.AddRange(originalSet.OrderBy(name => name));

            return categories;
        }

        private void DrawAutoMappingsInfo()
        {
            EditorGUILayout.HelpBox(S("auto_mappings.description"), MessageType.Info);

            // 目
            _foldEye = EditorGUILayout.Foldout(_foldEye, S("auto_mappings.fold.eye"));
            if (_foldEye)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("eyeBlinkLeft/Right", S("auto_mappings.row.eye_blink"));
                EditorGUILayout.LabelField("eyeSquintLeft/Right", S("auto_mappings.row.eye_squint"));
                EditorGUILayout.LabelField("eyeWideLeft/Right", S("auto_mappings.row.eye_wide"));
                EditorGUI.indentLevel--;
            }

            // 視線
            _foldEyeLook = EditorGUILayout.Foldout(_foldEyeLook, S("auto_mappings.fold.eye_look"));
            if (_foldEyeLook)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("eyeLookUpLeft/Right", S("auto_mappings.row.manual"));
                EditorGUILayout.LabelField("eyeLookDownLeft/Right", S("auto_mappings.row.manual"));
                EditorGUILayout.LabelField("eyeLookInLeft/Right", S("auto_mappings.row.eye_look_in"));
                EditorGUILayout.LabelField("eyeLookOutLeft/Right", S("auto_mappings.row.manual"));
                EditorGUI.indentLevel--;
            }

            // 眉毛
            _foldBrow = EditorGUILayout.Foldout(_foldBrow, S("auto_mappings.fold.brow"));
            if (_foldBrow)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("browDownLeft/Right", S("auto_mappings.row.brow_down"));
                EditorGUILayout.LabelField("browInnerUp", S("auto_mappings.row.brow_inner_up"));
                EditorGUILayout.LabelField("browOuterUpLeft/Right", S("auto_mappings.row.brow_outer_up"));
                EditorGUI.indentLevel--;
            }

            // 口
            _foldMouth = EditorGUILayout.Foldout(_foldMouth, S("auto_mappings.fold.mouth"));
            if (_foldMouth)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("jawOpen", S("auto_mappings.row.jaw_open"));
                EditorGUILayout.LabelField("mouthFunnel", S("auto_mappings.row.mouth_funnel"));
                EditorGUILayout.LabelField("mouthPucker", S("auto_mappings.row.mouth_pucker"));
                EditorGUILayout.LabelField("mouthSmileLeft/Right", S("auto_mappings.row.mouth_smile"));
                EditorGUILayout.LabelField("mouthFrownLeft/Right", S("auto_mappings.row.mouth_frown"));
                EditorGUILayout.LabelField("mouthLeft/Right", S("auto_mappings.row.procedural"));
                EditorGUILayout.LabelField("jawLeft/Right/Forward", S("auto_mappings.row.procedural"));
                EditorGUI.indentLevel--;
            }

            // 頬
            _foldCheek = EditorGUILayout.Foldout(_foldCheek, S("auto_mappings.fold.cheek"));
            if (_foldCheek)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("cheekPuff", S("auto_mappings.row.cheek_puff"));
                EditorGUILayout.LabelField("cheekSquintLeft/Right", S("auto_mappings.row.cheek_squint"));
                EditorGUI.indentLevel--;
            }

            // 鼻
            _foldNose = EditorGUILayout.Foldout(_foldNose, S("auto_mappings.fold.nose"));
            if (_foldNose)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("noseSneerLeft/Right", S("auto_mappings.row.nose_sneer"));
                EditorGUI.indentLevel--;
            }

            // 舌
            _foldTongue = EditorGUILayout.Foldout(_foldTongue, S("auto_mappings.fold.tongue"));
            if (_foldTongue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("tongueOut", S("auto_mappings.row.tongue_out"));
                EditorGUI.indentLevel--;
            }
        }
    }
}
