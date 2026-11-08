using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ARKitBlendShapeGenerator.Handler;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// ヒエラルキーの右クリックからGeneratorを付けたオブジェクトを作るメニューを検証する
    /// </summary>
    public class CreateGeneratorMenuTests
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
        public void CreateObject_CreatesChildWithGenerator_TargetingAvatarBody()
        {
            var avatar = CreateAvatar();
            var body = AddRenderer(avatar.transform, "Body");

            var created = CreateGeneratorMenu.CreateObject(avatar);

            Assert.That(created, Is.Not.Null);
            Assert.That(created.name, Is.EqualTo(CreateGeneratorMenu.ObjectName));
            Assert.That(created.transform.parent, Is.EqualTo(avatar.transform));

            var component = created.GetComponent<ARKitBlendShapeGeneratorComponent>();
            Assert.That(component, Is.Not.Null);
            Assert.That(component.targetRenderer, Is.EqualTo(body));
        }

        [Test]
        public void FindExistingGenerator_FindsGeneratorAnywhereInAvatar()
        {
            var avatar = CreateAvatar();
            var face = AddRenderer(avatar.transform, "Face");
            var existing = face.gameObject.AddComponent<ARKitBlendShapeGeneratorComponent>();
            var armature = new GameObject("Armature");
            armature.transform.SetParent(avatar.transform, false);

            // 右クリックしたのが別の枝でも、同じアバターの中にあれば見つける
            Assert.That(CreateGeneratorMenu.FindExistingGenerator(armature), Is.EqualTo(existing));
        }

        [Test]
        public void FindExistingGenerator_ReturnsNull_WhenAvatarHasNoGenerator()
        {
            var avatar = CreateAvatar();

            Assert.That(CreateGeneratorMenu.FindExistingGenerator(avatar), Is.Null);
            Assert.That(CreateGeneratorMenu.FindExistingGenerator(null), Is.Null);
        }
    }
}
