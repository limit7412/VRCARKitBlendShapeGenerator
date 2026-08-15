using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    public class AutoMappingSummaryTests
    {
        private static string SideLabel(BlendShapeSide side)
        {
            switch (side)
            {
                case BlendShapeSide.LeftOnly:
                    return "左のみ";
                case BlendShapeSide.RightOnly:
                    return "右のみ";
                default:
                    return "両側";
            }
        }

        private static AutoMappingEntry FindEntry(
            IReadOnlyList<AutoMappingCategory> categories,
            string arkitName)
        {
            return categories
                .SelectMany(category => category.Entries)
                .FirstOrDefault(entry => entry.ArkitName == arkitName);
        }

        [Test]
        public void Build_PutsEntriesInTheCategoryOfTheArkitName()
        {
            var mappings = new List<ARKitMapping>
            {
                new ARKitMapping("tongueOut", new List<SourceMapping> { new SourceMapping(1.0f, "べー") }),
                new ARKitMapping("eyeBlinkLeft", new List<SourceMapping> { new SourceMapping(1.0f, "vrc.blink_left") }),
            };

            var categories = AutoMappingSummary.Build(mappings, null);

            // カテゴリの並びはARKitBlendShapeNamesの定義順に従う
            Assert.That(categories.Select(c => c.Key), Is.EqualTo(new[] { "eye", "tongue" }));
            Assert.That(categories[0].Entries.Single().ArkitName, Is.EqualTo("eyeBlinkLeft"));
            Assert.That(categories[1].Entries.Single().ArkitName, Is.EqualTo("tongueOut"));
        }

        [Test]
        public void Build_KeepsFallbackCandidatesInTableOrder()
        {
            var mappings = new List<ARKitMapping>
            {
                new ARKitMapping("eyeBlinkLeft", new List<SourceMapping> { new SourceMapping(1.0f, "vrc.blink_left") }),
                new ARKitMapping(
                    "eyeBlinkLeft",
                    new List<SourceMapping> { new SourceMapping(1.0f, "vrc.blink", "まばたき") },
                    BlendShapeSide.LeftOnly),
            };

            var entry = FindEntry(AutoMappingSummary.Build(mappings, null), "eyeBlinkLeft");

            Assert.That(entry.Candidates.Count, Is.EqualTo(2));
            Assert.That(entry.Candidates[0].Side, Is.EqualTo(BlendShapeSide.Both));
            Assert.That(entry.Candidates[1].Side, Is.EqualTo(BlendShapeSide.LeftOnly));
            Assert.That(
                AutoMappingSummary.DescribeCandidates(entry, SideLabel),
                Is.EqualTo("vrc.blink_left / vrc.blink, まばたき [左のみ]"));
        }

        [Test]
        public void DescribeCandidates_ShowsWeightOnlyWhenItIsNotOne()
        {
            var mappings = new List<ARKitMapping>
            {
                new ARKitMapping("jawOpen", new List<SourceMapping> { new SourceMapping(0.7f, "vrc.v_aa", "あ") }),
                new ARKitMapping("mouthFunnel", new List<SourceMapping> { new SourceMapping(1.0f, "vrc.v_ou") }),
                new ARKitMapping("mouthPucker", new List<SourceMapping> { new SourceMapping(1.2f, "vrc.v_ou") }),
            };

            var categories = AutoMappingSummary.Build(mappings, null);

            Assert.That(
                AutoMappingSummary.DescribeCandidates(FindEntry(categories, "jawOpen"), SideLabel),
                Is.EqualTo("vrc.v_aa, あ (70%)"));
            Assert.That(
                AutoMappingSummary.DescribeCandidates(FindEntry(categories, "mouthFunnel"), SideLabel),
                Is.EqualTo("vrc.v_ou"));
            Assert.That(
                AutoMappingSummary.DescribeCandidates(FindEntry(categories, "mouthPucker"), SideLabel),
                Is.EqualTo("vrc.v_ou (120%)"));
        }

        [Test]
        public void DescribeCandidates_JoinsSourcesThatAreCombined()
        {
            var mappings = new List<ARKitMapping>
            {
                new ARKitMapping(
                    "jawOpen",
                    new List<SourceMapping>
                    {
                        new SourceMapping(1.0f, "あ"),
                        new SourceMapping(0.5f, "お"),
                    }),
            };

            var entry = FindEntry(AutoMappingSummary.Build(mappings, null), "jawOpen");

            Assert.That(
                AutoMappingSummary.DescribeCandidates(entry, SideLabel),
                Is.EqualTo("あ + お (50%)"));
        }

        [Test]
        public void Build_MarksProceduralFallback()
        {
            var mappings = new List<ARKitMapping>
            {
                new ARKitMapping("mouthLeft", new List<SourceMapping> { new SourceMapping(1.0f, "口左") }),
                new ARKitMapping("mouthFunnel", new List<SourceMapping> { new SourceMapping(1.0f, "vrc.v_ou") }),
            };

            var categories = AutoMappingSummary.Build(mappings, new[] { "mouthLeft" });

            Assert.That(FindEntry(categories, "mouthLeft").HasProceduralFallback, Is.True);
            Assert.That(FindEntry(categories, "mouthFunnel").HasProceduralFallback, Is.False);
        }

        [Test]
        public void Build_ListsProceduralOnlyNamesWithoutCandidates()
        {
            var categories = AutoMappingSummary.Build(new List<ARKitMapping>(), new[] { "jawForward" });

            var entry = FindEntry(categories, "jawForward");
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Candidates, Is.Empty);
            Assert.That(entry.HasProceduralFallback, Is.True);
            Assert.That(AutoMappingSummary.DescribeCandidates(entry, SideLabel), Is.Empty);
        }

        [Test]
        public void Build_OmitsNamesThatAreNeverGenerated()
        {
            // mouthDimpleはマッピングも手続き的生成も無いので一覧に出さない
            var categories = AutoMappingSummary.Build(new List<ARKitMapping>(), null);

            Assert.That(categories, Is.Empty);
        }

        [Test]
        public void Build_CoversEveryMappingInTheTable()
        {
            var mappings = ARKitMappingTable.GetMappings();
            var categories = AutoMappingSummary.Build();

            var shown = new HashSet<string>(
                categories.SelectMany(category => category.Entries).Select(entry => entry.ArkitName));
            var missing = mappings
                .Select(mapping => mapping.arkitName)
                .Where(name => !shown.Contains(name))
                .Distinct()
                .ToArray();

            Assert.That(missing, Is.Empty, "一覧に出ないマッピングがある: " + string.Join(", ", missing));
        }

        [Test]
        public void FindUncategorizedNames_DetectsNamesOutsideARKitBlendShapeNames()
        {
            // ARKitBlendShapeNamesのどの配列にも無い名前はカテゴリに割り当てられず一覧から漏れる
            var mappings = new List<ARKitMapping>
            {
                new ARKitMapping("notAnArkitName", new List<SourceMapping> { new SourceMapping(1.0f, "x") }),
            };

            Assert.That(
                AutoMappingSummary.FindUncategorizedNames(mappings),
                Is.EqualTo(new[] { "notAnArkitName" }));
            Assert.That(AutoMappingSummary.FindUncategorizedNames(ARKitMappingTable.GetMappings()), Is.Empty);
        }

        [Test]
        public void Build_UsesTheCategoryKeysThatHaveFoldLabels()
        {
            // カテゴリを増やすときは auto_mappings.fold.<key> を ja/en の両方へ追加する必要がある。
            // 追加漏れがあると見出しにキー名がそのまま出るため、ここで気付けるようにする
            var keys = AutoMappingSummary.Build().Select(category => category.Key);

            Assert.That(
                keys,
                Is.EqualTo(new[] { "eye", "eye_look", "brow", "mouth", "cheek", "nose", "tongue" }));
        }

        [Test]
        public void Build_CoversEveryProceduralTarget()
        {
            var categories = AutoMappingSummary.Build();

            var shown = new HashSet<string>(
                categories.SelectMany(category => category.Entries).Select(entry => entry.ArkitName));
            var missing = ProceduralMouthShapeGenerator.TargetShapeNames
                .Where(name => !shown.Contains(name))
                .ToArray();

            Assert.That(missing, Is.Empty, "一覧に出ない手続き的生成の対象がある: " + string.Join(", ", missing));
        }
    }
}
