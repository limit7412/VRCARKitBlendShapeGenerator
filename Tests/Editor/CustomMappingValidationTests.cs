using System.Collections.Generic;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    public class CustomMappingValidationTests
    {
        private static CustomBlendShapeMapping Mapping(string arkitName)
        {
            return new CustomBlendShapeMapping { arkitName = arkitName, enabled = true };
        }

        private static CustomBlendShapeMapping DisabledMapping(string arkitName)
        {
            return new CustomBlendShapeMapping { arkitName = arkitName, enabled = false };
        }

        [Test]
        public void GetDuplicateArkitNames_ReturnsEmpty_WhenNoDuplicates()
        {
            var mappings = new List<CustomBlendShapeMapping>
            {
                Mapping("eyeBlinkLeft"),
                Mapping("eyeBlinkRight"),
            };

            Assert.That(CustomMappingValidation.GetDuplicateArkitNames(mappings), Is.Empty);
        }

        [Test]
        public void GetDuplicateArkitNames_DetectsDuplicates_ByExactName()
        {
            var mappings = new List<CustomBlendShapeMapping>
            {
                Mapping("eyeBlinkLeft"),
                Mapping("eyeBlinkLeft"),
                Mapping("jawOpen"),
            };

            Assert.That(
                CustomMappingValidation.GetDuplicateArkitNames(mappings),
                Is.EqualTo(new[] { "eyeBlinkLeft" }));
        }

        [Test]
        public void GetDuplicateArkitNames_TreatsSurroundingWhitespaceAsDifferentName()
        {
            // 名前は加工せず完全一致で比較する。生成側も同じ規則なので、
            // " eyeBlinkLeft " は別名として別のシェイプを作る（重複ではない）
            var mappings = new List<CustomBlendShapeMapping>
            {
                Mapping("eyeBlinkLeft"),
                Mapping(" eyeBlinkLeft "),
            };

            Assert.That(CustomMappingValidation.GetDuplicateArkitNames(mappings), Is.Empty);
        }

        [Test]
        public void GetDuplicateArkitNames_IgnoresNullAndEmptyEntries()
        {
            var mappings = new List<CustomBlendShapeMapping>
            {
                null,
                Mapping(null),
                Mapping(""),
                Mapping("  "),
                Mapping("jawOpen"),
            };

            Assert.That(CustomMappingValidation.GetDuplicateArkitNames(mappings), Is.Empty);
        }

        [Test]
        public void GetDuplicateArkitNames_IgnoresDisabledMapping_AgainstEnabledSameName()
        {
            // 無効なマッピングは生成側が読み飛ばすため、同名でも重複にしない
            // （差し替え前の定義を無効のまま取り置きできる）
            var mappings = new List<CustomBlendShapeMapping>
            {
                Mapping("jawOpen"),
                DisabledMapping("jawOpen"),
            };

            Assert.That(CustomMappingValidation.GetDuplicateArkitNames(mappings), Is.Empty);
        }

        [Test]
        public void GetDuplicateArkitNames_IgnoresDuplicatesAmongDisabledMappings()
        {
            var mappings = new List<CustomBlendShapeMapping>
            {
                DisabledMapping("jawOpen"),
                DisabledMapping("jawOpen"),
            };

            Assert.That(CustomMappingValidation.GetDuplicateArkitNames(mappings), Is.Empty);
        }

        [Test]
        public void GetDuplicateArkitNames_DetectsDuplicates_AmongEnabledMappingsOnly()
        {
            var mappings = new List<CustomBlendShapeMapping>
            {
                Mapping("jawOpen"),
                DisabledMapping("eyeBlinkLeft"),
                Mapping("jawOpen"),
                DisabledMapping("eyeBlinkLeft"),
            };

            Assert.That(
                CustomMappingValidation.GetDuplicateArkitNames(mappings),
                Is.EqualTo(new[] { "jawOpen" }));
        }

        [Test]
        public void HasDuplicateArkitNames_ReturnsFalse_ForNullList()
        {
            Assert.That(CustomMappingValidation.HasDuplicateArkitNames(null, out var duplicates), Is.False);
            Assert.That(duplicates, Is.Empty);
        }
    }
}
