using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Domain
{
    internal sealed class BlendShapeGenerationOptions
    {
        public float IntensityMultiplier { get; set; } = 1.0f;
        public bool EnableLeftRightSplit { get; set; } = true;
        public float BlendWidth { get; set; } = 0.02f;
        public bool OverwriteExisting { get; set; }
        public bool EnableProceduralMouthShapes { get; set; }
        public float ProceduralMouthIntensity { get; set; } = 1.0f;
        public bool EnableMouthCancellation { get; set; }
        public List<BlendShapeSource> MouthCancellationSources { get; set; }
        public float MouthCancellationStrength { get; set; } = 1.0f;
        public HashSet<string> MouthCancellationTargets { get; set; }
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
                EnableMouthCancellation = component.enableMouthCancellation,
                MouthCancellationSources = component.mouthCancellationSources,
                MouthCancellationStrength = component.mouthCancellationStrength,
                MouthCancellationTargets = BuildTargetSet(component.mouthCancellationTargets),
                Debug = component.debugMode
            };
        }

        /// <summary>
        /// 打ち消し対象のARKit名の集合を作る。
        /// 名前は加工せず、AppliesToでの照合も完全一致で行う（ここでTrimすると
        /// インスペクタの選択状態と実際の打ち消し対象が食い違う）。空白のみは未設定として無視する。
        /// </summary>
        private static HashSet<string> BuildTargetSet(List<string> targets)
        {
            var result = new HashSet<string>();
            if (targets == null)
            {
                return result;
            }

            foreach (var target in targets)
            {
                if (!string.IsNullOrWhiteSpace(target))
                {
                    result.Add(target);
                }
            }

            return result;
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

        /// <summary>
        /// 生成した口関連BlendShapeに焼き込む打ち消し用のデルタ（対象BlendShapeの逆方向の変形）
        /// </summary>
        private sealed class MouthCancellationDelta
        {
            public readonly Vector3[] DeltaVertices;
            public readonly Vector3[] DeltaNormals;
            public readonly Vector3[] DeltaTangents;
            public readonly HashSet<string> TargetArkitNames;

            public MouthCancellationDelta(
                Vector3[] deltaVertices,
                Vector3[] deltaNormals,
                Vector3[] deltaTangents,
                HashSet<string> targetArkitNames)
            {
                DeltaVertices = deltaVertices;
                DeltaNormals = deltaNormals;
                DeltaTangents = deltaTangents;
                TargetArkitNames = targetArkitNames;
            }

            public bool AppliesTo(string arkitName)
            {
                return !string.IsNullOrEmpty(arkitName) && TargetArkitNames.Contains(arkitName);
            }
        }

        public static BlendShapeGenerationResult Generate(
            IMeshRepository sourceMesh,
            IMeshRepository targetMesh,
            List<CustomBlendShapeMapping> customMappings,
            List<ARKitMapping> autoMappings,
            BlendShapeGenerationOptions options,
            IGenerationLogger logger)
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
                logger?.Error(S("log.duplicate_abort", string.Join(", ", duplicateArkitNames)));
                return new BlendShapeGenerationResult(
                    new List<string>(),
                    new Dictionary<string, int>());
            }

            // 空名シェイプは生成対象にも照合キーにもしない（キー""での偶発的な一致を避ける）
            var existingShapes = new Dictionary<string, int>();
            for (int i = 0; i < sourceMesh.BlendShapeCount; i++)
            {
                var existingName = sourceMesh.GetBlendShapeName(i);
                if (!string.IsNullOrEmpty(existingName))
                {
                    existingShapes[existingName] = i;
                }
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
                plannedBlendShapes,
                logger);

            CollectAutoMappings(
                sourceMesh,
                autoMappings,
                options,
                existingShapes,
                customMappedNames,
                plannedBlendShapes,
                logger);

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
                        Log(logger, options, $"Replaced existing blendshapes: {string.Join(", ", namesToReplace.OrderBy(name => name))}");
                    }
                }
            }

            var cancellation = BuildMouthCancellationDelta(sourceMesh, existingShapes, options, logger);

            foreach (var planned in plannedBlendShapes)
            {
                if (TryAddBlendShape(
                        sourceMesh,
                        targetMesh,
                        planned.ArkitName,
                        planned.Sources,
                        options,
                        cancellation,
                        logger))
                {
                    existingShapes[planned.ArkitName] = targetMesh.BlendShapeCount - 1;
                    generatedShapes.Add(planned.ArkitName);
                }
            }

            if (options.EnableProceduralMouthShapes)
            {
                GenerateProceduralMouthShapes(
                    sourceMesh,
                    targetMesh,
                    options,
                    customMappedNames,
                    generatedShapes,
                    cancellation,
                    logger);
            }

            // 生成・削除後の最終状態からインデックスを再構築する
            // 空名シェイプは名前で引けないためエントリを持たないが、iは実メッシュ上の位置のままなので
            // 非空シェイプのインデックスは常に実体と一致する
            var shapeIndices = new Dictionary<string, int>();
            for (int i = 0; i < targetMesh.BlendShapeCount; i++)
            {
                var shapeName = targetMesh.GetBlendShapeName(i);
                if (!string.IsNullOrEmpty(shapeName))
                {
                    shapeIndices[shapeName] = i;
                }
            }

            return new BlendShapeGenerationResult(generatedShapes, shapeIndices);
        }

        /// <summary>
        /// 打ち消し対象BlendShapeの逆方向デルタを合成する。
        /// 対象や強度が未設定の場合はnull（打ち消しなし）。
        /// </summary>
        private static MouthCancellationDelta BuildMouthCancellationDelta(
            IMeshRepository sourceMesh,
            Dictionary<string, int> existingShapes,
            BlendShapeGenerationOptions options,
            IGenerationLogger logger)
        {
            if (!options.EnableMouthCancellation)
            {
                return null;
            }

            if (options.MouthCancellationSources == null || options.MouthCancellationSources.Count == 0)
            {
                return null;
            }

            var targets = options.MouthCancellationTargets;
            if (targets == null || targets.Count == 0)
            {
                Log(logger, options, "Skip cancellation (no target shape selected)");
                return null;
            }

            float strength = Mathf.Clamp01(options.MouthCancellationStrength);
            if (strength <= Mathf.Epsilon)
            {
                return null;
            }

            int vertexCount = sourceMesh.VertexCount;
            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];
            var vertices = sourceMesh.GetVertices();
            float blendWidth = Mathf.Max(0.0001f, options.BlendWidth);
            bool hasDelta = false;

            foreach (var source in options.MouthCancellationSources)
            {
                if (source == null || string.IsNullOrEmpty(source.blendShapeName))
                {
                    continue;
                }

                if (!TryGetSourceIndex(existingShapes, sourceMesh, source.blendShapeName, out int srcIndex))
                {
                    Log(logger, options, $"Warning: Cancellation source not found: {source.blendShapeName}");
                    continue;
                }

                // アバター側での適用ウェイト（1.0 = ウェイト100）時点の変形を打ち消す
                float targetWeight = source.weight * 100f;
                if (Mathf.Abs(targetWeight) <= Mathf.Epsilon)
                {
                    continue;
                }

                var srcDeltaV = new Vector3[vertexCount];
                var srcDeltaN = new Vector3[vertexCount];
                var srcDeltaT = new Vector3[vertexCount];
                if (!TryEvaluateBlendShapeAtWeight(
                        sourceMesh,
                        srcIndex,
                        targetWeight,
                        srcDeltaV,
                        srcDeltaN,
                        srcDeltaT))
                {
                    continue;
                }

                // 評価済みの変形をそのまま反転する（生成強度 IntensityMultiplier は掛けない）
                float adjustedWeight = -strength;

                for (int i = 0; i < vertexCount; i++)
                {
                    // 打ち消し元の左右指定はアバター側での適用範囲を表すため、生成側の左右分割設定とは独立に適用する
                    float sideMultiplier = source.side != BlendShapeSide.Both
                        ? CalculateSideMultiplier(vertices[i].x, source.side, blendWidth)
                        : 1.0f;

                    if (sideMultiplier <= 0.0f)
                    {
                        continue;
                    }

                    float finalWeight = adjustedWeight * sideMultiplier;
                    deltaVertices[i] += srcDeltaV[i] * finalWeight;
                    deltaNormals[i] += srcDeltaN[i] * finalWeight;
                    deltaTangents[i] += srcDeltaT[i] * finalWeight;
                }

                hasDelta = true;
            }

            if (!hasDelta)
            {
                Log(logger, options, "Skip cancellation (no valid source)");
                return null;
            }

            Log(logger, options, $"Cancellation targets: {string.Join(", ", targets.OrderBy(name => name))}");
            return new MouthCancellationDelta(
                deltaVertices,
                deltaNormals,
                deltaTangents,
                new HashSet<string>(targets));
        }

        /// <summary>
        /// 指定ウェイト（0-100基準）時点のBlendShapeの変形を、フレーム間を補間して取得する。
        /// フレームを持たない場合はfalse。
        /// </summary>
        private static bool TryEvaluateBlendShapeAtWeight(
            IMeshRepository sourceMesh,
            int shapeIndex,
            float targetWeight,
            Vector3[] deltaVertices,
            Vector3[] deltaNormals,
            Vector3[] deltaTangents)
        {
            int frameCount = sourceMesh.GetBlendShapeFrameCount(shapeIndex);
            if (frameCount == 0)
            {
                return false;
            }

            // フレームウェイトは昇順のため、目標ウェイト以上になる最初のフレームが上側の境界になる
            int upperIndex = frameCount - 1;
            for (int i = 0; i < frameCount; i++)
            {
                if (sourceMesh.GetBlendShapeFrameWeight(shapeIndex, i) >= targetWeight)
                {
                    upperIndex = i;
                    break;
                }
            }

            float upperWeight = sourceMesh.GetBlendShapeFrameWeight(shapeIndex, upperIndex);
            sourceMesh.GetBlendShapeFrameVertices(shapeIndex, upperIndex, deltaVertices, deltaNormals, deltaTangents);

            int vertexCount = sourceMesh.VertexCount;

            if (upperIndex == 0)
            {
                // 最小ウェイトのフレームより下は、変形なし（0）との線形補間になる
                // （負ウェイトのフレームも扱えるよう、分母の判定は絶対値で行う）
                float scale = Mathf.Abs(upperWeight) > Mathf.Epsilon ? targetWeight / upperWeight : 0f;
                if (!Mathf.Approximately(scale, 1f))
                {
                    for (int i = 0; i < vertexCount; i++)
                    {
                        deltaVertices[i] *= scale;
                        deltaNormals[i] *= scale;
                        deltaTangents[i] *= scale;
                    }
                }

                return true;
            }

            float lowerWeight = sourceMesh.GetBlendShapeFrameWeight(shapeIndex, upperIndex - 1);
            float range = upperWeight - lowerWeight;
            if (range <= Mathf.Epsilon)
            {
                return true;
            }

            var lowerDeltaV = new Vector3[vertexCount];
            var lowerDeltaN = new Vector3[vertexCount];
            var lowerDeltaT = new Vector3[vertexCount];
            sourceMesh.GetBlendShapeFrameVertices(shapeIndex, upperIndex - 1, lowerDeltaV, lowerDeltaN, lowerDeltaT);

            // 最終フレームを超えるウェイトはクランプせず、Unityの評価に合わせて外挿する
            float t = (targetWeight - lowerWeight) / range;
            for (int i = 0; i < vertexCount; i++)
            {
                deltaVertices[i] = lowerDeltaV[i] + ((deltaVertices[i] - lowerDeltaV[i]) * t);
                deltaNormals[i] = lowerDeltaN[i] + ((deltaNormals[i] - lowerDeltaN[i]) * t);
                deltaTangents[i] = lowerDeltaT[i] + ((deltaTangents[i] - lowerDeltaT[i]) * t);
            }

            return true;
        }

        private static void GenerateProceduralMouthShapes(
            IMeshRepository sourceMesh,
            IMeshRepository targetMesh,
            BlendShapeGenerationOptions options,
            HashSet<string> customMappedNames,
            List<string> generatedShapes,
            MouthCancellationDelta cancellation,
            IGenerationLogger logger)
        {
            if (sourceMesh.VertexCount != targetMesh.VertexCount)
            {
                Log(logger, options, "Skip procedural (vertex count mismatch)");
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

                // カスタムマッピングで定義済みの名前はユーザー設定を尊重して対象外にする
                // （ソース未検出等で生成に失敗した場合もフォールバックしない）
                if (customMappedNames != null && customMappedNames.Contains(arkitName))
                {
                    Log(logger, options, $"Skip procedural (custom defined): {arkitName}");
                    continue;
                }

                if (targetExistingNames.Contains(arkitName))
                {
                    if (!options.OverwriteExisting)
                    {
                        Log(logger, options, $"Skip procedural (exists): {arkitName}");
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
                Log(logger, options, "Skip procedural (mouth region not found)");
                return;
            }

            // 生成できなかったシェイプの既存データを消さないよう、先にデルタを確定させる
            var plannedDeltas = new List<(string arkitName, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents)>();
            foreach (var arkitName in namesToGenerate)
            {
                if (!ProceduralMouthShapeGenerator.TryBuildDeltaVertices(context, arkitName, options, out var deltaVertices))
                {
                    namesToReplace.Remove(arkitName);
                    continue;
                }

                // 手続き的生成は平行移動のみのため、打ち消しを焼き込まない限り法線・接線のデルタは不要
                Vector3[] deltaNormals = null;
                Vector3[] deltaTangents = null;
                if (cancellation != null && cancellation.AppliesTo(arkitName))
                {
                    deltaNormals = new Vector3[deltaVertices.Length];
                    deltaTangents = new Vector3[deltaVertices.Length];
                    for (int i = 0; i < deltaVertices.Length; i++)
                    {
                        deltaVertices[i] += cancellation.DeltaVertices[i];
                        deltaNormals[i] = cancellation.DeltaNormals[i];
                        deltaTangents[i] = cancellation.DeltaTangents[i];
                    }

                    Log(logger, options, $"Applied cancellation (procedural): {arkitName}");
                }

                plannedDeltas.Add((arkitName, deltaVertices, deltaNormals, deltaTangents));
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
                    Log(logger, options, $"Replaced existing blendshapes (procedural): {string.Join(", ", namesToReplace.OrderBy(name => name))}");
                }
            }

            foreach (var (arkitName, deltaVertices, deltaNormals, deltaTangents) in plannedDeltas)
            {
                targetMesh.AddBlendShapeFrame(
                    arkitName,
                    100f,
                    deltaVertices,
                    deltaNormals,
                    deltaTangents);
                generatedShapes.Add(arkitName);
                Log(logger, options, $"Generated (procedural): {arkitName}");
            }
        }

        private static void CollectCustomMappings(
            IMeshRepository sourceMesh,
            List<CustomBlendShapeMapping> customMappings,
            BlendShapeGenerationOptions options,
            Dictionary<string, int> existingShapes,
            HashSet<string> customMappedNames,
            List<PlannedBlendShape> plannedBlendShapes,
            IGenerationLogger logger)
        {
            foreach (var mapping in customMappings)
            {
                // 生成する名前として使うため、空白のみは未設定として扱う
                // （重複判定側も空白のみを無視するので、判定を素通りして生成されるのを防ぐ）
                if (mapping == null || !mapping.enabled || string.IsNullOrWhiteSpace(mapping.arkitName))
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
                    Log(logger, options, $"Skip custom (exists): {mapping.arkitName}");
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
                        Log(logger, options, $"Warning: Source not found: {source.blendShapeName} for {mapping.arkitName}");
                    }
                }

                if (sources.Count == 0)
                {
                    Log(logger, options, $"Skip custom (no valid source): {mapping.arkitName}");
                    continue;
                }

                plannedBlendShapes.Add(new PlannedBlendShape(mapping.arkitName, sources));
            }
        }

        private static void CollectAutoMappings(
            IMeshRepository sourceMesh,
            List<ARKitMapping> autoMappings,
            BlendShapeGenerationOptions options,
            Dictionary<string, int> existingShapes,
            HashSet<string> customMappedNames,
            List<PlannedBlendShape> plannedBlendShapes,
            IGenerationLogger logger)
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
                    Log(logger, options, $"Skip auto (custom defined): {mapping.arkitName}");
                    continue;
                }

                if (processedArkitNames.Contains(mapping.arkitName))
                {
                    Log(logger, options, $"Skip auto (already generated in this pass): {mapping.arkitName}");
                    continue;
                }

                if (existingShapes.ContainsKey(mapping.arkitName) && !options.OverwriteExisting)
                {
                    Log(logger, options, $"Skip auto (exists in source): {mapping.arkitName}");
                    continue;
                }

                var sources = FindAutoSources(mapping.sources, existingShapes, sourceMesh);
                if (sources.Count == 0)
                {
                    Log(logger, options, $"Skip auto (no source): {mapping.arkitName}");
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
            IMeshRepository sourceMesh)
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
            IMeshRepository sourceMesh,
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

            if (index < 0 || index >= sourceMesh.BlendShapeCount)
            {
                return false;
            }

            srcIndex = index;
            return true;
        }

        private static bool TryAddBlendShape(
            IMeshRepository sourceMesh,
            IMeshRepository targetMesh,
            string arkitName,
            List<(int index, float weight, BlendShapeSide side)> sources,
            BlendShapeGenerationOptions options,
            MouthCancellationDelta cancellation,
            IGenerationLogger logger)
        {
            int vertexCount = sourceMesh.VertexCount;
            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];
            var vertices = sourceMesh.GetVertices();

            int sourceCount = 0;
            float blendWidth = Mathf.Max(0.0001f, options.BlendWidth);

            foreach (var (index, weight, side) in sources)
            {
                if (index < 0 || index >= sourceMesh.BlendShapeCount)
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

            // 打ち消しのみのBlendShapeを作らないよう、ソースから生成できた場合だけ焼き込む
            if (cancellation != null && cancellation.AppliesTo(arkitName))
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    deltaVertices[i] += cancellation.DeltaVertices[i];
                    deltaNormals[i] += cancellation.DeltaNormals[i];
                    deltaTangents[i] += cancellation.DeltaTangents[i];
                }

                Log(logger, options, $"Applied cancellation: {arkitName}");
            }

            targetMesh.AddBlendShapeFrame(arkitName, 100f, deltaVertices, deltaNormals, deltaTangents);
            Log(logger, options, $"Generated: {arkitName} from {sourceCount} source(s)");
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

        /// <summary>
        /// メッシュ上の既存BlendShape名を集める。
        /// 空名シェイプは生成・置き換えの対象にならないため含めない（保持自体は削除処理側で担保する）。
        /// </summary>
        private static HashSet<string> GetExistingBlendShapeNames(IMeshRepository mesh)
        {
            var result = new HashSet<string>();
            if (mesh == null)
            {
                return result;
            }

            for (int i = 0; i < mesh.BlendShapeCount; i++)
            {
                var shapeName = mesh.GetBlendShapeName(i);
                if (!string.IsNullOrEmpty(shapeName))
                {
                    result.Add(shapeName);
                }
            }

            return result;
        }

        private static int RemoveBlendShapesByNames(IMeshRepository mesh, HashSet<string> shapeNamesToRemove)
        {
            if (mesh == null || shapeNamesToRemove == null || shapeNamesToRemove.Count == 0)
            {
                return 0;
            }

            int blendShapeCount = mesh.BlendShapeCount;
            if (blendShapeCount == 0)
            {
                return 0;
            }

            int vertexCount = mesh.VertexCount;
            int removedCount = 0;
            var preserved = new List<BlendShapeData>(blendShapeCount);

            for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
            {
                string existingName = mesh.GetBlendShapeName(shapeIndex);

                // 空名シェイプは生成・照合の対象外だが、メッシュのデータとしては保持する。
                // 削除対象は必ず非空のARKit名のため、空名が削除に該当することはない。
                // （ここでpreservedに積まないと、ClearBlendShapes後の再構築で黙って消える）
                //
                // 既知の制限: AddBlendShapeFrameは名前をキーにするため、同名シェイプを個別には
                // 復元できない。削除によって同名（空名を含む）シェイプが隣接すると、それらは
                // 1つのシェイプへ統合されデルタは各フレームとして残るが個数は減る。
                if (!string.IsNullOrEmpty(existingName) && shapeNamesToRemove.Contains(existingName))
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

        private static void Log(IGenerationLogger logger, BlendShapeGenerationOptions options, string message)
        {
            if (logger != null && options != null && options.Debug)
            {
                logger.Debug(message);
            }
        }
    }
}
