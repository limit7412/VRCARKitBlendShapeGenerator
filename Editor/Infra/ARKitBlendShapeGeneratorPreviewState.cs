using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace ARKitBlendShapeGenerator.Infra
{
    internal static class ARKitBlendShapeGeneratorPreviewState
    {
        internal sealed class Snapshot
        {
            public readonly int ActiveComponentInstanceId;
            public readonly bool InteractiveEnabled;
            public readonly int Revision;
            public readonly Dictionary<string, float> WeightsByArkitName;

            public Snapshot(
                int activeComponentInstanceId,
                bool interactiveEnabled,
                int revision,
                Dictionary<string, float> weightsByArkitName)
            {
                ActiveComponentInstanceId = activeComponentInstanceId;
                InteractiveEnabled = interactiveEnabled;
                Revision = revision;
                WeightsByArkitName = weightsByArkitName ?? new Dictionary<string, float>();
            }

            public float GetWeight(string arkitName)
            {
                if (string.IsNullOrEmpty(arkitName))
                {
                    return 0f;
                }

                if (WeightsByArkitName.TryGetValue(arkitName, out float value))
                {
                    return value;
                }

                return 0f;
            }
        }

        private static readonly Snapshot EmptySnapshot = new Snapshot(
            activeComponentInstanceId: 0,
            interactiveEnabled: false,
            revision: 0,
            weightsByArkitName: new Dictionary<string, float>());

        public static readonly PublishedValue<Snapshot> RuntimeState = new PublishedValue<Snapshot>(
            EmptySnapshot,
            debugName: "ARKitBlendShapeGenerator/PreviewRuntimeState");
        public static readonly PublishedValue<int> ComponentConfigRevision = new PublishedValue<int>(
            0,
            debugName: "ARKitBlendShapeGenerator/ComponentConfigRevision");

        public static Snapshot Current => RuntimeState.Value;

        public static void BeginEdit(int componentInstanceId)
        {
            if (componentInstanceId == 0)
            {
                return;
            }

            var current = Current;
            if (current.ActiveComponentInstanceId == componentInstanceId && current.InteractiveEnabled)
            {
                return;
            }

            RuntimeState.Value = new Snapshot(
                activeComponentInstanceId: componentInstanceId,
                interactiveEnabled: true,
                revision: current.Revision + 1,
                weightsByArkitName: new Dictionary<string, float>());
        }

        public static void ReleaseIfActive(int componentInstanceId)
        {
            if (componentInstanceId == 0)
            {
                return;
            }

            var current = Current;
            if (current.ActiveComponentInstanceId != componentInstanceId)
            {
                return;
            }

            RuntimeState.Value = new Snapshot(
                activeComponentInstanceId: 0,
                interactiveEnabled: false,
                revision: current.Revision + 1,
                weightsByArkitName: new Dictionary<string, float>());
        }

        public static void SetWeight(int componentInstanceId, string arkitName, float value)
        {
            if (componentInstanceId == 0 || string.IsNullOrEmpty(arkitName))
            {
                return;
            }

            var current = Current;
            var nextWeights = current.ActiveComponentInstanceId == componentInstanceId
                ? new Dictionary<string, float>(current.WeightsByArkitName)
                : new Dictionary<string, float>();

            float clamped = Mathf.Clamp01(value);
            if (clamped <= 0.0001f)
            {
                nextWeights.Remove(arkitName);
            }
            else
            {
                nextWeights[arkitName] = clamped;
            }

            RuntimeState.Value = new Snapshot(
                activeComponentInstanceId: componentInstanceId,
                interactiveEnabled: true,
                revision: current.Revision + 1,
                weightsByArkitName: nextWeights);
        }

        public static void SetAllWeights(int componentInstanceId, IEnumerable<string> arkitNames, float value)
        {
            if (componentInstanceId == 0)
            {
                return;
            }

            float clamped = Mathf.Clamp01(value);
            var names = arkitNames != null
                ? arkitNames.Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList()
                : new List<string>();
            var nextWeights = new Dictionary<string, float>();

            if (clamped > 0.0001f)
            {
                foreach (var arkitName in names)
                {
                    nextWeights[arkitName] = clamped;
                }
            }

            var current = Current;
            RuntimeState.Value = new Snapshot(
                activeComponentInstanceId: componentInstanceId,
                interactiveEnabled: true,
                revision: current.Revision + 1,
                weightsByArkitName: nextWeights);
        }

        public static void NotifyComponentConfigurationChanged()
        {
            ComponentConfigRevision.Value = ComponentConfigRevision.Value + 1;
        }

    }
}
