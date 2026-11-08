using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ARKitBlendShapeGenerator.Handler;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// アバタールートに付いたGeneratorを専用のオブジェクトへ移すメニューを検証する
    /// </summary>
    public class SeparateGeneratorMenuTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }

            _created.Clear();
        }

        private GameObject CreateAvatar()
        {
            var root = new GameObject("Avatar");
            _created.Add(root);
            root.AddComponent<VRCAvatarDescriptor>();
            return root;
        }

        private static SkinnedMeshRenderer AddRenderer(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<SkinnedMeshRenderer>();
        }

        [Test]
        public void Separate_MovesGeneratorToChild_KeepingSettings()
        {
            var avatar = CreateAvatar();
            AddRenderer(avatar.transform, "Body");
            var face = AddRenderer(avatar.transform, "Face");

            var source = avatar.AddComponent<ARKitBlendShapeGeneratorComponent>();
            source.targetRenderer = face;
            source.intensityMultiplier = 1.3f;
            source.outputNaming = BlendShapeNaming.UnifiedExpressions;
            source.customMappings = new List<CustomBlendShapeMapping>
            {
                new CustomBlendShapeMapping { arkitName = "jawOpen", enabled = true },
            };
            source.enabled = false;

            var separated = SeparateGeneratorMenu.Separate(source);

            Assert.That(avatar.GetComponent<ARKitBlendShapeGeneratorComponent>(), Is.Null);
            Assert.That(separated.name, Is.EqualTo(CreateGeneratorMenu.ObjectName));
            Assert.That(separated.transform.parent, Is.EqualTo(avatar.transform));

            var moved = separated.GetComponent<ARKitBlendShapeGeneratorComponent>();
            Assert.That(moved, Is.Not.Null);

            // AddComponentのResetが選ぶBodyではなく、元の設定のFaceが残る
            Assert.That(moved.targetRenderer, Is.EqualTo(face));
            Assert.That(moved.intensityMultiplier, Is.EqualTo(1.3f));
            Assert.That(moved.outputNaming, Is.EqualTo(BlendShapeNaming.UnifiedExpressions));
            Assert.That(moved.customMappings, Has.Count.EqualTo(1));
            Assert.That(moved.customMappings[0].arkitName, Is.EqualTo("jawOpen"));
            Assert.That(moved.enabled, Is.False);
        }

        [Test]
        public void Separate_CanBeUndoneAtOnce()
        {
            var avatar = CreateAvatar();
            var source = avatar.AddComponent<ARKitBlendShapeGeneratorComponent>();
            source.intensityMultiplier = 1.3f;

            SeparateGeneratorMenu.Separate(source);
            Undo.PerformUndo();

            var restored = avatar.GetComponent<ARKitBlendShapeGeneratorComponent>();
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.intensityMultiplier, Is.EqualTo(1.3f));
            Assert.That(avatar.transform.childCount, Is.EqualTo(0));
        }

        [Test]
        public void CanSeparate_IsTrueOnlyOnAvatarRoot()
        {
            var avatar = CreateAvatar();
            var onRoot = avatar.AddComponent<ARKitBlendShapeGeneratorComponent>();

            var otherAvatar = CreateAvatar();
            var child = new GameObject("Child");
            child.transform.SetParent(otherAvatar.transform, false);
            var onChild = child.AddComponent<ARKitBlendShapeGeneratorComponent>();

            Assert.That(SeparateGeneratorMenu.CanSeparate(onRoot), Is.True);
            Assert.That(SeparateGeneratorMenu.CanSeparate(onChild), Is.False);
            Assert.That(SeparateGeneratorMenu.CanSeparate(null), Is.False);
        }
    }
}
