using System;
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
        /// メッシュへ書き込む直前にデルタを組み立てる生成単位。
        ///
        /// 生成物を全て作ってから書き込むと、生成シェイプ数 × 頂点数 × 3配列のメモリを
        /// 一度に確保することになる。書き込む位置が決まってから1件ずつ実体化することで、
        /// 書き終えたデルタをその場で解放できるようにしている。
        /// </summary>
        private sealed class BlendShapeBuildTask
        {
            public readonly string ArkitName;

            /// <summary>手続き的生成によるものか（ログの出し分けにのみ使う）</summary>
            public readonly bool IsProcedural;

            private readonly Func<BlendShapeData> _build;
            private BlendShapeData _materialized;
            private bool _isMaterialized;

            public BlendShapeBuildTask(string arkitName, bool isProcedural, Func<BlendShapeData> build)
            {
                ArkitName = arkitName;
                IsProcedural = isProcedural;
                _build = build;
            }

            /// <summary>
            /// 先にデルタを組み立てて抱え込む。
            /// 組み立てにソースメッシュを読む必要があるため、ソースが書き換わる前に確定させたい場合に使う
            /// </summary>
            public void Materialize()
            {
                if (_isMaterialized)
                {
                    return;
                }

                _materialized = _build();
                _isMaterialized = true;
            }

            /// <summary>デルタを組み立てる。生成できなかった場合はnull</summary>
            public BlendShapeData Build()
            {
                return _isMaterialized ? _materialized : _build();
            }
        }

        /// <summary>
        /// 生成処理を通して使い回すデルタ読み出し用のバッファ。
        /// ソースを1件読むたびに配列を確保し直すと、頂点数 × 参照したソース数のGC圧がかかる。
        ///
        /// IMeshRepositoryはデルタの読み出しで配列全体を書き潰し、書き込みでは配列を複製するため、
        /// 読み出し→書き込みを終えるたびに同じバッファを次の用途へ回してよい。
        ///
        /// 各バッファは最初に使うときまで確保しない。生成対象が1件も無いプレビュー更新や、
        /// フレーム間補間が要らないメッシュで、使わないバッファ分の確保が毎回走るのを避ける。
        /// </summary>
        private sealed class DeltaScratch
        {
            private readonly int _vertexCount;

            private Vector3[] _sourceVertices;
            private Vector3[] _sourceNormals;
            private Vector3[] _sourceTangents;
            private Vector3[] _frameVertices;
            private Vector3[] _frameNormals;
            private Vector3[] _frameTangents;

            public DeltaScratch(int vertexCount)
            {
                _vertexCount = vertexCount;
            }

            /// <summary>ソースBlendShape1件分のデルタ</summary>
            public Vector3[] SourceVertices => _sourceVertices ?? (_sourceVertices = new Vector3[_vertexCount]);
            public Vector3[] SourceNormals => _sourceNormals ?? (_sourceNormals = new Vector3[_vertexCount]);
            public Vector3[] SourceTangents => _sourceTangents ?? (_sourceTangents = new Vector3[_vertexCount]);

            /// <summary>フレーム間補間で下側フレームを読むためのバッファ</summary>
            public Vector3[] FrameVertices => _frameVertices ?? (_frameVertices = new Vector3[_vertexCount]);
            public Vector3[] FrameNormals => _frameNormals ?? (_frameNormals = new Vector3[_vertexCount]);
            public Vector3[] FrameTangents => _frameTangents ?? (_frameTangents = new Vector3[_vertexCount]);
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

        /// <summary>
        /// ClearBlendShapes後の再構築で読み直す、対象メッシュの元のBlendShapeデータ
        /// </summary>
        private interface IBlendShapeOrigin
        {
            int BlendShapeCount { get; }

            string GetBlendShapeName(int shapeIndex);

            int GetBlendShapeFrameCount(int shapeIndex);

            float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex);

            void GetBlendShapeFrameVertices(
                int shapeIndex,
                int frameIndex,
                Vector3[] deltaVertices,
                Vector3[] deltaNormals,
                Vector3[] deltaTangents);
        }

        /// <summary>
        /// 対象メッシュの複製元をそのまま読み直す再構築元。
        /// メモリを一切増やさずに済むため、複製元が使える場合はこちらを使う
        /// </summary>
        private sealed class MeshBlendShapeOrigin : IBlendShapeOrigin
        {
            private readonly IMeshRepository _mesh;

            public MeshBlendShapeOrigin(IMeshRepository mesh)
            {
                _mesh = mesh;
            }

            public int BlendShapeCount => _mesh.BlendShapeCount;

            public string GetBlendShapeName(int shapeIndex) => _mesh.GetBlendShapeName(shapeIndex);

            public int GetBlendShapeFrameCount(int shapeIndex) => _mesh.GetBlendShapeFrameCount(shapeIndex);

            public float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex) =>
                _mesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);

            public void GetBlendShapeFrameVertices(
                int shapeIndex,
                int frameIndex,
                Vector3[] deltaVertices,
                Vector3[] deltaNormals,
                Vector3[] deltaTangents)
            {
                _mesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);
            }
        }

        /// <summary>
        /// メモリへ退避した再構築元。複製元が使えない場合のフォールバックで、
        /// 全シェイプ分のデルタ（シェイプ数 × フレーム数 × 頂点数 × 3配列）を保持する
        /// </summary>
        private sealed class BufferedBlendShapeOrigin : IBlendShapeOrigin
        {
            private readonly List<BlendShapeData> _shapes;

            public BufferedBlendShapeOrigin(List<BlendShapeData> shapes)
            {
                _shapes = shapes;
            }

            public int BlendShapeCount => _shapes.Count;

            public string GetBlendShapeName(int shapeIndex) => _shapes[shapeIndex].Name;

            public int GetBlendShapeFrameCount(int shapeIndex) => _shapes[shapeIndex].Frames.Count;

            public float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex) =>
                _shapes[shapeIndex].Frames[frameIndex].Weight;

            public void GetBlendShapeFrameVertices(
                int shapeIndex,
                int frameIndex,
                Vector3[] deltaVertices,
                Vector3[] deltaNormals,
                Vector3[] deltaTangents)
            {
                var frame = _shapes[shapeIndex].Frames[frameIndex];
                frame.DeltaVertices.CopyTo(deltaVertices, 0);
                frame.DeltaNormals.CopyTo(deltaNormals, 0);
                frame.DeltaTangents.CopyTo(deltaTangents, 0);
            }
        }

        /// <summary>
        /// sourceMeshのBlendShapeとマッピング定義から、targetMeshへARKit BlendShapeを生成する。
        ///
        /// 生成内容はメッシュへ触れる前にすべて決め、書き込みは1回にまとめる。
        /// デルタの組み立ては書き込む直前に1シェイプずつ行うため、
        /// 一度に確保するデルタは生成するシェイプ数ではなく頂点数のオーダーに収まる
        /// </summary>
        /// <param name="targetIsSourceCopy">
        /// targetMeshがsourceMeshの複製（生成前の内容が完全に一致する）かどうか。
        /// trueの場合、上書き時の再構築で対象メッシュのデルタをメモリへ退避せず、
        /// sourceMeshから1シェイプずつ読み直して積み直す。
        /// 実際に並びが一致するかは内部でも確認し、食い違う場合は退避経路へ落とす
        /// </param>
        public static BlendShapeGenerationResult Generate(
            IMeshRepository sourceMesh,
            IMeshRepository targetMesh,
            List<CustomBlendShapeMapping> customMappings,
            List<ARKitMapping> autoMappings,
            BlendShapeGenerationOptions options,
            IGenerationLogger logger,
            bool targetIsSourceCopy = false)
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

            var scratch = new DeltaScratch(sourceMesh.VertexCount);
            var cancellation = BuildMouthCancellationDelta(sourceMesh, existingShapes, options, scratch, logger);

            // メッシュへ書き込む前に、どのシェイプをどこへ書くかを全て確定させる。
            // 置き換えは削除+末尾追加ではなく元の位置への差し替えで行うため、
            // 差し替えと追加をまとめて1回の書き込みで済ませる
            var targetExistingNames = GetExistingBlendShapeNames(targetMesh);
            var adjacentDuplicateName = FindAdjacentDuplicateName(targetMesh);

            var replacements = new Dictionary<string, BlendShapeBuildTask>();
            var appended = new List<BlendShapeBuildTask>();
            var plannedTasks = new List<BlendShapeBuildTask>();

            foreach (var planned in plannedBlendShapes)
            {
                // 実体化して初めて失敗が分かると、手続き的生成の要否を書き込み前に決められない。
                // 失敗する条件（有効なフレームを持つソースが1つも無い）はデルタを読まずに判定できる
                if (!HasBuildableSource(sourceMesh, planned.Sources))
                {
                    continue;
                }

                var task = new BlendShapeBuildTask(
                    planned.ArkitName,
                    false,
                    () => BuildBlendShape(sourceMesh, planned, options, cancellation, scratch, logger));

                plannedTasks.Add(task);
                if (options.OverwriteExisting && targetExistingNames.Contains(planned.ArkitName))
                {
                    replacements[planned.ArkitName] = task;
                }
                else
                {
                    appended.Add(task);
                }
            }

            if (replacements.Count > 0 && adjacentDuplicateName != null)
            {
                // 元から同名シェイプが隣接しているメッシュは再構築でデータを失うため、
                // メッシュを変更しないまま生成全体を中止する
                logger?.Error(S("log.overwrite_merge_abort", adjacentDuplicateName));
                return new BlendShapeGenerationResult(
                    new List<string>(),
                    BuildShapeIndices(targetMesh));
            }

            if (options.EnableProceduralMouthShapes)
            {
                CollectProceduralMouthShapes(
                    sourceMesh,
                    targetMesh,
                    options,
                    customMappedNames,
                    targetExistingNames,
                    adjacentDuplicateName,
                    cancellation,
                    plannedTasks,
                    replacements,
                    appended,
                    logger);
            }

            var writtenNames = WriteBlendShapes(
                targetMesh,
                sourceMesh,
                targetIsSourceCopy,
                replacements,
                appended,
                options,
                logger);

            // 書き込めたものだけを、計画した順（カスタム → 自動 → 手続き的）で並べる
            var generatedShapes = new List<string>();
            foreach (var task in plannedTasks)
            {
                if (writtenNames.Contains(task.ArkitName))
                {
                    generatedShapes.Add(task.ArkitName);
                }
            }

            return new BlendShapeGenerationResult(generatedShapes, BuildShapeIndices(targetMesh));
        }

        /// <summary>
        /// 生成・削除後の最終状態からインデックス表を作る。
        /// 空名シェイプは名前で引けないためエントリを持たないが、iは実メッシュ上の位置のままなので
        /// 非空シェイプのインデックスは常に実体と一致する
        /// </summary>
        private static Dictionary<string, int> BuildShapeIndices(IMeshRepository targetMesh)
        {
            var shapeIndices = new Dictionary<string, int>();
            for (int i = 0; i < targetMesh.BlendShapeCount; i++)
            {
                var shapeName = targetMesh.GetBlendShapeName(i);
                if (!string.IsNullOrEmpty(shapeName))
                {
                    shapeIndices[shapeName] = i;
                }
            }

            return shapeIndices;
        }

        /// <summary>
        /// 打ち消し対象BlendShapeの逆方向デルタを合成する。
        /// 対象や強度が未設定の場合はnull（打ち消しなし）。
        /// </summary>
        private static MouthCancellationDelta BuildMouthCancellationDelta(
            IMeshRepository sourceMesh,
            Dictionary<string, int> existingShapes,
            BlendShapeGenerationOptions options,
            DeltaScratch scratch,
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

                var srcDeltaV = scratch.SourceVertices;
                var srcDeltaN = scratch.SourceNormals;
                var srcDeltaT = scratch.SourceTangents;
                if (!TryEvaluateBlendShapeAtWeight(
                        sourceMesh,
                        srcIndex,
                        targetWeight,
                        srcDeltaV,
                        srcDeltaN,
                        srcDeltaT,
                        scratch))
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
            Vector3[] deltaTangents,
            DeltaScratch scratch)
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

            var lowerDeltaV = scratch.FrameVertices;
            var lowerDeltaN = scratch.FrameNormals;
            var lowerDeltaT = scratch.FrameTangents;
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

        /// <summary>
        /// 手続き的生成の対象を選び、書き込み計画へ加える。
        /// メッシュへ書き込む前に呼ぶため、既存シェイプ名の判定には書き込み前の状態を使う
        /// （書き込みで増える名前は、いずれもここで既に除外済みの「生成済み」の名前になる）。
        /// </summary>
        private static void CollectProceduralMouthShapes(
            IMeshRepository sourceMesh,
            IMeshRepository targetMesh,
            BlendShapeGenerationOptions options,
            HashSet<string> customMappedNames,
            HashSet<string> targetExistingNames,
            string adjacentDuplicateName,
            MouthCancellationDelta cancellation,
            List<BlendShapeBuildTask> plannedTasks,
            Dictionary<string, BlendShapeBuildTask> replacements,
            List<BlendShapeBuildTask> appended,
            IGenerationLogger logger)
        {
            if (sourceMesh.VertexCount != targetMesh.VertexCount)
            {
                Log(logger, options, "Skip procedural (vertex count mismatch)");
                return;
            }

            var generatedNames = new HashSet<string>();
            foreach (var task in plannedTasks)
            {
                generatedNames.Add(task.ArkitName);
            }

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

            if (namesToReplace.Count > 0 &&
                adjacentDuplicateName != null &&
                !TryDropUnbuildableProceduralReplacements(context, options, namesToGenerate, namesToReplace))
            {
                // 手続き的生成は補助機能のため、メッシュを守って生成を見送る
                Log(logger, options, $"Skip procedural (replacement would merge duplicate shape: {adjacentDuplicateName})");
                return;
            }

            if (namesToGenerate.Count == 0)
            {
                return;
            }

            foreach (var arkitName in namesToGenerate)
            {
                var task = new BlendShapeBuildTask(
                    arkitName,
                    true,
                    () => BuildProceduralBlendShape(context, arkitName, options, cancellation, logger));

                plannedTasks.Add(task);
                if (namesToReplace.Contains(arkitName))
                {
                    replacements[arkitName] = task;
                }
                else
                {
                    appended.Add(task);
                }
            }
        }

        /// <summary>
        /// 差し替えが1件も成立しないことを確かめ、成立しないと分かった候補を生成対象から外す。
        /// 1件でも成立する場合はfalse（再構築が走って同名シェイプが統合されるため、呼び出し側で中止する）。
        ///
        /// 差し替え候補はデルタを作れないことがあり、その場合は差し替えが起きないので
        /// 再構築も走らない。候補が残っているという理由だけで手続き的生成全体を諦めないよう、
        /// 実際に組み立てられるかを確かめる。元から同名シェイプが隣接するメッシュでしか
        /// 通らない経路のため、ここで先に組み立てを試すコストは許容する
        /// </summary>
        private static bool TryDropUnbuildableProceduralReplacements(
            ProceduralMouthShapeGenerator.MouthRegionContext context,
            BlendShapeGenerationOptions options,
            List<string> namesToGenerate,
            HashSet<string> namesToReplace)
        {
            foreach (var arkitName in namesToReplace)
            {
                if (ProceduralMouthShapeGenerator.TryBuildDeltaVertices(context, arkitName, options, out _))
                {
                    return false;
                }
            }

            namesToGenerate.RemoveAll(name => namesToReplace.Contains(name));
            namesToReplace.Clear();
            return true;
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

        /// <summary>
        /// 合成に使えるソース（有効なインデックスで、フレームを1つ以上持つもの）があるか。
        /// デルタを読まずに判定できるため、BuildBlendShapeを走らせる前に生成の成否が分かる。
        /// BuildBlendShapeが「ソース0件」で失敗する条件と一致させること
        /// </summary>
        private static bool HasBuildableSource(
            IMeshRepository sourceMesh,
            List<(int index, float weight, BlendShapeSide side)> sources)
        {
            foreach (var source in sources)
            {
                if (source.index < 0 || source.index >= sourceMesh.BlendShapeCount)
                {
                    continue;
                }

                if (sourceMesh.GetBlendShapeFrameCount(source.index) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 既存シェイプキーを合成して生成するBlendShapeのデルタを組み立てる。メッシュへの書き込みは行わない。
        /// 生成できなかった場合はnull（HasBuildableSourceで事前に弾いていれば発生しない）
        /// </summary>
        private static BlendShapeData BuildBlendShape(
            IMeshRepository sourceMesh,
            PlannedBlendShape planned,
            BlendShapeGenerationOptions options,
            MouthCancellationDelta cancellation,
            DeltaScratch scratch,
            IGenerationLogger logger)
        {
            int vertexCount = sourceMesh.VertexCount;
            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];
            var vertices = sourceMesh.GetVertices();

            int sourceCount = 0;
            float blendWidth = Mathf.Max(0.0001f, options.BlendWidth);

            foreach (var (index, weight, side) in planned.Sources)
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

                var srcDeltaV = scratch.SourceVertices;
                var srcDeltaN = scratch.SourceNormals;
                var srcDeltaT = scratch.SourceTangents;

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
                return null;
            }

            // 打ち消しのみのBlendShapeを作らないよう、ソースから生成できた場合だけ焼き込む
            if (cancellation != null && cancellation.AppliesTo(planned.ArkitName))
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    deltaVertices[i] += cancellation.DeltaVertices[i];
                    deltaNormals[i] += cancellation.DeltaNormals[i];
                    deltaTangents[i] += cancellation.DeltaTangents[i];
                }

                Log(logger, options, $"Applied cancellation: {planned.ArkitName}");
            }

            Log(logger, options, $"Generated: {planned.ArkitName} from {sourceCount} source(s)");
            return new BlendShapeData(
                planned.ArkitName,
                new List<BlendShapeFrameData>
                {
                    new BlendShapeFrameData(100f, deltaVertices, deltaNormals, deltaTangents),
                });
        }

        /// <summary>
        /// 手続き的生成のデルタを組み立てる。口領域から生成できなかった場合はnull
        /// </summary>
        private static BlendShapeData BuildProceduralBlendShape(
            ProceduralMouthShapeGenerator.MouthRegionContext context,
            string arkitName,
            BlendShapeGenerationOptions options,
            MouthCancellationDelta cancellation,
            IGenerationLogger logger)
        {
            if (!ProceduralMouthShapeGenerator.TryBuildDeltaVertices(context, arkitName, options, out var deltaVertices))
            {
                return null;
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

            Log(logger, options, $"Generated (procedural): {arkitName}");
            return new BlendShapeData(
                arkitName,
                new List<BlendShapeFrameData>
                {
                    new BlendShapeFrameData(100f, deltaVertices, deltaNormals, deltaTangents),
                });
        }

        private static void AppendBlendShape(IMeshRepository mesh, BlendShapeData shape)
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
        /// 空名シェイプは生成・置き換えの対象にならないため含めない（保持自体は再構築側で担保する）。
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

        /// <summary>
        /// 確定した差し替え・追加をメッシュへ書き込み、実際に書き込めたARKit名を返す。
        /// 差し替えと追加は1回の再構築でまとめて行う（差し替えが無ければ再構築自体を行わない）。
        /// </summary>
        private static HashSet<string> WriteBlendShapes(
            IMeshRepository targetMesh,
            IMeshRepository sourceMesh,
            bool targetIsSourceCopy,
            Dictionary<string, BlendShapeBuildTask> replacements,
            List<BlendShapeBuildTask> appended,
            BlendShapeGenerationOptions options,
            IGenerationLogger logger)
        {
            var writtenNames = new HashSet<string>();

            if (replacements.Count > 0 && ReferenceEquals(sourceMesh, targetMesh))
            {
                // 対象メッシュ自身がソースを兼ねる場合、再構築のClearBlendShapesでソースのデルタも消える。
                // 組み立てを書き込み直前まで遅らせると読み直せなくなるため、ここで先に確定させる
                // （抱えるメモリは生成シェイプ数分に戻るが、この経路を通るのは複製を渡さない呼び出しだけ）
                foreach (var task in replacements.Values)
                {
                    task.Materialize();
                }

                foreach (var task in appended)
                {
                    task.Materialize();
                }
            }

            if (replacements.Count > 0)
            {
                RebuildWithReplacements(
                    targetMesh,
                    sourceMesh,
                    targetIsSourceCopy,
                    replacements,
                    writtenNames,
                    options,
                    logger);
            }

            foreach (var task in appended)
            {
                var built = task.Build();
                if (built == null)
                {
                    continue;
                }

                AppendBlendShape(targetMesh, built);
                writtenNames.Add(built.Name);
            }

            return writtenNames;
        }

        /// <summary>
        /// 差し替え対象のBlendShapeを、その場で新しいデルタへ置き換える（ClearBlendShapes後に全体を再構築する）。
        ///
        /// 削除して末尾へ追加し直すのではなく元の位置へ書き戻すのは、削除で空いた隙間によって
        /// 同名シェイプが隣接するのを防ぐため。AddBlendShapeFrameは名前をキーにするうえ、
        /// 同名シェイプのフレームウェイトは昇順でなければならないため、同名シェイプが隣接すると
        /// 後続の同一ウェイトのフレームが追加されずデルタが失われる。
        /// 並びを変えないので、隣接が新たに生じることはない。
        /// （元から同名が隣接しているメッシュは、呼び出し前にFindAdjacentDuplicateNameで弾いている）
        ///
        /// 同じ名前のシェイプが複数ある場合は、そのすべてを置き換える。
        ///
        /// 差し替えないシェイプのデルタは再構築元から1シェイプずつ読み直し、
        /// 差し替えるシェイプのデルタもこの時点で組み立てる。どちらも書き込んだ端から捨てられるため、
        /// 一度に確保するデルタは頂点数のオーダーに収まる。
        /// </summary>
        private static void RebuildWithReplacements(
            IMeshRepository targetMesh,
            IMeshRepository sourceMesh,
            bool targetIsSourceCopy,
            Dictionary<string, BlendShapeBuildTask> replacements,
            HashSet<string> writtenNames,
            BlendShapeGenerationOptions options,
            IGenerationLogger logger)
        {
            // ClearBlendShapes後は対象メッシュから元のデルタを読めないため、読み直す先を先に決める。
            // 複製元が使えるならメモリを増やさずに済み、使えない場合だけ対象メッシュの内容を退避する。
            // 複製ではなく対象そのものを渡された場合は、読み直す前に消えてしまうので退避側へ落とす
            bool canReadFromSource = targetIsSourceCopy
                && !ReferenceEquals(sourceMesh, targetMesh)
                && HasSameBlendShapeLayout(sourceMesh, targetMesh);
            var origin = canReadFromSource
                ? (IBlendShapeOrigin)new MeshBlendShapeOrigin(sourceMesh)
                : CaptureBlendShapes(targetMesh);

            // 同名シェイプが複数ある場合は同じデルタを各位置へ書く。
            // 最後の1つを書き終えるまでだけ保持し、それ以外は書き込んだ端から解放する
            var remainingOccurrences = CountReplacementOccurrences(origin, replacements);
            var builtByName = new Dictionary<string, BlendShapeData>();

            var replacedNames = new HashSet<string>();
            var replacedProceduralNames = new HashSet<string>();

            int vertexCount = targetMesh.VertexCount;
            var frameVertices = new Vector3[vertexCount];
            var frameNormals = new Vector3[vertexCount];
            var frameTangents = new Vector3[vertexCount];

            int blendShapeCount = origin.BlendShapeCount;
            targetMesh.ClearBlendShapes();

            for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
            {
                string shapeName = origin.GetBlendShapeName(shapeIndex);

                // 空名シェイプは生成・照合の対象外だが、メッシュのデータとしては保持する
                // （ここで積み直さないと、ClearBlendShapes後の再構築で黙って消える）
                if (!string.IsNullOrEmpty(shapeName) &&
                    replacements.TryGetValue(shapeName, out var task) &&
                    TryTakeReplacement(task, shapeName, remainingOccurrences, builtByName, out var built))
                {
                    AppendBlendShape(targetMesh, built);
                    writtenNames.Add(shapeName);
                    (task.IsProcedural ? replacedProceduralNames : replacedNames).Add(shapeName);
                    continue;
                }

                int frameCount = origin.GetBlendShapeFrameCount(shapeIndex);
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    origin.GetBlendShapeFrameVertices(
                        shapeIndex,
                        frameIndex,
                        frameVertices,
                        frameNormals,
                        frameTangents);

                    targetMesh.AddBlendShapeFrame(
                        shapeName,
                        origin.GetBlendShapeFrameWeight(shapeIndex, frameIndex),
                        frameVertices,
                        frameNormals,
                        frameTangents);
                }
            }

            if (replacedNames.Count > 0)
            {
                Log(logger, options, $"Replaced existing blendshapes: {string.Join(", ", replacedNames.OrderBy(name => name))}");
            }

            if (replacedProceduralNames.Count > 0)
            {
                Log(logger, options, $"Replaced existing blendshapes (procedural): {string.Join(", ", replacedProceduralNames.OrderBy(name => name))}");
            }
        }

        /// <summary>
        /// 差し替えるデルタを取り出す。組み立てに失敗した場合はfalse（元のシェイプをそのまま残す）。
        /// 同名シェイプが残っている間だけ組み立て結果を保持し、最後の1つを渡したら手放す
        /// </summary>
        private static bool TryTakeReplacement(
            BlendShapeBuildTask task,
            string shapeName,
            Dictionary<string, int> remainingOccurrences,
            Dictionary<string, BlendShapeData> builtByName,
            out BlendShapeData built)
        {
            if (!builtByName.TryGetValue(shapeName, out built))
            {
                built = task.Build();
            }

            int remaining = remainingOccurrences[shapeName] - 1;
            remainingOccurrences[shapeName] = remaining;

            if (built == null)
            {
                return false;
            }

            if (remaining > 0)
            {
                builtByName[shapeName] = built;
            }
            else
            {
                builtByName.Remove(shapeName);
            }

            return true;
        }

        private static Dictionary<string, int> CountReplacementOccurrences(
            IBlendShapeOrigin origin,
            Dictionary<string, BlendShapeBuildTask> replacements)
        {
            var occurrences = new Dictionary<string, int>();
            for (int shapeIndex = 0; shapeIndex < origin.BlendShapeCount; shapeIndex++)
            {
                string shapeName = origin.GetBlendShapeName(shapeIndex);
                if (string.IsNullOrEmpty(shapeName) || !replacements.ContainsKey(shapeName))
                {
                    continue;
                }

                occurrences.TryGetValue(shapeName, out int count);
                occurrences[shapeName] = count + 1;
            }

            return occurrences;
        }

        /// <summary>
        /// 対象メッシュのBlendShapeをメモリへ退避する。
        /// 全シェイプ分のデルタを抱えるため、複製元から読み直せない場合のフォールバックに限る
        /// </summary>
        private static IBlendShapeOrigin CaptureBlendShapes(IMeshRepository mesh)
        {
            int vertexCount = mesh.VertexCount;
            int blendShapeCount = mesh.BlendShapeCount;
            var shapes = new List<BlendShapeData>(blendShapeCount);

            for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
            {
                int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
                var frames = new List<BlendShapeFrameData>(frameCount);

                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
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
                        mesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex),
                        deltaVertices,
                        deltaNormals,
                        deltaTangents));
                }

                shapes.Add(new BlendShapeData(mesh.GetBlendShapeName(shapeIndex), frames));
            }

            return new BufferedBlendShapeOrigin(shapes);
        }

        /// <summary>
        /// 2つのメッシュのBlendShapeの並び（頂点数・シェイプ名・フレーム構成）が一致するか。
        /// デルタは読まないため、頂点数に比例したコストはかからない
        /// </summary>
        private static bool HasSameBlendShapeLayout(IMeshRepository a, IMeshRepository b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (a.VertexCount != b.VertexCount || a.BlendShapeCount != b.BlendShapeCount)
            {
                return false;
            }

            for (int shapeIndex = 0; shapeIndex < a.BlendShapeCount; shapeIndex++)
            {
                if (!string.Equals(a.GetBlendShapeName(shapeIndex), b.GetBlendShapeName(shapeIndex)))
                {
                    return false;
                }

                int frameCount = a.GetBlendShapeFrameCount(shapeIndex);
                if (frameCount != b.GetBlendShapeFrameCount(shapeIndex))
                {
                    return false;
                }

                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    if (!Mathf.Approximately(
                            a.GetBlendShapeFrameWeight(shapeIndex, frameIndex),
                            b.GetBlendShapeFrameWeight(shapeIndex, frameIndex)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 再構築したときに統合されてしまう同名シェイプ（隣り合う同名の並び）を探す。
        /// 見つからなければnull。空名も統合の対象になるため同様に扱う。
        ///
        /// 元から同名シェイプが隣接しているメッシュ（FBXインポート等、AddBlendShapeFrameを
        /// 経由せずに作られたもの）だけは再構築でデータを失うため、書き込み前に判定する
        /// </summary>
        private static string FindAdjacentDuplicateName(IMeshRepository mesh)
        {
            for (int shapeIndex = 1; shapeIndex < mesh.BlendShapeCount; shapeIndex++)
            {
                if (string.Equals(mesh.GetBlendShapeName(shapeIndex - 1), mesh.GetBlendShapeName(shapeIndex)))
                {
                    return mesh.GetBlendShapeName(shapeIndex);
                }
            }

            return null;
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
