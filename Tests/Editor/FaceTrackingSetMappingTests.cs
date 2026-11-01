using System;
using System.Linq;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 他のパッケージへ公開する対応付け（FaceTrackingSetMapping）の検証。
    /// Kx VRC NativeFT Manager がリフレクションで呼ぶので、型とメソッドの形が変わっていないことも確かめる
    /// </summary>
    public class FaceTrackingSetMappingTests
    {
        [Test]
        public void KeyNames_AreTheSameAsTheTable()
        {
            Assert.That(FaceTrackingSetMapping.KeyCount, Is.EqualTo(FaceTrackingBlendshapesSetTable.KeyCount));
            Assert.That(
                FaceTrackingSetMapping.GetKeyNames(),
                Is.EqualTo(FaceTrackingBlendshapesSetTable.GetKeyNames().ToArray()));
        }

        [Test]
        public void FindBlendShapeNames_AssignsArkitShapes_OfAMeshThisToolDidNotTouch()
        {
            var names = FaceTrackingSetMapping.FindBlendShapeNames(ARKitBlendShapeNames.GetAll());

            Assert.That(names.Length, Is.EqualTo(FaceTrackingSetMapping.KeyCount));
            Assert.That(NameFor(names, "EyeClosedLeft"), Is.EqualTo("eyeBlinkLeft"));
            Assert.That(NameFor(names, "MouthCornerPullRight"), Is.EqualTo("mouthSmileRight"));
            Assert.That(NameFor(names, "JawOpen"), Is.EqualTo("jawOpen"));
            Assert.That(NameFor(names, "TongueRoll"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void FindBlendShapeNames_FindsCandidatesIgnoringCase_AndKeepsTheMeshSpelling()
        {
            var names = FaceTrackingSetMapping.FindBlendShapeNames(new[] { "EyeBlinkLeft", "JAWOPEN", "jawopen" });

            Assert.That(NameFor(names, "EyeClosedLeft"), Is.EqualTo("EyeBlinkLeft"));
            // 大文字小文字だけが違う名前が複数あるときは、メッシュ上で先にあるもの
            Assert.That(NameFor(names, "JawOpen"), Is.EqualTo("JAWOPEN"));
        }

        [Test]
        public void FindBlendShapeNames_PrefersAnExactCandidate_OverAnEarlierCaseInsensitiveOne()
        {
            // キー名の完全一致（JawOpen）が無くても、別名の完全一致（jawOpen）を大文字小文字違いのキー名より優先する
            var names = FaceTrackingSetMapping.FindBlendShapeNames(new[] { "JAWOPEN", "jawOpen" });

            Assert.That(NameFor(names, "JawOpen"), Is.EqualTo("jawOpen"));
        }

        [Test]
        public void FindBlendShapeNames_IgnoresNullAndEmptyNames()
        {
            var names = FaceTrackingSetMapping.FindBlendShapeNames(new[] { null, string.Empty });

            Assert.That(names.All(name => name == string.Empty), Is.True);
            Assert.That(FaceTrackingSetMapping.FindBlendShapeNames(null).Length, Is.EqualTo(FaceTrackingSetMapping.KeyCount));
        }

        [Test]
        public void PublicShape_IsStable_ForReflection()
        {
            var type = typeof(FaceTrackingBlendshapesSetTable).Assembly
                .GetType("ARKitBlendShapeGenerator.FaceTrackingSetMapping", false);
            Assert.That(type, Is.Not.Null);

            var find = type.GetMethod("FindBlendShapeNames", new[] { typeof(System.Collections.Generic.IEnumerable<string>) });
            Assert.That(find, Is.Not.Null);
            Assert.That(find.IsStatic && find.IsPublic, Is.True);
            Assert.That(find.ReturnType, Is.EqualTo(typeof(string[])));

            var keyNames = type.GetMethod("GetKeyNames", Type.EmptyTypes);
            Assert.That(keyNames, Is.Not.Null);
            Assert.That(keyNames.ReturnType, Is.EqualTo(typeof(string[])));
        }

        private static string NameFor(string[] names, string key)
        {
            int index = Array.IndexOf(FaceTrackingSetMapping.GetKeyNames(), key);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), key);
            return names[index];
        }
    }
}
