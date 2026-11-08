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
        public void MoveToSeparateObject_MovesComponentToNewChild_KeepingSettings()
        {
            var avatar = CreateAvatar();
            var face = AddRenderer(avatar.transform, "Face");
            var source = avatar.AddComponent<ARKitBlendShapeGeneratorComponent>();
            source.targetRenderer = face;
            source.intensityMultiplier = 1.3f;
            source.outputNaming = BlendShapeNaming.UnifiedExpressions;
            source.customMappings = new List<CustomBlendShapeMapping>
            {
                new CustomBlendShapeMapping
                {
                    arkitName = "jawOpen",
                    sources = new List<BlendShapeSource> { new BlendShapeSource { blendShapeName = "あ", weight = 0.8f } },
                },
            };

            var created = SeparateGeneratorMenu.MoveToSeparateObject(source);

            Assert.That(avatar.GetComponent<ARKitBlendShapeGeneratorComponent>(), Is.Null);
            Assert.That(created.name, Is.EqualTo(CreateGeneratorMenu.ObjectName));
            Assert.That(created.transform.parent, Is.EqualTo(avatar.transform));

            var moved = created.GetComponent<ARKitBlendShapeGeneratorComponent>();
            Assert.That(moved, Is.Not.Null);
            Assert.That(moved.targetRenderer, Is.EqualTo(face));
            Assert.That(moved.intensityMultiplier, Is.EqualTo(1.3f));
            Assert.That(moved.outputNaming, Is.EqualTo(BlendShapeNaming.UnifiedExpressions));
            Assert.That(moved.customMappings, Has.Count.EqualTo(1));
            Assert.That(moved.customMappings[0].arkitName, Is.EqualTo("jawOpen"));
            Assert.That(moved.customMappings[0].sources[0].blendShapeName, Is.EqualTo("あ"));
            Assert.That(moved.customMappings[0].sources[0].weight, Is.EqualTo(0.8f));
        }

        [Test]
        public void MoveToSeparateObject_KeepsResolvedRenderer_WhenTargetRendererIsUnset()
        {
            var avatar = CreateAvatar();
            AddRenderer(avatar.transform, "Hair");
            var body = AddRenderer(avatar.transform, "Body");
            var source = avatar.AddComponent<ARKitBlendShapeGeneratorComponent>();
            source.targetRenderer = null;
            Assert.That(TargetRendererResolver.Resolve(source), Is.EqualTo(body));

            var moved = SeparateGeneratorMenu.MoveToSeparateObject(source)
                .GetComponent<ARKitBlendShapeGeneratorComponent>();

            // 未設定のまま引き継ぎ、アバタールートの配下から同じレンダラーが選ばれる
            Assert.That(moved.targetRenderer, Is.Null);
            Assert.That(TargetRendererResolver.Resolve(moved), Is.EqualTo(body));
        }

        [Test]
        public void MoveToSeparateObject_IsRevertedBySingleUndo()
        {
            var avatar = CreateAvatar();
            var source = avatar.AddComponent<ARKitBlendShapeGeneratorComponent>();
            source.intensityMultiplier = 0.7f;

            var created = SeparateGeneratorMenu.MoveToSeparateObject(source);
            Undo.PerformUndo();

            Assert.That(created == null, Is.True);
            var restored = avatar.GetComponent<ARKitBlendShapeGeneratorComponent>();
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.intensityMultiplier, Is.EqualTo(0.7f));
        }

        [Test]
        public void FindMovableComponent_ReturnsComponentOnlyOnAvatarRoot()
        {
            var avatar = CreateAvatar();
            var face = AddRenderer(avatar.transform, "Face");
            var onFace = face.gameObject.AddComponent<ARKitBlendShapeGeneratorComponent>();

            // 顔メッシュなど、アバタールート以外に置いたものは対象外にする
            Assert.That(SeparateGeneratorMenu.FindMovableComponent(face.gameObject), Is.Null);
            Assert.That(SeparateGeneratorMenu.FindMovableComponent(avatar), Is.Null);
            Assert.That(SeparateGeneratorMenu.FindMovableComponent(null), Is.Null);

            Object.DestroyImmediate(onFace);
            var onRoot = avatar.AddComponent<ARKitBlendShapeGeneratorComponent>();
            Assert.That(SeparateGeneratorMenu.FindMovableComponent(avatar), Is.EqualTo(onRoot));
        }
    }
}
