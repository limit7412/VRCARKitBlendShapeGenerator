using NUnit.Framework;
using ARKitBlendShapeGenerator.Infra;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 更新確認が読み取る2つのJSON（releases/latestの応答とpackage.json）の解釈の検証。
    ///
    /// どちらも手元で作ったものではないため、想定と違う形が来ても
    /// 例外を投げずに「分からなかった」として返ることを確かめる。
    /// </summary>
    public class UpdatePayloadTests
    {
        [Test]
        public void TryParseTag_ReadsTheTagFromAReleaseResponse()
        {
            const string json = @"{""tag_name"":""0.1.9"",""name"":""0.1.9"",""prerelease"":false}";

            Assert.That(UpdateCheck.TryParseTag(json, out var tag), Is.True);
            Assert.That(tag, Is.EqualTo("0.1.9"));
        }

        // 応答にはアセット一覧などが並ぶ。必要なキー以外は読み飛ばせること
        [Test]
        public void TryParseTag_IgnoresTheOtherFields()
        {
            const string json =
                @"{""id"":373145658,""tag_name"":""0.2.0"",""assets"":[{""name"":""package.zip""}],""body"":""...""}";

            Assert.That(UpdateCheck.TryParseTag(json, out var tag), Is.True);
            Assert.That(tag, Is.EqualTo("0.2.0"));
        }

        // releases/latestはプレリリースを除いて返すが、除かれなかった場合も通さない
        [Test]
        public void TryParseTag_RejectsAPrereleaseTag()
        {
            const string json = @"{""tag_name"":""0.1.9-test2"",""prerelease"":true}";

            Assert.That(UpdateCheck.TryParseTag(json, out var tag), Is.False);
            Assert.That(tag, Is.Null);
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json")]
        [TestCase(@"{""message"":""Not Found""}")]
        [TestCase(@"{""tag_name"":""""}")]
        public void TryParseTag_Fails_WhenTheResponseIsNotAUsableRelease(string json)
        {
            Assert.That(UpdateCheck.TryParseTag(json, out var tag), Is.False);
            Assert.That(tag, Is.Null);
        }

        // 自己更新は同じ応答からアセットを選ぶ
        [Test]
        public void TryParseRelease_ReadsTheAttachedAssets()
        {
            const string json = @"{""tag_name"":""0.2.0"",""assets"":[" +
                @"{""name"":""VRCARKitBlendShapeGenerator_0.2.0.zip""," +
                @"""browser_download_url"":""https://example/booth.zip""," +
                @"""digest"":""sha256:abc""}]}";

            Assert.That(UpdateCheck.TryParseRelease(json, out var tag, out var assets), Is.True);
            Assert.That(tag, Is.EqualTo("0.2.0"));
            Assert.That(assets.Length, Is.EqualTo(1));
            Assert.That(assets[0].Name, Is.EqualTo("VRCARKitBlendShapeGenerator_0.2.0.zip"));
            Assert.That(assets[0].DownloadUrl, Is.EqualTo("https://example/booth.zip"));
            Assert.That(assets[0].Digest, Is.EqualTo("sha256:abc"));
        }

        // ダイジェストの付かない応答もあるが、アセットの選択そのものは行える
        [Test]
        public void TryParseRelease_AcceptsAnAssetWithoutADigest()
        {
            const string json = @"{""tag_name"":""0.2.0"",""assets"":[" +
                @"{""name"":""VRCARKitBlendShapeGenerator_0.2.0.zip""," +
                @"""browser_download_url"":""https://example/booth.zip""}]}";

            Assert.That(UpdateCheck.TryParseRelease(json, out _, out var assets), Is.True);
            Assert.That(assets.Length, Is.EqualTo(1));
            Assert.That(string.IsNullOrEmpty(assets[0].Digest), Is.True);
        }

        [Test]
        public void TryParseRelease_ReturnsNoAssets_WhenTheResponseHasNone()
        {
            Assert.That(UpdateCheck.TryParseRelease(@"{""tag_name"":""0.2.0""}", out _, out var assets), Is.True);
            Assert.That(assets, Is.Empty);
        }

        [Test]
        public void TryParseVersion_ReadsTheVersionFromAPackageManifest()
        {
            const string json = @"{""name"":""com.qazx7412.kx-vrc-arkit-blendshape-generator"",""version"":""0.1.9""}";

            Assert.That(PackageLocation.TryParseVersion(json, out var version), Is.True);
            Assert.That(version, Is.EqualTo("0.1.9"));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("not json")]
        [TestCase(@"{""name"":""com.example.package""}")]
        [TestCase(@"{""version"":""  ""}")]
        public void TryParseVersion_Fails_WhenTheManifestHasNoVersion(string json)
        {
            Assert.That(PackageLocation.TryParseVersion(json, out var version), Is.False);
            Assert.That(version, Is.Null);
        }
    }
}
