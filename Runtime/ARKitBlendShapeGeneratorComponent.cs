using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// VRChat/MMDのBlendShapeからARKit用BlendShapeを自動生成するコンポーネント
    /// Jerry's Templatesと組み合わせて使用することを想定
    ///
    /// 使用方法:
    /// 1. このコンポーネントをアバターまたは顔メッシュに追加
    /// 2. Jerry's Templates (MA版) をアバターに追加
    /// 3. アップロード時に自動的にBlendShapeが生成される
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("KxVRCARKitBlendShapeGenerator/Kx VRC ARKit BlendShape Generator")]
    public class ARKitBlendShapeGeneratorComponent : MonoBehaviour, IEditorOnly
    {
        [Header("対象設定")]
        [Tooltip("対象のSkinnedMeshRenderer（空の場合はBodyを自動検出）")]
        public SkinnedMeshRenderer targetRenderer;

        [Header("生成設定")]
        [Tooltip("生成時の強度係数（0.5-1.5推奨）")]
        [Range(0.1f, 2.0f)]
        public float intensityMultiplier = 1.0f;

        [Tooltip("左右分割を有効にする（まばたき等を左右別々に生成）")]
        public bool enableLeftRightSplit = true;

        [Tooltip("左右分割時のグラデーション幅（中央付近で左右をブレンドする範囲）")]
        [Range(0.001f, 0.1f)]
        public float blendWidth = 0.02f;

        [Tooltip("既存のARKit BlendShapeを上書きする")]
        public bool overwriteExisting = false;

        [Header("口の手続き的生成")]
        [Tooltip("既存シェイプキーから生成できない口周りのBlendShape（mouthLeft/Right、jaw系等）を、口領域の頂点移動で自動生成する")]
        public bool enableProceduralMouthShapes = false;

        [Tooltip("手続き的生成の変形量係数")]
        [Range(0.1f, 2.0f)]
        public float proceduralMouthIntensity = 1.0f;

        [Header("カスタムマッピング")]
        [Tooltip("自動マッピングできないBlendShapeを手動で指定")]
        public List<CustomBlendShapeMapping> customMappings = new List<CustomBlendShapeMapping>();

        [Header("デバッグ")]
        [Tooltip("デバッグログを出力する")]
        public bool debugMode = false;

#if UNITY_EDITOR
        [NonSerialized]
        private bool _pendingDuplicateRemoval;
#endif

        private void Reset()
        {
            // 自動でBodyメッシュを検索
            targetRenderer = FindBodyMesh();

            // デフォルトのカスタムマッピングを追加（視線系）
            InitializeDefaultCustomMappings();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            EnforceSingleComponentPerAvatar();
#endif
        }

        private SkinnedMeshRenderer FindBodyMesh()
        {
            // よくある名前パターンで検索
            string[] bodyNames = { "Body", "body", "Face", "face", "Head", "head" };

            foreach (var name in bodyNames)
            {
                var found = transform.Find(name);
                if (found != null)
                {
                    var smr = found.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null) return smr;
                }
            }

            // 見つからない場合は最初のSkinnedMeshRendererを返す
            return GetComponentInChildren<SkinnedMeshRenderer>();
        }

        private void InitializeDefaultCustomMappings()
        {
            customMappings = new List<CustomBlendShapeMapping>
            {
                // 視線系（MMDには通常存在しない）
                new CustomBlendShapeMapping { arkitName = "eyeLookUpLeft", enabled = false },
                new CustomBlendShapeMapping { arkitName = "eyeLookUpRight", enabled = false },
                new CustomBlendShapeMapping { arkitName = "eyeLookDownLeft", enabled = false },
                new CustomBlendShapeMapping { arkitName = "eyeLookDownRight", enabled = false },
                new CustomBlendShapeMapping { arkitName = "eyeLookInLeft", enabled = false },
                new CustomBlendShapeMapping { arkitName = "eyeLookInRight", enabled = false },
                new CustomBlendShapeMapping { arkitName = "eyeLookOutLeft", enabled = false },
                new CustomBlendShapeMapping { arkitName = "eyeLookOutRight", enabled = false },
            };
        }

        /// <summary>
        /// 利用可能なBlendShape名のリストを取得
        /// </summary>
        public List<string> GetAvailableBlendShapes()
        {
            var result = new List<string>();
            var renderer = targetRenderer ?? GetComponentInChildren<SkinnedMeshRenderer>();

            if (renderer != null && renderer.sharedMesh != null)
            {
                var mesh = renderer.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    result.Add(mesh.GetBlendShapeName(i));
                }
            }

            return result;
        }

