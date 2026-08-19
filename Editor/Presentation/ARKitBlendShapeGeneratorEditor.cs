using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using nadena.dev.ndmf.ui;
using ARKitBlendShapeGenerator.Domain;
using ARKitBlendShapeGenerator.Handler;
using ARKitBlendShapeGenerator.Infra;
using ARKitBlendShapeGenerator.UseCase;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Presentation
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
        private Vector2 _mouthCancellationTargetScrollPosition;
        private int _cachedPreviewConfigRevision = -1;
        private int _cachedPreviewRendererInstanceId;
        private int _cachedPreviewMeshInstanceId;
        private PreviewShapeCategories _cachedPreviewCategories;

        // 自動マッピング一覧はARKitMappingTableから組み立てる。テーブルは実行中変化しないため使い回す
        private static IReadOnlyList<AutoMappingCategory> _autoMappingCategories;

        // 自動マッピング一覧のカテゴリごとの折りたたみ状態（キーはAutoMappingCategory.Key）
        private readonly Dictionary<string, bool> _autoMappingFolds = new Dictionary<string, bool>
        {
            { "eye", true },
            { "eye_look", true },
            { "brow", true },
        };

        // 検索
        private string _searchFilter = "";

        // マッピング行チェックボックスの幅
        private const float MappingToggleWidth = 20f;

        // インデントレベル
        private const float IndentWidth = 15f;

        private string _pendingSelectionPropertyPath;
        private string _pendingSelectionValue;

        private void OnEnable()
        {
            _component = (ARKitBlendShapeGeneratorComponent)target;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            InvalidatePreviewCategoryCache();
            RefreshBlendShapeList();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            int componentId = _component != null ? _component.GetInstanceID() : 0;
            ARKitBlendShapeGeneratorPreviewState.ReleaseIfActive(componentId);
            InvalidatePreviewCategoryCache();
        }

        private void OnUndoRedoPerformed()
        {
            // Undo/Redoはインスペクタの描画を経由せずコンポーネントへ反映されるため、
            // ApplyModifiedPropertiesの変更検出では設定変更を拾えない
            InvalidatePreviewCategoryCache();
            ARKitBlendShapeGeneratorPreviewState.NotifyComponentConfigurationChanged();
            Repaint();
        }

        private void RefreshBlendShapeList()
        {
            _availableBlendShapes = _component.GetAvailableBlendShapes();
            InvalidatePreviewCategoryCache();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ApplyPendingDropdownSelection();

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
            EditorGUILayout.LabelField(S("inspector.section.mouth_cancellation"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(S("inspector.mouth_cancellation.description"), MessageType.Info);
            var enableCancellationProperty = serializedObject.FindProperty("enableMouthCancellation");
            EditorGUILayout.PropertyField(enableCancellationProperty, G("prop.enable_mouth_cancellation"));
            if (enableCancellationProperty.boolValue)
            {
                EditorGUI.indentLevel++;
                DrawMouthCancellationUI();
                EditorGUI.indentLevel--;
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

            var customMappingsProperty = serializedObject.FindProperty("customMappings");

            var duplicateArkitNames = CustomMappingValidation.GetDuplicateArkitNames(
                EnumerateArkitNames(customMappingsProperty, enabledOnly: true));
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
                AddCategoryMappings(customMappingsProperty, ARKitBlendShapeNames.EyeLook);
            }
            if (GUILayout.Button(S("category.eye"), EditorStyles.miniButton))
            {
                AddCategoryMappings(customMappingsProperty, ARKitBlendShapeNames.Eye);
            }
            if (GUILayout.Button(S("category.brow"), EditorStyles.miniButton))
            {
                AddCategoryMappings(customMappingsProperty, ARKitBlendShapeNames.Brow);
            }
            if (GUILayout.Button(S("category.mouth"), EditorStyles.miniButton))
            {
                AddCategoryMappings(customMappingsProperty, ARKitBlendShapeNames.Mouth);
            }
            if (GUILayout.Button(S("category.cheek"), EditorStyles.miniButton))
            {
                AddCategoryMappings(customMappingsProperty, ARKitBlendShapeNames.Cheek);
            }
            if (GUILayout.Button(S("category.nose"), EditorStyles.miniButton))
            {
                AddCategoryMappings(customMappingsProperty, ARKitBlendShapeNames.Nose);
            }
            if (GUILayout.Button(S("category.tongue"), EditorStyles.miniButton))
            {
                AddCategoryMappings(customMappingsProperty, ARKitBlendShapeNames.Tongue);
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
                AddNewMapping(customMappingsProperty);
            }
            if (GUILayout.Button(S("custom_mappings.delete_all"), GUILayout.Width(80)))
            {
                if (EditorUtility.DisplayDialog(S("dialog.title"), S("dialog.delete_all.message"), S("common.yes"), S("common.no")))
                {
                    customMappingsProperty.ClearArray();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // マッピング数表示
            int mappingCount = customMappingsProperty.arraySize;
            int enabledCount = 0;
            for (int i = 0; i < mappingCount; i++)
            {
                if (customMappingsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("enabled").boolValue)
                {
                    enabledCount++;
                }
            }
            EditorGUILayout.LabelField(S("custom_mappings.count", enabledCount, mappingCount));

            // マッピングリスト表示
            string normalizedFilter = string.IsNullOrWhiteSpace(_searchFilter)
                ? string.Empty
                : _searchFilter.Trim().ToLowerInvariant();

            int visibleCount = 0;
            float estimatedContentHeight = 0f;
            for (int i = 0; i < mappingCount; i++)
            {
                var mappingProperty = customMappingsProperty.GetArrayElementAtIndex(i);
                if (!IsMappingVisible(mappingProperty, normalizedFilter))
                {
                    continue;
                }

                visibleCount++;
                estimatedContentHeight += EstimateCustomMappingItemHeight(mappingProperty);
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

            for (int i = 0; i < customMappingsProperty.arraySize; i++)
            {
                var mappingProperty = customMappingsProperty.GetArrayElementAtIndex(i);
                if (!IsMappingVisible(mappingProperty, normalizedFilter))
                {
                    continue;
                }

                if (!DrawMappingItem(customMappingsProperty, i))
                {
                    // 要素を削除したため、以降のインデックスがずれる。この描画パスは打ち切る
                    break;
                }
            }

            if (useScrollView)
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private bool IsMappingVisible(SerializedProperty mappingProperty, string normalizedFilter)
        {
            if (mappingProperty == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(normalizedFilter))
            {
                return true;
            }

            var arkitName = mappingProperty.FindPropertyRelative("arkitName").stringValue;
            if (!string.IsNullOrEmpty(arkitName) && arkitName.ToLowerInvariant().Contains(normalizedFilter))
            {
                return true;
            }

            var sourcesProperty = mappingProperty.FindPropertyRelative("sources");
            for (int i = 0; i < sourcesProperty.arraySize; i++)
            {
                var sourceName = sourcesProperty.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("blendShapeName").stringValue;
                if (!string.IsNullOrEmpty(sourceName) && sourceName.ToLowerInvariant().Contains(normalizedFilter))
                {
                    return true;
                }
            }

            return false;
        }

        private float EstimateCustomMappingItemHeight(SerializedProperty mappingProperty)
        {
            int sourceCount = mappingProperty.FindPropertyRelative("sources").arraySize;
            // helpBoxのヘッダー1行 + source行 + 余白の概算
            return 46f + (sourceCount * 22f);
        }

        /// <summary>
        /// カスタムマッピングに設定済みのARKit名を列挙する（空白のみの要素は除外）。
        /// 名前は加工しない。生成側と同じ規則で扱わないと、重複判定や使用済み判定が実挙動とずれる
        /// </summary>
        /// <param name="enabledOnly">
        /// 有効なマッピングだけを対象にするか。
        /// 重複判定は生成側と同じく無効なマッピングを読み飛ばすためtrueを渡す。
        /// 一方、既存の行と同名の行を作らないための使用済み判定は、無効な行も一覧に見えている以上は対象に含める
        /// </param>
        private static IEnumerable<string> EnumerateArkitNames(
            SerializedProperty customMappingsProperty,
            bool enabledOnly = false)
        {
            for (int i = 0; i < customMappingsProperty.arraySize; i++)
            {
                var mappingProperty = customMappingsProperty.GetArrayElementAtIndex(i);
                if (enabledOnly && !mappingProperty.FindPropertyRelative("enabled").boolValue)
                {
                    continue;
                }

                var arkitName = mappingProperty.FindPropertyRelative("arkitName").stringValue;
                if (!string.IsNullOrWhiteSpace(arkitName))
                {
                    yield return arkitName;
                }
            }
        }

        /// <summary>
        /// 指定のマッピングでは選べないARKit名を集める。
        /// 選ぶと重複エラーになる名前だけを対象にするため、有効な他のマッピングが使っている名前を返す。
        /// 編集中のマッピング自体が無効なら、どの名前を選んでも重複にならないので何も制限しない
        /// （同じARKit名の差し替え候補を、無効のまま用意できる）
        /// </summary>
        private static HashSet<string> GetArkitNamesBlockedForMapping(
            SerializedProperty customMappingsProperty,
            int mappingIndex)
        {
            var blocked = new HashSet<string>();

            var targetProperty = customMappingsProperty.GetArrayElementAtIndex(mappingIndex);
            if (!targetProperty.FindPropertyRelative("enabled").boolValue)
            {
                return blocked;
            }

            for (int i = 0; i < customMappingsProperty.arraySize; i++)
            {
                if (i == mappingIndex)
                {
                    continue;
                }

                var mappingProperty = customMappingsProperty.GetArrayElementAtIndex(i);
                if (!mappingProperty.FindPropertyRelative("enabled").boolValue)
                {
                    continue;
                }

                var arkitName = mappingProperty.FindPropertyRelative("arkitName").stringValue;
                if (!string.IsNullOrWhiteSpace(arkitName))
                {
                    blocked.Add(arkitName);
                }
            }

            return blocked;
        }

        private string GetFirstUnusedArkitName(SerializedProperty customMappingsProperty)
        {
            var usedNames = new HashSet<string>(EnumerateArkitNames(customMappingsProperty));

            foreach (var arkitName in ARKitBlendShapeNames.GetAll())
            {
                if (!usedNames.Contains(arkitName))
                {
                    return arkitName;
                }
            }

            return null;
        }

        private void AddCategoryMappings(SerializedProperty customMappingsProperty, string[] arkitNames)
        {
            var usedNames = new HashSet<string>(EnumerateArkitNames(customMappingsProperty));

            foreach (var name in arkitNames)
            {
                if (!usedNames.Add(name))
                {
                    continue;
                }

                AppendMappingElement(customMappingsProperty, name);
            }
        }

        private static void AppendMappingElement(SerializedProperty customMappingsProperty, string arkitName)
        {
            int index = customMappingsProperty.arraySize;
            customMappingsProperty.InsertArrayElementAtIndex(index);

            // InsertArrayElementAtIndexは直前の要素を複製するため、明示的に初期化する
            var mappingProperty = customMappingsProperty.GetArrayElementAtIndex(index);
            mappingProperty.FindPropertyRelative("arkitName").stringValue = arkitName;
            mappingProperty.FindPropertyRelative("enabled").boolValue = true;
            mappingProperty.FindPropertyRelative("sources").ClearArray();
        }

        /// <summary>
        /// マッピング1件を描画する。要素を削除した場合はfalse（呼び出し元は列挙を打ち切る）
        /// </summary>
        private bool DrawMappingItem(SerializedProperty customMappingsProperty, int index)
        {
            var mappingProperty = customMappingsProperty.GetArrayElementAtIndex(index);
            var enabledProperty = mappingProperty.FindPropertyRelative("enabled");
            var arkitNameProperty = mappingProperty.FindPropertyRelative("arkitName");
            var sourcesProperty = mappingProperty.FindPropertyRelative("sources");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ヘッダー行
            EditorGUILayout.BeginHorizontal();

            int outerIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            bool newEnabled = EditorGUILayout.Toggle(
                enabledProperty.boolValue, GUILayout.Width(MappingToggleWidth));
            if (newEnabled != enabledProperty.boolValue)
            {
                enabledProperty.boolValue = newEnabled;
            }

            // ARKit名の選択
            string arkitNameLabel = ArkitNameSearchDropdown.BuildButtonLabel(arkitNameProperty.stringValue);
            if (DrawDropdownButton(arkitNameLabel, out var arkitNameRect))
            {
                ShowArkitNameDropdown(arkitNameRect, customMappingsProperty, index, arkitNameProperty);
            }

            if (GUILayout.Button("+", GUILayout.Width(25)))
            {
                AppendSourceElement(sourcesProperty);
            }

            bool removed = false;
            if (GUILayout.Button("×", GUILayout.Width(25)))
            {
                customMappingsProperty.DeleteArrayElementAtIndex(index);
                removed = true;
            }

            EditorGUI.indentLevel = outerIndentLevel;
            EditorGUILayout.EndHorizontal();

            if (removed)
            {
                EditorGUILayout.EndVertical();
                return false;
            }

            // ソースBlendShape（ドロップダウンの左端をヘッダー行のARKit名に揃える）
            for (int j = 0; j < sourcesProperty.arraySize; j++)
            {
                // カスタムマッピングのSideは左右分割がOFFのとき生成側で無視される
                if (!DrawSourceItem(
                        sourcesProperty,
                        j,
                        MappingToggleWidth,
                        sideAppliesNow: IsLeftRightSplitEnabled()))
                {
                    break;
                }
            }

            EditorGUILayout.EndVertical();
            return true;
        }

        /// <summary>
        /// 左右分割が有効か。編集途中の値を見るためserializedObject側から取る
        /// </summary>
        private bool IsLeftRightSplitEnabled()
        {
            var property = serializedObject.FindProperty("enableLeftRightSplit");
            return property == null || property.boolValue;
        }

        /// <summary>
        /// ソースBlendShape1件を描画する。要素を削除した場合はfalse（呼び出し元は列挙を打ち切る）
        /// </summary>
        /// <param name="sideAppliesNow">
        /// 現在の設定でSideが生成に効くか。効かない場合はドロップダウンを非活性にして無視されることを示す
        /// </param>
        private bool DrawSourceItem(
            SerializedProperty sourcesProperty,
            int sourceIndex,
            float leadingSpace = 0f,
            string sideTooltipKey = "enum.side.tooltip",
            bool sideAppliesNow = true)
        {
            var sourceProperty = sourcesProperty.GetArrayElementAtIndex(sourceIndex);
            var blendShapeNameProperty = sourceProperty.FindPropertyRelative("blendShapeName");
            var weightProperty = sourceProperty.FindPropertyRelative("weight");
            var sideProperty = sourceProperty.FindPropertyRelative("side");

            EditorGUILayout.BeginHorizontal();

            int outerIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            if (leadingSpace > 0f)
            {
                GUILayout.Space(leadingSpace);
            }

            // BlendShape名の選択
            if (_availableBlendShapes.Count > 0)
            {
                string label = BlendShapeSearchDropdown.BuildButtonLabel(
                    blendShapeNameProperty.stringValue, _availableBlendShapes);
                if (DrawDropdownButton(label, out var buttonRect))
                {
                    ShowBlendShapeDropdown(buttonRect, blendShapeNameProperty);
                }
            }
            else
            {
                string newName = EditorGUILayout.TextField(blendShapeNameProperty.stringValue);
                if (newName != blendShapeNameProperty.stringValue)
                {
                    blendShapeNameProperty.stringValue = newName;
                }
            }

            // 重み
            EditorGUILayout.LabelField("×", GUILayout.Width(15));
            float newWeight = EditorGUILayout.Slider(weightProperty.floatValue, -2f, 2f, GUILayout.Width(100));
            if (Mathf.Abs(newWeight - weightProperty.floatValue) > 0.0001f)
            {
                weightProperty.floatValue = newWeight;
            }

            // 左右適用範囲
            var sideTooltip = S(sideAppliesNow ? sideTooltipKey : "enum.side.split_off.tooltip");
            var sideLabels = new[]
            {
                new GUIContent(S("enum.side.both"), sideTooltip),
                new GUIContent(S("enum.side.left_only"), sideTooltip),
                new GUIContent(S("enum.side.right_only"), sideTooltip),
            };
            // 値そのものは保持したまま、効いていないことを非活性表示で示す
            using (new EditorGUI.DisabledScope(!sideAppliesNow))
            {
                int newSide = EditorGUILayout.Popup(sideProperty.enumValueIndex, sideLabels, GUILayout.Width(70));
                if (newSide != sideProperty.enumValueIndex)
                {
                    sideProperty.enumValueIndex = newSide;
                }
            }

            bool removed = false;

            // 削除ボタン
            if (GUILayout.Button("－", GUILayout.Width(25)))
            {
                sourcesProperty.DeleteArrayElementAtIndex(sourceIndex);
                removed = true;
            }

            EditorGUI.indentLevel = outerIndentLevel;

            EditorGUILayout.EndHorizontal();
            return !removed;
        }

        /// <summary>
        /// ドロップダウンを開くボタンを描画する。押された場合はtrue（buttonRectは表示位置として使う）
        /// </summary>
        private static bool DrawDropdownButton(string label, out Rect buttonRect)
        {
            var content = new GUIContent(label, label);

            // BlendShape名のラベルによって右側のコントロールが範囲外に押し出されるのを防ぐ
            buttonRect = GUILayoutUtility.GetRect(
                content,
                EditorStyles.popup,
                GUILayout.ExpandWidth(true),
                GUILayout.MinWidth(EditorGUIUtility.fieldWidth));

            return EditorGUI.DropdownButton(buttonRect, content, FocusType.Keyboard, EditorStyles.popup);
        }

        private void ShowBlendShapeDropdown(Rect buttonRect, SerializedProperty blendShapeNameProperty)
        {
            RefreshBlendShapeList();

            var dropdown = new BlendShapeSearchDropdown(
                new AdvancedDropdownState(),
                _availableBlendShapes,
                blendShapeNameProperty.stringValue,
                CreateSelectionCallback(blendShapeNameProperty));

            dropdown.Open(buttonRect);
        }

        private void ShowArkitNameDropdown(
            Rect buttonRect,
            SerializedProperty customMappingsProperty,
            int mappingIndex,
            SerializedProperty arkitNameProperty)
        {
            var dropdown = new ArkitNameSearchDropdown(
                new AdvancedDropdownState(),
                ARKitBlendShapeNames.GetAll(),
                GetArkitNamesBlockedForMapping(customMappingsProperty, mappingIndex),
                arkitNameProperty.stringValue,
                CreateSelectionCallback(arkitNameProperty));

            dropdown.Open(buttonRect);
        }

        private Action<string> CreateSelectionCallback(SerializedProperty property)
        {
            string propertyPath = property.propertyPath;

            return selectedValue =>
            {
                if (this == null)
                {
                    return;
                }

                _pendingSelectionPropertyPath = propertyPath;
                _pendingSelectionValue = selectedValue;
                Repaint();
            };
        }

        private void ApplyPendingDropdownSelection()
        {
            if (_pendingSelectionPropertyPath == null)
            {
                return;
            }

            var property = serializedObject.FindProperty(_pendingSelectionPropertyPath);
            if (property != null && property.propertyType == SerializedPropertyType.String)
            {
                property.stringValue = _pendingSelectionValue;
            }

            _pendingSelectionPropertyPath = null;
            _pendingSelectionValue = null;
        }

        private static void AppendSourceElement(SerializedProperty sourcesProperty)
        {
            int index = sourcesProperty.arraySize;
            sourcesProperty.InsertArrayElementAtIndex(index);

            // InsertArrayElementAtIndexは直前の要素を複製するため、明示的に初期化する
            var sourceProperty = sourcesProperty.GetArrayElementAtIndex(index);
            sourceProperty.FindPropertyRelative("blendShapeName").stringValue = string.Empty;
            sourceProperty.FindPropertyRelative("weight").floatValue = 1.0f;
            sourceProperty.FindPropertyRelative("side").enumValueIndex = (int)BlendShapeSide.Both;
        }

        private void DrawMouthCancellationUI()
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("mouthCancellationStrength"),
                G("prop.mouth_cancellation_strength"));

            // 打ち消し対象（アバター側で常時適用しているBlendShape）
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(S("mouth_cancellation.sources.label"), EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(S("mouth_cancellation.sources.hint"), EditorStyles.miniLabel);

            var sourcesProperty = serializedObject.FindProperty("mouthCancellationSources");
            if (sourcesProperty.arraySize == 0)
            {
                EditorGUILayout.LabelField(S("mouth_cancellation.sources.empty"), EditorStyles.miniLabel);
            }

            // DrawSourceItemは行内のインデントを無効にするため周囲の要素と揃うようにインデントさせる
            float indentSpace = EditorGUI.indentLevel * IndentWidth;

            for (int i = 0; i < sourcesProperty.arraySize; i++)
            {
                // 打ち消し元のSideは左右分割の設定と独立に適用されるため、別の説明を出す
                if (!DrawSourceItem(sourcesProperty, i, indentSpace, "enum.side.cancellation.tooltip"))
                {
                    // 要素を削除したため、以降のインデックスがずれる。この描画パスは打ち切る
                    break;
                }
            }

            if (GUILayout.Button(S("mouth_cancellation.sources.add")))
            {
                AppendSourceElement(sourcesProperty);
            }

            // 焼き込み先のARKit BlendShape
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(S("mouth_cancellation.targets.label"), EditorStyles.miniBoldLabel);
            DrawMouthCancellationTargets();
        }

        private void DrawMouthCancellationTargets()
        {
            var targetsProperty = serializedObject.FindProperty("mouthCancellationTargets");
            var selectableNames = ARKitBlendShapeNames.Mouth;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S("mouth_cancellation.targets.select_all"), EditorStyles.miniButton))
            {
                targetsProperty.ClearArray();
                foreach (var arkitName in selectableNames)
                {
                    AppendStringElement(targetsProperty, arkitName);
                }
            }
            if (GUILayout.Button(S("mouth_cancellation.targets.clear"), EditorStyles.miniButton))
            {
                targetsProperty.ClearArray();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                S("mouth_cancellation.targets.count", targetsProperty.arraySize, selectableNames.Length),
                EditorStyles.miniLabel);

            if (targetsProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox(S("mouth_cancellation.targets.none_warning"), MessageType.Warning);
            }
            else if (targetsProperty.arraySize > 1)
            {
                // 同時適用時は打ち消しが重なって過剰になるため、対象は絞ることを推奨する
                EditorGUILayout.HelpBox(S("mouth_cancellation.targets.overshoot_warning"), MessageType.Warning);
            }

            _mouthCancellationTargetScrollPosition = EditorGUILayout.BeginScrollView(
                _mouthCancellationTargetScrollPosition,
                GUILayout.Height(150f));

            foreach (var arkitName in selectableNames)
            {
                int existingIndex = IndexOfStringElement(targetsProperty, arkitName);
                bool isSelected = existingIndex >= 0;
                bool newSelected = EditorGUILayout.ToggleLeft(arkitName, isSelected);
                if (newSelected == isSelected)
                {
                    continue;
                }

                if (newSelected)
                {
                    AppendStringElement(targetsProperty, arkitName);
                }
                else
                {
                    targetsProperty.DeleteArrayElementAtIndex(existingIndex);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static int IndexOfStringElement(SerializedProperty arrayProperty, string value)
        {
            for (int i = 0; i < arrayProperty.arraySize; i++)
            {
                if (string.Equals(arrayProperty.GetArrayElementAtIndex(i).stringValue, value))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AppendStringElement(SerializedProperty arrayProperty, string value)
        {
            int index = arrayProperty.arraySize;
            arrayProperty.InsertArrayElementAtIndex(index);
            arrayProperty.GetArrayElementAtIndex(index).stringValue = value;
        }

        private void AddNewMapping(SerializedProperty customMappingsProperty)
        {
            string firstUnusedArkitName = GetFirstUnusedArkitName(customMappingsProperty);
            if (string.IsNullOrEmpty(firstUnusedArkitName))
            {
                EditorUtility.DisplayDialog(
                    S("dialog.title"),
                    S("dialog.no_available_name.message"),
                    S("common.ok"));
                return;
            }

            AppendMappingElement(customMappingsProperty, firstUnusedArkitName);
        }

        /// <summary>
        /// VRChat標準の表情のみを使用したカスタムマッピングを設定
        /// MMD用シェイプキーが存在しないアバター向け
        /// </summary>
        private void ApplyVRChatStandardPreset()
        {
            // BlendShapeリストを先に更新
            RefreshBlendShapeList();

            // 変更はOnInspectorGUI末尾のApplyModifiedPropertiesでまとめて適用される
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
            // mouthPuckerは「う」から生成すると口元が破綻しやすいためプリセットには含めない
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

            if (_component.debugMode)
            {
                Debug.Log("[ARKitGenerator] " + S("log.preset_applied"));
            }
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
            return GenerateBlendShapesUseCase.ResolveTargetRenderer(_component);
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
                // 生成側（CollectCustomMappings）と同じ空判定にする。
                // ここだけIsNullOrEmptyだと、生成されない空白のみの名前が
                // 空欄のスライダーとしてプレビューに並んでしまう
                if (mapping == null || !mapping.enabled || string.IsNullOrWhiteSpace(mapping.arkitName))
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

            // 空名シェイプは名前で指定できずウェイト操作も効かないため、プレビュー対象から除外する
            // （メッシュ上のデータとしてはそのまま保持される）
            var sourceShapeNames = new HashSet<string>();
            for (int i = 0; i < targetRenderer.sharedMesh.blendShapeCount; i++)
            {
                var shapeName = targetRenderer.sharedMesh.GetBlendShapeName(i);
                if (!string.IsNullOrEmpty(shapeName))
                {
                    sourceShapeNames.Add(shapeName);
                }
            }

            var processedAutoNames = new HashSet<string>();
            foreach (var mapping in ARKitMappingTable.GetMappings())
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

        /// <summary>
        /// 自動マッピング一覧の1行を描画する。
        /// 対応シェイプキー名は通常のインスペクタ幅に収まらず右端で省略されるため、
        /// 全文をツールチップからも読めるようにする
        /// </summary>
        private static void DrawAutoMappingRow(string arkitName, string sources)
        {
            EditorGUILayout.LabelField(
                new GUIContent(arkitName, sources),
                new GUIContent(sources, sources));
        }

        /// <summary>
        /// ARKit名1件分の説明文を組み立てる。
        /// 候補はARKitMappingTableから生成し、手続き的生成で補える場合はその旨を後ろに付ける
        /// </summary>
        private static string DescribeAutoMappingEntry(AutoMappingEntry entry)
        {
            var sources = AutoMappingSummary.DescribeCandidates(entry, SideLabel);
            var description = string.IsNullOrEmpty(sources) ? string.Empty : "\u2190 " + sources;

            if (!entry.HasProceduralFallback)
            {
                return description;
            }

            return description + S("auto_mappings.row.procedural_fallback");
        }

        private static string SideLabel(BlendShapeSide side)
        {
            switch (side)
            {
                case BlendShapeSide.LeftOnly:
                    return S("enum.side.left_only");
                case BlendShapeSide.RightOnly:
                    return S("enum.side.right_only");
                default:
                    return S("enum.side.both");
            }
        }

        private void DrawAutoMappingsInfo()
        {
            EditorGUILayout.HelpBox(S("auto_mappings.description"), MessageType.Info);

            // 表示はARKitMappingTableから生成する（手書きすると実装とずれるため）
            if (_autoMappingCategories == null)
            {
                _autoMappingCategories = AutoMappingSummary.Build();
            }

            foreach (var category in _autoMappingCategories)
            {
                if (!_autoMappingFolds.TryGetValue(category.Key, out bool expanded))
                {
                    expanded = false;
                }

                expanded = EditorGUILayout.Foldout(expanded, S("auto_mappings.fold." + category.Key));
                _autoMappingFolds[category.Key] = expanded;

                if (!expanded)
                {
                    continue;
                }

                EditorGUI.indentLevel++;
                foreach (var entry in category.Entries)
                {
                    DrawAutoMappingRow(entry.ArkitName, DescribeAutoMappingEntry(entry));
                }
                EditorGUI.indentLevel--;
            }
        }
    }
}
