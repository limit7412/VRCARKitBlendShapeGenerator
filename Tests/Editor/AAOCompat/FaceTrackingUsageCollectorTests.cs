using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ARKitBlendShapeGenerator.AAOCompat.Tests
{
    /// <summary>
    /// Avatar Descriptorのフェイストラッキング設定から、AAOへ申告する（メッシュ, BlendShape）の組を集める処理の検証
    /// </summary>
    public class FaceTrackingUsageCollectorTests
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

        private VRCAvatarDescriptor CreateDescriptor()
        {
            var root = new GameObject("Avatar");
            _created.Add(root);
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            descriptor.enableFaceTracking = true;
            descriptor.faceTrackingMeshes = new List<SkinnedMeshRenderer>();
            descriptor.faceTrackingBlendshapesSetList = new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>();
            return descriptor;
        }

        private SkinnedMeshRenderer AddRenderer(VRCAvatarDescriptor descriptor, string name, params string[] shapes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(descriptor.transform, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero } };
            foreach (var shape in shapes)
            {
                mesh.AddBlendShapeFrame(shape, 100f, new[] { Vector3.up }, null, null);
            }

            _created.Add(mesh);
            renderer.sharedMesh = mesh;
            descriptor.faceTrackingMeshes.Add(renderer);
            return renderer;
        }

        private static VRCAvatarDescriptor.FaceTrackingBlendshapesSet AddSet(
            VRCAvatarDescriptor descriptor, string[] trackingNames, string[] visemeNames = null)
        {
            var set = new VRCAvatarDescriptor.FaceTrackingBlendshapesSet
            {
                setName = "Test",
                faceTrackingBlendshapeNames = trackingNames,
                visemeBlendshapeNames = visemeNames,
            };
            descriptor.faceTrackingBlendshapesSetList.Add(set);
            return set;
        }

        [Test]
        public void Collect_PicksOnlyShapesPresentOnEachFaceTrackingMesh()
        {
            var descriptor = CreateDescriptor();
            var face = AddRenderer(descriptor, "Face", "JawOpen", "MouthSmileLeft");
            var brows = AddRenderer(descriptor, "Brows", "BrowInnerUpLeft");
            AddSet(descriptor, new[] { "JawOpen", "", "BrowInnerUpLeft", "Missing" });

            var entries = FaceTrackingUsageCollector.Collect(descriptor);

            var pairs = entries.Select(e => (e.renderer, e.blendShapeName)).ToList();
            CollectionAssert.AreEquivalent(
                new[] { (face, "JawOpen"), (brows, "BrowInnerUpLeft") },
                pairs);
        }

        [Test]
        public void Collect_IncludesVisemeNamesAndMergesAcrossSets()
        {
            var descriptor = CreateDescriptor();
            var face = AddRenderer(descriptor, "Face", "JawOpen", "vrc.v_aa", "CheekPuffLeft");
            AddSet(descriptor, new[] { "JawOpen" }, new[] { "vrc.v_aa" });
            AddSet(descriptor, new[] { "JawOpen", "CheekPuffLeft" });

            var entries = FaceTrackingUsageCollector.Collect(descriptor);

            CollectionAssert.AreEquivalent(
                new[] { "JawOpen", "vrc.v_aa", "CheekPuffLeft" },
                entries.Select(e => e.blendShapeName).ToList());
            Assert.That(entries.All(e => e.renderer == face));
        }

        [Test]
        public void Collect_ReturnsNothingWhenFaceTrackingDisabled()
        {
            var descriptor = CreateDescriptor();
            AddRenderer(descriptor, "Face", "JawOpen");
            AddSet(descriptor, new[] { "JawOpen" });
            descriptor.enableFaceTracking = false;

            Assert.That(FaceTrackingUsageCollector.Collect(descriptor), Is.Empty);
        }

        [Test]
        public void Collect_SkipsNullMeshesAndNullSets()
        {
            var descriptor = CreateDescriptor();
            descriptor.faceTrackingMeshes.Add(null);
            var face = AddRenderer(descriptor, "Face", "JawOpen");
            descriptor.faceTrackingMeshes.Add(face); // 重複
            descriptor.faceTrackingBlendshapesSetList.Add(null);
            AddSet(descriptor, new[] { "JawOpen" });

            var entries = FaceTrackingUsageCollector.Collect(descriptor);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].renderer, Is.EqualTo(face));
        }

        [Test]
        public void Collect_ReturnsNothingForNullDescriptor()
        {
            Assert.That(FaceTrackingUsageCollector.Collect(null), Is.Empty);
        }
    }
}
