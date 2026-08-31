using System;
using System.Globalization;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// リリースのタグとpackage.jsonのversionを比較するための版数。
    ///
    /// 知らせる相手は安定版だけなので、リリースのタグは安定版しか受け付けない。
    /// releases/latestはプレリリースを除いて返すが、それを解釈側でも守る。
    ///
    /// 一方、手元の版はプレリリースになり得る。
    /// prerelease.ymlが`X.Y.Z-testN`をpackage.jsonへ書き込んで配布しているため、
    /// これを解釈できないと、プレリリースを入れた利用者には通知が一度も出ない。
    /// 同じX.Y.Zの安定版より前として並べ、安定版が出た時点で更新として扱う
    /// </summary>
    internal readonly struct PackageVersion : IEquatable<PackageVersion>, IComparable<PackageVersion>
    {
        private const int ComponentCount = 3;

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        /// <summary>
        /// プレリリース（`0.1.9-test2`）かどうか。
        ///
        /// 接尾辞そのものは持たない。
        /// プレリリースになり得るのは手元の版だけで、同じX.Y.Zのプレリリース同士を
        /// 並べる場面が無いため、どのプレリリースかを区別する必要がない
        /// </summary>
        public bool IsPrerelease { get; }

        private PackageVersion(int major, int minor, int patch, bool isPrerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            IsPrerelease = isPrerelease;
        }

        /// <summary>
        /// リリースのタグを解釈する。
        ///
        /// `0.1.9` `v0.1.9` `0.1` `1` を受け付け、省略された下位の要素は0として扱う。
        /// プレリリースの接尾辞（`-test2`）が付くものは解釈しない。
        /// 知らせるのは安定版が出たときだけなので、タグの側では受け付ける必要がない
        /// </summary>
        public static bool TryParse(string text, out PackageVersion version)
        {
            return TryParse(text, false, out version);
        }

        /// <summary>
        /// 手元に入っている版を解釈する。
        ///
        /// タグの側と違い、プレリリースの接尾辞（`-test2`）を受け付ける。
        /// 接尾辞の中身は見ず、同じX.Y.Zの安定版より前として扱う
        /// </summary>
        public static bool TryParseInstalled(string text, out PackageVersion version)
        {
            return TryParse(text, true, out version);
        }

        private static bool TryParse(string text, bool allowPrerelease, out PackageVersion version)
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

            // ビルドメタデータ（`+build`）はどちらの側でも受け付けない。
            // 同じX.Y.Zの版を並べる規則を決められない
            if (body.IndexOf('+') >= 0)
            {
                return false;
            }

            var isPrerelease = false;
            var hyphen = body.IndexOf('-');
            if (hyphen >= 0)
            {
                // 接尾辞が空の`0.1.9-`は形が違う。
                // 数値の側が空になる`-1.0.0`は、この後の数値の解釈で落ちる
                if (!allowPrerelease || hyphen == body.Length - 1)
                {
                    return false;
                }

                isPrerelease = true;
                body = body.Substring(0, hyphen);
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

            version = new PackageVersion(components[0], components[1], components[2], isPrerelease);
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
            if (!TryParseInstalled(currentVersionText, out var current) || !TryParse(latestTagText, out var latest))
            {
                return false;
            }

            return latest.CompareTo(current) > 0;
        }

        /// <summary>
        /// 2つの表記が同じ版を指すか。
        ///
        /// `v0.2.0`と`0.2.0`、`0.2`と`0.2.0`は同じ版として扱う。
        /// タグとpackage.jsonのversionは書き方が揃うとは限らないため、文字列では比べられない。
        /// 自己更新が取りに行くのは安定版だけなので、プレリリースは同じ版とみなさない
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
            if (minor != 0)
            {
                return minor;
            }

            var patch = Patch.CompareTo(other.Patch);
            if (patch != 0)
            {
                return patch;
            }

            // 同じX.Y.Zなら、プレリリースを安定版より前に置く。
            // `0.1.9-test2`を入れている利用者にとって、`0.1.9`の公開は更新にあたる
            return (IsPrerelease ? 0 : 1).CompareTo(other.IsPrerelease ? 0 : 1);
        }

        public bool Equals(PackageVersion other)
        {
            return Major == other.Major
                && Minor == other.Minor
                && Patch == other.Patch
                && IsPrerelease == other.IsPrerelease;
        }

        public override bool Equals(object obj)
        {
            return obj is PackageVersion other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Major * 397 ^ Minor) * 397 ^ Patch) * 397 ^ (IsPrerelease ? 1 : 0);
            }
        }

        /// <summary>
        /// 表示用。接尾辞は持っていないため、プレリリースであることだけを添える
        /// </summary>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                IsPrerelease ? "{0}.{1}.{2}-prerelease" : "{0}.{1}.{2}",
                Major, Minor, Patch);
        }
    }
}
