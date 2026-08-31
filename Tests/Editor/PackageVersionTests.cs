using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// リリースのタグと手元の版を比べる規則の検証。
    ///
    /// 誤って新しいと判断すると、更新の必要が無い利用者を動かしてしまう。
    /// 解釈できないものを黙って通さないことと、プレリリースを新しい版として扱わないことを固定化する。
    ///
    /// 手元の版とタグでは受け付ける形が違う。
    /// プレリリースを配布しているため、手元の版だけは`X.Y.Z-testN`を解釈できなければならない。
    /// </summary>
    public class PackageVersionTests
    {
        [TestCase("0.1.9", 0, 1, 9)]
        [TestCase("v0.1.9", 0, 1, 9)]
        [TestCase("V1.2.3", 1, 2, 3)]
        [TestCase("1.2", 1, 2, 0)]
        [TestCase("2", 2, 0, 0)]
        [TestCase(" 0.1.9 ", 0, 1, 9)]
        [TestCase("0.10.0", 0, 10, 0)]
        public void TryParse_AcceptsStableVersions(string text, int major, int minor, int patch)
        {
            Assert.That(PackageVersion.TryParse(text, out var version), Is.True);
            Assert.That(version.Major, Is.EqualTo(major));
            Assert.That(version.Minor, Is.EqualTo(minor));
            Assert.That(version.Patch, Is.EqualTo(patch));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("v")]
        [TestCase("0.1.9.1")]
        [TestCase("0..9")]
        [TestCase("0.1.x")]
        [TestCase("-1.0.0")]
        [TestCase("+1.0.0")]
        [TestCase("1,000.0.0")]
        [TestCase("latest")]
        public void TryParse_RejectsWhatItCannotOrder(string text)
        {
            Assert.That(PackageVersion.TryParse(text, out _), Is.False);
        }

        // releases/latestはプレリリースを除いて返すが、応答の形が変わっても
        // プレリリースを新しい版として扱わないことを、解釈側でも保証する
        [TestCase("0.1.9-test2")]
        [TestCase("v0.2.0-rc.1")]
        [TestCase("0.1.9+build.5")]
        public void TryParse_RejectsPrereleaseAndBuildMetadata(string text)
        {
            Assert.That(PackageVersion.TryParse(text, out _), Is.False);
        }

        [TestCase("0.1.8", "0.1.9")]
        [TestCase("0.1.9", "0.2.0")]
        [TestCase("0.9.0", "1.0.0")]
        [TestCase("0.1.9", "v0.1.10")]
        [TestCase("0.1", "0.1.1")]
        public void IsUpdateAvailable_IsTrue_WhenTheReleaseIsNewer(string current, string latest)
        {
            Assert.That(PackageVersion.IsUpdateAvailable(current, latest), Is.True);
        }

        [TestCase("0.1.9", "0.1.9")]
        [TestCase("0.1.9", "v0.1.9")]
        [TestCase("0.2.0", "0.1.9")]
        [TestCase("1.0.0", "0.9.9")]
        public void IsUpdateAvailable_IsFalse_WhenTheReleaseIsNotNewer(string current, string latest)
        {
            Assert.That(PackageVersion.IsUpdateAvailable(current, latest), Is.False);
        }

        [TestCase("0.1.10-test4", 0, 1, 10)]
        [TestCase("v0.1.10-test4", 0, 1, 10)]
        [TestCase(" 0.2.0-rc.1 ", 0, 2, 0)]
        public void TryParseInstalled_AcceptsAPrerelease(string text, int major, int minor, int patch)
        {
            Assert.That(PackageVersion.TryParseInstalled(text, out var version), Is.True);
            Assert.That(version.Major, Is.EqualTo(major));
            Assert.That(version.Minor, Is.EqualTo(minor));
            Assert.That(version.Patch, Is.EqualTo(patch));
            Assert.That(version.IsPrerelease, Is.True);
        }

        [Test]
        public void TryParseInstalled_AcceptsAStableVersion()
        {
            Assert.That(PackageVersion.TryParseInstalled("0.1.10", out var version), Is.True);
            Assert.That(version.IsPrerelease, Is.False);
        }

        // 接尾辞が空のものと、ビルドメタデータは手元の版でも受け付けない
        [TestCase("0.1.10-")]
        [TestCase("0.1.10+build.5")]
        [TestCase("-1.0.0")]
        [TestCase("unknown")]
        public void TryParseInstalled_RejectsWhatItCannotOrder(string text)
        {
            Assert.That(PackageVersion.TryParseInstalled(text, out _), Is.False);
        }

        // プレリリースを入れている利用者にも、安定版が出たら知らせる。
        // prerelease.ymlが`X.Y.Z-testN`をpackage.jsonへ書き込んで配布しているため、
        // ここを落とすとその利用者には通知が一度も出ない
        [TestCase("0.1.10-test4", "0.1.10")]
        [TestCase("0.1.10-test4", "v0.1.10")]
        [TestCase("0.1.10-test4", "0.1.11")]
        [TestCase("0.1.10-test4", "0.2.0")]
        public void IsUpdateAvailable_IsTrue_WhenAStableReleaseSupersedesAnInstalledPrerelease(
            string current, string latest)
        {
            Assert.That(PackageVersion.IsUpdateAvailable(current, latest), Is.True);
        }

        // プレリリースは次期バージョンの番号を持つ。
        // その手前の安定版はプレリリースより古く、更新にはあたらない
        [TestCase("0.1.10-test4", "0.1.9")]
        [TestCase("0.2.0-test1", "0.1.10")]
        public void IsUpdateAvailable_IsFalse_WhenTheReleaseIsOlderThanTheInstalledPrerelease(
            string current, string latest)
        {
            Assert.That(PackageVersion.IsUpdateAvailable(current, latest), Is.False);
        }

        // 解釈できない値では黙る。通知が出ないのは実害が無いが、誤った通知は利用者を無駄に動かす
        [TestCase(null, "0.1.9")]
        [TestCase("0.1.9", null)]
        [TestCase("unknown", "0.1.9")]
        [TestCase("0.1.9", "0.2.0-test1")]
        public void IsUpdateAvailable_IsFalse_WhenEitherSideCannotBeParsed(string current, string latest)
        {
            Assert.That(PackageVersion.IsUpdateAvailable(current, latest), Is.False);
        }

        // タグは`v0.2.0`のようにも書ける。package.jsonのversionとは表記が揃わない
        [TestCase("0.2.0", "0.2.0")]
        [TestCase("0.2.0", "v0.2.0")]
        [TestCase("0.2", "0.2.0")]
        public void IsSameVersion_IsTrue_ForTheSameVersionWrittenDifferently(string left, string right)
        {
            Assert.That(PackageVersion.IsSameVersion(left, right), Is.True);
        }

        // 自己更新が取りに行くのは安定版だけなので、プレリリースは同じ版とみなさない
        [TestCase("0.2.0", "0.2.1")]
        [TestCase("0.2.0", "0.2.0-test1")]
        [TestCase("0.1.10-test4", "0.1.10")]
        [TestCase("0.2.0", "")]
        [TestCase(null, "0.2.0")]
        public void IsSameVersion_IsFalse_WhenTheyDifferOrCannotBeRead(string left, string right)
        {
            Assert.That(PackageVersion.IsSameVersion(left, right), Is.False);
        }
    }
}
