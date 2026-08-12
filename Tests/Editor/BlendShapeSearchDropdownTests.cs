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
    }
}
