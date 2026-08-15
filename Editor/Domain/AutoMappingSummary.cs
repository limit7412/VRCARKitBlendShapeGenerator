using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// 自動マッピングの候補（優先度1段分。ARKitMapping 1件に対応する）
    /// </summary>
    internal sealed class AutoMappingCandidate
    {
        /// <summary>この段で合成されるソース。それぞれ先に見つかった名前1件が使われる</summary>
        public IReadOnlyList<SourceMapping> Sources { get; }

        /// <summary>この段に適用される左右フィルタ</summary>
        public BlendShapeSide Side { get; }

        public AutoMappingCandidate(IReadOnlyList<SourceMapping> sources, BlendShapeSide side)
        {
            Sources = sources;
            Side = side;
        }
    }

    /// <summary>
    /// ARKit名1件分の自動マッピング
    /// </summary>
    internal sealed class AutoMappingEntry
    {
        public string ArkitName { get; }

        /// <summary>優先度順の候補。先に成立したものが採用される</summary>
        public IReadOnlyList<AutoMappingCandidate> Candidates { get; }

        /// <summary>シェイプキーから生成できなかった場合に手続き的生成で補えるか</summary>
        public bool HasProceduralFallback { get; }

        public AutoMappingEntry(
            string arkitName,
            IReadOnlyList<AutoMappingCandidate> candidates,
            bool hasProceduralFallback)
        {
            ArkitName = arkitName;
            Candidates = candidates;
            HasProceduralFallback = hasProceduralFallback;
        }
    }

    /// <summary>
    /// インスペクタのカテゴリ単位のまとまり
    /// </summary>
    internal sealed class AutoMappingCategory
    {
        /// <summary>ローカライズキーの末尾（auto_mappings.fold.&lt;Key&gt;）</summary>
        public string Key { get; }

        public IReadOnlyList<AutoMappingEntry> Entries { get; }

        public AutoMappingCategory(string key, IReadOnlyList<AutoMappingEntry> entries)
        {
            Key = key;
            Entries = entries;
        }
    }

    /// <summary>
    /// インスペクタの「自動マッピング一覧」表示を ARKitMappingTable から組み立てる。
    /// 一覧を手書きすると実装とずれるため、表示は必ずここを経由して生成する
    /// </summary>
    internal static class AutoMappingSummary
    {
        /// <summary>
        /// カテゴリの並びと、そこに属するARKit名。
        /// ARKitBlendShapeNamesの並びをそのまま使うので、左右が隣り合って表示される
        /// </summary>
        private static readonly (string Key, string[] ArkitNames)[] Categories =
        {
            ("eye", ARKitBlendShapeNames.Eye),
            ("eye_look", ARKitBlendShapeNames.EyeLook),
            ("brow", ARKitBlendShapeNames.Brow),
            ("mouth", ARKitBlendShapeNames.Mouth),
            ("cheek", ARKitBlendShapeNames.Cheek),
            ("nose", ARKitBlendShapeNames.Nose),
            ("tongue", ARKitBlendShapeNames.Tongue),
        };

        /// <summary>
        /// 自動マッピングと手続き的生成のどちらでも生成されないARKit名は一覧に載せない
        /// </summary>
        public static IReadOnlyList<AutoMappingCategory> Build()
        {
            return Build(ARKitMappingTable.GetMappings(), ProceduralMouthShapeGenerator.TargetShapeNames);
        }

        internal static IReadOnlyList<AutoMappingCategory> Build(
            IReadOnlyList<ARKitMapping> mappings,
            IReadOnlyList<string> proceduralTargets)
        {
            var candidatesByName = new Dictionary<string, List<AutoMappingCandidate>>();
            if (mappings != null)
            {
                foreach (var mapping in mappings)
                {
                    if (mapping == null || string.IsNullOrEmpty(mapping.arkitName) || mapping.sources == null)
                    {
                        continue;
                    }

                    if (!candidatesByName.TryGetValue(mapping.arkitName, out var list))
                    {
                        list = new List<AutoMappingCandidate>();
                        candidatesByName[mapping.arkitName] = list;
                    }

                    list.Add(new AutoMappingCandidate(mapping.sources, mapping.side));
                }
            }

            var proceduralNames = new HashSet<string>();
            if (proceduralTargets != null)
            {
                foreach (var name in proceduralTargets)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        proceduralNames.Add(name);
                    }
                }
            }

            var result = new List<AutoMappingCategory>();
            foreach (var (key, arkitNames) in Categories)
            {
                var entries = new List<AutoMappingEntry>();
                foreach (var arkitName in arkitNames)
                {
                    bool hasProcedural = proceduralNames.Contains(arkitName);
                    if (!candidatesByName.TryGetValue(arkitName, out var candidates))
                    {
                        // 手続き的生成でのみ生成される名前も一覧に残す
                        if (hasProcedural)
                        {
                            entries.Add(new AutoMappingEntry(arkitName, Array.Empty<AutoMappingCandidate>(), true));
                        }

                        continue;
                    }

                    entries.Add(new AutoMappingEntry(arkitName, candidates, hasProcedural));
                }

                if (entries.Count > 0)
                {
                    result.Add(new AutoMappingCategory(key, entries));
                }
            }

            return result;
        }

        /// <summary>
        /// ARKitBlendShapeNamesのどのカテゴリにも属さないARKit名を返す。
        /// ここに何か入るとその名前は一覧から漏れるため、テストで検出する
        /// </summary>
        internal static IReadOnlyList<string> FindUncategorizedNames(IReadOnlyList<ARKitMapping> mappings)
        {
            var categorized = new HashSet<string>();
            foreach (var (_, arkitNames) in Categories)
            {
                foreach (var name in arkitNames)
                {
                    categorized.Add(name);
                }
            }

            var result = new List<string>();
            if (mappings == null)
            {
                return result;
            }

            foreach (var mapping in mappings)
            {
                if (mapping == null || string.IsNullOrEmpty(mapping.arkitName))
                {
                    continue;
                }

                if (!categorized.Contains(mapping.arkitName) && !result.Contains(mapping.arkitName))
                {
                    result.Add(mapping.arkitName);
                }
            }

            return result;
        }

        /// <summary>
        /// 1行分の候補の説明を組み立てる。
        /// 優先度の段を " / "、同時に合成されるソースを " + " で区切り、
        /// 重みが1.0でない場合と左右フィルタが指定されている場合だけ注記を付ける
        /// </summary>
        /// <param name="sideLabel">BlendShapeSideの表示名（ローカライズ済み）を返す</param>
        public static string DescribeCandidates(
            AutoMappingEntry entry,
            Func<BlendShapeSide, string> sideLabel)
        {
            if (entry == null || entry.Candidates.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < entry.Candidates.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" / ");
                }

                AppendCandidate(builder, entry.Candidates[i], sideLabel);
            }

            return builder.ToString();
        }

        private static void AppendCandidate(
            StringBuilder builder,
            AutoMappingCandidate candidate,
            Func<BlendShapeSide, string> sideLabel)
        {
            bool first = true;
            foreach (var source in candidate.Sources)
            {
                if (source == null || source.names == null || source.names.Length == 0)
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append(" + ");
                }

                builder.Append(string.Join(", ", source.names));
                AppendWeight(builder, source.weight);
                first = false;
            }

            if (candidate.Side != BlendShapeSide.Both && sideLabel != null)
            {
                builder.Append(" [").Append(sideLabel(candidate.Side)).Append(']');
            }
        }

        private static void AppendWeight(StringBuilder builder, float weight)
        {
            if (Mathf.Approximately(weight, 1f))
            {
                return;
            }

            builder.Append(" (").Append(Mathf.RoundToInt(weight * 100f)).Append("%)");
        }
    }
}
