using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// コンポーネントの設定値から生成オプションへの変換の検証。
    /// 設定項目を増やしたときに変換の追加を忘れると、インスペクタの操作が生成へ届かなくなる。
    /// </summary>
    public class BlendShapeGenerationOptionsTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        private ARKitBlendShapeGeneratorComponent CreateComponent()
        {
            _gameObject = new GameObject("Avatar");
            return _gameObject.AddComponent<ARKitBlendShapeGeneratorComponent>();
        }

        [Test]
        public void FromComponent_CopiesGenerationSettings()
        {
            var component = CreateComponent();
            component.intensityMultiplier = 0.8f;
            component.enableLeftRightSplit = false;
            component.blendWidth = 0.05f;
            component.overwriteExisting = true;
            component.enableProceduralMouthShapes = true;
            component.proceduralMouthIntensity = 1.4f;
            component.debugMode = true;

            var options = BlendShapeGenerationOptions.FromComponent(component);

            Assert.That(options.IntensityMultiplier, Is.EqualTo(0.8f));
            Assert.That(options.EnableLeftRightSplit, Is.False);
            Assert.That(options.BlendWidth, Is.EqualTo(0.05f));
            Assert.That(options.OverwriteExisting, Is.True);
            Assert.That(options.EnableProceduralMouthShapes, Is.True);
            Assert.That(options.ProceduralMouthIntensity, Is.EqualTo(1.4f));
            Assert.That(options.Debug, Is.True);
        }

        [Test]
        public void FromComponent_CopiesMouthCancellationSettings()
        {
            var component = CreateComponent();
            component.enableMouthCancellation = true;
            component.mouthCancellationStrength = 0.6f;
            component.mouthCancellationSources = new List<BlendShapeSource>
            {
                new BlendShapeSource { blendShapeName = "口開き", weight = 0.5f, side = BlendShapeSide.LeftOnly },
            };
            component.mouthCancellationTargets = new List<string> { "jawOpen", "mouthFunnel" };

            var options = BlendShapeGenerationOptions.FromComponent(component);

            Assert.That(options.EnableMouthCancellation, Is.True);
            Assert.That(options.MouthCancellationStrength, Is.EqualTo(0.6f));
            Assert.That(options.MouthCancellationSources, Is.SameAs(component.mouthCancellationSources));
            Assert.That(options.MouthCancellationTargets, Is.EquivalentTo(new[] { "jawOpen", "mouthFunnel" }));
        }

        [Test]
        public void FromComponent_IgnoresBlankCancellationTargets()
        {
            // 空白のみの項目は未設定として扱う。名前自体は加工せず、照合は完全一致で行う
            var component = CreateComponent();
            component.mouthCancellationTargets = new List<string> { "jawOpen", "", "   ", null, " mouthFunnel" };

            var options = BlendShapeGenerationOptions.FromComponent(component);

            Assert.That(options.MouthCancellationTargets, Is.EquivalentTo(new[] { "jawOpen", " mouthFunnel" }));
        }

        [Test]
        public void FromComponent_ReturnsEmptyTargets_WhenTargetListIsNull()
        {
            var component = CreateComponent();
            component.mouthCancellationTargets = null;

            var options = BlendShapeGenerationOptions.FromComponent(component);

            Assert.That(options.MouthCancellationTargets, Is.Empty);
        }
    }
}
