using System.Collections.Generic;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// スクリプト参照の切れたコンポーネントを見分ける規則と、参照の付け替えの検証。
    ///
    /// 利用者のプレハブやシーンを書き換える機能なので、別のコンポーネントを巻き込まないことと、
    /// GUID以外を1文字も変えないことを確かめる。
    /// </summary>
    public class MissingScriptReferencesTests
    {
        private const string CurrentGuid = "cfe038bef4164364db38e499eaeb6e10";
        private const string StaleGuid = "461d5de429930ce4c883f88715b796d2";
        private const string OtherGuid = "0123456789abcdef0123456789abcdef";

        private const string Header = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";

        // 0.1.0のコンポーネントが保存した形。後の版で増えたフィールドを持たない
        private const string OldestFields =
            "  targetRenderer: {fileID: 4745576506794454373}\n" +
            "  intensityMultiplier: 1\n" +
            "  enableLeftRightSplit: 1\n" +
            "  overwriteExisting: 0\n" +
            "  customMappings: []\n" +
            "  debugMode: 0\n";

        private const string LatestFields =
            "  targetRenderer: {fileID: 4745576506794454373}\n" +
            "  intensityMultiplier: 1\n" +
            "  enableLeftRightSplit: 1\n" +
            "  blendWidth: 0.02\n" +
            "  overwriteExisting: 0\n" +
            "  outputNaming: 1\n" +
            "  writeFaceTrackingBlendshapesSet: 0\n" +
            "  enableProceduralMouthShapes: 1\n" +
            "  proceduralMouthIntensity: 1\n" +
            "  enableMouthCancellation: 0\n" +
            "  mouthCancellationSources: []\n" +
            "  mouthCancellationStrength: 1\n" +
            "  mouthCancellationTargets:\n" +
            "  - jawOpen\n" +
            "  customMappings:\n" +
            "  - arkitName: jawOpen\n" +
            "    enabled: 1\n" +
            "    sources:\n" +
            "    - blendShapeName: vrc.v_aa\n" +
            "      weight: 1\n" +
            "      side: 0\n" +
            "  debugMode: 0\n";

        private static string MonoBehaviour(string guid, string fields, string classIdentifier = "")
        {
            return "--- !u!114 &7321625273785740913\n" +
                   "MonoBehaviour:\n" +
                   "  m_ObjectHideFlags: 0\n" +
                   "  m_CorrespondingSourceObject: {fileID: 0}\n" +
                   "  m_PrefabInstance: {fileID: 0}\n" +
                   "  m_PrefabAsset: {fileID: 0}\n" +
                   "  m_GameObject: {fileID: 6610011028700253542}\n" +
                   "  m_Enabled: 1\n" +
                   "  m_EditorHideFlags: 0\n" +
                   "  m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}\n" +
                   "  m_Name: \n" +
                   "  m_EditorClassIdentifier: " + classIdentifier + "\n" +
                   fields;
        }

        private static string StrippedMonoBehaviour(string guid)
        {
            return "--- !u!114 &1234567890 stripped\n" +
                   "MonoBehaviour:\n" +
                   "  m_CorrespondingSourceObject: {fileID: 7321625273785740913, guid: " + OtherGuid + ", type: 3}\n" +
                   "  m_PrefabInstance: {fileID: 42}\n" +
                   "  m_PrefabAsset: {fileID: 0}\n" +
                   "  m_GameObject: {fileID: 0}\n" +
                   "  m_Enabled: 1\n" +
                   "  m_EditorHideFlags: 0\n" +
                   "  m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}\n" +
                   "  m_Name: \n" +
                   "  m_EditorClassIdentifier: \n";
        }

        private const string GameObject =
            "--- !u!1 &6610011028700253542\n" +
            "GameObject:\n" +
            "  m_ObjectHideFlags: 0\n" +
            "  m_Name: Body\n";

        private static int Collect(string yaml, ISet<string> stale, params string[] resolvable)
        {
            var known = new HashSet<string>(resolvable);
            return MissingScriptReferences.CollectStaleGuids(yaml, CurrentGuid, known.Contains, stale);
        }

        [Test]
        public void CollectStaleGuids_FindsAComponentWhoseScriptIsMissing()
        {
            var stale = new HashSet<string>();
            var yaml = Header + GameObject + MonoBehaviour(StaleGuid, LatestFields);

            Assert.That(Collect(yaml, stale), Is.EqualTo(1));
            Assert.That(stale, Is.EquivalentTo(new[] { StaleGuid }));
        }

        // 古い版で保存されたまま参照が切れたコンポーネントも拾う
        [Test]
        public void CollectStaleGuids_FindsAComponentSavedByTheOldestVersion()
        {
            var stale = new HashSet<string>();

            Assert.That(Collect(Header + MonoBehaviour(StaleGuid, OldestFields), stale), Is.EqualTo(1));
            Assert.That(stale, Is.EquivalentTo(new[] { StaleGuid }));
        }

        [Test]
        public void CollectStaleGuids_ReadsCrLfFiles()
        {
            var stale = new HashSet<string>();
            var yaml = (Header + MonoBehaviour(StaleGuid, LatestFields)).Replace("\n", "\r\n");

            Assert.That(Collect(yaml, stale), Is.EqualTo(1));
        }

        [Test]
        public void CollectStaleGuids_Ignores_AComponentThatAlreadyPointsAtTheCurrentScript()
        {
            var stale = new HashSet<string>();

            Assert.That(Collect(Header + MonoBehaviour(CurrentGuid, LatestFields), stale), Is.EqualTo(0));
            Assert.That(stale, Is.Empty);
        }

        // GUIDがアセットを指しているなら、同じフィールドを持つ別のスクリプトが生きている
        [Test]
        public void CollectStaleGuids_Ignores_AComponentWhoseScriptStillExists()
        {
            var stale = new HashSet<string>();
            var yaml = Header + MonoBehaviour(OtherGuid, LatestFields);

            Assert.That(Collect(yaml, stale, OtherGuid), Is.EqualTo(0));
            Assert.That(stale, Is.Empty);
        }

        // 参照が切れていても、フィールドが揃わなければ別のツールのコンポーネントである
        [Test]
        public void CollectStaleGuids_Ignores_AMissingScriptWithDifferentFields()
        {
            var stale = new HashSet<string>();
            var fields =
                "  targetRenderer: {fileID: 1}\n" +
                "  enableLeftRightSplit: 1\n" +
                "  intensityMultiplier: 1\n";

            Assert.That(Collect(Header + MonoBehaviour(StaleGuid, fields), stale), Is.EqualTo(0));
            Assert.That(stale, Is.Empty);
        }

        // 入れ子の中に同じ名前があっても、コンポーネント直下のフィールドとは数えない
        [Test]
        public void CollectStaleGuids_DoesNotCountNestedFields()
        {
            var stale = new HashSet<string>();
            var fields =
                "  settings:\n" +
                "    targetRenderer: {fileID: 1}\n" +
                "    intensityMultiplier: 1\n" +
                "    enableLeftRightSplit: 1\n" +
                "    overwriteExisting: 0\n" +
                "    customMappings: []\n" +
                "    debugMode: 0\n";

            Assert.That(Collect(Header + MonoBehaviour(StaleGuid, fields), stale), Is.EqualTo(0));
        }

        // フィールドが別々のコンポーネントに散らばっている場合は、揃っているとみなさない
        [Test]
        public void CollectStaleGuids_DoesNotCombineFieldsAcrossComponents()
        {
            var stale = new HashSet<string>();
            var yaml = Header +
                       MonoBehaviour(StaleGuid, "  targetRenderer: {fileID: 1}\n  intensityMultiplier: 1\n  enableLeftRightSplit: 1\n") +
                       MonoBehaviour(StaleGuid, "  overwriteExisting: 0\n  customMappings: []\n  debugMode: 0\n");

            Assert.That(Collect(yaml, stale), Is.EqualTo(0));
        }

        [Test]
        public void CollectStaleGuids_Ignores_AComponentOfAnotherClass()
        {
            var stale = new HashSet<string>();
            var yaml = Header + MonoBehaviour(StaleGuid, LatestFields, "Some.Assembly::Some.OtherComponent");

            Assert.That(Collect(yaml, stale), Is.EqualTo(0));
        }

        [Test]
        public void CollectStaleGuids_Accepts_TheClassIdentifierOfThisComponent()
        {
            var stale = new HashSet<string>();
            var yaml = Header + MonoBehaviour(
                StaleGuid,
                LatestFields,
                "ARKitBlendShapeGenerator.Runtime::ARKitBlendShapeGenerator.ARKitBlendShapeGeneratorComponent");

            Assert.That(Collect(yaml, stale), Is.EqualTo(1));
        }

        [Test]
        public void CollectStaleGuids_CountsEveryComponentAndCollectsEachGuidOnce()
        {
            var stale = new HashSet<string>();
            var yaml = Header +
                       MonoBehaviour(StaleGuid, LatestFields) +
                       GameObject +
                       MonoBehaviour(StaleGuid, OldestFields) +
                       MonoBehaviour(OtherGuid, LatestFields);

            Assert.That(Collect(yaml, stale), Is.EqualTo(3));
            Assert.That(stale, Is.EquivalentTo(new[] { StaleGuid, OtherGuid }));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: Body\n")]
        public void CollectStaleGuids_FindsNothing_WhenTheTextHasNoComponent(string yaml)
        {
            var stale = new HashSet<string>();

            Assert.That(Collect(yaml, stale), Is.EqualTo(0));
            Assert.That(stale, Is.Empty);
        }

        [Test]
        public void ReplaceScriptGuids_RewritesOnlyTheGuid()
        {
            var yaml = Header + GameObject + MonoBehaviour(StaleGuid, LatestFields);

            var rewritten = MissingScriptReferences.ReplaceScriptGuids(
                yaml, new[] { StaleGuid }, CurrentGuid, out var replaced);

            Assert.That(replaced, Is.EqualTo(1));
            Assert.That(rewritten, Is.EqualTo(Header + GameObject + MonoBehaviour(CurrentGuid, LatestFields)));
        }

        [Test]
        public void ReplaceScriptGuids_KeepsCrLfLineEndings()
        {
            var yaml = (Header + MonoBehaviour(StaleGuid, LatestFields)).Replace("\n", "\r\n");

            var rewritten = MissingScriptReferences.ReplaceScriptGuids(
                yaml, new[] { StaleGuid }, CurrentGuid, out var replaced);

            Assert.That(replaced, Is.EqualTo(1));
            Assert.That(
                rewritten,
                Is.EqualTo((Header + MonoBehaviour(CurrentGuid, LatestFields)).Replace("\n", "\r\n")));
        }

        // ネストしたプレハブの中のコンポーネントを指す記述は、設定値を持たずGUIDだけを持つ
        [Test]
        public void ReplaceScriptGuids_RewritesStrippedReferencesToo()
        {
            var yaml = Header + StrippedMonoBehaviour(StaleGuid);

            var rewritten = MissingScriptReferences.ReplaceScriptGuids(
                yaml, new[] { StaleGuid }, CurrentGuid, out var replaced);

            Assert.That(replaced, Is.EqualTo(1));
            Assert.That(rewritten, Is.EqualTo(Header + StrippedMonoBehaviour(CurrentGuid)));
        }

        // 同じGUIDがスクリプト参照以外の場所（プレハブの参照元など）に現れても触らない
        [Test]
        public void ReplaceScriptGuids_LeavesOtherReferencesToTheSameGuid()
        {
            var other =
                "--- !u!1001 &42\n" +
                "PrefabInstance:\n" +
                "  m_SourcePrefab: {fileID: 100100000, guid: " + StaleGuid + ", type: 3}\n";
            var yaml = Header + other + MonoBehaviour(StaleGuid, LatestFields);

            var rewritten = MissingScriptReferences.ReplaceScriptGuids(
                yaml, new[] { StaleGuid }, CurrentGuid, out var replaced);

            Assert.That(replaced, Is.EqualTo(1));
            Assert.That(rewritten, Is.EqualTo(Header + other + MonoBehaviour(CurrentGuid, LatestFields)));
        }

        [Test]
        public void ReplaceScriptGuids_ReturnsTheSameText_WhenNothingMatches()
        {
            var yaml = Header + MonoBehaviour(OtherGuid, LatestFields);

            var rewritten = MissingScriptReferences.ReplaceScriptGuids(
                yaml, new[] { StaleGuid }, CurrentGuid, out var replaced);

            Assert.That(replaced, Is.EqualTo(0));
            Assert.That(rewritten, Is.SameAs(yaml));
        }

        // 付け替え先が壊れた値のまま書き込むと、参照を別の形で壊す
        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("not-a-guid")]
        [TestCase("CFE038BEF4164364DB38E499EAEB6E10")]
        public void ReplaceScriptGuids_DoesNothing_WhenTheCurrentGuidIsInvalid(string currentGuid)
        {
            var yaml = Header + MonoBehaviour(StaleGuid, LatestFields);

            var rewritten = MissingScriptReferences.ReplaceScriptGuids(
                yaml, new[] { StaleGuid }, currentGuid, out var replaced);

            Assert.That(replaced, Is.EqualTo(0));
            Assert.That(rewritten, Is.SameAs(yaml));
        }

        [Test]
        public void CollectThenReplace_RepairsEveryComponentInTheText()
        {
            var stale = new HashSet<string>();
            var yaml = Header +
                       MonoBehaviour(StaleGuid, LatestFields) +
                       GameObject +
                       StrippedMonoBehaviour(StaleGuid);

            Collect(yaml, stale);
            var rewritten = MissingScriptReferences.ReplaceScriptGuids(yaml, stale, CurrentGuid, out var replaced);

            Assert.That(replaced, Is.EqualTo(2));
            Assert.That(Collect(rewritten, new HashSet<string>()), Is.EqualTo(0));
        }
    }
}
