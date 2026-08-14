using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.UseCase;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 処理対象コンポーネントの選定の検証。
    ///
    /// 各コンポーネントは別々のルートへ配置している。
    /// 同一階層に複数置くと重複コンポーネントガードが働き、選定ロジックとは別の要因で結果が変わるため。
    /// 選定はTransformの一致だけで決まるので、階層を組まなくても仕様は確認できる。
    /// </summary>
    public class GenerateBlendShapesUseCaseTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in _created)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            _created.Clear();
        }

        private ARKitBlendShapeGeneratorComponent CreateComponent(string name)
        {
            var gameObject = new GameObject(name);
            _created.Add(gameObject);
            return gameObject.AddComponent<ARKitBlendShapeGeneratorComponent>();
        }

        [Test]
        public void SelectPrimaryComponent_ReturnsNull_WhenComponentsAreNull()
        {
            var avatarRoot = CreateComponent("Avatar").transform;

            Assert.That(GenerateBlendShapesUseCase.SelectPrimaryComponent(avatarRoot, null), Is.Null);
        }

        [Test]
        public void SelectPrimaryComponent_ReturnsNull_WhenComponentsAreEmpty()
        {
            var avatarRoot = CreateComponent("Avatar").transform;

            Assert.That(
                GenerateBlendShapesUseCase.SelectPrimaryComponent(
                    avatarRoot, new List<ARKitBlendShapeGeneratorComponent>()),
                Is.Null);
        }

        [Test]
        public void SelectPrimaryComponent_PrefersComponentOnAvatarRoot()
        {
            var face = CreateComponent("Face");
            var root = CreateComponent("Avatar");

            var primary = GenerateBlendShapesUseCase.SelectPrimaryComponent(
                root.transform,
                new List<ARKitBlendShapeGeneratorComponent> { face, root });

            // 先頭ではなくアバタールート直付けのものが選ばれる
            Assert.That(primary, Is.SameAs(root));
        }

        [Test]
        public void SelectPrimaryComponent_ReturnsFirst_WhenNoComponentIsOnAvatarRoot()
        {
            var avatarRoot = new GameObject("Avatar");
            _created.Add(avatarRoot);
            var face = CreateComponent("Face");
            var hair = CreateComponent("Hair");

            var primary = GenerateBlendShapesUseCase.SelectPrimaryComponent(
                avatarRoot.transform,
                new List<ARKitBlendShapeGeneratorComponent> { face, hair });

            Assert.That(primary, Is.SameAs(face));
        }

        [Test]
        public void SelectPrimaryComponent_ReturnsFirst_WhenAvatarRootIsNull()
        {
            var face = CreateComponent("Face");
            var root = CreateComponent("Avatar");

            var primary = GenerateBlendShapesUseCase.SelectPrimaryComponent(
                (Transform)null,
                new List<ARKitBlendShapeGeneratorComponent> { face, root });

            Assert.That(primary, Is.SameAs(face));
        }

        [Test]
        public void SelectPrimaryComponent_SkipsNullEntries_WhileSearchingAvatarRoot()
        {
            var root = CreateComponent("Avatar");

            var primary = GenerateBlendShapesUseCase.SelectPrimaryComponent(
                root.transform,
                new List<ARKitBlendShapeGeneratorComponent> { null, root });

            // nullを踏んでもTransformの比較へ進まず、後続のルート直付けを見つける
            Assert.That(primary, Is.SameAs(root));
        }

        [Test]
        public void SelectPrimaryComponent_ReturnsFirstEntryAsIs_WhenNoComponentIsOnAvatarRoot()
        {
            var avatarRoot = new GameObject("Avatar");
            _created.Add(avatarRoot);
            var face = CreateComponent("Face");

            var primary = GenerateBlendShapesUseCase.SelectPrimaryComponent(
                avatarRoot.transform,
                new List<ARKitBlendShapeGeneratorComponent> { null, face });

            // ルート直付けが無いときのフォールバックは先頭をそのまま返す。
            // nullを除くのは呼び出し側の責務で、現在の呼び出し3経路はいずれも渡す前に除いている
            Assert.That(primary, Is.Null);
        }

        [Test]
        public void SelectPrimaryComponent_AcceptsGameObjectAsAvatarRoot()
        {
            var face = CreateComponent("Face");
            var root = CreateComponent("Avatar");

            var primary = GenerateBlendShapesUseCase.SelectPrimaryComponent(
                root.gameObject,
                new List<ARKitBlendShapeGeneratorComponent> { face, root });

            Assert.That(primary, Is.SameAs(root));
        }

        [Test]
        public void SelectPrimaryComponent_ReturnsFirst_WhenAvatarRootGameObjectIsNull()
        {
            var face = CreateComponent("Face");
            var root = CreateComponent("Avatar");

            var primary = GenerateBlendShapesUseCase.SelectPrimaryComponent(
                (GameObject)null,
                new List<ARKitBlendShapeGeneratorComponent> { face, root });

            Assert.That(primary, Is.SameAs(face));
        }
    }
}
