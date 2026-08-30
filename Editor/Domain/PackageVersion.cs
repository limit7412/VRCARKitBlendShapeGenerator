using System;
using System.Globalization;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// リリースのタグとpackage.jsonのversionを比較するための版数。
    ///
    /// 扱うのは`0.1.9`形式の安定版だけで、`0.1.9-test2`のようなプレリリースは解釈しない。
    /// releases/latestはプレリリースを除いて返すが、更新の通知は
    /// 「安定版が出たときだけ出す」ことを解釈側でも守る
    /// </summary>
    internal readonly struct PackageVersion : IEquatable<PackageVersion>, IComparable<PackageVersion>
    {
        private const int ComponentCount = 3;

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        private PackageVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        /// <summary>
        /// `0.1.9` `v0.1.9` `0.1` `1` を受け付け、省略された下位の要素は0として扱う。
        ///
        /// プレリリースやビルドメタデータの接尾辞（`-test2` `+build`）が付くものは、
        /// 安定版との大小を決められないため解釈しない
        /// </summary>
        public static bool TryParse(string text, out PackageVersion version)
        {
            version = default;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var body = text.Trim();
            if (body.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                body = body.Substring(1);
            }

            if (body.IndexOf('-') >= 0 || body.IndexOf('+') >= 0)
            {
                return false;
            }

            var parts = body.Split('.');
            if (parts.Length > ComponentCount)
            {
                return false;
            }

            var components = new int[ComponentCount];
            for (var i = 0; i < parts.Length; i++)
            {
                // NumberStyles.Noneを指定して、符号や桁区切り、前後の空白が混ざったものを弾く
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out components[i]))
                {
                    return false;
                }
            }

            version = new PackageVersion(components[0], components[1], components[2]);
            return true;
        }

        /// <summary>
        /// 手元の版と最新リリースのタグを比べ、更新が出ていれば真を返す。
        ///
        /// どちらかを解釈できないときは偽を返す。
        /// 通知は無くても実害が無い一方、誤った通知は利用者を無駄に動かすため、
        /// 判断できない場合は黙っている側へ倒す
        /// </summary>
        public static bool IsUpdateAvailable(string currentVersionText, string latestTagText)
        {
            if (!TryParse(currentVersionText, out var current) || !TryParse(latestTagText, out var latest))
            {
                return false;
            }

            return latest.CompareTo(current) > 0;
        }

        /// <summary>
        /// 2つの表記が同じ版を指すか。
        ///
        /// `v0.2.0`と`0.2.0`、`0.2`と`0.2.0`は同じ版として扱う。
        /// タグとpackage.jsonのversionは書き方が揃うとは限らないため、文字列では比べられない
        /// </summary>
        public static bool IsSameVersion(string leftText, string rightText)
        {
            return TryParse(leftText, out var left)
                && TryParse(rightText, out var right)
                && left.Equals(right);
        }

        public int CompareTo(PackageVersion other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            var minor = Minor.CompareTo(other.Minor);
            return minor != 0 ? minor : Patch.CompareTo(other.Patch);
        }

        public bool Equals(PackageVersion other)
        {
            return Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        }

        public override bool Equals(object obj)
        {
            return obj is PackageVersion other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Major * 397 ^ Minor) * 397 ^ Patch;
            }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, Patch);
        }
    }
}
