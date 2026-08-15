using System.Collections.Generic;
using UnityEngine;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// プレビューの再生成が必要かどうかの判定結果
    /// </summary>
    internal enum PreviewSettingsChange
    {
        /// <summary>生成結果に影響する変更は無い</summary>
        None,

        /// <summary>スライダーで連続的に変わる値だけが変わった（実行を遅らせてよい）</summary>
        Continuous,

        /// <summary>生成対象や合成内容そのものが変わった（すぐ作り直す必要がある）</summary>
        Structural,
    }

    /// <summary>
    /// プレビュー結果に影響するコンポーネント設定のスナップショット。
    ///
    /// 値の取得（ComputeContextでの監視登録を伴う）は呼び出し側が行い、ここでは
    /// 取得済みの値どうしの比較だけを担う。NDMFに触れないので単体テストできる。
    /// </summary>
    internal sealed class PreviewSettingsSnapshot
    {
        // スライダーでドラッグ中に毎フレーム変わる値
        public float IntensityMultiplier { get; set; }
        public float BlendWidth { get; set; }
        public float ProceduralMouthIntensity { get; set; }
        public float MouthCancellationStrength { get; set; }

        // 1回の操作で1度だけ変わる値
        public bool EnableLeftRightSplit { get; set; }
        public bool OverwriteExisting { get; set; }
        public bool EnableProceduralMouthShapes { get; set; }
        public bool EnableMouthCancellation { get; set; }
        public int MouthCancellationSignature { get; set; }
        public int CustomMappingsSignature { get; set; }
        public int TargetRendererInstanceId { get; set; }

        /// <summary>
        /// 生成結果に影響する差分を分類する。
        ///
        /// 連続値だけの差分を分けているのは、スライダーのドラッグ中に毎フレーム
        /// フル再生成が走るのを避けるため。どちらの値も最終的には同じ経路で反映される
        /// </summary>
        public PreviewSettingsChange CompareWith(PreviewSettingsSnapshot other)
        {
            if (other == null)
            {
                return PreviewSettingsChange.Structural;
            }

            if (EnableLeftRightSplit != other.EnableLeftRightSplit ||
                OverwriteExisting != other.OverwriteExisting ||
                EnableProceduralMouthShapes != other.EnableProceduralMouthShapes ||
                EnableMouthCancellation != other.EnableMouthCancellation ||
                MouthCancellationSignature != other.MouthCancellationSignature ||
                CustomMappingsSignature != other.CustomMappingsSignature ||
                TargetRendererInstanceId != other.TargetRendererInstanceId)
            {
                return PreviewSettingsChange.Structural;
            }

            if (!Mathf.Approximately(IntensityMultiplier, other.IntensityMultiplier) ||
                !Mathf.Approximately(BlendWidth, other.BlendWidth) ||
                !Mathf.Approximately(ProceduralMouthIntensity, other.ProceduralMouthIntensity) ||
                !Mathf.Approximately(MouthCancellationStrength, other.MouthCancellationStrength))
            {
                return PreviewSettingsChange.Continuous;
            }

            return PreviewSettingsChange.None;
        }

        /// <summary>
        /// カスタムマッピングの内容を1つの整数へ畳む。
        /// リストの中身はComputeContextの監視で差分を取れないため、署名で比較する
        /// </summary>
        public static int BuildCustomMappingsSignature(List<CustomBlendShapeMapping> customMappings)
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

        /// <summary>打ち消しの元と対象の内容を1つの整数へ畳む</summary>
        public static int BuildMouthCancellationSignature(List<BlendShapeSource> sources, List<string> targets)
        {
            unchecked
            {
                int hash = HashSources(17, sources);

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
    }
}
