using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Infra
{
    /// <summary>
    /// プロジェクト内のプレハブとシーンを調べ、スクリプト参照の切れたコンポーネントを付け替える。
    ///
    /// どれが対象かの判断はMissingScriptReferencesが持ち、ここはファイルの読み書きと
    /// AssetDatabaseへの問い合わせを担う。
    /// 調べる（Scan）と書き換える（Repair）を分けているのは、利用者のファイルを書き換える前に
    /// 件数を見せて同意を得るためである
    /// </summary>
    internal static class MissingScriptRepairer
    {
        /// <summary>
        /// 調べた結果。Repairへそのまま渡す
        /// </summary>
        internal sealed class ScanResult
        {
            /// <summary>付け替え先。手元に入っているコンポーネントのスクリプトのGUID</summary>
            public string CurrentGuid;

            /// <summary>切れた参照が指していたGUID</summary>
            public readonly HashSet<string> StaleGuids = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>参照の切れたコンポーネントを持つファイル</summary>
            public readonly List<string> Paths = new List<string>();

            /// <summary>参照の切れたコンポーネントの数</summary>
            public int ComponentCount;

            /// <summary>調べ終える前に利用者が中断した</summary>
            public bool Cancelled;
        }

        private const string ScanRoot = "Assets";

        // テキスト形式で保存されたアセットの先頭。バイナリ形式のファイルは解釈できないため読み飛ばす
        private const string YamlHeader = "%YAML";

        // UnityはBOM無しのUTF-8で書く。書き戻すときも合わせる
        private static readonly Encoding FileEncoding = new UTF8Encoding(false);

        /// <summary>
        /// 手元に入っているコンポーネントのスクリプトのGUID。求められなければnull
        /// </summary>
        public static string FindCurrentGuid()
        {
            foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            {
                if (script == null || script.GetClass() != typeof(ARKitBlendShapeGeneratorComponent))
                {
                    continue;
                }

                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out var guid, out long _)
                    && MissingScriptReferences.IsGuid(guid))
                {
                    return guid;
                }
            }

            return null;
        }

        /// <summary>
        /// `Assets/`配下のプレハブとシーンから、参照の切れたコンポーネントを探す。
        ///
        /// パッケージの中のプレハブは利用者が編集するものではなく、書き換えてもVCC/ALCOMの更新で
        /// 戻るため対象にしない
        /// </summary>
        public static ScanResult Scan(string progressTitle)
        {
            var result = new ScanResult { CurrentGuid = FindCurrentGuid() };
            if (result.CurrentGuid == null)
            {
                return result;
            }

            var paths = FindCandidatePaths();
            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            progressTitle, paths[i], (float)i / paths.Count))
                    {
                        result.Cancelled = true;
                        break;
                    }

                    if (!TryReadYaml(paths[i], out var yaml))
                    {
                        continue;
                    }

                    var found = MissingScriptReferences.CollectStaleGuids(
                        yaml, result.CurrentGuid, IsResolvable, result.StaleGuids);
                    if (found > 0)
                    {
                        result.ComponentCount += found;
                        result.Paths.Add(paths[i]);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return result;
        }

        /// <summary>
        /// Scanが見つけた切れた参照を、手元のスクリプトへ付け替える。
        ///
        /// Scanが挙げたファイルのほかも書き換えることがある。
        /// 設定値を持たない参照が、同じGUIDを別のファイルに残している場合である。
        /// 戻り値は書き換えたファイルのパス
        /// </summary>
        public static List<string> Repair(ScanResult scan, string progressTitle)
        {
            var repaired = new List<string>();
            if (scan == null || scan.CurrentGuid == null || scan.StaleGuids.Count == 0)
            {
                return repaired;
            }

            var paths = FindCandidatePaths();
            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    EditorUtility.DisplayProgressBar(progressTitle, paths[i], (float)i / paths.Count);

                    if (!TryReadYaml(paths[i], out var yaml) || !ContainsAny(yaml, scan.StaleGuids))
                    {
                        continue;
                    }

                    var rewritten = MissingScriptReferences.ReplaceScriptGuids(
                        yaml, scan.StaleGuids, scan.CurrentGuid, out var replaced);
                    if (replaced == 0)
                    {
                        continue;
                    }

                    if (TryWrite(paths[i], rewritten))
                    {
                        repaired.Add(paths[i]);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // 書き換えたファイルをUnityに読み直させる。開いているシーンが含まれる場合は、
            // Unityが読み直すかどうかを利用者に尋ねる
            if (repaired.Count > 0)
            {
                AssetDatabase.Refresh();
            }

            return repaired;
        }

        private static List<string> FindCandidatePaths()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { ScanRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seen.Add(path))
                {
                    continue;
                }

                // モデル（.fbxなど）もプレハブとして見つかるが、コンポーネントを書き込める先ではない
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        /// <summary>
        /// GUIDがプロジェクト内のアセットを指しているか。
        ///
        /// GUIDToAssetPathは削除済みのアセットのパスを返すことがあるため、実体があるかまで確かめる
        /// </summary>
        private static bool IsResolvable(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return !string.IsNullOrEmpty(path) && AssetDatabase.GetMainAssetTypeAtPath(path) != null;
        }

        private static bool ContainsAny(string yaml, HashSet<string> guids)
        {
            foreach (var guid in guids)
            {
                if (yaml.IndexOf(guid, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadYaml(string path, out string yaml)
        {
            yaml = null;
            try
            {
                var text = File.ReadAllText(path, FileEncoding);
                if (!text.StartsWith(YamlHeader, StringComparison.Ordinal))
                {
                    return false;
                }

                yaml = text;
                return true;
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                // 読めないファイルは対象から外すだけで、ほかのファイルの修復は続ける
                return false;
            }
        }

        private static bool TryWrite(string path, string yaml)
        {
            try
            {
                File.WriteAllText(path, yaml, FileEncoding);
                return true;
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                Debug.LogWarning("[ARKitGenerator] " + Localization.S("repair.log.write_failed", path, exception.Message));
                return false;
            }
        }

        private static bool IsFileFailure(Exception exception)
        {
            return exception is IOException
                || exception is UnauthorizedAccessException
                || exception is ArgumentException
                || exception is NotSupportedException;
        }
    }
}
