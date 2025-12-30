using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// NDMFプレビューシステム統合
    /// Tools > NDMf > Preview でON/OFFを切り替え可能
    /// </summary>
    public class ARKitBlendShapeGeneratorPreview : IRenderFilter
    {
        /// <summary>
        /// プレビューのON/OFFを制御するノード
        /// NDMFのPreviewメニューに表示される
        /// </summary>
        public static readonly TogglablePreviewNode EnableNode = TogglablePreviewNode.Create(
            () => "ARKit BlendShape Generator",
            qualifiedName: "com.example.arkit-blendshape-generator/Preview",
            initialState: true
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
            var components = context.GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(avatarRoot, true);

            foreach (var component in components)
            {
                if (component == null) continue;

                // 対象のRendererを取得
                var renderer = component.targetRenderer;
                if (renderer == null)
                {
                    // GetComponentInChildrenは存在しないため、GameObjectから直接取得
                    renderer = component.GetComponentInChildren<SkinnedMeshRenderer>(true);
                }

                if (renderer != null && renderer.sharedMesh != null)
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
            var (original, proxy) = proxyPairs.First();
            var component = group.GetData<ARKitBlendShapeGeneratorComponent>();

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
            private readonly ARKitBlendShapeGeneratorComponent _component;
            private readonly SkinnedMeshRenderer _originalRenderer;
            private readonly Mesh _generatedMesh;
            private readonly Dictionary<string, int> _generatedBlendShapeIndices;

            public RenderAspects WhatChanged => RenderAspects.Mesh | RenderAspects.Shapes;

            public PreviewNode(
                ARKitBlendShapeGeneratorComponent component,
                SkinnedMeshRenderer originalRenderer,
                SkinnedMeshRenderer proxyRenderer,
                ComputeContext context)
            {
                _component = component;
                _originalRenderer = originalRenderer;
                _generatedBlendShapeIndices = new Dictionary<string, int>();

                // プレビュー用にBlendShapeを生成
                _generatedMesh = GeneratePreviewMesh(proxyRenderer);

                // プロキシにメッシュを適用
                proxyRenderer.sharedMesh = _generatedMesh;
            }

            private Mesh GeneratePreviewMesh(SkinnedMeshRenderer proxyRenderer)
            {
                var originalMesh = _originalRenderer.sharedMesh;
                var mesh = Object.Instantiate(originalMesh);
                mesh.name = originalMesh.name + "_ARKitPreview";

                // 既存のBlendShapeをインデックス化
                var existingShapes = new Dictionary<string, int>();
                for (int i = 0; i < originalMesh.blendShapeCount; i++)
                {
                    existingShapes[originalMesh.GetBlendShapeName(i)] = i;
                }

                // カスタムマッピングからBlendShapeを生成
                if (_component.customMappings != null)
                {
                    foreach (var mapping in _component.customMappings)
                    {
                        if (!mapping.enabled || string.IsNullOrEmpty(mapping.arkitName))
                            continue;

                        if (mapping.sources == null || mapping.sources.Count == 0)
                            continue;

                        // 既に存在し、上書きしない場合はスキップ
                        if (existingShapes.ContainsKey(mapping.arkitName) && !_component.overwriteExisting)
                            continue;

                        // BlendShapeを生成
                        GenerateBlendShapeForMapping(mesh, originalMesh, existingShapes, mapping);
                    }
                }

                return mesh;
            }

            private void GenerateBlendShapeForMapping(
                Mesh targetMesh,
                Mesh sourceMesh,
                Dictionary<string, int> existingShapes,
                CustomBlendShapeMapping mapping)
            {
                int vertexCount = sourceMesh.vertexCount;
                var deltaVertices = new Vector3[vertexCount];
                var deltaNormals = new Vector3[vertexCount];
                var deltaTangents = new Vector3[vertexCount];
                var vertices = sourceMesh.vertices;

                int sourceCount = 0;

                foreach (var source in mapping.sources)
                {
                    if (string.IsNullOrEmpty(source.blendShapeName))
                        continue;

                    if (!existingShapes.TryGetValue(source.blendShapeName, out int srcIndex))
                        continue;

                    var srcDeltaV = new Vector3[vertexCount];
                    var srcDeltaN = new Vector3[vertexCount];
                    var srcDeltaT = new Vector3[vertexCount];

                    int frameCount = sourceMesh.GetBlendShapeFrameCount(srcIndex);
                    if (frameCount == 0)
                        continue;

                    int targetFrame = frameCount > 0 ? frameCount - 1 : 0;
                    sourceMesh.GetBlendShapeFrameVertices(srcIndex, targetFrame, srcDeltaV, srcDeltaN, srcDeltaT);

                    float adjustedWeight = source.weight * _component.intensityMultiplier;

                    for (int i = 0; i < vertexCount; i++)
                    {
                        // 左右フィルタリング（enableLeftRightSplitがtrueの場合のみ）
                        // ARKitは視聴者視点（アバターを見ている人の視点）で左右を定義:
                        // eyeBlinkLeft = 視聴者の左 = アバターの右側 = X < 0
                        // eyeBlinkRight = 視聴者の右 = アバターの左側 = X > 0
                        float sideMultiplier = 1.0f;
                        if (_component.enableLeftRightSplit && source.side != BlendShapeSide.Both)
                        {
                            float vertexX = vertices[i].x;
                            float blendWidth = _component.blendWidth;

                            if (source.side == BlendShapeSide.LeftOnly)
                            {
                                // ARKit Left = 視聴者の左 = アバターの右側 = X < 0
                                if (vertexX > blendWidth)
                                {
                                    sideMultiplier = 0.0f;
                                }
                                else if (vertexX > -blendWidth)
                                {
                                    sideMultiplier = (blendWidth - vertexX) / (blendWidth * 2);
                                }
                            }
                            else if (source.side == BlendShapeSide.RightOnly)
                            {
                                // ARKit Right = 視聴者の右 = アバターの左側 = X > 0
                                if (vertexX < -blendWidth)
                                {
                                    sideMultiplier = 0.0f;
                                }
                                else if (vertexX < blendWidth)
                                {
                                    sideMultiplier = (vertexX + blendWidth) / (blendWidth * 2);
                                }
                            }
                        }

                        if (sideMultiplier > 0)
                        {
                            float finalWeight = adjustedWeight * sideMultiplier;
                            deltaVertices[i] += srcDeltaV[i] * finalWeight;
                            deltaNormals[i] += srcDeltaN[i] * finalWeight;
                            deltaTangents[i] += srcDeltaT[i] * finalWeight;
                        }
                    }

                    sourceCount++;
                }

                if (sourceCount > 0)
                {
                    targetMesh.AddBlendShapeFrame(mapping.arkitName, 100f, deltaVertices, deltaNormals, deltaTangents);
                    _generatedBlendShapeIndices[mapping.arkitName] = targetMesh.blendShapeCount - 1;
                }
            }

            public Task<IRenderFilterNode> Refresh(
                IEnumerable<(Renderer, Renderer)> proxyPairs,
                ComputeContext context,
                RenderAspects updatedAspects)
            {
                // メッシュが変わった場合は再生成が必要
                if ((updatedAspects & RenderAspects.Mesh) != 0)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                return Task.FromResult<IRenderFilterNode>(this);
            }

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (proxy is not SkinnedMeshRenderer proxySmr) return;

                // プロキシのメッシュが正しいか確認
                if (proxySmr.sharedMesh != _generatedMesh)
                {
                    proxySmr.sharedMesh = _generatedMesh;
                }
            }

            public void Dispose()
            {
                if (_generatedMesh != null)
                {
                    Object.DestroyImmediate(_generatedMesh);
                }
            }
        }
    }
}
