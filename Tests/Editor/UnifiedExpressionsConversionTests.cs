using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// 他のパッケージへ公開するUE変換（UnifiedExpressionsConversion）の検証。
    /// Kx VRC NativeFT Manager がリフレクションで呼ぶので、型とメソッドの形が変わっていないことも確かめる
    /// </summary>
    public class UnifiedExpressionsConversionTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
        }

        private SkinnedMeshRenderer CreateRenderer(params string[] shapeNames)
        {
            var mesh = new Mesh
            {
                name = "Body",
                vertices = new[] { new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) },
                triangles = new[] { 0, 1, 2 },
            };
            var deltas = new[] { Vector3.up, Vector3.up, Vector3.zero };
            foreach (var name in shapeNames)
            {
                mesh.AddBlendShapeFrame(name, 100f, deltas, null, null);
            }

            _created.Add(mesh);
            var gameObject = new GameObject("Body");
            _created.Add(gameObject);
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private static List<string> ShapeNames(Mesh mesh)
        {
            return Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToList();
        }

        [Test]
        public void ConvertArkitShapes_AddsUnifiedExpressionsShapes_OnACopyOfTheMesh()
        {
            var renderer = CreateRenderer("eyeBlinkLeft", "browInnerUp", "あ");
            var original = renderer.sharedMesh;

            var converted = UnifiedExpressionsConversion.ConvertArkitShapes(renderer);

            Assert.That(converted, Is.EquivalentTo(new[] { "EyeClosedLeft", "BrowInnerUpLeft", "BrowInnerUpRight" }));
            Assert.That(renderer.sharedMesh, Is.Not.SameAs(original));
            _created.Add(renderer.sharedMesh);
            Assert.That(
                ShapeNames(renderer.sharedMesh),
                Is.EqualTo(new[] { "eyeBlinkLeft", "browInnerUp", "あ", "EyeClosedLeft", "BrowInnerUpLeft", "BrowInnerUpRight" }));

            // 元のメッシュアセットは変えない
            Assert.That(ShapeNames(original), Is.EqualTo(new[] { "eyeBlinkLeft", "browInnerUp", "あ" }));
        }

        [Test]
        public void ConvertArkitShapes_DoesNotGenerateFromNonArkitShapes()
        {
            // VRChat/MMDのシェイプキーからの自動マッピングは使わない
            var renderer = CreateRenderer("あ", "まばたき");
            var original = renderer.sharedMesh;

            var converted = UnifiedExpressionsConversion.ConvertArkitShapes(renderer);

            Assert.That(converted, Is.Empty);
            Assert.That(renderer.sharedMesh, Is.SameAs(original));
        }

        [Test]
        public void ConvertArkitShapes_KeepsExistingUnifiedExpressionsShapes()
        {
            var renderer = CreateRenderer("eyeBlinkLeft", "EyeClosedLeft");
            var original = renderer.sharedMesh;

            var converted = UnifiedExpressionsConversion.ConvertArkitShapes(renderer);

            Assert.That(converted, Is.Empty);
            Assert.That(renderer.sharedMesh, Is.SameAs(original));
        }

        [Test]
        public void ConvertArkitShapes_ReturnsEmpty_WhenRendererIsNull()
        {
            Assert.That(UnifiedExpressionsConversion.ConvertArkitShapes(null), Is.Empty);
        }

        [Test]
        public void Signature_IsWhatTheManagerLooksUp()
        {
            var type = typeof(UnifiedExpressionsConversion);
            Assert.That(type.FullName, Is.EqualTo("ARKitBlendShapeGenerator.UnifiedExpressionsConversion"));

            var method = type.GetMethod(
                "ConvertArkitShapes",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(SkinnedMeshRenderer) },
                null);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(string[])));
        }
    }
}
