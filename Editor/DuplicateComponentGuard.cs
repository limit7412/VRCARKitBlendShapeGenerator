using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// 同一アバター内にARKitBlendShapeGeneratorComponentが複数配置された場合、
    /// 後から追加されたコンポーネントを自動削除するガード
    /// RuntimeコンポーネントのOnValidateフック経由で呼び出される
    /// </summary>
    [InitializeOnLoad]
    internal static class DuplicateComponentGuard
    {
        private static readonly HashSet<int> PendingRemoval = new HashSet<int>();

        static DuplicateComponentGuard()
        {
            ARKitBlendShapeGeneratorComponent.EditorOnValidateHook = EnforceSingleComponentPerAvatar;
        }

        private static void EnforceSingleComponentPerAvatar(ARKitBlendShapeGeneratorComponent component)
        {
            if (component == null)
            {
                return;
            }

            var avatarRoot = FindAvatarRootForUniqueness(component);
            if (avatarRoot == null)
            {
                return;
            }

            var components = avatarRoot.GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(true)
                .Where(c => c != null)
                .ToArray();

            int instanceId = component.GetInstanceID();
            if (components.Length <= 1)
            {
                PendingRemoval.Remove(instanceId);
                return;
            }

            var primary = SelectPrimaryComponent(avatarRoot, components);
            if (primary == component)
            {
                PendingRemoval.Remove(instanceId);
                return;
            }

            if (!PendingRemoval.Add(instanceId))
            {
                return;
            }

            Debug.LogWarning("[ARKitGenerator] " + S("guard.log.duplicate"), component);

            EditorApplication.delayCall += () =>
            {
                PendingRemoval.Remove(instanceId);

                if (component == null || avatarRoot == null)
                {
                    return;
                }

                var refreshed = avatarRoot.GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(true)
                    .Where(c => c != null)
                    .ToArray();
                var refreshedPrimary = SelectPrimaryComponent(avatarRoot, refreshed);

                if (refreshedPrimary != component)
                {
                    Undo.DestroyObjectImmediate(component);

                    if (!Application.isBatchMode)
                    {
                        EditorUtility.DisplayDialog(
                            S("dialog.title"),
                            S("guard.dialog.duplicate_removed"),
                            S("common.ok"));
                    }
                }
            };
        }

        private static Transform FindAvatarRootForUniqueness(ARKitBlendShapeGeneratorComponent component)
        {
            Transform lastDescriptorRoot = null;
            var cursor = component.transform;

            while (cursor != null)
            {
                if (HasAvatarDescriptor(cursor.gameObject))
                {
                    lastDescriptorRoot = cursor;
                }

                cursor = cursor.parent;
            }

            if (lastDescriptorRoot != null)
            {
                return lastDescriptorRoot;
            }

            return component.transform.root;
        }

        private static bool HasAvatarDescriptor(GameObject go)
        {
            // 文字列指定のGetComponentは配列アロケーションなしで型名一致を判定できる
            // （EditorアセンブリはVRC SDKを参照していないため型名での判定が必要）
            return go != null && go.GetComponent("VRCAvatarDescriptor") != null;
        }

        private static ARKitBlendShapeGeneratorComponent SelectPrimaryComponent(
            Transform avatarRoot,
            ARKitBlendShapeGeneratorComponent[] components)
        {
            if (components == null || components.Length == 0)
            {
                return null;
            }

            if (avatarRoot != null)
            {
                var onRoot = components.FirstOrDefault(c => c != null && c.transform == avatarRoot);
                if (onRoot != null)
                {
                    return onRoot;
                }
            }

            return components[0];
        }
    }
}
