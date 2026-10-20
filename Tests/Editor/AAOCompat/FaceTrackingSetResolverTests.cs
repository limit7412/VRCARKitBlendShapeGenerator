using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ARKitBlendShapeGenerator.AAOCompat.Tests
{
    /// <summary>
    /// AAOの処理後に、目印の記録でBlendshapes SetとFace Tracking Meshesを書き直す処理の検証
    /// </summary>
    public class FaceTrackingSetResolverTests
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

        private SkinnedMeshRenderer CreateRenderer(string name, params string[] shapes)
        {
            var go = new GameObject(name);
            _created.Add(go);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero } };
            foreach (var shape in shapes)
            {
                mesh.AddBlendShapeFrame(shape, 100f, new[] { Vector3.up }, null, null);
            }

            _created.Add(mesh);
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private NativeFaceTrackingBlendShapeUsage CreateUsage(bool mappingApplied)
        {
            var go = new GameObject("Usage");
            _created.Add(go);
            var usage = go.AddComponent<NativeFaceTrackingBlendShapeUsage>();
            usage.mappingApplied = mappingApplied;
            return usage;
        }

        private static NativeFaceTrackingBlendShapeUsage.Entry Mapped(
            SkinnedMeshRenderer from, string name, SkinnedMeshRenderer to, string newName)
        {
            return new NativeFaceTrackingBlendShapeUsage.Entry
            {
                renderer = from,
                blendShapeName = name,
                resolved = true,
                resolvedRenderer = to,
                resolvedBlendShapeName = newName,
            };
        }

        private static NativeFaceTrackingBlendShapeUsage.Entry Removed(SkinnedMeshRenderer from, string name)
        {
            return new NativeFaceTrackingBlendShapeUsage.Entry
            {
                renderer = from,
                blendShapeName = name,
                resolved = false,
            };
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
        public void Apply_RenamesMergedShapesAndClearsRemovedOnes()
        {
            var descriptor = CreateDescriptor();
            var face = CreateRenderer("Face", "AAO_Merged_JawOpen_MouthClosed_0");
            descriptor.faceTrackingMeshes.Add(face);
            var set = AddSet(descriptor, new[] { "JawOpen", "MouthClosed", "CheekPuffLeft", "", "Untouched" });

            var usage = CreateUsage(mappingApplied: true);
            usage.entries.Add(Mapped(face, "JawOpen", face, "AAO_Merged_JawOpen_MouthClosed_0"));
            usage.entries.Add(Mapped(face, "MouthClosed", face, "AAO_Merged_JawOpen_MouthClosed_0"));
            usage.entries.Add(Removed(face, "CheekPuffLeft"));

            var result = FaceTrackingSetResolver.Apply(descriptor, usage);

            CollectionAssert.AreEqual(
                new[] { "AAO_Merged_JawOpen_MouthClosed_0", "AAO_Merged_JawOpen_MouthClosed_0", "", "", "Untouched" },
                set.faceTrackingBlendshapeNames);
            Assert.That(result.Renamed, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { "CheekPuffLeft" }, result.Removed);
        }

        [Test]
        public void Apply_FollowsShapesMovedToAnotherMeshAndRegistersIt()
        {
            var descriptor = CreateDescriptor();
            var face = CreateRenderer("Face");
            var body = CreateRenderer("Body", "JawOpen");
            // AAOの参照の置き換えで、統合元の参照は統合先に差し替わる。ここではその後の状態を作る
            descriptor.faceTrackingMeshes.Add(body);
            descriptor.faceTrackingMeshes.Add(null);
            var set = AddSet(descriptor, new[] { "JawOpen" }, new[] { "vrc.v_aa" });

            var usage = CreateUsage(mappingApplied: true);
            usage.entries.Add(Mapped(face, "JawOpen", body, "JawOpen"));
            usage.entries.Add(Mapped(face, "vrc.v_aa", body, "vrc.v_aa"));

            var result = FaceTrackingSetResolver.Apply(descriptor, usage);

            CollectionAssert.AreEqual(new[] { "JawOpen" }, set.faceTrackingBlendshapeNames);
            CollectionAssert.AreEqual(new[] { "vrc.v_aa" }, set.visemeBlendshapeNames);
            CollectionAssert.AreEqual(new[] { body }, descriptor.faceTrackingMeshes);
            Assert.That(result.Renamed, Is.EqualTo(0));
            Assert.That(result.MeshesAdded, Is.EqualTo(0));
        }

        [Test]
        public void Apply_AddsResolvedMeshMissingFromFaceTrackingMeshes()
        {
            var descriptor = CreateDescriptor();
            var face = CreateRenderer("Face");
            var merged = CreateRenderer("Merged", "JawOpen");
            descriptor.faceTrackingMeshes.Add(face);
            var set = AddSet(descriptor, new[] { "JawOpen" });

            var usage = CreateUsage(mappingApplied: true);
            usage.entries.Add(Mapped(face, "JawOpen", merged, "JawOpen"));

            var result = FaceTrackingSetResolver.Apply(descriptor, usage);

            CollectionAssert.AreEquivalent(new[] { face, merged }, descriptor.faceTrackingMeshes);
            Assert.That(result.MeshesAdded, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { "JawOpen" }, set.faceTrackingBlendshapeNames);
        }

        [Test]
        public void Apply_ReportsMeshesOverLimit()
        {
            var descriptor = CreateDescriptor();
            var face = CreateRenderer("Face");
            for (int i = 0; i < 16; i++)
            {
                descriptor.faceTrackingMeshes.Add(CreateRenderer($"Filler{i}"));
            }

            var merged = CreateRenderer("Merged", "JawOpen");
            AddSet(descriptor, new[] { "JawOpen" });

            var usage = CreateUsage(mappingApplied: true);
            usage.entries.Add(Mapped(face, "JawOpen", merged, "JawOpen"));

            var result = FaceTrackingSetResolver.Apply(descriptor, usage);

            Assert.That(descriptor.faceTrackingMeshes, Does.Not.Contain(merged));
            CollectionAssert.AreEqual(new[] { merged }, result.MeshesOverLimit);
        }

        [Test]
        public void Apply_WithoutMappingInfoResolvesByNameOnCurrentMesh()
        {
            var descriptor = CreateDescriptor();
            var face = CreateRenderer("Face", "JawOpen");
            descriptor.faceTrackingMeshes.Add(face);
            var set = AddSet(descriptor, new[] { "JawOpen", "CheekPuffLeft" });

            var usage = CreateUsage(mappingApplied: false);
            usage.entries.Add(Removed(face, "JawOpen"));
            usage.entries.Add(Removed(face, "CheekPuffLeft"));

            var result = FaceTrackingSetResolver.Apply(descriptor, usage);

            CollectionAssert.AreEqual(new[] { "JawOpen", "" }, set.faceTrackingBlendshapeNames);
            CollectionAssert.AreEqual(new[] { "CheekPuffLeft" }, result.Removed);
        }

        [Test]
        public void Apply_LeavesNamesUnknownToTheMarkerAlone()
        {
            var descriptor = CreateDescriptor();
            var face = CreateRenderer("Face");
            descriptor.faceTrackingMeshes.Add(face);
            var set = AddSet(descriptor, new[] { "NeverExisted" });

            var usage = CreateUsage(mappingApplied: true);

            var result = FaceTrackingSetResolver.Apply(descriptor, usage);

            CollectionAssert.AreEqual(new[] { "NeverExisted" }, set.faceTrackingBlendshapeNames);
            Assert.That(result.Renamed, Is.EqualTo(0));
            Assert.That(result.Removed, Is.Empty);
        }
    }
}
