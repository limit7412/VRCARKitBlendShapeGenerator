using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// ネイティブフェイストラッキングが動かすBlendShapeを、AAO: Avatar Optimizer へ伝えるための目印。
    ///
    /// AAOはBlendShapeが使われているかをアニメーターと各コンポーネントの申告から判定し、
    /// Avatar DescriptorのBlendshapes Setは見ない（AAO 1.9.20時点）。
    /// そのためネイティブFTからしか使われないBlendShapeは、未使用として固定や統合の対象になる。
    /// このコンポーネントはビルド中にだけアバタールートへ置かれ、Editor側の
    /// ComponentInformation（AAOの公開API）を通じて「このBlendShapeは実行時に動く」と申告する。
    /// AAOがBlendShapeを別の名前や別のメッシュへ移したときは、その結果もここへ記録される。
    ///
    /// AAOの処理が終わったあとに本ツールが取り除く。万一残ってもIEditorOnlyなのでSDKが取り除く
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class NativeFaceTrackingBlendShapeUsage : MonoBehaviour, IEditorOnly
    {
        /// <summary>
        /// ネイティブFTが動かすBlendShape 1つ分
        /// </summary>
        [Serializable]
        public sealed class Entry
        {
            /// <summary>AAOの処理前に、このBlendShapeを持っていたメッシュ</summary>
            public SkinnedMeshRenderer renderer;

            /// <summary>Blendshapes Setに書かれている名前</summary>
            public string blendShapeName;

            /// <summary>
            /// AAOの処理後も、このBlendShapeが残っているか。
            /// falseのときはAAOが取り除いたことを表し、Setの割り当ては解決できない
            /// </summary>
            public bool resolved;

            /// <summary>AAOの処理後に、このBlendShapeを持つメッシュ（統合先）</summary>
            public SkinnedMeshRenderer resolvedRenderer;

            /// <summary>AAOの処理後の名前（統合や衝突回避で変わりうる）</summary>
            public string resolvedBlendShapeName;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>
        /// AAOが移動先の情報（resolved系）を書き込んだか。
        /// falseのままなら、AAOの処理後のメッシュを名前で探して解決する
        /// </summary>
        public bool mappingApplied;
    }
}