#if UNITY_EDITOR
        private void EnforceSingleComponentPerAvatar()
        {
            var avatarRoot = FindAvatarRootForUniqueness();
            if (avatarRoot == null)
            {
                return;
            }

            var components = avatarRoot.GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(true)
                .Where(c => c != null)
                .ToArray();

            if (components.Length <= 1)
            {
                _pendingDuplicateRemoval = false;
                return;
            }

            var primary = SelectPrimaryComponent(avatarRoot, components);
            if (primary == this)
            {
                _pendingDuplicateRemoval = false;
                return;
            }

            if (_pendingDuplicateRemoval)
            {
                return;
            }

            _pendingDuplicateRemoval = true;

            Debug.LogWarning(
                "[ARKitGenerator] 同一アバター内にはARKitBlendShapeGeneratorComponentを1つだけ設定できます。重複コンポーネントを削除します。",
                this);

            EditorApplication.delayCall += () =>
            {
                _pendingDuplicateRemoval = false;

                if (this == null || avatarRoot == null)
                {
                    return;
                }

                var refreshed = avatarRoot.GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(true)
                    .Where(c => c != null)
                    .ToArray();
                var refreshedPrimary = SelectPrimaryComponent(avatarRoot, refreshed);

                if (refreshedPrimary != this)
                {
                    DestroyImmediate(this);

                    if (!Application.isBatchMode)
                    {
                        EditorUtility.DisplayDialog(
                            "ARKit BlendShape Generator",
                            "同一アバター内には ARKitBlendShapeGeneratorComponent を1つだけ設定できます。\n\n" +
                            "重複して追加されたコンポーネントを削除しました。",
                            "OK");
                    }
                }
            };
        }

        private Transform FindAvatarRootForUniqueness()
        {
            Transform lastDescriptorRoot = null;
            var cursor = transform;

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

            return transform.root;
        }

        private static bool HasAvatarDescriptor(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            var components = go.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                if (component.GetType().Name == "VRCAvatarDescriptor")
                {
                    return true;
                }
            }

            return false;
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
#endif
    }

    /// <summary>
    /// カスタムBlendShapeマッピング定義
    /// </summary>
    [Serializable]
    public class CustomBlendShapeMapping
    {
        [Tooltip("生成するARKit BlendShape名")]
        public string arkitName;

        [Tooltip("このマッピングを有効にする")]
        public bool enabled = true;

        [Tooltip("ソースBlendShapeのリスト")]
        public List<BlendShapeSource> sources = new List<BlendShapeSource>();
    }

    /// <summary>
    /// ソースBlendShape定義
    /// </summary>
    [Serializable]
    public class BlendShapeSource
    {
        [Tooltip("ソースBlendShape名")]
        public string blendShapeName;

        [Tooltip("適用する重み（-2.0〜2.0）")]
        [Range(-2f, 2f)]
        public float weight = 1.0f;

        [Tooltip("適用範囲（左右分かれていないBlendShapeを片側だけ使用する場合）")]
        public BlendShapeSide side = BlendShapeSide.Both;
    }

    /// <summary>
    /// BlendShape適用範囲
    /// </summary>
    public enum BlendShapeSide
    {
        [Tooltip("両側に適用")]
        Both,
        [Tooltip("左側（X > 0）のみ適用")]
        LeftOnly,
        [Tooltip("右側（X < 0）のみ適用")]
        RightOnly
    }

    /// <summary>
    /// ARKit BlendShape名の定義
    /// </summary>
    public static class ARKitBlendShapeNames
    {
        // 目
        public static readonly string[] Eye = {
            "eyeBlinkLeft", "eyeBlinkRight",
            "eyeSquintLeft", "eyeSquintRight",
            "eyeWideLeft", "eyeWideRight"
        };

        // 視線
        public static readonly string[] EyeLook = {
            "eyeLookUpLeft", "eyeLookUpRight",
            "eyeLookDownLeft", "eyeLookDownRight",
            "eyeLookInLeft", "eyeLookInRight",
            "eyeLookOutLeft", "eyeLookOutRight"
        };

        // 眉毛
        public static readonly string[] Brow = {
            "browDownLeft", "browDownRight",
            "browInnerUp",
            "browOuterUpLeft", "browOuterUpRight"
        };

        // 口
        public static readonly string[] Mouth = {
            "jawOpen", "jawForward", "jawLeft", "jawRight",
            "mouthFunnel", "mouthPucker",
            "mouthSmileLeft", "mouthSmileRight",
            "mouthFrownLeft", "mouthFrownRight",
            "mouthLeft", "mouthRight",
            "mouthUpperUpLeft", "mouthUpperUpRight",
            "mouthLowerDownLeft", "mouthLowerDownRight",
            "mouthClose",
            "mouthShrugUpper", "mouthShrugLower",
            "mouthPress",
            "mouthStretchLeft", "mouthStretchRight",
            "mouthDimpleLeft", "mouthDimpleRight",
            "mouthRollUpper", "mouthRollLower"
        };

        // 頬
        public static readonly string[] Cheek = {
            "cheekPuff",
            "cheekSquintLeft", "cheekSquintRight"
        };

        // 鼻
        public static readonly string[] Nose = {
            "noseSneerLeft", "noseSneerRight"
        };

        // 舌
        public static readonly string[] Tongue = {
            "tongueOut"
        };

        /// <summary>
        /// 全てのARKit BlendShape名を取得
        /// </summary>
        public static string[] GetAll()
        {
            var all = new List<string>();
            all.AddRange(Eye);
            all.AddRange(EyeLook);
            all.AddRange(Brow);
            all.AddRange(Mouth);
            all.AddRange(Cheek);
            all.AddRange(Nose);
            all.AddRange(Tongue);
            return all.ToArray();
        }
    }
}
