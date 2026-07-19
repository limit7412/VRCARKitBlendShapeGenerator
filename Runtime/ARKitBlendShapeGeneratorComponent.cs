using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

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
        // インスペクタ表示用の文言はEditorアセンブリ側でローカライズされる
        // （以下の属性はカスタムエディタが無効な場合のフォールバック表示）
        [Header("Target")]
        [Tooltip("Target SkinnedMeshRenderer (auto-detects the Body mesh when empty)")]
        public SkinnedMeshRenderer targetRenderer;

        [Header("Generation")]
        [Tooltip("Intensity multiplier for generation (0.5-1.5 recommended)")]
        [Range(0.1f, 2.0f)]
        public float intensityMultiplier = 1.0f;

        [Tooltip("Generate left/right variants separately (e.g. blink)")]
        public bool enableLeftRightSplit = true;

        [Tooltip("Width of the gradient around the center where left and right are blended when splitting")]
        [Range(0.001f, 0.1f)]
        public float blendWidth = 0.02f;

        [Tooltip("Overwrite existing ARKit blend shapes")]
        public bool overwriteExisting = false;

        [Header("Procedural Mouth Generation")]
        [Tooltip("Procedurally generate mouth-related blend shapes (mouthLeft/Right, jaw*, etc.) that cannot be derived from existing blend shapes, by moving vertices in the mouth region")]
        public bool enableProceduralMouthShapes = false;

        [Tooltip("Deformation intensity for procedural generation")]
        [Range(0.1f, 2.0f)]
        public float proceduralMouthIntensity = 1.0f;

        [Header("Custom Mappings")]
        [Tooltip("Manually specify blend shapes that cannot be mapped automatically")]
        public List<CustomBlendShapeMapping> customMappings = new List<CustomBlendShapeMapping>();

        [Header("Debug")]
        [Tooltip("Output debug logs")]
        public bool debugMode = false;

#if UNITY_EDITOR
        /// <summary>
        /// Editorアセンブリ側から差し込まれるOnValidateフック
        /// （同一アバター内の重複コンポーネント排除に使用）
        /// </summary>
        internal static Action<ARKitBlendShapeGeneratorComponent> EditorOnValidateHook;
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
            EditorOnValidateHook?.Invoke(this);
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
    }

    /// <summary>
    /// カスタムBlendShapeマッピング定義
    /// </summary>
    [Serializable]
    public class CustomBlendShapeMapping
    {
        [Tooltip("ARKit blend shape name to generate")]
        public string arkitName;

        [Tooltip("Enable this mapping")]
        public bool enabled = true;

        [Tooltip("List of source blend shapes")]
        public List<BlendShapeSource> sources = new List<BlendShapeSource>();
    }

    /// <summary>
    /// ソースBlendShape定義
    /// </summary>
    [Serializable]
    public class BlendShapeSource
    {
        [Tooltip("Source blend shape name")]
        public string blendShapeName;

        [Tooltip("Weight to apply (-2.0 to 2.0)")]
        [Range(-2f, 2f)]
        public float weight = 1.0f;

        [Tooltip("Side to apply (for using one side of a non-split blend shape)")]
        public BlendShapeSide side = BlendShapeSide.Both;
    }

    /// <summary>
    /// BlendShape適用範囲
    /// </summary>
    public enum BlendShapeSide
    {
        [Tooltip("Apply to both sides")]
        Both,
        [Tooltip("Apply to the left side (X > 0) only")]
        LeftOnly,
        [Tooltip("Apply to the right side (X < 0) only")]
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
