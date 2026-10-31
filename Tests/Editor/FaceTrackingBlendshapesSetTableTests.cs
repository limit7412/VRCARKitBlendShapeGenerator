using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// ネイティブフェイストラッキングのトラッキングキーから生成シェイプキーへの対応表の検証。
    /// Setはキーの番号を添字にした配列なので、キーの数と並びが崩れていないことを最初に確かめる
    /// </summary>
    public class FaceTrackingBlendshapesSetTableTests
    {
        /// <summary>
        /// SDK 3.10.6 の VRCAvatarDescriptor.FaceTrackingKey の順（3.10.6-beta.2 から変わっていない）。
        /// 書き込み側はSDKの列挙体と突き合わせるが、SDKの無いテスト環境でも並びを固定しておくための写し
        /// </summary>
        private static readonly string[] SdkKeyOrder =
        {
            "EyeLookOutRight", "EyeLookInRight", "EyeLookUpRight", "EyeLookDownRight",
            "EyeLookOutLeft", "EyeLookInLeft", "EyeLookUpLeft", "EyeLookDownLeft",
            "EyeClosedRight", "EyeClosedLeft", "EyeDilationRight", "EyeDilationLeft",
            "EyeConstrictRight", "EyeConstrictLeft", "EyeSquintRight", "EyeSquintLeft",
            "EyeWideRight", "EyeWideLeft",
            "BrowPinchRight", "BrowPinchLeft", "BrowLowererRight", "BrowLowererLeft",
            "BrowInnerUpRight", "BrowInnerUpLeft", "BrowOuterUpRight", "BrowOuterUpLeft",
            "NasalDilationRight", "NasalDilationLeft", "NasalConstrictRight", "NasalConstrictLeft",
            "CheekSquintRight", "CheekSquintLeft", "CheekPuffRight", "CheekPuffLeft",
            "CheekSuckRight", "CheekSuckLeft",
            "JawOpen", "JawRight", "JawLeft", "JawForward", "JawBackward", "JawClench", "JawMandibleRaise",
            "MouthClosed",
            "LipSuckUpperRight", "LipSuckUpperLeft", "LipSuckLowerRight", "LipSuckLowerLeft",
            "LipSuckCornerRight", "LipSuckCornerLeft",
            "LipFunnelUpperRight", "LipFunnelUpperLeft", "LipFunnelLowerRight", "LipFunnelLowerLeft",
            "LipPuckerUpperRight", "LipPuckerUpperLeft", "LipPuckerLowerRight", "LipPuckerLowerLeft",
            "MouthUpperUpRight", "MouthUpperUpLeft", "MouthUpperDeepenRight", "MouthUpperDeepenLeft",
            "NoseSneerRight", "NoseSneerLeft", "MouthLowerDownRight", "MouthLowerDownLeft",
            "MouthUpperRight", "MouthUpperLeft", "MouthLowerRight", "MouthLowerLeft",
            "MouthCornerPullRight", "MouthCornerPullLeft", "MouthCornerSlantRight", "MouthCornerSlantLeft",
            "MouthFrownRight", "MouthFrownLeft", "MouthStretchRight", "MouthStretchLeft",
            "MouthDimpleRight", "MouthDimpleLeft", "MouthRaiserUpper", "MouthRaiserLower",
            "MouthPressRight", "MouthPressLeft", "MouthTightenerRight", "MouthTightenerLeft",
            "TongueOut", "TongueUp", "TongueDown", "TongueRight", "TongueLeft", "TongueRoll",
            "TongueBendDown", "TongueCurlUp", "TongueSquish", "TongueFlat", "TongueTwistRight", "TongueTwistLeft",
            "SoftPalateClose", "ThroatSwallow", "NeckFlexRight", "NeckFlexLeft",
        };

        [Test]
        public void KeyNames_MatchSdkOrder_WithAllKeys()
        {
            Assert.That(FaceTrackingBlendshapesSetTable.KeyCount, Is.EqualTo(102));
            Assert.That(FaceTrackingBlendshapesSetTable.GetKeyNames().ToArray(), Is.EqualTo(SdkKeyOrder));
        }

        [Test]
        public void Build_LeavesEveryKeyEmpty_WhenMeshHasNoCandidates()
        {
            var plan = FaceTrackingBlendshapesSetTable.Build(new[] { "vrc.v_aa", "まばたき", string.Empty, null });

            Assert.That(plan.BlendShapeNames.Length, Is.EqualTo(102));
            Assert.That(plan.BlendShapeNames.All(name => name == string.Empty), Is.True);
            Assert.That(plan.AssignedCount, Is.EqualTo(0));
        }

        [TestCase("EyeClosedLeft", "EyeClosedLeft")]
        [TestCase("JawOpen", "JawOpen")]
        [TestCase("MouthCornerPullLeft", "MouthSmileLeft")]
        [TestCase("MouthCornerSlantLeft", "MouthSmileLeft")]
        [TestCase("BrowPinchRight", "BrowDownRight")]
        [TestCase("BrowLowererRight", "BrowDownRight")]
        [TestCase("LipFunnelUpperLeft", "LipFunnelUpper")]
        [TestCase("LipFunnelLowerRight", "LipFunnelLower")]
        [TestCase("LipSuckUpperLeft", "LipSuckUpper")]
        [TestCase("LipPuckerLowerRight", "LipPucker")]
        [TestCase("MouthUpperLeft", "MouthUpperLeft")]
        [TestCase("MouthLowerLeft", "MouthLowerLeft")]
        [TestCase("BrowInnerUpRight", "BrowInnerUpRight")]
        [TestCase("MouthRaiserUpper", "MouthRaiserUpper")]
        public void Build_AssignsUnifiedExpressionsOutputs_ToEachKeyTheyCover(string key, string expected)
        {
            var plan = FaceTrackingBlendshapesSetTable.Build(UnifiedExpressionsOutputs());

            Assert.That(NameFor(plan, key), Is.EqualTo(expected));
        }

        [TestCase("EyeClosedLeft", "eyeBlinkLeft")]
        [TestCase("JawOpen", "jawOpen")]
        [TestCase("MouthCornerPullLeft", "mouthSmileLeft")]
        [TestCase("MouthCornerSlantLeft", "mouthSmileLeft")]
        [TestCase("BrowPinchRight", "browDownRight")]
        [TestCase("BrowInnerUpRight", "browInnerUp")]
        [TestCase("BrowInnerUpLeft", "browInnerUp")]
        [TestCase("CheekPuffLeft", "cheekPuff")]
        [TestCase("LipFunnelUpperLeft", "mouthFunnel")]
        [TestCase("LipFunnelLowerRight", "mouthFunnel")]
        [TestCase("LipSuckLowerLeft", "mouthRollLower")]
        [TestCase("MouthUpperLeft", "mouthLeft")]
        [TestCase("MouthLowerLeft", "mouthLeft")]
        [TestCase("MouthPressRight", "mouthPress")]
        [TestCase("MouthRaiserLower", "mouthShrugLower")]
        [TestCase("MouthClosed", "mouthClose")]
        [TestCase("TongueOut", "tongueOut")]
        public void Build_FallsBackToArkitOutputs_WhenOnlyArkitNamesExist(string key, string expected)
        {
            var plan = FaceTrackingBlendshapesSetTable.Build(ARKitBlendShapeNames.GetAll());

            Assert.That(NameFor(plan, key), Is.EqualTo(expected));
        }

        [Test]
        public void Build_PrefersUnifiedExpressionsName_OverArkitName_InBothMode()
        {
            var plan = FaceTrackingBlendshapesSetTable.Build(
                ARKitBlendShapeNames.GetAll().Concat(UnifiedExpressionsOutputs()));

            Assert.That(NameFor(plan, "EyeClosedLeft"), Is.EqualTo("EyeClosedLeft"));
            Assert.That(NameFor(plan, "MouthCornerPullLeft"), Is.EqualTo("MouthSmileLeft"));
            Assert.That(NameFor(plan, "LipFunnelUpperLeft"), Is.EqualTo("LipFunnelUpper"));
            Assert.That(NameFor(plan, "MouthUpperLeft"), Is.EqualTo("MouthUpperLeft"));
        }

        [TestCase("EyeDilationLeft")]
        [TestCase("JawClench")]
        [TestCase("TongueRoll")]
        [TestCase("NeckFlexLeft")]
        public void Build_LeavesKeysEmpty_ThatThisToolNeverGenerates(string key)
        {
            var plan = FaceTrackingBlendshapesSetTable.Build(
                ARKitBlendShapeNames.GetAll().Concat(UnifiedExpressionsOutputs()));

            Assert.That(NameFor(plan, key), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Build_MatchesNamesExactly_NotIgnoringCase()
        {
            // 候補は本ツールの出力名そのもの。別の大文字小文字で付けられたシェイプキーは本ツールの生成物ではない
            var plan = FaceTrackingBlendshapesSetTable.Build(new[] { "JAWOPEN", "Jawopen" });

            Assert.That(NameFor(plan, "JawOpen"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Build_AssignsAKeyNamedShape_EvenIfThisToolDoesNotGenerateIt()
        {
            // キー名そのものを持つシェイプキー（カスタムマッピングで付けた名前など）はSDKの自動入力と同じく割り当てる
            var plan = FaceTrackingBlendshapesSetTable.Build(new[] { "TongueRoll", "EyeDilationLeft" });

            Assert.That(NameFor(plan, "TongueRoll"), Is.EqualTo("TongueRoll"));
            Assert.That(NameFor(plan, "EyeDilationLeft"), Is.EqualTo("EyeDilationLeft"));
        }

        [Test]
        public void Candidates_StartWithKeyName_ForEveryKey()
        {
            foreach (var key in FaceTrackingBlendshapesSetTable.GetKeyNames())
            {
                var candidates = FaceTrackingBlendshapesSetTable.GetCandidates(key);
                Assert.That(candidates[0], Is.EqualTo(key));
                Assert.That(candidates, Is.Unique, key);
            }
        }

        [Test]
        public void EveryUnifiedExpressionsOutput_IsACandidateOfSomeKey()
        {
            var allCandidates = new HashSet<string>(
                FaceTrackingBlendshapesSetTable.GetKeyNames()
                    .SelectMany(FaceTrackingBlendshapesSetTable.GetCandidates));

            foreach (var output in UnifiedExpressionsOutputs())
            {
                Assert.That(allCandidates, Does.Contain(output), output);
            }
        }

        [Test]
        public void EveryArkitName_IsACandidateOfSomeKey()
        {
            var allCandidates = new HashSet<string>(
                FaceTrackingBlendshapesSetTable.GetKeyNames()
                    .SelectMany(FaceTrackingBlendshapesSetTable.GetCandidates));

            foreach (var arkitName in ARKitBlendShapeNames.GetAll())
            {
                Assert.That(allCandidates, Does.Contain(arkitName), arkitName);
            }
        }

        /// <summary>
        /// 本ツールがUnified Expressionsモードで出す名前の全部
        /// </summary>
        private static IEnumerable<string> UnifiedExpressionsOutputs()
        {
            return ARKitBlendShapeNames.GetAll()
                .SelectMany(name => OutputBlendShapeNameTable.Resolve(name, BlendShapeNaming.UnifiedExpressions))
                .Select(output => output.Name)
                .ToList();
        }

        private static string NameFor(FaceTrackingBlendshapesSetPlan plan, string key)
        {
            int index = System.Array.IndexOf(SdkKeyOrder, key);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), key);
            return plan.BlendShapeNames[index];
        }
    }
}
