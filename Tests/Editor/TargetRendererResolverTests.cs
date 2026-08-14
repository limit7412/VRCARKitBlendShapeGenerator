using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARKitBlendShapeGenerator.UseCase;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 生成対象レンダラーの解決順序を検証する
    /// ビルド・NDMFプレビュー・インスペクタUI・シェイプキー一覧の全経路が同じ結果を返すことを担保する
    /// </summary>
    public class TargetRendererResolverTests
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

        private ARKitBlendShapeGeneratorComponent CreateRoot()
        {
            var root = new GameObject("Avatar");
            _created.Add(root);
            return root.AddComponent<ARKitBlendShapeGeneratorComponent>();
        }

        private SkinnedMeshRenderer AddRenderer(Transform parent, string name, bool active = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.SetActive(active);
            return go.AddComponent<SkinnedMeshRenderer>();
        }

        private Mesh CreateMeshWithShape(string shapeName)
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero } };
            mesh.AddBlendShapeFrame(shapeName, 100f, new[] { Vector3.up }, null, null);
            _created.Add(mesh);
            return mesh;
        }

        /// <summary>
        /// NDMFプレビューはComputeContext経由でレンダラーを列挙するため、
        /// 同じ候補列をSelectFallbackに渡す形で経路を再現する
        /// </summary>
        private static SkinnedMeshRenderer ResolveAsPreview(ARKitBlendShapeGeneratorComponent component)
        {
            if (component.targetRenderer != null)
            {
                return component.targetRenderer;
            }

            return TargetRendererResolver.SelectFallback(
                component.transform,
                component.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        [Test]
        public void Resolve_ReturnsNull_WhenComponentIsNull()
        {
            Assert.That(TargetRendererResolver.Resolve(null), Is.Null);
        }

        [Test]
        public void Resolve_ReturnsNull_WhenNoRendererExists()
        {
            var component = CreateRoot();

            Assert.That(TargetRendererResolver.Resolve(component), Is.Null);
        }

        [Test]
        public void Resolve_ReturnsTargetRenderer_WhenAssigned()
        {
            var component = CreateRoot();
            var body = AddRenderer(component.transform, "Body");
            var explicitTarget = AddRenderer(component.transform, "Hair");
            component.targetRenderer = explicitTarget;

            // 名前検索より明示的な指定が優先される
            Assert.That(TargetRendererResolver.Resolve(component), Is.EqualTo(explicitTarget));
            Assert.That(TargetRendererResolver.Resolve(component), Is.Not.EqualTo(body));
        }

        [Test]
        public void Resolve_PrefersNamedChild_OverFirstInHierarchy()
        {
            var component = CreateRoot();
            AddRenderer(component.transform, "Hair");
            var body = AddRenderer(component.transform, "Body");

            Assert.That(TargetRendererResolver.Resolve(component), Is.EqualTo(body));
        }

        [Test]
        public void Resolve_PrefersBody_OverFaceAndHead()
        {
            var component = CreateRoot();
            AddRenderer(component.transform, "Head");
            AddRenderer(component.transform, "Face");
            var body = AddRenderer(component.transform, "Body");

            Assert.That(TargetRendererResolver.Resolve(component), Is.EqualTo(body));
        }

        [Test]
        public void Resolve_IncludesInactiveRenderer()
        {
            var component = CreateRoot();
            var inactive = AddRenderer(component.transform, "Hair", active: false);

            Assert.That(TargetRendererResolver.Resolve(component), Is.EqualTo(inactive));
        }

        [Test]
        public void Resolve_PrefersInactiveNamedChild_OverActiveUnnamedChild()
        {
            var component = CreateRoot();
            AddRenderer(component.transform, "Hair");
            var inactiveBody = AddRenderer(component.transform, "Body", active: false);

            Assert.That(TargetRendererResolver.Resolve(component), Is.EqualTo(inactiveBody));
        }

        [Test]
        public void Resolve_IgnoresNamedGrandchild_ForNameSearch()
        {
            var component = CreateRoot();
            var hair = AddRenderer(component.transform, "Hair");
            var container = new GameObject("Armature");
            container.transform.SetParent(component.transform, false);
            AddRenderer(container.transform, "Body");

            // 名前検索の対象は直下の子のみ。孫階層のBodyは一般フォールバックとしてのみ扱う
            Assert.That(TargetRendererResolver.Resolve(component), Is.EqualTo(hair));
        }

        [Test]
        public void Resolve_FallsBackToChild_WhenTargetRendererIsDestroyed()
        {
            var component = CreateRoot();
            var body = AddRenderer(component.transform, "Body");
            var doomed = AddRenderer(component.transform, "Hair");
            component.targetRenderer = doomed;

            Object.DestroyImmediate(doomed.gameObject);

            // Missing参照はC#のnullではないため、==演算子ではなくUnityのnull判定が必要
            Assert.That(TargetRendererResolver.Resolve(component), Is.EqualTo(body));
        }

        [Test]
        public void AllEntryPoints_ResolveSameRenderer_WhenTargetRendererIsUnset()
        {
            var component = CreateRoot();
            AddRenderer(component.transform, "Hair");
            var inactiveBody = AddRenderer(component.transform, "Body", active: false);
            inactiveBody.sharedMesh = CreateMeshWithShape("まばたき");

            // ビルドとインスペクタUIが共有する経路
            Assert.That(
                GenerateBlendShapesUseCase.ResolveTargetRenderer(component),
                Is.EqualTo(inactiveBody));

            // NDMFプレビューの経路
            Assert.That(ResolveAsPreview(component), Is.EqualTo(inactiveBody));

            // シェイプキー一覧の経路
            Assert.That(component.GetAvailableBlendShapes(), Is.EqualTo(new[] { "まばたき" }));
        }

        [Test]
        public void GetAvailableBlendShapes_FallsBackToChild_WhenTargetRendererIsDestroyed()
        {
            var component = CreateRoot();
            var body = AddRenderer(component.transform, "Body");
            body.sharedMesh = CreateMeshWithShape("まばたき");
            var doomed = AddRenderer(component.transform, "Hair");
            component.targetRenderer = doomed;

            Object.DestroyImmediate(doomed.gameObject);

            Assert.That(component.GetAvailableBlendShapes(), Is.EqualTo(new[] { "まばたき" }));
        }

        [Test]
        public void SelectFallback_SkipsNullEntries()
        {
            var component = CreateRoot();
            var body = AddRenderer(component.transform, "Body");

            var candidates = new SkinnedMeshRenderer[] { null, body };

            Assert.That(
                TargetRendererResolver.SelectFallback(component.transform, candidates),
                Is.EqualTo(body));
        }

        [Test]
        public void SelectFallback_ReturnsNull_WhenCandidatesAreNull()
        {
            var component = CreateRoot();

            Assert.That(TargetRendererResolver.SelectFallback(component.transform, null), Is.Null);
        }
    }
}
