using System.Collections.Generic;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Presentation;

namespace ARKitBlendShapeGenerator.Tests
{
    public class BlendShapeSearchDropdownTests
    {
        private static readonly List<string> Available = new List<string> { "vrc.v_aa", "まばたき" };

        [Test]
        public void ResolveState_ReturnsEmpty_WhenValueIsNotSet()
        {
            Assert.That(
                BlendShapeSearchDropdown.ResolveState(null, Available),
                Is.EqualTo(BlendShapeSearchDropdown.SourceValueState.Empty));
            Assert.That(
                BlendShapeSearchDropdown.ResolveState("", Available),
                Is.EqualTo(BlendShapeSearchDropdown.SourceValueState.Empty));
        }

        [Test]
        public void ResolveState_ReturnsFound_WhenValueExistsInMesh()
        {
            Assert.That(
                BlendShapeSearchDropdown.ResolveState("まばたき", Available),
                Is.EqualTo(BlendShapeSearchDropdown.SourceValueState.Found));
        }

        [Test]
        public void ResolveState_ReturnsMissing_WhenValueIsNotInMesh()
        {
            Assert.That(
                BlendShapeSearchDropdown.ResolveState("vrc.v_zz", Available),
                Is.EqualTo(BlendShapeSearchDropdown.SourceValueState.Missing));
        }

        [Test]
        public void ResolveState_ReturnsMissing_WhenAvailableListIsNull()
        {
            Assert.That(
                BlendShapeSearchDropdown.ResolveState("まばたき", null),
                Is.EqualTo(BlendShapeSearchDropdown.SourceValueState.Missing));
        }

        [Test]
        public void ResolveState_ReturnsMissing_WhenValueHasSurroundingWhitespace()
        {
            // 生成側と同じく完全一致で照合するため未検出になる。
            // 表示と実挙動が一致することが重要で、ここで救済すると設定値の誤りが隠れる
            Assert.That(
                BlendShapeSearchDropdown.ResolveState(" まばたき ", Available),
                Is.EqualTo(BlendShapeSearchDropdown.SourceValueState.Missing));
        }

        [Test]
        public void ResolveState_ReturnsFound_WhenMeshNameItselfHasSurroundingWhitespace()
        {
            // メッシュ側に空白付きの名前が実在すれば、そのまま指定できる
            var available = new List<string> { " まばたき " };
            Assert.That(
                BlendShapeSearchDropdown.ResolveState(" まばたき ", available),
                Is.EqualTo(BlendShapeSearchDropdown.SourceValueState.Found));
        }

        [Test]
        public void ResolveState_KeepsInternalSpaces()
        {
            // メッシュ由来の名前は加工しないため、名前中間のスペースはそのまま一致する
            var available = new List<string> { "mouth smile" };
            Assert.That(
                BlendShapeSearchDropdown.ResolveState("mouth smile", available),
                Is.EqualTo(BlendShapeSearchDropdown.SourceValueState.Found));
        }
    }
}
