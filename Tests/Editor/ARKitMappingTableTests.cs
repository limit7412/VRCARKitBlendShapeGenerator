using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    public class ARKitMappingTableTests
    {
        [Test]
        public void GetMappings_IsNotEmpty()
        {
            Assert.That(ARKitMappingTable.GetMappings(), Is.Not.Empty);
        }

        [Test]
        public void GetMappings_AllEntriesHaveArkitNameAndSources()
        {
            foreach (var mapping in ARKitMappingTable.GetMappings())
            {
                Assert.That(mapping, Is.Not.Null);
                Assert.That(mapping.arkitName, Is.Not.Null.And.Not.Empty);
                Assert.That(mapping.sources, Is.Not.Null.And.Not.Empty, mapping.arkitName);
                foreach (var source in mapping.sources)
                {
                    Assert.That(source.names, Is.Not.Null.And.Not.Empty, mapping.arkitName);
                }
            }
        }

        [Test]
        public void GetMappings_AllArkitNamesAreKnownArkitBlendShapes()
        {
            var knownNames = new HashSet<string>(ARKitBlendShapeNames.GetAll());
            var unknownNames = ARKitMappingTable.GetMappings()
                .Select(mapping => mapping.arkitName)
                .Where(name => !knownNames.Contains(name))
                .Distinct()
                .ToList();

            Assert.That(unknownNames, Is.Empty);
        }

        [Test]
        public void GetMappings_DoesNotContainMouthPucker()
        {
            // mouthPuckerは母音のシェイプキーから生成すると口元が破綻しやすいため自動生成しない。
            // ただしARKit名としては残すので、カスタムマッピングでの手動指定は引き続きできる
            var arkitNames = ARKitMappingTable.GetMappings()
                .Select(mapping => mapping.arkitName)
                .ToList();

            Assert.That(arkitNames, Has.No.Member("mouthPucker"));
            Assert.That(ARKitBlendShapeNames.GetAll(), Has.Member("mouthPucker"));
        }

        [Test]
        public void GetMappings_SideSpecificFallbacksComeAfterDirectMappings()
        {
            // 同一ARKit名が複数ある場合、左右別ソースの定義（Both）が
            // 左右分割フォールバック（LeftOnly/RightOnly）より先に並んでいること
            // （エンジンは最初にソースが見つかった定義を採用するため順序が意味を持つ）
            var mappings = ARKitMappingTable.GetMappings();
            var firstIndexByName = new Dictionary<string, int>();
            for (int i = 0; i < mappings.Count; i++)
            {
                if (!firstIndexByName.ContainsKey(mappings[i].arkitName))
                {
                    firstIndexByName[mappings[i].arkitName] = i;
                }
            }

            for (int i = 0; i < mappings.Count; i++)
            {
                var mapping = mappings[i];
                if (firstIndexByName[mapping.arkitName] == i)
                {
                    continue;
                }

                // 2番目以降の同名定義は左右分割フォールバックであること
                Assert.That(
                    mapping.side,
                    Is.Not.EqualTo(BlendShapeSide.Both),
                    $"{mapping.arkitName} (index {i})");
            }
        }
    }
}
