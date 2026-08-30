using System.Linq;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 自己更新の段取りの検証。
    ///
    /// 誤ると利用者のプロジェクトからファイルが消えるため、
    /// 消してよいものの選び方と、更新してよい置かれ方の判定を確かめる。
    /// </summary>
    public class SelfUpdatePlanTests
    {
        private const string Root = SelfUpdatePlan.InstallRoot;

        [Test]
        public void BoothAssetName_MatchesTheNameAttachedToARelease()
        {
            Assert.That(SelfUpdatePlan.BoothAssetName("0.2.0"), Is.EqualTo("VRCARKitBlendShapeGenerator_0.2.0.zip"));
        }

        [Test]
        public void CanSelfUpdate_WhenABoothInstallSitsAtThePackagedPath()
        {
            Assert.That(SelfUpdatePlan.CanSelfUpdate(InstallLocation.Booth, Root), Is.True);
        }

        // 取り込み先はunitypackageの側で決まっており、手元の位置へは追従しない。
        // 動かされたフォルダのまま実行すると、同じアセンブリが二組できる
        [Test]
        public void CannotSelfUpdate_WhenTheFolderHasBeenMoved()
        {
            Assert.That(SelfUpdatePlan.CanSelfUpdate(InstallLocation.Booth, "Assets/MyAssets/ARKitGenerator"), Is.False);
        }

        [TestCase(InstallLocation.Vpm)]
        [TestCase(InstallLocation.Unknown)]
        public void CannotSelfUpdate_OutsideABoothInstall(InstallLocation location)
        {
            Assert.That(SelfUpdatePlan.CanSelfUpdate(location, Root), Is.False);
        }

        [Test]
        public void TrySelectBoothAsset_PicksTheZipForTheTag()
        {
            var assets = new[]
            {
                new ReleaseAsset("com.qazx7412.kx-vrc-arkit-blendshape-generator-0.2.0.zip", "https://example/vpm", ""),
                new ReleaseAsset("VRCARKitBlendShapeGenerator_0.2.0.zip", "https://example/booth", "sha256:abc"),
            };

            Assert.That(SelfUpdatePlan.TrySelectBoothAsset(assets, "0.2.0", out var selected), Is.True);
            Assert.That(selected.DownloadUrl, Is.EqualTo("https://example/booth"));
            Assert.That(selected.Digest, Is.EqualTo("sha256:abc"));
        }

        [Test]
        public void TrySelectBoothAsset_Fails_WhenTheReleaseHasNoBoothZip()
        {
            var assets = new[]
            {
                new ReleaseAsset("com.qazx7412.kx-vrc-arkit-blendshape-generator-0.2.0.zip", "https://example/vpm", ""),
            };

            Assert.That(SelfUpdatePlan.TrySelectBoothAsset(assets, "0.2.0", out _), Is.False);
        }

        [Test]
        public void TrySelectBoothAsset_Fails_WhenTheAssetHasNoUrl()
        {
            var assets = new[] { new ReleaseAsset("VRCARKitBlendShapeGenerator_0.2.0.zip", null, "") };

            Assert.That(SelfUpdatePlan.TrySelectBoothAsset(assets, "0.2.0", out _), Is.False);
        }

        [Test]
        public void SelectObsoleteAssets_PicksTheFilesTheNewPackageNoLongerHas()
        {
            var installed = new[]
            {
                Root + "/Editor/Domain/Removed.cs",
                Root + "/Editor/Domain/Kept.cs",
                Root + "/package.json",
            };
            var packaged = new[]
            {
                Root,
                Root + "/Editor/Domain/Kept.cs",
                Root + "/package.json",
            };

            var obsolete = SelfUpdatePlan.SelectObsoleteAssets(installed, packaged);

            Assert.That(obsolete, Is.EqualTo(new[] { Root + "/Editor/Domain/Removed.cs" }));
        }

        // Windowsで並ぶ`\`混じりのパスでも、同じアセットは同じものとして扱う
        [Test]
        public void SelectObsoleteAssets_IgnoresTheDirectionOfSeparators()
        {
            var installed = new[] { Root.Replace('/', '\\') + "\\Editor\\Domain\\Kept.cs" };
            var packaged = new[] { Root + "/Editor/Domain/Kept.cs" };

            Assert.That(SelfUpdatePlan.SelectObsoleteAssets(installed, packaged), Is.Empty);
        }

        // 新しい版の一覧を読めなかった場合に、手元を消し尽くさないこと
        [Test]
        public void SelectObsoleteAssets_KeepsEverything_WhenTheNewPackageListIsEmpty()
        {
            var installed = new[] { Root + "/Editor/Domain/Kept.cs" };

            Assert.That(SelfUpdatePlan.SelectObsoleteAssets(installed, Enumerable.Empty<string>()), Is.Empty);
        }
    }
}
