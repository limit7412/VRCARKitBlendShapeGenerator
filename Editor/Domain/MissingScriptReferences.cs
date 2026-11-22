using System;
using System.Collections.Generic;
using System.Text;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// プレハブやシーンのテキストから、スクリプト参照の切れたARKitBlendShapeGeneratorComponentを探し、
    /// 参照を付け替える。
    ///
    /// Unityはコンポーネントのスクリプトを`.meta`のGUIDで引く。
    /// 0.3.0までのVPM用zipは`.meta`を含まず、導入のたびにUnityがGUIDを振り直していたため、
    /// パッケージを更新すると配置済みのコンポーネントがMissing Scriptになった。
    /// その状態でも設定値はファイルに残っているので、スクリプトのGUIDを書き換えれば元に戻る。
    ///
    /// 切れた参照のGUIDはプロジェクトごとに違い、あらかじめ知ることができない。
    /// そこで、参照先の無いMonoBehaviourのうち、このコンポーネントのフィールドを持つものを対象とみなす。
    /// ファイルの読み書きとGUIDの問い合わせは呼び出し側が行い、ここはテキストの解釈だけを担う
    /// </summary>
    internal static class MissingScriptReferences
    {
        /// <summary>
        /// 本文を読む前の絞り込みに使う目印。この名前を含まないファイルに対象は無い
        /// </summary>
        public const string Marker = "enableLeftRightSplit";

        // 0.1.0から現在まで、どの版のコンポーネントも持っているフィールド。
        // 後の版で増えたフィールドを条件に入れると、古い版で保存されたコンポーネントを見落とす
        private static readonly string[] SignatureFields =
        {
            "targetRenderer",
            "intensityMultiplier",
            "enableLeftRightSplit",
            "overwriteExisting",
            "customMappings",
            "debugMode",
        };

        private const string ComponentClassName = "ARKitBlendShapeGeneratorComponent";

        private const string DocumentPrefix = "--- !u!";
        private const string MonoBehaviourDocumentPrefix = "--- !u!114 ";

        // スクリプトアセット（.cs）の中のMonoScriptは、常にこのfileIDで参照される
        private const string ScriptLinePrefix = "  m_Script: {fileID: 11500000, guid: ";
        private const string ScriptLineSuffix = ", type: 3}";
        private const string ClassIdentifierPrefix = "  m_EditorClassIdentifier:";

        private const int GuidLength = 32;

        /// <summary>
        /// 参照の切れたこのコンポーネントが指しているGUIDを集める。
        ///
        /// isResolvableはGUIDがプロジェクト内のアセットを指しているかを答える。
        /// 指している場合は別のスクリプトが生きているということなので、フィールドが似ていても対象にしない。
        /// 戻り値は見つけたコンポーネントの数
        /// </summary>
        public static int CollectStaleGuids(
            string yaml,
            string currentGuid,
            Func<string, bool> isResolvable,
            ISet<string> staleGuids)
        {
            if (isResolvable == null)
            {
                throw new ArgumentNullException(nameof(isResolvable));
            }

            if (staleGuids == null)
            {
                throw new ArgumentNullException(nameof(staleGuids));
            }

            if (string.IsNullOrEmpty(yaml) || yaml.IndexOf(Marker, StringComparison.Ordinal) < 0)
            {
                return 0;
            }

            var found = 0;
            var document = new DocumentState();
            var position = 0;

            while (position < yaml.Length)
            {
                var lineEnd = FindLineEnd(yaml, position, out var next);

                if (StartsWith(yaml, position, lineEnd, DocumentPrefix))
                {
                    found += document.Finish(currentGuid, isResolvable, staleGuids);
                    document = new DocumentState
                    {
                        IsMonoBehaviour = StartsWith(yaml, position, lineEnd, MonoBehaviourDocumentPrefix),
                    };
                }
                else if (document.IsMonoBehaviour)
                {
                    document.ReadLine(yaml, position, lineEnd);
                }

                position = next;
            }

            found += document.Finish(currentGuid, isResolvable, staleGuids);
            return found;
        }

        /// <summary>
        /// staleGuidsのいずれかを指すスクリプト参照を、currentGuidへ書き換えたテキストを返す。
        ///
        /// 書き換えるのは`m_Script`の行のGUIDだけで、改行を含むほかの部分は元のまま残す。
        /// 設定値を持たない参照（ネストしたプレハブの中のコンポーネントを指す、strippedと付いた記述）も
        /// 同じGUIDを持つため、フィールドの有無は見ずにGUIDだけで判断する。
        /// replacedは書き換えた箇所の数
        /// </summary>
        public static string ReplaceScriptGuids(
            string yaml,
            ICollection<string> staleGuids,
            string currentGuid,
            out int replaced)
        {
            replaced = 0;

            if (string.IsNullOrEmpty(yaml)
                || staleGuids == null
                || staleGuids.Count == 0
                || !IsGuid(currentGuid))
            {
                return yaml;
            }

            StringBuilder builder = null;
            var copiedUntil = 0;
            var position = 0;

            while (position < yaml.Length)
            {
                var lineEnd = FindLineEnd(yaml, position, out var next);

                if (TryReadScriptGuid(yaml, position, lineEnd, out var guidStart)
                    && staleGuids.Contains(yaml.Substring(guidStart, GuidLength)))
                {
                    if (builder == null)
                    {
                        builder = new StringBuilder(yaml.Length);
                    }

                    builder.Append(yaml, copiedUntil, guidStart - copiedUntil);
                    builder.Append(currentGuid);
                    copiedUntil = guidStart + GuidLength;
                    replaced++;
                }

                position = next;
            }

            if (builder == null)
            {
                return yaml;
            }

            builder.Append(yaml, copiedUntil, yaml.Length - copiedUntil);
            return builder.ToString();
        }

        /// <summary>UnityのGUIDの表記（16進の小文字32桁）かどうか</summary>
        public static bool IsGuid(string value)
        {
            return value != null && value.Length == GuidLength && IsGuid(value, 0);
        }

        private static bool IsGuid(string text, int start)
        {
            if (start + GuidLength > text.Length)
            {
                return false;
            }

            for (var i = start; i < start + GuidLength; i++)
            {
                var c = text[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 行の終わり（改行文字の手前）を返し、nextへ次の行の先頭を入れる。
        /// 改行はLFとCRLFのどちらでもよい
        /// </summary>
        private static int FindLineEnd(string text, int start, out int next)
        {
            var newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                next = text.Length;
                return text.Length;
            }

            next = newline + 1;
            return newline > start && text[newline - 1] == '\r' ? newline - 1 : newline;
        }

        private static bool StartsWith(string text, int lineStart, int lineEnd, string prefix)
        {
            return lineEnd - lineStart >= prefix.Length
                && string.CompareOrdinal(text, lineStart, prefix, 0, prefix.Length) == 0;
        }

        private static bool TryReadScriptGuid(string text, int lineStart, int lineEnd, out int guidStart)
        {
            guidStart = lineStart + ScriptLinePrefix.Length;

            return lineEnd - lineStart == ScriptLinePrefix.Length + GuidLength + ScriptLineSuffix.Length
                && StartsWith(text, lineStart, lineEnd, ScriptLinePrefix)
                && IsGuid(text, guidStart)
                && string.CompareOrdinal(
                    text, guidStart + GuidLength, ScriptLineSuffix, 0, ScriptLineSuffix.Length) == 0;
        }

        /// <summary>
        /// MonoBehaviour1つ分の記述を読み進めるあいだの状態
        /// </summary>
        private struct DocumentState
        {
            public bool IsMonoBehaviour;

            private string _scriptGuid;
            private bool _otherClass;
            private int _signatureHits;

            public void ReadLine(string text, int lineStart, int lineEnd)
            {
                if (TryReadScriptGuid(text, lineStart, lineEnd, out var guidStart))
                {
                    _scriptGuid = text.Substring(guidStart, GuidLength);
                    return;
                }

                if (StartsWith(text, lineStart, lineEnd, ClassIdentifierPrefix))
                {
                    // Unityの版によっては、ここへ「アセンブリ名::名前空間.クラス名」が書き残される。
                    // 別のクラスの名前があれば、フィールドが一致していても別物である。
                    // 空の場合は判断材料にならないため、フィールドだけで決める
                    var valueStart = lineStart + ClassIdentifierPrefix.Length;
                    var value = text.Substring(valueStart, lineEnd - valueStart).Trim();
                    _otherClass = value.Length > 0 && !value.EndsWith(ComponentClassName, StringComparison.Ordinal);
                    return;
                }

                // コンポーネント直下のフィールドは字下げが2文字で、リストの要素や入れ子はそれより深い
                foreach (var field in SignatureFields)
                {
                    if (lineEnd - lineStart > field.Length + 2
                        && text[lineStart] == ' '
                        && text[lineStart + 1] == ' '
                        && string.CompareOrdinal(text, lineStart + 2, field, 0, field.Length) == 0
                        && text[lineStart + 2 + field.Length] == ':')
                    {
                        _signatureHits++;
                        return;
                    }
                }
            }

            public int Finish(string currentGuid, Func<string, bool> isResolvable, ISet<string> staleGuids)
            {
                if (!IsMonoBehaviour
                    || _scriptGuid == null
                    || _otherClass
                    || _signatureHits != SignatureFields.Length
                    || string.Equals(_scriptGuid, currentGuid, StringComparison.Ordinal))
                {
                    return 0;
                }

                // 一度対象と分かったGUIDは問い合わせ直さない
                if (!staleGuids.Contains(_scriptGuid))
                {
                    if (isResolvable(_scriptGuid))
                    {
                        return 0;
                    }

                    staleGuids.Add(_scriptGuid);
                }

                return 1;
            }
        }
    }
}
