using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// asmdefの位置からインストール形態を見分ける規則の検証。
    ///
    /// VPM版とbooth版では更新の手段が違い、案内の文面もそれで決まる。
    /// booth版は利用者がフォルダを移動できるため、固定のパスではなく
    /// asmdefの位置を起点に判別できることを確かめる。
    /// </summary>
    public class PackageInstallationTests
    {
        private const string VpmAsmdefPath =
            "Packages/com.qazx7412.kx-vrc-arkit-blendshape-generator/Editor/ARKitBlendShapeGenerator.Editor.asmdef";

        private const string BoothAsmdefPath =
            "Assets/AtelierKairox/VRCARKitBlendShapeGenerator/Editor/ARKitBlendShapeGenerator.Editor.asmdef";

        [Test]
        public void TryResolve_ReadsAVpmInstall()
        {
            Assert.That(PackageInstallation.TryResolve(VpmAsmdefPath, out var location, out var root), Is.True);
            Assert.That(location, Is.EqualTo(InstallLocation.Vpm));
            Assert.That(root, Is.EqualTo("Packages/com.qazx7412.kx-vrc-arkit-blendshape-generator"));
        }

        [Test]
        public void TryResolve_ReadsABoothInstall()
        {
            Assert.That(PackageInstallation.TryResolve(BoothAsmdefPath, out var location, out var root), Is.True);
            Assert.That(location, Is.EqualTo(InstallLocation.Booth));
            Assert.That(root, Is.EqualTo("Assets/AtelierKairox/VRCARKitBlendShapeGenerator"));
        }

        // booth版はunitypackageで取り込まれるため、利用者が任意の場所へ移動できる
        [Test]
        public void TryResolve_FollowsABoothInstallThatWasMoved()
        {
            const string movedPath = "Assets/ThirdParty/Tools/ARKitGen/Editor/ARKitBlendShapeGenerator.Editor.asmdef";

            Assert.That(PackageInstallation.TryResolve(movedPath, out var location, out var root), Is.True);
            Assert.That(location, Is.EqualTo(InstallLocation.Booth));
            Assert.That(root, Is.EqualTo("Assets/ThirdParty/Tools/ARKitGen"));
        }

        [Test]
        public void TryResolve_NormalizesWindowsSeparators()
        {
            const string windowsPath =
                @"Assets\AtelierKairox\VRCARKitBlendShapeGenerator\Editor\ARKitBlendShapeGenerator.Editor.asmdef";

            Assert.That(PackageInstallation.TryResolve(windowsPath, out var location, out var root), Is.True);
            Assert.That(location, Is.EqualTo(InstallLocation.Booth));
            Assert.That(root, Is.EqualTo("Assets/AtelierKairox/VRCARKitBlendShapeGenerator"));
        }

        // ルートは分かるが形態を決められない場合。案内の文面だけが一般的なものになる
        [Test]
        public void TryResolve_ReportsUnknown_ForAnUnexpectedRoot()
        {
            const string path = "Library/PackageCache/whatever/Editor/ARKitBlendShapeGenerator.Editor.asmdef";

            Assert.That(PackageInstallation.TryResolve(path, out var location, out var root), Is.True);
            Assert.That(location, Is.EqualTo(InstallLocation.Unknown));
            Assert.That(root, Is.EqualTo("Library/PackageCache/whatever"));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("ARKitBlendShapeGenerator.Editor.asmdef")]
        [TestCase("Packages/whatever/ARKitBlendShapeGenerator.Editor.asmdef")]
        [TestCase("Packages/whatever/Runtime/ARKitBlendShapeGenerator.Runtime.asmdef")]
        public void TryResolve_Fails_WhenThePathIsNotUnderAnEditorDirectory(string path)
        {
            Assert.That(PackageInstallation.TryResolve(path, out var location, out var root), Is.False);
            Assert.That(location, Is.EqualTo(InstallLocation.Unknown));
            Assert.That(root, Is.Null);
        }
    }
}
