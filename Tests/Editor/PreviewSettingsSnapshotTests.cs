using System.Collections.Generic;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// プレビュー設定の差分の分類を検証する。
    ///
    /// 連続値（スライダー）だけの差分は実行を遅らせてよく、それ以外はすぐ作り直す。
    /// 分類を誤ると「操作しても反映されない」か「重いまま」のどちらかになる。
    /// </summary>
    public class PreviewSettingsSnapshotTests
    {
        private static PreviewSettingsSnapshot CreateSnapshot()
        {
            return new PreviewSettingsSnapshot
            {
                IntensityMultiplier = 1.0f,
                BlendWidth = 0.02f,
                ProceduralMouthIntensity = 1.0f,
                MouthCancellationStrength = 1.0f,
                EnableLeftRightSplit = true,
                OverwriteExisting = false,
                EnableProceduralMouthShapes = false,
                EnableMouthCancellation = false,
                MouthCancellationSignature = 123,
                CustomMappingsSignature = 456,
                TargetRendererInstanceId = 789,
            };
        }

        [Test]
        public void CompareWith_ReturnsNone_WhenNothingChanged()
        {
            Assert.That(
                CreateSnapshot().CompareWith(CreateSnapshot()),
                Is.EqualTo(PreviewSettingsChange.None));
        }

        [Test]
        public void CompareWith_ReturnsStructural_WhenComparedWithNothing()
        {
            Assert.That(
                CreateSnapshot().CompareWith(null),
                Is.EqualTo(PreviewSettingsChange.Structural));
        }

        [TestCase("IntensityMultiplier")]
        [TestCase("BlendWidth")]
        [TestCase("ProceduralMouthIntensity")]
        [TestCase("MouthCancellationStrength")]
        public void CompareWith_ReturnsContinuous_WhenOnlyASliderValueChanged(string changedField)
        {
            var current = CreateSnapshot();
            switch (changedField)
            {
                case "IntensityMultiplier":
                    current.IntensityMultiplier += 0.1f;
                    break;
                case "BlendWidth":
                    current.BlendWidth += 0.01f;
                    break;
                case "ProceduralMouthIntensity":
                    current.ProceduralMouthIntensity += 0.1f;
                    break;
                case "MouthCancellationStrength":
                    current.MouthCancellationStrength -= 0.1f;
                    break;
            }

            Assert.That(
                current.CompareWith(CreateSnapshot()),
                Is.EqualTo(PreviewSettingsChange.Continuous));
        }

        [Test]
        public void CompareWith_ReturnsStructural_WhenAToggleChanged()
        {
            var current = CreateSnapshot();
            current.EnableLeftRightSplit = !current.EnableLeftRightSplit;

            Assert.That(
                current.CompareWith(CreateSnapshot()),
                Is.EqualTo(PreviewSettingsChange.Structural));
        }

        [Test]
        public void CompareWith_ReturnsStructural_WhenMappingContentChanged()
        {
            var current = CreateSnapshot();
            current.CustomMappingsSignature += 1;

            Assert.That(
                current.CompareWith(CreateSnapshot()),
                Is.EqualTo(PreviewSettingsChange.Structural));
        }

        [Test]
        public void CompareWith_PrefersStructural_WhenBothKindsChanged()
        {
            // 作り直しが必要な変更が混ざっていれば、遅らせずに作り直す
            var current = CreateSnapshot();
            current.IntensityMultiplier += 0.1f;
            current.OverwriteExisting = !current.OverwriteExisting;

            Assert.That(
                current.CompareWith(CreateSnapshot()),
                Is.EqualTo(PreviewSettingsChange.Structural));
        }

        [Test]
        public void BuildCustomMappingsSignature_ChangesWithTheMappingContent()
        {
            var mappings = new List<CustomBlendShapeMapping>
            {
                new CustomBlendShapeMapping
                {
                    arkitName = "eyeBlinkLeft",
                    enabled = true,
                    sources = new List<BlendShapeSource>
                    {
                        new BlendShapeSource { blendShapeName = "vrc.blink", weight = 1.0f },
                    },
                },
            };

            int baseline = PreviewSettingsSnapshot.BuildCustomMappingsSignature(mappings);

            mappings[0].sources[0].weight = 0.5f;
            Assert.That(PreviewSettingsSnapshot.BuildCustomMappingsSignature(mappings), Is.Not.EqualTo(baseline));

            mappings[0].sources[0].weight = 1.0f;
            mappings[0].enabled = false;
            Assert.That(PreviewSettingsSnapshot.BuildCustomMappingsSignature(mappings), Is.Not.EqualTo(baseline));
        }

        [Test]
        public void BuildMouthCancellationSignature_ChangesWithSourcesAndTargets()
        {
            var sources = new List<BlendShapeSource>
            {
                new BlendShapeSource { blendShapeName = "口開き", weight = 1.0f },
            };
            var targets = new List<string> { "jawOpen" };

            int baseline = PreviewSettingsSnapshot.BuildMouthCancellationSignature(sources, targets);

            targets.Add("mouthFunnel");
            int withExtraTarget = PreviewSettingsSnapshot.BuildMouthCancellationSignature(sources, targets);
            Assert.That(withExtraTarget, Is.Not.EqualTo(baseline));

            sources[0].side = BlendShapeSide.LeftOnly;
            Assert.That(
                PreviewSettingsSnapshot.BuildMouthCancellationSignature(sources, targets),
                Is.Not.EqualTo(withExtraTarget));
        }
    }
}
