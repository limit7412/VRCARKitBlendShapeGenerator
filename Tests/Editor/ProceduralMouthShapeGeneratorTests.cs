using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 口周りBlendShapeの手続き的生成の検証。
    ///
    /// 口領域の検出（マスク生成）と、検出した領域に対する頂点移動の2段階を分けて確認する。
    /// テスト用のメッシュは口領域を模した直方体（左右2 × 上下2 × 前後2の8頂点）に、
    /// 口から離れた頂点を1つ足した構成にしてある。
    /// </summary>
    public class ProceduralMouthShapeGeneratorTests
    {
        // 口領域の直方体の広がり。X方向の幅（SideX * 2）が移動量の基準になる
        private const float SideX = 0.05f;
        private const float UpperY = 0.01f;
        private const float LowerY = -0.01f;
        private const float FrontZ = 0.05f;
        private const float BackZ = 0f;

        private const float MouthWidth = SideX * 2f;

        // 口領域検出に使うシェイプキーのデルタ量。全頂点で同じ量にして口領域ウェイトを1に揃える
        private const float MouthSourceDelta = 0.02f;

        // 移動量の比率と奥行き減衰（ProceduralMouthShapeGeneratorの定数に対応）
        private const float MouthTranslateRatio = 0.12f;
        private const float JawTranslateRatio = 0.20f;
        private const float DepthFalloffMinScale = 0.3f;

        // 頂点インデックス（Lは X < 0、Rは X > 0）
        private const int UpperFrontL = 0;
        private const int UpperFrontR = 1;
        private const int LowerFrontL = 2;
        private const int LowerFrontR = 3;
        private const int UpperBackL = 4;
        private const int UpperBackR = 5;
        private const int LowerBackL = 6;

        // 口領域の頂点数と、口から離れた頂点のインデックス
        private const int RegionVertexCount = 8;
        private const int OutsideVertex = 8;

        private static Vector3[] MouthVertices() => new[]
        {
            new Vector3(-SideX, UpperY, FrontZ),
            new Vector3(SideX, UpperY, FrontZ),
            new Vector3(-SideX, LowerY, FrontZ),
            new Vector3(SideX, LowerY, FrontZ),
            new Vector3(-SideX, UpperY, BackZ),
            new Vector3(SideX, UpperY, BackZ),
            new Vector3(-SideX, LowerY, BackZ),
            new Vector3(SideX, LowerY, BackZ),
            new Vector3(0f, 0.5f, 0f),
        };

        /// <summary>口領域の8頂点だけを動かすシェイプキーを1つ持つメッシュ</summary>
        private static FakeMeshRepository MouthMesh(string sourceShapeName = "vrc.v_aa")
        {
            return new FakeMeshRepository(MouthVertices())
                .AddShape(sourceShapeName, RegionDeltas());
        }

        private static Vector3[] RegionDeltas()
        {
            var deltas = new Vector3[MouthVertices().Length];
            for (int i = 0; i < RegionVertexCount; i++)
            {
                deltas[i] = Vector3.down * MouthSourceDelta;
            }

            return deltas;
        }

        private static BlendShapeGenerationOptions CreateOptions(bool enableLeftRightSplit = false)
        {
            return new BlendShapeGenerationOptions
            {
                IntensityMultiplier = 1.0f,
                EnableLeftRightSplit = enableLeftRightSplit,
                BlendWidth = 0.02f,
                EnableProceduralMouthShapes = true,
                ProceduralMouthIntensity = 1.0f,
            };
        }

        private static ProceduralMouthShapeGenerator.MouthRegionContext CreateContext(
            FakeMeshRepository mesh = null)
        {
            Assert.That(
                ProceduralMouthShapeGenerator.TryCreateContext(mesh ?? MouthMesh(), out var context),
                Is.True,
                "テストメッシュから口領域を検出できていない");
            return context;
        }

        private static Vector3[] BuildDeltas(
            string arkitName,
            BlendShapeGenerationOptions options = null,
            ProceduralMouthShapeGenerator.MouthRegionContext context = null)
        {
            Assert.That(
                ProceduralMouthShapeGenerator.TryBuildDeltaVertices(
                    context ?? CreateContext(),
                    arkitName,
                    options ?? CreateOptions(),
                    out var deltas),
                Is.True,
                $"{arkitName} のデルタを生成できていない");
            return deltas;
        }

        [TestCase("vrc.v_aa")]
        [TestCase("vrc_v_ou")]
        [TestCase("あ")]
        [TestCase("ん")]
        [TestCase("E")]
        public void HasMouthSource_ReturnsTrue_ForCandidateName(string shapeName)
        {
            Assert.That(
                ProceduralMouthShapeGenerator.HasMouthSource(new List<string> { "unrelated", shapeName }),
                Is.True);
        }

        [Test]
        public void HasMouthSource_ReturnsFalse_WhenNoCandidateExists()
        {
            Assert.That(
                ProceduralMouthShapeGenerator.HasMouthSource(new List<string> { "vrc.blink", "まばたき" }),
                Is.False);
        }

        [Test]
        public void HasMouthSource_ReturnsFalse_ForNullOrEmpty()
        {
            Assert.That(ProceduralMouthShapeGenerator.HasMouthSource(null), Is.False);
            Assert.That(ProceduralMouthShapeGenerator.HasMouthSource(new List<string>()), Is.False);
        }

        [Test]
        public void TryCreateContext_ReturnsFalse_ForNullMesh()
        {
            Assert.That(ProceduralMouthShapeGenerator.TryCreateContext(null, out var context), Is.False);
            Assert.That(context, Is.Null);
        }

        [Test]
        public void TryCreateContext_ReturnsFalse_WhenMeshHasNoVertices()
        {
            var mesh = new FakeMeshRepository(new Vector3[0]).AddShape("vrc.v_aa");

            Assert.That(ProceduralMouthShapeGenerator.TryCreateContext(mesh, out _), Is.False);
        }

        [Test]
        public void TryCreateContext_ReturnsFalse_WhenMouthSourceMissing()
        {
            // 口領域の検出に使えるシェイプキーが無いメッシュ
            var mesh = new FakeMeshRepository(MouthVertices()).AddShape("vrc.blink", RegionDeltas());

            Assert.That(ProceduralMouthShapeGenerator.TryCreateContext(mesh, out _), Is.False);
        }

        [Test]
        public void TryCreateContext_ReturnsFalse_WhenMouthSourceHasNoDeformation()
        {
            // 名前は一致するがデルタが全て0のシェイプキーからは領域を決められない
            var mesh = new FakeMeshRepository(MouthVertices())
                .AddShape("vrc.v_aa", new Vector3[MouthVertices().Length]);

            Assert.That(ProceduralMouthShapeGenerator.TryCreateContext(mesh, out _), Is.False);
        }

        [Test]
        public void TryCreateContext_ReturnsFalse_WhenMouthRegionHasNoWidth()
        {
            // 口領域がX方向に広がりを持たない場合、移動量の基準となる幅が取れない
            var vertices = new[]
            {
                new Vector3(0f, UpperY, FrontZ),
                new Vector3(0f, LowerY, FrontZ),
            };
            var mesh = new FakeMeshRepository(vertices)
                .AddShape("vrc.v_aa", Vector3.down * MouthSourceDelta, Vector3.down * MouthSourceDelta);

            Assert.That(ProceduralMouthShapeGenerator.TryCreateContext(mesh, out _), Is.False);
        }

        [Test]
        public void TryCreateContext_DetectsMouthRegion()
        {
            var context = CreateContext();

            Assert.That(context.MouthWidth, Is.EqualTo(MouthWidth).Within(1e-6f));
            for (int i = 0; i < RegionVertexCount; i++)
            {
                Assert.That(context.Weights[i], Is.EqualTo(1f).Within(1e-6f), $"頂点{i}が口領域に入っていない");
            }

            Assert.That(context.Weights[OutsideVertex], Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void TryCreateContext_ExcludesVerticesFarFromCoreRegion()
        {
            // 顔全体が動くシェイプキーを想定し、口から離れた頂点にもわずかなデルタを持たせる。
            // ウェイト自体は0より大きくなるが、コア領域のバウンディングボックス外なので除外される。
            var deltas = RegionDeltas();
            deltas[OutsideVertex] = Vector3.down * (MouthSourceDelta * 0.1f);
            var mesh = new FakeMeshRepository(MouthVertices()).AddShape("vrc.v_aa", deltas);

            var context = CreateContext(mesh);

            Assert.That(context.Weights[OutsideVertex], Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void TryCreateContext_SeparatesUpperAndLowerLip()
        {
            var context = CreateContext();

            // 唇の境界線は口領域のウェイト付き重心Y（このメッシュではY=0）になる
            Assert.That(context.LowerRatios[UpperFrontL], Is.EqualTo(0f).Within(1e-6f));
            Assert.That(context.LowerRatios[UpperFrontR], Is.EqualTo(0f).Within(1e-6f));
            Assert.That(context.LowerRatios[LowerFrontL], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(context.LowerRatios[LowerFrontR], Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void TryCreateContext_AttenuatesDepth()
        {
            var context = CreateContext();

            // 最前面は減衰なし、最奥はDepthFalloffMinScaleまで減衰する
            Assert.That(context.DepthFactors[UpperFrontL], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(context.DepthFactors[UpperBackL], Is.EqualTo(DepthFalloffMinScale).Within(1e-6f));
        }

        [Test]
        public void TryCreateContext_UsesMultipleSourceGroups()
        {
            // グループごとに1つずつ拾うため、母音違いのシェイプキーが混在しても領域は合成される
            var frontOnly = new Vector3[MouthVertices().Length];
            frontOnly[UpperFrontL] = Vector3.down * MouthSourceDelta;
            frontOnly[UpperFrontR] = Vector3.down * MouthSourceDelta;
            var backOnly = new Vector3[MouthVertices().Length];
            backOnly[UpperBackL] = Vector3.down * MouthSourceDelta;
            backOnly[UpperBackR] = Vector3.down * MouthSourceDelta;

            var mesh = new FakeMeshRepository(MouthVertices())
                .AddShape("vrc.v_aa", frontOnly)
                .AddShape("vrc.v_ou", backOnly);

            Assert.That(ProceduralMouthShapeGenerator.TryCreateContext(mesh, out var context), Is.True);
            Assert.That(context.Weights[UpperFrontL], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(context.Weights[UpperBackL], Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void TryBuildDeltaVertices_ReturnsFalse_ForUnknownName()
        {
            Assert.That(
                ProceduralMouthShapeGenerator.TryBuildDeltaVertices(
                    CreateContext(), "eyeBlinkLeft", CreateOptions(), out var deltas),
                Is.False);
            Assert.That(deltas, Is.Null);
        }

        [Test]
        public void TryBuildDeltaVertices_ReturnsFalse_ForNullArguments()
        {
            Assert.That(
                ProceduralMouthShapeGenerator.TryBuildDeltaVertices(
                    null, "mouthLeft", CreateOptions(), out _),
                Is.False);
            Assert.That(
                ProceduralMouthShapeGenerator.TryBuildDeltaVertices(
                    CreateContext(), "mouthLeft", null, out _),
                Is.False);
        }

        [Test]
        public void TryBuildDeltaVertices_ReturnsFalse_WhenIntensityIsZero()
        {
            var options = CreateOptions();
            options.ProceduralMouthIntensity = 0f;

            Assert.That(
                ProceduralMouthShapeGenerator.TryBuildDeltaVertices(
                    CreateContext(), "mouthLeft", options, out _),
                Is.False);
        }

        [Test]
        public void TryBuildDeltaVertices_SupportsAllTargetShapeNames()
        {
            // TargetShapeNamesに名前を足したのに移動定義を足し忘れる事故を防ぐ
            var context = CreateContext();
            var options = CreateOptions();

            foreach (var arkitName in ProceduralMouthShapeGenerator.TargetShapeNames)
            {
                Assert.That(
                    ProceduralMouthShapeGenerator.TryBuildDeltaVertices(context, arkitName, options, out _),
                    Is.True,
                    arkitName);
            }
        }

        // アバターは+Zを向いている前提。左右分割の規約に合わせ、mouthLeftは-X方向へ動く
        [TestCase("mouthLeft", -1f, 0f, 0f)]
        [TestCase("mouthRight", 1f, 0f, 0f)]
        [TestCase("jawLeft", -1f, 0f, 0f)]
        [TestCase("jawRight", 1f, 0f, 0f)]
        [TestCase("jawForward", 0f, 0f, 1f)]
        [TestCase("mouthShrugUpper", 0f, 0f, 1f)]
        [TestCase("mouthShrugLower", 0f, 0f, 1f)]
        [TestCase("mouthUpperUpLeft", 0f, 1f, 0f)]
        [TestCase("mouthUpperUpRight", 0f, 1f, 0f)]
        [TestCase("mouthLowerDownLeft", 0f, -1f, 0f)]
        [TestCase("mouthLowerDownRight", 0f, -1f, 0f)]
        public void TryBuildDeltaVertices_MovesVerticesTowardDefinedDirection(
            string arkitName, float x, float y, float z)
        {
            var expected = new Vector3(x, y, z);
            var deltas = BuildDeltas(arkitName);

            bool moved = false;
            for (int i = 0; i < deltas.Length; i++)
            {
                if (deltas[i] == Vector3.zero)
                {
                    continue;
                }

                Assert.That(
                    Vector3.Dot(deltas[i].normalized, expected),
                    Is.EqualTo(1f).Within(1e-4f),
                    $"{arkitName} の頂点{i}が想定と違う方向へ動いている");
                moved = true;
            }

            Assert.That(moved, Is.True, $"{arkitName} でどの頂点も動いていない");
        }

        [Test]
        public void TryBuildDeltaVertices_MovesMouthByWidthRatio()
        {
            var deltas = BuildDeltas("mouthLeft");

            // 移動量は口領域の幅に対する比率で決まる
            Assert.That(deltas[UpperFrontL].x, Is.EqualTo(-MouthWidth * MouthTranslateRatio).Within(1e-6f));
        }

        [Test]
        public void TryBuildDeltaVertices_ScalesWithIntensityOptions()
        {
            var options = CreateOptions();
            options.IntensityMultiplier = 0.5f;
            options.ProceduralMouthIntensity = 1.5f;

            var deltas = BuildDeltas("mouthLeft", options);

            Assert.That(
                deltas[UpperFrontL].x,
                Is.EqualTo(-MouthWidth * MouthTranslateRatio * 0.5f * 1.5f).Within(1e-6f));
        }

        [Test]
        public void TryBuildDeltaVertices_AppliesDepthFalloff()
        {
            var deltas = BuildDeltas("mouthLeft");

            // 奥の頂点は前面より動きが小さくなり、単純な平行移動にならない
            Assert.That(
                deltas[UpperBackL].x,
                Is.EqualTo(deltas[UpperFrontL].x * DepthFalloffMinScale).Within(1e-6f));
        }

        [Test]
        public void TryBuildDeltaVertices_LeavesVerticesOutsideRegionUntouched()
        {
            var deltas = BuildDeltas("mouthLeft");

            Assert.That(deltas[OutsideVertex], Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void TryBuildDeltaVertices_IsSymmetric_BetweenLeftAndRight()
        {
            var left = BuildDeltas("mouthLeft");
            var right = BuildDeltas("mouthRight");

            for (int i = 0; i < left.Length; i++)
            {
                Assert.That(right[i].x, Is.EqualTo(-left[i].x).Within(1e-6f), $"頂点{i}");
            }
        }

        // 上唇側/下唇側のどちらを動かすかは唇の境界線で分ける
        [TestCase("mouthLeft", true, true)]
        [TestCase("jawLeft", false, true)]
        [TestCase("jawForward", false, true)]
        [TestCase("mouthShrugUpper", true, false)]
        [TestCase("mouthShrugLower", false, true)]
        [TestCase("mouthUpperUpLeft", true, false)]
        [TestCase("mouthLowerDownLeft", false, true)]
        public void TryBuildDeltaVertices_AppliesLipMask(string arkitName, bool movesUpper, bool movesLower)
        {
            // 左右分割は無効にして、唇の分離だけを見る
            var deltas = BuildDeltas(arkitName);

            Assert.That(deltas[UpperFrontL] != Vector3.zero, Is.EqualTo(movesUpper), "上唇側");
            Assert.That(deltas[LowerFrontL] != Vector3.zero, Is.EqualTo(movesLower), "下唇側");
        }

        [Test]
        public void TryBuildDeltaVertices_MovesJawByJawRatio()
        {
            var deltas = BuildDeltas("jawLeft");

            Assert.That(deltas[LowerFrontL].x, Is.EqualTo(-MouthWidth * JawTranslateRatio).Within(1e-6f));
        }

        [TestCase("mouthUpperUpLeft", UpperFrontL, UpperFrontR)]
        [TestCase("mouthLowerDownLeft", LowerFrontL, LowerFrontR)]
        public void TryBuildDeltaVertices_AppliesLeftSideOnly_WhenSplitEnabled(
            string arkitName, int leftVertex, int rightVertex)
        {
            var deltas = BuildDeltas(arkitName, CreateOptions(enableLeftRightSplit: true));

            // LeftOnlyはX<0側にのみ適用される
            Assert.That(deltas[leftVertex], Is.Not.EqualTo(Vector3.zero));
            Assert.That(deltas[rightVertex], Is.EqualTo(Vector3.zero));
        }

        [TestCase("mouthUpperUpRight", UpperFrontR, UpperFrontL)]
        [TestCase("mouthLowerDownRight", LowerFrontR, LowerFrontL)]
        public void TryBuildDeltaVertices_AppliesRightSideOnly_WhenSplitEnabled(
            string arkitName, int rightVertex, int leftVertex)
        {
            var deltas = BuildDeltas(arkitName, CreateOptions(enableLeftRightSplit: true));

            Assert.That(deltas[rightVertex], Is.Not.EqualTo(Vector3.zero));
            Assert.That(deltas[leftVertex], Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void TryBuildDeltaVertices_IgnoresSide_WhenSplitDisabled()
        {
            var deltas = BuildDeltas("mouthUpperUpLeft");

            // 左右分割が無効なら左右どちらの頂点も同じだけ動く
            Assert.That(deltas[UpperFrontL].y, Is.GreaterThan(0f));
            Assert.That(deltas[UpperFrontR].y, Is.EqualTo(deltas[UpperFrontL].y).Within(1e-6f));
        }

        [Test]
        public void TryBuildDeltaVertices_KeepsUpperLipStill_ForLowerLipShapes()
        {
            var deltas = BuildDeltas("jawForward");

            Assert.That(deltas[LowerBackL].z, Is.GreaterThan(0f));
            Assert.That(deltas[UpperBackL], Is.EqualTo(Vector3.zero));
        }
    }
}
