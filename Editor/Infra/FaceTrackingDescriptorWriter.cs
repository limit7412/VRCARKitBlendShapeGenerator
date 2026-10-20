using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;
#if KX_VRC_NATIVE_FACE_TRACKING
using VRC.SDKBase;
using VRC.SDK3.Avatars.Components;
#endif

namespace ARKitBlendShapeGenerator.Infra
{
    /// <summary>
    /// Descriptorへの書き込みの結果
    /// </summary>
    internal enum FaceTrackingWriteStatus
    {
        /// <summary>書き込んだ</summary>
        Written,
        /// <summary>導入されているVRChat SDKにネイティブフェイストラッキングのAPIが無い</summary>
        Unsupported,
        /// <summary>アバタールートにVRCAvatarDescriptorが無い</summary>
        DescriptorNotFound,
        /// <summary>SDKのトラッキングキーの列挙体が、本ツールの対応表と食い違う</summary>
        KeyMismatch,
    }

    internal sealed class FaceTrackingWriteResult
    {
        public readonly FaceTrackingWriteStatus Status;

        /// <summary>
        /// 対象メッシュをFace Tracking Meshesへ加えられなかった（上限に達している）
        /// </summary>
        public readonly bool MeshLimitReached;

        /// <summary>
        /// 列挙体との食い違いの内容（KeyMismatchのとき）
        /// </summary>
        public readonly string MismatchDetail;

        public FaceTrackingWriteResult(
            FaceTrackingWriteStatus status,
            bool meshLimitReached = false,
            string mismatchDetail = null)
        {
            Status = status;
            MeshLimitReached = meshLimitReached;
            MismatchDetail = mismatchDetail;
        }
    }

    /// <summary>
    /// ネイティブフェイストラッキングのBlendshapes SetをVRCAvatarDescriptorへ書き込む。
    ///
    /// Face Trackingの項目はVRChat SDK 3.10.6-beta.2で加わったもので、それより前のSDKには無い。
    /// asmdefのversionDefinesで、対応するSDKが入っているときだけ KX_VRC_NATIVE_FACE_TRACKING が定義され、
    /// 無いときは何も書き込まずにUnsupportedを返す。SDKの型に触れるのはこのクラスだけにする
    /// </summary>
    internal static class FaceTrackingDescriptorWriter
    {
        /// <summary>
        /// SDKの faceTrackingMeshes が有効と見なす件数の上限（SDK 3.10.6-beta.2）
        /// </summary>
        private const int FaceTrackingMeshLimit = 16;

        public static bool IsSupported
        {
            get
            {
#if KX_VRC_NATIVE_FACE_TRACKING
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 対応表の内容をSetとして書き込み、対象メッシュをFace Tracking Meshesへ加え、フェイストラッキングを有効にする。
        /// 本ツールが前のビルドで書いた同名のSetがあれば置き換える（NDMFのビルドは複製へ行うため通常は残らないが、
        /// 利用者がシーンのDescriptorへ手で写した場合に二重にならないようにする）
        /// </summary>
        public static FaceTrackingWriteResult Write(
            GameObject avatarRoot,
            SkinnedMeshRenderer targetRenderer,
            FaceTrackingBlendshapesSetPlan plan)
        {
#if KX_VRC_NATIVE_FACE_TRACKING
            var descriptor = avatarRoot != null ? avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            if (descriptor == null)
            {
                return new FaceTrackingWriteResult(FaceTrackingWriteStatus.DescriptorNotFound);
            }

            var mismatch = FindKeyMismatch();
            if (mismatch != null)
            {
                return new FaceTrackingWriteResult(FaceTrackingWriteStatus.KeyMismatch, mismatchDetail: mismatch);
            }

            var set = new VRCAvatarDescriptor.FaceTrackingBlendshapesSet
            {
                setName = FaceTrackingBlendshapesSetTable.SetName,
                setWeight = 1f,
                faceTrackingBlendshapeNames = (string[])plan.BlendShapeNames.Clone(),
                faceTrackingBlendshapeMultipliers = Filled((int)VRCAvatarDescriptor.FaceTrackingKey.Max, 1f),
                visemeBlendshapeNames = Filled((int)VRC_AvatarDescriptor.Viseme.Count, string.Empty),
                visemeBlendshapeMultipliers = Filled((int)VRC_AvatarDescriptor.Viseme.Count, 1f),
            };

            if (descriptor.faceTrackingBlendshapesSetList == null)
            {
                descriptor.faceTrackingBlendshapesSetList = new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>();
            }

            descriptor.faceTrackingBlendshapesSetList.RemoveAll(
                existing => existing != null && existing.setName == FaceTrackingBlendshapesSetTable.SetName);
            descriptor.faceTrackingBlendshapesSetList.Add(set);

            if (descriptor.faceTrackingMeshes == null)
            {
                descriptor.faceTrackingMeshes = new List<SkinnedMeshRenderer>();
            }

            bool meshLimitReached = false;
            if (targetRenderer != null && !descriptor.faceTrackingMeshes.Contains(targetRenderer))
            {
                if (descriptor.faceTrackingMeshes.Count(mesh => mesh != null) >= FaceTrackingMeshLimit)
                {
                    meshLimitReached = true;
                }
                else
                {
                    descriptor.faceTrackingMeshes.Add(targetRenderer);
                }
            }

            descriptor.enableFaceTracking = true;

            return new FaceTrackingWriteResult(FaceTrackingWriteStatus.Written, meshLimitReached);
#else
            return new FaceTrackingWriteResult(FaceTrackingWriteStatus.Unsupported);
#endif
        }

#if KX_VRC_NATIVE_FACE_TRACKING
        /// <summary>
        /// 対応表のキーの並びがSDKの列挙体と一致するか確かめる。
        /// Setはキーの番号を添字にした配列なので、順序が1つずれると全部の割り当てが別のキーへ行く。
        /// 一致しないときはその内容を返し、一致するときはnull
        /// </summary>
        private static string FindKeyMismatch()
        {
            int sdkCount = (int)VRCAvatarDescriptor.FaceTrackingKey.Max;
            if (sdkCount != FaceTrackingBlendshapesSetTable.KeyCount)
            {
                return $"FaceTrackingKey.Max = {sdkCount}, table = {FaceTrackingBlendshapesSetTable.KeyCount}";
            }

            int index = 0;
            foreach (var keyName in FaceTrackingBlendshapesSetTable.GetKeyNames())
            {
                var sdkName = Enum.GetName(typeof(VRCAvatarDescriptor.FaceTrackingKey), index);
                if (sdkName != keyName)
                {
                    return $"[{index}] SDK = {sdkName}, table = {keyName}";
                }

                index++;
            }

            return null;
        }

        private static T[] Filled<T>(int length, T value)
        {
            var array = new T[length];
            for (int i = 0; i < length; i++)
            {
                array[i] = value;
            }

            return array;
        }
#endif
    }
}
