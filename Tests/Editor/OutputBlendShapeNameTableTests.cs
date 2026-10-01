using System.Linq;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 正準名（ARKit名）から出力名への変換表の検証。
    /// 名前の体系を切り替えても、生成の計画は常にARKit名のまま動くことが前提になっている
    /// </summary>
    public class OutputBlendShapeNameTableTests
    {
        [Test]
        public void Resolve_ReturnsArkitNameUnchanged_InArkitMode()
        {
            var outputs = OutputBlendShapeNameTable.Resolve("eyeBlinkLeft", BlendShapeNaming.ARKit);

            Assert.That(outputs.Select(o => o.Name), Is.EqualTo(new[] { "eyeBlinkLeft" }));
            Assert.That(outputs[0].Side, Is.EqualTo(BlendShapeSide.Both));
            Assert.That(outputs[0].LipMask, Is.EqualTo(BlendShapeLipMask.All));
        }

        [TestCase("jawOpen", "JawOpen")]
        [TestCase("eyeWideLeft", "EyeWideLeft")]
        [TestCase("mouthUpperUpRight", "MouthUpperUpRight")]
        [TestCase("tongueOut", "TongueOut")]
        public void Resolve_CapitalizesFirstLetter_ForPlainUnifiedExpressionsNames(string arkitName, string expected)
        {
            var outputs = OutputBlendShapeNameTable.Resolve(arkitName, BlendShapeNaming.UnifiedExpressions);

            Assert.That(outputs.Select(o => o.Name), Is.EqualTo(new[] { expected }));
        }

        [TestCase("eyeBlinkLeft", "EyeClosedLeft")]
        [TestCase("mouthClose", "MouthClosed")]
        [TestCase("mouthShrugUpper", "MouthRaiserUpper")]
        [TestCase("mouthRollLower", "LipSuckLower")]
        public void Resolve_UsesOverrideTable_ForNamesThatDifferByWord(string arkitName, string expected)
        {
            var outputs = OutputBlendShapeNameTable.Resolve(arkitName, BlendShapeNaming.UnifiedExpressions);

            Assert.That(outputs.Select(o => o.Name), Is.EqualTo(new[] { expected }));
            Assert.That(outputs[0].LipMask, Is.EqualTo(BlendShapeLipMask.All));
        }

        [TestCase("browInnerUp", "BrowInnerUpLeft", "BrowInnerUpRight")]
        [TestCase("cheekPuff", "CheekPuffLeft", "CheekPuffRight")]
        [TestCase("mouthPress", "MouthPressLeft", "MouthPressRight")]
        public void Resolve_SplitsLeftAndRight_WhereUnifiedExpressionsHasBothSides(
            string arkitName, string left, string right)
        {
            var outputs = OutputBlendShapeNameTable.Resolve(arkitName, BlendShapeNaming.UnifiedExpressions);

            Assert.That(outputs.Select(o => o.Name), Is.EqualTo(new[] { left, right }));
            Assert.That(outputs[0].Side, Is.EqualTo(BlendShapeSide.LeftOnly));
            Assert.That(outputs[1].Side, Is.EqualTo(BlendShapeSide.RightOnly));
        }

        [TestCase("mouthFunnel", "LipFunnelUpper", "LipFunnelLower")]
        [TestCase("mouthLeft", "MouthUpperLeft", "MouthLowerLeft")]
        [TestCase("mouthRight", "MouthUpperRight", "MouthLowerRight")]
        public void Resolve_SplitsUpperAndLower_WhereUnifiedExpressionsHasBothLips(
            string arkitName, string upper, string lower)
        {
            var outputs = OutputBlendShapeNameTable.Resolve(arkitName, BlendShapeNaming.UnifiedExpressions);

            Assert.That(outputs.Select(o => o.Name), Is.EqualTo(new[] { upper, lower }));
            Assert.That(outputs[0].LipMask, Is.EqualTo(BlendShapeLipMask.Upper));
            Assert.That(outputs[1].LipMask, Is.EqualTo(BlendShapeLipMask.Lower));
            Assert.That(outputs.All(o => o.Side == BlendShapeSide.Both), Is.True);
        }

        [Test]
        public void Resolve_ReturnsArkitAndUnifiedExpressions_InBothMode()
        {
            var outputs = OutputBlendShapeNameTable.Resolve("browInnerUp", BlendShapeNaming.Both);

            Assert.That(
                outputs.Select(o => o.Name),
                Is.EqualTo(new[] { "browInnerUp", "BrowInnerUpLeft", "BrowInnerUpRight" }));
        }

        [Test]
        public void Resolve_KeepsUnknownNameUnchanged_AndEmitsItOnce()
        {
            // カスタムマッピングで自由に付けた名前（UE名を直接書いたものを含む）は変換しない
            Assert.That(
                OutputBlendShapeNameTable.Resolve("myShape", BlendShapeNaming.UnifiedExpressions).Select(o => o.Name),
                Is.EqualTo(new[] { "myShape" }));
            Assert.That(
                OutputBlendShapeNameTable.Resolve("EyeClosedLeft", BlendShapeNaming.Both).Select(o => o.Name),
                Is.EqualTo(new[] { "EyeClosedLeft" }));
        }

        [Test]
        public void Resolve_CoversEveryArkitName_WithDistinctUnifiedExpressionsNames()
        {
            var allOutputs = ARKitBlendShapeNames.GetAll()
                .SelectMany(name => OutputBlendShapeNameTable.Resolve(name, BlendShapeNaming.UnifiedExpressions))
                .Select(o => o.Name)
                .ToList();

            Assert.That(allOutputs, Is.Not.Empty);
            Assert.That(allOutputs, Is.Unique);
            Assert.That(allOutputs.All(name => char.IsUpper(name[0])), Is.True);
        }
    }
}
