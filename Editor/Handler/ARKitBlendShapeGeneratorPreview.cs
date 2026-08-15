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
            private readonly float _observedIntensityMultiplier;
            private readonly bool _observedEnableLeftRightSplit;
            private readonly float _observedBlendWidth;
            private readonly bool _observedOverwriteExisting;
            private readonly bool _observedEnableProceduralMouthShapes;
            private readonly float _observedProceduralMouthIntensity;
            private readonly bool _observedEnableMouthCancellation;
            private readonly float _observedMouthCancellationStrength;
            private readonly int _observedMouthCancellationSignature;
            private readonly int _observedTargetRendererInstanceId;
            private readonly int _observedCustomMappingsSignature;
            private readonly int _observedExcludedArkitNamesSignature;
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

                // customMappingsの内容を含むコンポーネント変更全体を監視
                float observedIntensityMultiplier = 0f;
                bool observedEnableLeftRightSplit = false;
                float observedBlendWidth = 0f;
                bool observedOverwriteExisting = false;
                bool observedEnableProceduralMouthShapes = false;
                float observedProceduralMouthIntensity = 0f;
                bool observedEnableMouthCancellation = false;
                float observedMouthCancellationStrength = 0f;
                int observedMouthCancellationSignature = 0;
                int observedCustomMappingsSignature = 0;
                int observedExcludedArkitNamesSignature = 0;
                SkinnedMeshRenderer observedTargetRenderer = null;
                if (component != null)
                {
                    context.Observe(component);
                    observedIntensityMultiplier = context.Observe(component, c => c.intensityMultiplier);
                    observedEnableLeftRightSplit = context.Observe(component, c => c.enableLeftRightSplit);
                    observedBlendWidth = context.Observe(component, c => c.blendWidth);
                    observedOverwriteExisting = context.Observe(component, c => c.overwriteExisting);
                    observedEnableProceduralMouthShapes = context.Observe(component, c => c.enableProceduralMouthShapes);
                    observedProceduralMouthIntensity = context.Observe(component, c => c.proceduralMouthIntensity);
                    observedEnableMouthCancellation = context.Observe(component, c => c.enableMouthCancellation);
                    observedMouthCancellationStrength = context.Observe(component, c => c.mouthCancellationStrength);
                    observedMouthCancellationSignature = BuildMouthCancellationSignature(component);
                    observedCustomMappingsSignature = BuildCustomMappingsSignature(component.customMappings);
                    observedExcludedArkitNamesSignature = BuildExcludedArkitNamesSignature(component);
                    observedTargetRenderer = context.Observe(component, c => c.targetRenderer);
                }

                _observedIntensityMultiplier = observedIntensityMultiplier;
                _observedEnableLeftRightSplit = observedEnableLeftRightSplit;
                _observedBlendWidth = observedBlendWidth;
                _observedOverwriteExisting = observedOverwriteExisting;
                _observedEnableProceduralMouthShapes = observedEnableProceduralMouthShapes;
                _observedProceduralMouthIntensity = observedProceduralMouthIntensity;
                _observedEnableMouthCancellation = observedEnableMouthCancellation;
                _observedMouthCancellationStrength = observedMouthCancellationStrength;
                _observedMouthCancellationSignature = observedMouthCancellationSignature;
                _observedCustomMappingsSignature = observedCustomMappingsSignature;
                _observedExcludedArkitNamesSignature = observedExcludedArkitNamesSignature;
                _observedTargetRendererInstanceId = observedTargetRenderer != null ? observedTargetRenderer.GetInstanceID() : 0;

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

                int currentConfigRevision = context.Observe(ARKitBlendShapeGeneratorPreviewState.ComponentConfigRevision);
                if (currentConfigRevision != _observedComponentConfigRevision)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                if ((updatedAspects & RenderAspects.Mesh) != 0)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                context.Observe(_component);

                float currentIntensityMultiplier = context.Observe(_component, c => c.intensityMultiplier);
                if (!Mathf.Approximately(currentIntensityMultiplier, _observedIntensityMultiplier))
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                bool currentEnableLeftRightSplit = context.Observe(_component, c => c.enableLeftRightSplit);
                if (currentEnableLeftRightSplit != _observedEnableLeftRightSplit)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                float currentBlendWidth = context.Observe(_component, c => c.blendWidth);
                if (!Mathf.Approximately(currentBlendWidth, _observedBlendWidth))
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                bool currentOverwriteExisting = context.Observe(_component, c => c.overwriteExisting);
                if (currentOverwriteExisting != _observedOverwriteExisting)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                bool currentEnableProceduralMouthShapes = context.Observe(_component, c => c.enableProceduralMouthShapes);
                if (currentEnableProceduralMouthShapes != _observedEnableProceduralMouthShapes)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                float currentProceduralMouthIntensity = context.Observe(_component, c => c.proceduralMouthIntensity);
                if (!Mathf.Approximately(currentProceduralMouthIntensity, _observedProceduralMouthIntensity))
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                bool currentEnableMouthCancellation = context.Observe(_component, c => c.enableMouthCancellation);
                if (currentEnableMouthCancellation != _observedEnableMouthCancellation)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                float currentMouthCancellationStrength = context.Observe(_component, c => c.mouthCancellationStrength);
                if (!Mathf.Approximately(currentMouthCancellationStrength, _observedMouthCancellationStrength))
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                int currentMouthCancellationSignature = BuildMouthCancellationSignature(_component);
                if (currentMouthCancellationSignature != _observedMouthCancellationSignature)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                int currentCustomMappingsSignature = BuildCustomMappingsSignature(_component.customMappings);
                if (currentCustomMappingsSignature != _observedCustomMappingsSignature)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                int currentExcludedArkitNamesSignature = BuildExcludedArkitNamesSignature(_component);
                if (currentExcludedArkitNamesSignature != _observedExcludedArkitNamesSignature)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                var currentTargetRenderer = context.Observe(_component, c => c.targetRenderer);
                int currentTargetRendererId = currentTargetRenderer != null ? currentTargetRenderer.GetInstanceID() : 0;
                if (currentTargetRendererId != _observedTargetRendererInstanceId)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                var pair = proxyPairs.FirstOrDefault();
                if (pair.Item1 is not SkinnedMeshRenderer ||
                    pair.Item2 is not SkinnedMeshRenderer)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                return Task.FromResult<IRenderFilterNode>(this);
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

            private static int BuildCustomMappingsSignature(List<CustomBlendShapeMapping> customMappings)
            {
                unchecked
                {
                    int hash = 17;
                    if (customMappings == null)
                    {
                        return hash;
                    }

                    hash = (hash * 31) + customMappings.Count;
                    foreach (var mapping in customMappings)
                    {
                        if (mapping == null)
                        {
                            hash = (hash * 31) + 1;
                            continue;
                        }

                        hash = (hash * 31) + (mapping.enabled ? 1 : 0);
                        hash = (hash * 31) + HashString(mapping.arkitName);
                        hash = HashSources(hash, mapping.sources);
                    }

                    return hash;
                }
            }

            private static int BuildMouthCancellationSignature(ARKitBlendShapeGeneratorComponent component)
            {
                unchecked
                {
                    int hash = 17;
                    if (component == null)
                    {
                        return hash;
                    }

                    hash = HashSources(hash, component.mouthCancellationSources);

                    var targets = component.mouthCancellationTargets;
                    if (targets == null)
                    {
                        return (hash * 31) + 2;
                    }

                    hash = (hash * 31) + targets.Count;
                    foreach (var target in targets)
                    {
                        hash = (hash * 31) + HashString(target);
                    }

                    return hash;
                }
            }

            private static int BuildExcludedArkitNamesSignature(ARKitBlendShapeGeneratorComponent component)
            {
                unchecked
                {
                    int hash = 17;
                    var excluded = component != null ? component.excludedArkitNames : null;
                    if (excluded == null)
                    {
                        return (hash * 31) + 2;
                    }

                    hash = (hash * 31) + excluded.Count;
                    foreach (var name in excluded)
                    {
                        hash = (hash * 31) + HashString(name);
                    }

                    return hash;
                }
            }

            private static int HashSources(int hash, List<BlendShapeSource> sources)
            {
                unchecked
                {
                    if (sources == null)
                    {
                        return (hash * 31) + 2;
                    }

                    hash = (hash * 31) + sources.Count;
                    foreach (var source in sources)
                    {
                        if (source == null)
                        {
                            hash = (hash * 31) + 3;
                            continue;
                        }

                        hash = (hash * 31) + HashString(source.blendShapeName);
                        hash = (hash * 31) + source.weight.GetHashCode();
                        hash = (hash * 31) + (int)source.side;
                    }

                    return hash;
                }
            }

            private static int HashString(string value)
            {
                return value != null ? value.GetHashCode() : 0;
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
