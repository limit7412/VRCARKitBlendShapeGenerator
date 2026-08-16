using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;
using ARKitBlendShapeGenerator.Infra;
using ARKitBlendShapeGenerator.UseCase;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Handler
{
    /// <summary>
    /// NDMFプレビューシステム統合
    /// Tools > NDM Framework > Configure Previews、または
    /// コンポーネントのインスペクタ上のトグルでON/OFFを切り替え可能
    /// </summary>
    public class ARKitBlendShapeGeneratorPreview : IRenderFilter
    {
        /// <summary>
        /// プレビューのON/OFFを制御するノード
        /// NDMFのConfigure Previewsウィンドウに表示される
        /// </summary>
        public static readonly TogglablePreviewNode EnableNode = TogglablePreviewNode.Create(
            () => "ARKit BlendShape Generator",
            qualifiedName: "com.qazx7412.kx-vrc-arkit-blendshape-generator/Preview",
            initialState: false
        );

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return EnableNode;
        }

        public bool IsEnabled(ComputeContext context)
        {
            return context.Observe(EnableNode.IsEnabled);
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var avatarRoots = context.GetAvatarRoots();
            return avatarRoots.SelectMany(r => GroupsForAvatar(context, r)).ToImmutableList();
        }

        private IEnumerable<RenderGroup> GroupsForAvatar(ComputeContext context, GameObject avatarRoot)
        {
            // このアバターにARKitBlendShapeGeneratorComponentがあるか確認
            var components = context
                .GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(avatarRoot, true)
                .Where(c => c != null)
                .ToArray();
            var component = GenerateBlendShapesUseCase.SelectPrimaryComponent(avatarRoot, components);

            if (component != null)
            {
                // targetRenderer変更に追従させる
                var renderer = context.Observe(component, c => c.targetRenderer);
                if (renderer == null)
                {
                    // targetRenderer未設定時は子要素をフォールバック対象にする
                    // レンダラーの列挙はComputeContext経由で行い（変更監視のため）、
                    // どれを選ぶかの判断は他経路と共通のTargetRendererResolverに委ねる
                    // 監視の登録と選択で二度走査するため、ここで確定させる
                    var candidates = context
                        .GetComponentsInChildren<SkinnedMeshRenderer>(component.gameObject, true)
                        .ToArray();

                    // フォールバックは名前も選択条件に含むため、候補の改名にも追従させる
                    foreach (var candidate in candidates)
                    {
                        if (TargetRendererResolver.IsNameSearchTarget(component.transform, candidate))
                        {
                            context.Observe(candidate.gameObject, go => go.name);
                        }
                    }

                    renderer = TargetRendererResolver.SelectFallback(component.transform, candidates);
                }

                if (renderer != null && context.Observe(renderer, r => r.sharedMesh) != null)
                {
                    yield return RenderGroup.For(renderer).WithData(component);
                }
            }
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            if (group == null || proxyPairs == null)
            {
                return Task.FromResult<IRenderFilterNode>(null);
            }

            var pair = proxyPairs.FirstOrDefault();
            var original = pair.Item1;
            var proxy = pair.Item2;
            var component = group.GetData<ARKitBlendShapeGeneratorComponent>();

            if (component == null)
            {
                return Task.FromResult<IRenderFilterNode>(null);
            }

            if (CustomMappingValidation.HasDuplicateArkitNames(component.customMappings, out var duplicateArkitNames))
            {
                Debug.LogError(
                    "[ARKitGenerator] " + S("log.duplicate_preview_stop", string.Join(", ", duplicateArkitNames)),
                    component);
                return Task.FromResult<IRenderFilterNode>(null);
            }

            if (original is not SkinnedMeshRenderer originalSmr ||
                proxy is not SkinnedMeshRenderer proxySmr)
            {
                return Task.FromResult<IRenderFilterNode>(null);
            }

            var node = new PreviewNode(component, originalSmr, proxySmr, context);
            return Task.FromResult<IRenderFilterNode>(node);
        }

        /// <summary>
        /// プレビューノード - 実際のBlendShape生成処理を行う
        /// </summary>
        private class PreviewNode : IRenderFilterNode
        {
            private Mesh _generatedMesh;
            private readonly ARKitBlendShapeGeneratorComponent _component;
            private readonly int _componentInstanceId;
            private readonly int _observedComponentConfigRevision;
            private readonly PreviewSettingsSnapshot _observedSettings;

            // 先送りの合図は全プレビューノードへ届く。自分が受け取った分まで進めておかないと、
            // 他のコンポーネントの操作で進んだ合図をいつまでも「未反映」と見なしてしまう
            private int _observedDeferredRebuildRevision;
            private readonly Dictionary<string, int> _shapeIndices = new Dictionary<string, int>();
            private HashSet<int> _appliedInteractiveIndices = new HashSet<int>();
            private HashSet<int> _nextAppliedInteractiveIndices = new HashSet<int>();

            public RenderAspects WhatChanged => RenderAspects.Mesh | RenderAspects.Shapes;

            public PreviewNode(
                ARKitBlendShapeGeneratorComponent component,
                SkinnedMeshRenderer originalRenderer,
                SkinnedMeshRenderer proxyRenderer,
                ComputeContext context)
            {
                _component = component;
                _componentInstanceId = component != null ? component.GetInstanceID() : 0;
                _observedComponentConfigRevision = context.Observe(ARKitBlendShapeGeneratorPreviewState.ComponentConfigRevision);
                _observedDeferredRebuildRevision = context.Observe(ARKitBlendShapeGeneratorPreviewState.DeferredRebuildRevision);

                // customMappingsの内容を含むコンポーネント変更全体を監視
                if (component != null)
                {
                    context.Observe(component);
                }

                _observedSettings = ObserveSettings(component, context);

                context.Observe(originalRenderer, r => r.sharedMesh);
                context.Observe(proxyRenderer, r => r.sharedMesh);

                var sourceMesh = proxyRenderer.sharedMesh ?? originalRenderer.sharedMesh;
                if (sourceMesh == null)
                {
                    return;
                }

                _generatedMesh = Object.Instantiate(sourceMesh);
                _generatedMesh.name = sourceMesh.name + "_ARKitPreview";

                var customMappings = component != null ? component.customMappings : null;
                var options = component != null
                    ? BlendShapeGenerationOptions.FromComponent(component)
                    : new BlendShapeGenerationOptions();

                var result = GenerateBlendShapesUseCase.GenerateInto(
                    sourceMesh,
                    _generatedMesh,
                    customMappings,
                    options);

                CacheShapeIndices(result);
                proxyRenderer.sharedMesh = _generatedMesh;
            }

            public Task<IRenderFilterNode> Refresh(
                IEnumerable<(Renderer, Renderer)> proxyPairs,
                ComputeContext context,
                RenderAspects updatedAspects)
            {
                if (_generatedMesh == null || proxyPairs == null || _component == null)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                if ((updatedAspects & RenderAspects.Mesh) != 0)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                // ノードを残す場合に監視が切れないよう、判定より先に監視をすべて登録しておく
                int currentDeferredRebuildRevision = context.Observe(
                    ARKitBlendShapeGeneratorPreviewState.DeferredRebuildRevision);
                int currentConfigRevision = context.Observe(ARKitBlendShapeGeneratorPreviewState.ComponentConfigRevision);
                context.Observe(_component);
                var currentSettings = ObserveSettings(_component, context);

                var pair = proxyPairs.FirstOrDefault();
                if (pair.Item1 is not SkinnedMeshRenderer ||
                    pair.Item2 is not SkinnedMeshRenderer)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                bool deferredRebuildRequested = currentDeferredRebuildRevision != _observedDeferredRebuildRevision;
                _observedDeferredRebuildRevision = currentDeferredRebuildRevision;

                // 反映済みの設定（_observedSettings）は更新しない。
                // 先送り中は差分が残り続けることで、実行までの延長を要求し続ける
                switch (currentSettings.CompareWith(_observedSettings))
                {
                    case PreviewSettingsChange.Structural:
                        return Task.FromResult<IRenderFilterNode>(null);

                    case PreviewSettingsChange.Continuous:
                        if (deferredRebuildRequested)
                        {
                            // 先送りしていた分をここで反映する
                            return Task.FromResult<IRenderFilterNode>(null);
                        }

                        // スライダーのドラッグ中は毎フレーム変更が届く。そのたびにフル再生成すると
                        // 大きいアバターでは操作が重くなるため、値が落ち着くまで実行を遅らせ、
                        // それまでは1つ前の結果を出したままにする
                        PreviewRebuildScheduler.Request();
                        return Task.FromResult<IRenderFilterNode>(this);

                    default:
                        if (currentConfigRevision != _observedComponentConfigRevision)
                        {
                            // 差分を検出できなくても設定自体は変わっているなら作り直す。
                            // 署名の衝突など、比較での取りこぼしに対する保険
                            return Task.FromResult<IRenderFilterNode>(null);
                        }

                        return Task.FromResult<IRenderFilterNode>(this);
                }
            }

            /// <summary>
            /// 生成に影響する設定を、ComputeContextへ監視を登録しながら読み出す
            /// </summary>
            private static PreviewSettingsSnapshot ObserveSettings(
                ARKitBlendShapeGeneratorComponent component,
                ComputeContext context)
            {
                if (component == null)
                {
                    return new PreviewSettingsSnapshot();
                }

                var targetRenderer = context.Observe(component, c => c.targetRenderer);
                return new PreviewSettingsSnapshot
                {
                    IntensityMultiplier = context.Observe(component, c => c.intensityMultiplier),
                    EnableLeftRightSplit = context.Observe(component, c => c.enableLeftRightSplit),
                    BlendWidth = context.Observe(component, c => c.blendWidth),
                    OverwriteExisting = context.Observe(component, c => c.overwriteExisting),
                    EnableProceduralMouthShapes = context.Observe(component, c => c.enableProceduralMouthShapes),
                    ProceduralMouthIntensity = context.Observe(component, c => c.proceduralMouthIntensity),
                    EnableMouthCancellation = context.Observe(component, c => c.enableMouthCancellation),
                    MouthCancellationStrength = context.Observe(component, c => c.mouthCancellationStrength),
                    MouthCancellationSignature = PreviewSettingsSnapshot.BuildMouthCancellationSignature(
                        component.mouthCancellationSources,
                        component.mouthCancellationTargets),
                    CustomMappingsSignature = PreviewSettingsSnapshot.BuildCustomMappingsSignature(component.customMappings),
                    TargetRendererInstanceId = targetRenderer != null ? targetRenderer.GetInstanceID() : 0,
                };
            }

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (_generatedMesh == null || proxy is not SkinnedMeshRenderer proxySmr) return;

                // プロキシのメッシュが正しいか確認
                if (proxySmr.sharedMesh != _generatedMesh)
                {
                    proxySmr.sharedMesh = _generatedMesh;
                }

                var interactiveState = ARKitBlendShapeGeneratorPreviewState.Current;
                if (!interactiveState.InteractiveEnabled ||
                    interactiveState.ActiveComponentInstanceId != _componentInstanceId)
                {
                    ClearAppliedInteractiveWeights(proxySmr);
                    return;
                }

                _nextAppliedInteractiveIndices.Clear();
                foreach (var kvp in interactiveState.WeightsByArkitName)
                {
                    if (!_shapeIndices.TryGetValue(kvp.Key, out int blendShapeIndex))
                    {
                        continue;
                    }

                    float clamped = Mathf.Clamp01(kvp.Value) * 100f;
                    if (clamped <= 0.0001f)
                    {
                        continue;
                    }

                    proxySmr.SetBlendShapeWeight(blendShapeIndex, clamped);
                    _nextAppliedInteractiveIndices.Add(blendShapeIndex);
                }

                foreach (int previouslyAppliedIndex in _appliedInteractiveIndices)
                {
                    if (!_nextAppliedInteractiveIndices.Contains(previouslyAppliedIndex))
                    {
                        proxySmr.SetBlendShapeWeight(previouslyAppliedIndex, 0f);
                    }
                }

                var swap = _appliedInteractiveIndices;
                _appliedInteractiveIndices = _nextAppliedInteractiveIndices;
                _nextAppliedInteractiveIndices = swap;
            }

            private void CacheShapeIndices(BlendShapeGenerationResult result)
            {
                _shapeIndices.Clear();
                if (result == null)
                {
                    return;
                }

                // 生成結果のインデックス表（同名は後勝ちで最新indexが入っている）をそのまま使う
                foreach (var kvp in result.ShapeIndices)
                {
                    _shapeIndices[kvp.Key] = kvp.Value;
                }
            }

            public void Dispose()
            {
                _appliedInteractiveIndices.Clear();
                _nextAppliedInteractiveIndices.Clear();

                if (_generatedMesh != null)
                {
                    Object.DestroyImmediate(_generatedMesh);
                }
            }

            private void ClearAppliedInteractiveWeights(SkinnedMeshRenderer proxySmr)
            {
                foreach (int index in _appliedInteractiveIndices)
                {
                    proxySmr.SetBlendShapeWeight(index, 0f);
                }

                _appliedInteractiveIndices.Clear();
                _nextAppliedInteractiveIndices.Clear();
            }
        }
    }
}
