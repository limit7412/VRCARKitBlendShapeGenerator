using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// ポップアップを出すかどうかの判断の検証。
    ///
    /// 作業の途中へ割り込む形になるため、同じ版で繰り返し出さないことを確かめる。
    /// </summary>
    public class UpdateAnnouncementTests
    {
        [Test]
        public void ShouldAnnounce_WhenABoothInstallHasNotSeenTheVersion()
        {
            Assert.That(UpdateAnnouncement.ShouldAnnounce(InstallLocation.Booth, "0.2.0", null), Is.True);
            Assert.That(UpdateAnnouncement.ShouldAnnounce(InstallLocation.Booth, "0.2.0", string.Empty), Is.True);
        }

        [Test]
        public void ShouldNotAnnounce_TheSameVersionTwice()
        {
            Assert.That(UpdateAnnouncement.ShouldAnnounce(InstallLocation.Booth, "0.2.0", "0.2.0"), Is.False);
        }

        // 一度知らせた後に更に新しい版が出たら、その版については改めて知らせる
        [Test]
        public void ShouldAnnounce_AVersionNewerThanTheAnnouncedOne()
        {
            Assert.That(UpdateAnnouncement.ShouldAnnounce(InstallLocation.Booth, "0.3.0", "0.2.0"), Is.True);
        }

        [Test]
        public void ShouldNotAnnounce_WhenThereIsNoUpdate()
        {
            Assert.That(UpdateAnnouncement.ShouldAnnounce(InstallLocation.Booth, null, "0.2.0"), Is.False);
        }

        // VPM版の更新はVCC/ALCOMが担うため、こちらから割り込まない
        [TestCase(InstallLocation.Vpm)]
        [TestCase(InstallLocation.Unknown)]
        public void ShouldNotAnnounce_OutsideABoothInstall(InstallLocation location)
        {
            Assert.That(UpdateAnnouncement.ShouldAnnounce(location, "0.2.0", null), Is.False);
        }
    }
}
