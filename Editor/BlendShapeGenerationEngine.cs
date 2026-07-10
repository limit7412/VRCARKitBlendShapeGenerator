using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ARKitBlendShapeGenerator
{
    internal sealed class BlendShapeGenerationOptions
    {
        public float IntensityMultiplier { get; set; } = 1.0f;
        public bool EnableLeftRightSplit { get; set; } = true;
        public float BlendWidth { get; set; } = 0.02f;
        public bool OverwriteExisting { get; set; }
        public bool EnableProceduralMouthShapes { get; set; } = true;
        public float ProceduralMouthIntensity { get; set; } = 1.0f;
        public bool Debug { get; set; }

        public static BlendShapeGenerationOptions FromComponent(ARKitBlendShapeGeneratorComponent component)
        {
            return new BlendShapeGenerationOptions
            {
                IntensityMultiplier = component.intensityMultiplier,
                EnableLeftRightSplit = component.enableLeftRightSplit,
                BlendWidth = component.blendWidth,
                OverwriteExisting = component.overwriteExisting,
                EnableProceduralMouthShapes = component.enableProceduralMouthShapes,
                ProceduralMouthIntensity = component.proceduralMouthIntensity,
                Debug = component.debugMode
            };
        }
    }

    internal sealed class BlendShapeGenerationResult
    {
        public List<string> GeneratedShapes { get; }
        public Dictionary<string, int> ShapeIndices { get; }

        public BlendShapeGenerationResult(List<string> generatedShapes, Dictionary<string, int> shapeIndices)
        {
            GeneratedShapes = generatedShapes;
            ShapeIndices = shapeIndices;
        }
    }

    internal static class BlendShapeGenerationEngine
    {
        private sealed class BlendShapeFrameData
        {
            public readonly float Weight;
            public readonly Vector3[] DeltaVertices;
            public readonly Vector3[] DeltaNormals;
            public readonly Vector3[] DeltaTangents;

            public BlendShapeFrameData(
                float weight,
                Vector3[] deltaVertices,
                Vector3[] deltaNormals,
                Vector3[] deltaTangents)
            {
                Weight = weight;
                DeltaVertices = deltaVertices;
                DeltaNormals = deltaNormals;
                DeltaTangents = deltaTangents;
            }
        }

        private sealed class BlendShapeData
        {
            public readonly string Name;
            public readonly List<BlendShapeFrameData> Frames;

            public BlendShapeData(string name, List<BlendShapeFrameData> frames)
            {
                Name = name;
                Frames = frames;
            }
        }

        private sealed class PlannedBlendShape
        {
            public readonly string ArkitName;
            public readonly List<(int index, float weight, BlendShapeSide side)> Sources;

            public PlannedBlendShape(string arkitName, List<(int index, float weight, BlendShapeSide side)> sources)
            {
                ArkitName = arkitName;
                Sources = sources;
            }
        }

        public static BlendShapeGenerationResult Generate(
            Mesh sourceMesh,
            Mesh targetMesh,
            List<CustomBlendShapeMapping> customMappings,
            List<ARKitMapping> autoMappings,
            BlendShapeGenerationOptions options)
        {
            if (sourceMesh == null || targetMesh == null)
            {
                return new BlendShapeGenerationResult(
                    new List<string>(),
                    new Dictionary<string, int>());
            }

            if (options == null)
            {
                options = new BlendShapeGenerationOptions();
            }

            if (customMappings == null)
            {
                customMappings = new List<CustomBlendShapeMapping>();
            }

            if (autoMappings == null)
            {
                autoMappings = new List<ARKitMapping>();
            }

            if (CustomMappingValidation.HasDuplicateArkitNames(customMappings, out var duplicateArkitNames))
            {
                Debug.LogError(
                    "[ARKitGenerator] カスタムマッピングで同一ARKit名が重複しているため、生成を中止しました。\n" +
                    $"重複: {string.Join(", ", duplicateArkitNames)}");
                return new BlendShapeGenerationResult(
                    new List<string>(),
                    new Dictionary<string, int>());
            }

            var existingShapes = new Dictionary<string, int>();
            for (int i = 0; i < sourceMesh.blendShapeCount; i++)
            {
                existingShapes[sourceMesh.GetBlendShapeName(i)] = i;
            }

            var generatedShapes = new List<string>();
            var customMappedNames = new HashSet<string>();
            var plannedBlendShapes = new List<PlannedBlendShape>();

            CollectCustomMappings(
                sourceMesh,
                customMappings,
                options,
                existingShapes,
                customMappedNames,
                plannedBlendShapes);

            CollectAutoMappings(
                sourceMesh,
                autoMappings,
                options,
                existingShapes,
                customMappedNames,
                plannedBlendShapes);

            if (options.OverwriteExisting && plannedBlendShapes.Count > 0)
            {
                var namesToReplace = new HashSet<string>(
                    plannedBlendShapes.Select(planned => planned.ArkitName));
                namesToReplace.IntersectWith(GetExistingBlendShapeNames(targetMesh));

                if (namesToReplace.Count > 0)
                {
                    int removedCount = RemoveBlendShapesByNames(targetMesh, namesToReplace);
                    if (removedCount > 0)
                    {
                        Log(options, $"Replaced existing blendshapes: {string.Join(", ", namesToReplace.OrderBy(name => name))}");
                    }
                }
            }

            foreach (var planned in plannedBlendShapes)
            {
                if (TryAddBlendShape(sourceMesh, targetMesh, planned.ArkitName, planned.Sources, options))
                {
                    existingShapes[planned.ArkitName] = targetMesh.blendShapeCount - 1;
                    generatedShapes.Add(planned.ArkitName);
                }
            }

            if (options.EnableProceduralMouthShapes)
            {
                GenerateProceduralMouthShapes(sourceMesh, targetMesh, options, generatedShapes);
            }

            // 生成・削除後の最終状態からインデックスを再構築する
            var shapeIndices = new Dictionary<string, int>();
            for (int i = 0; i < targetMesh.blendShapeCount; i++)
            {
                var shapeName = targetMesh.GetBlendShapeName(i);
                if (!string.IsNullOrEmpty(shapeName))
                {
                    shapeIndices[shapeName] = i;
                }
            }

            return new BlendShapeGenerationResult(generatedShapes, shapeIndices);
        }

        private static void GenerateProceduralMouthShapes(
            Mesh sourceMesh,
            Mesh targetMesh,
            BlendShapeGenerationOptions options,
            List<string> generatedShapes)
        {
            if (sourceMesh.vertexCount != targetMesh.vertexCount)
            {
                Log(options, "Skip procedural (vertex count mismatch)");
                return;
            }

            var generatedNames = new HashSet<string>(generatedShapes);
            var targetExistingNames = GetExistingBlendShapeNames(targetMesh);
            var namesToGenerate = new List<string>();
            var namesToReplace = new HashSet<string>();

            foreach (var arkitName in ProceduralMouthShapeGenerator.TargetShapeNames)
            {
                // 既存シェイプキーからの生成が成立している場合はそちらを優先
                if (generatedNames.Contains(arkitName))
                {
                    continue;
                }

                if (targetExistingNames.Contains(arkitName))
                {
                    if (!options.OverwriteExisting)
                    {
                        Log(options, $"Skip procedural (exists): {arkitName}");
                        continue;
                    }

                    namesToReplace.Add(arkitName);
                }

                namesToGenerate.Add(arkitName);
            }

            if (namesToGenerate.Count == 0)
            {
                return;
            }

            if (!ProceduralMouthShapeGenerator.TryCreateContext(sourceMesh, out var context))
            {
                Log(options, "Skip procedural (mouth region not found)");
                return;
            }

            // 生成できなかったシェイプの既存データを消さないよう、先にデルタを確定させる
            var plannedDeltas = new List<(string arkitName, Vector3[] deltaVertices)>();
            foreach (var arkitName in namesToGenerate)
            {
                if (ProceduralMouthShapeGenerator.TryBuildDeltaVertices(context, arkitName, options, out var deltaVertices))
                {
                    plannedDeltas.Add((arkitName, deltaVertices));
                }
                else
                {
                    namesToReplace.Remove(arkitName);
                }
            }

            if (plannedDeltas.Count == 0)
            {
                return;
            }

            if (namesToReplace.Count > 0)
            {
                int removedCount = RemoveBlendShapesByNames(targetMesh, namesToReplace);
                if (removedCount > 0)
                {
                    Log(options, $"Replaced existing blendshapes (procedural): {string.Join(", ", namesToReplace.OrderBy(name => name))}");
                }
            }

            foreach (var (arkitName, deltaVertices) in plannedDeltas)
            {
                // 平行移動のみのため法線・接線のデルタは不要（nullで省略可能）
                targetMesh.AddBlendShapeFrame(
                    arkitName,
                    100f,
                    deltaVertices,
                    null,
                    null);
                generatedShapes.Add(arkitName);
                Log(options, $"Generated (procedural): {arkitName}");
            }
        }

        private static void CollectCustomMappings(
            Mesh sourceMesh,
            List<CustomBlendShapeMapping> customMappings,
            BlendShapeGenerationOptions options,
            Dictionary<string, int> existingShapes,
            HashSet<string> customMappedNames,
            List<PlannedBlendShape> plannedBlendShapes)
        {
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

                customMappedNames.Add(mapping.arkitName);

                if (existingShapes.ContainsKey(mapping.arkitName) && !options.OverwriteExisting)
                {
                    Log(options, $"Skip custom (exists): {mapping.arkitName}");
                    continue;
                }

                var sources = new List<(int index, float weight, BlendShapeSide side)>();
                foreach (var source in mapping.sources)
                {
                    if (source == null || string.IsNullOrEmpty(source.blendShapeName))
                    {
                        continue;
                    }

                    if (TryGetSourceIndex(existingShapes, sourceMesh, source.blendShapeName, out int srcIndex))
                    {
                        sources.Add((srcIndex, source.weight, source.side));
                    }
                    else
                    {
                        Log(options, $"Warning: Source not found: {source.blendShapeName} for {mapping.arkitName}");
                    }
                }

                if (sources.Count == 0)
                {
                    Log(options, $"Skip custom (no valid source): {mapping.arkitName}");
                    continue;
                }

                plannedBlendShapes.Add(new PlannedBlendShape(mapping.arkitName, sources));
            }
        }

        private static void CollectAutoMappings(
            Mesh sourceMesh,
            List<ARKitMapping> autoMappings,
            BlendShapeGenerationOptions options,
            Dictionary<string, int> existingShapes,
            HashSet<string> customMappedNames,
            List<PlannedBlendShape> plannedBlendShapes)
        {
            var processedArkitNames = new HashSet<string>();

            foreach (var mapping in autoMappings)
            {
                if (mapping == null || string.IsNullOrEmpty(mapping.arkitName) || mapping.sources == null)
                {
                    continue;
                }

                if (customMappedNames.Contains(mapping.arkitName))
                {
                    Log(options, $"Skip auto (custom defined): {mapping.arkitName}");
                    continue;
                }

                if (processedArkitNames.Contains(mapping.arkitName))
                {
                    Log(options, $"Skip auto (already generated in this pass): {mapping.arkitName}");
                    continue;
                }

                if (existingShapes.ContainsKey(mapping.arkitName) && !options.OverwriteExisting)
                {
                    Log(options, $"Skip auto (exists in source): {mapping.arkitName}");
                    continue;
                }

                var sources = FindAutoSources(mapping.sources, existingShapes, sourceMesh);
                if (sources.Count == 0)
                {
                    Log(options, $"Skip auto (no source): {mapping.arkitName}");
                    continue;
                }

                var side = options.EnableLeftRightSplit ? mapping.side : BlendShapeSide.Both;
                var sourcesWithSide = sources.Select(s => (s.index, s.weight, side)).ToList();

                plannedBlendShapes.Add(new PlannedBlendShape(mapping.arkitName, sourcesWithSide));
                processedArkitNames.Add(mapping.arkitName);
            }
        }

        private static List<(int index, float weight)> FindAutoSources(
            List<SourceMapping> sourceMappings,
            Dictionary<string, int> existingShapes,
            Mesh sourceMesh)
        {
            var result = new List<(int index, float weight)>();

            foreach (var sourceMapping in sourceMappings)
            {
                if (sourceMapping == null || sourceMapping.names == null)
                {
                    continue;
                }

                foreach (var name in sourceMapping.names)
                {
                    if (TryGetSourceIndex(existingShapes, sourceMesh, name, out int srcIndex))
                    {
                        result.Add((srcIndex, sourceMapping.weight));
                        break;
                    }
                }
            }

            return result;
        }

        private static bool TryGetSourceIndex(
            Dictionary<string, int> existingShapes,
            Mesh sourceMesh,
            string sourceName,
            out int srcIndex)
        {
            srcIndex = -1;
            if (string.IsNullOrEmpty(sourceName))
            {
                return false;
            }

            if (!existingShapes.TryGetValue(sourceName, out int index))
            {
                return false;
            }

            if (index < 0 || index >= sourceMesh.blendShapeCount)
            {
                return false;
            }

            srcIndex = index;
            return true;
        }

        private static bool TryAddBlendShape(
            Mesh sourceMesh,
            Mesh targetMesh,
            string arkitName,
            List<(int index, float weight, BlendShapeSide side)> sources,
            BlendShapeGenerationOptions options)
        {
            int vertexCount = sourceMesh.vertexCount;
            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];
            var vertices = sourceMesh.vertices;

            int sourceCount = 0;
            float blendWidth = Mathf.Max(0.0001f, options.BlendWidth);

            foreach (var (index, weight, side) in sources)
            {
                if (index < 0 || index >= sourceMesh.blendShapeCount)
                {
                    continue;
                }

                int frameCount = sourceMesh.GetBlendShapeFrameCount(index);
                if (frameCount == 0)
                {
                    continue;
                }

                var srcDeltaV = new Vector3[vertexCount];
                var srcDeltaN = new Vector3[vertexCount];
                var srcDeltaT = new Vector3[vertexCount];

                int targetFrame = frameCount - 1;
                sourceMesh.GetBlendShapeFrameVertices(index, targetFrame, srcDeltaV, srcDeltaN, srcDeltaT);

                float adjustedWeight = weight * options.IntensityMultiplier;
                for (int i = 0; i < vertexCount; i++)
                {
                    float sideMultiplier = 1.0f;
                    if (options.EnableLeftRightSplit && side != BlendShapeSide.Both)
                    {
                        sideMultiplier = CalculateSideMultiplier(vertices[i].x, side, blendWidth);
                    }

                    if (sideMultiplier > 0.0f)
                    {
                        float finalWeight = adjustedWeight * sideMultiplier;
                        deltaVertices[i] += srcDeltaV[i] * finalWeight;
                        deltaNormals[i] += srcDeltaN[i] * finalWeight;
                        deltaTangents[i] += srcDeltaT[i] * finalWeight;
                    }
                }

                sourceCount++;
            }

            if (sourceCount == 0)
            {
                return false;
            }

            targetMesh.AddBlendShapeFrame(arkitName, 100f, deltaVertices, deltaNormals, deltaTangents);
            Log(options, $"Generated: {arkitName} from {sourceCount} source(s)");
            return true;
        }

        /// <summary>
        /// 左右分割時の頂点ごとの適用係数を算出する（中央付近はblendWidthの範囲でグラデーション）
        /// </summary>
        internal static float CalculateSideMultiplier(float vertexX, BlendShapeSide side, float blendWidth)
        {
            if (side == BlendShapeSide.LeftOnly)
            {
                if (vertexX > blendWidth)
                {
                    return 0.0f;
                }

                if (vertexX > -blendWidth)
                {
                    return (blendWidth - vertexX) / (blendWidth * 2.0f);
                }

                return 1.0f;
            }

            if (side == BlendShapeSide.RightOnly)
            {
                if (vertexX < -blendWidth)
                {
                    return 0.0f;
                }

                if (vertexX < blendWidth)
                {
                    return (vertexX + blendWidth) / (blendWidth * 2.0f);
                }

                return 1.0f;
            }

            return 1.0f;
        }

        private static HashSet<string> GetExistingBlendShapeNames(Mesh mesh)
        {
            var result = new HashSet<string>();
            if (mesh == null)
            {
                return result;
            }

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var shapeName = mesh.GetBlendShapeName(i);
                if (!string.IsNullOrEmpty(shapeName))
                {
                    result.Add(shapeName);
                }
            }

            return result;
        }

        private static int RemoveBlendShapesByNames(Mesh mesh, HashSet<string> shapeNamesToRemove)
        {
            if (mesh == null || shapeNamesToRemove == null || shapeNamesToRemove.Count == 0)
            {
                return 0;
            }

            int blendShapeCount = mesh.blendShapeCount;
            if (blendShapeCount == 0)
            {
                return 0;
            }

            int vertexCount = mesh.vertexCount;
            int removedCount = 0;
            var preserved = new List<BlendShapeData>(blendShapeCount);

            for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
            {
                string existingName = mesh.GetBlendShapeName(shapeIndex);
                if (string.IsNullOrEmpty(existingName))
                {
                    continue;
                }

                if (shapeNamesToRemove.Contains(existingName))
                {
                    removedCount++;
                    continue;
                }

                int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
                var frames = new List<BlendShapeFrameData>(frameCount);
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    float frameWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
                    var deltaVertices = new Vector3[vertexCount];
                    var deltaNormals = new Vector3[vertexCount];
                    var deltaTangents = new Vector3[vertexCount];

                    mesh.GetBlendShapeFrameVertices(
                        shapeIndex,
                        frameIndex,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents);

                    frames.Add(new BlendShapeFrameData(
                        frameWeight,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents));
                }

                preserved.Add(new BlendShapeData(existingName, frames));
            }

            if (removedCount == 0)
            {
                return 0;
            }

            mesh.ClearBlendShapes();
            foreach (var shape in preserved)
            {
                foreach (var frame in shape.Frames)
                {
                    mesh.AddBlendShapeFrame(
                        shape.Name,
                        frame.Weight,
                        frame.DeltaVertices,
                        frame.DeltaNormals,
                        frame.DeltaTangents);
                }
            }

            return removedCount;
        }

        private static void Log(BlendShapeGenerationOptions options, string message)
        {
            if (options != null && options.Debug)
            {
                Debug.Log($"[ARKitGenerator] {message}");
            }
        }
    }
}
