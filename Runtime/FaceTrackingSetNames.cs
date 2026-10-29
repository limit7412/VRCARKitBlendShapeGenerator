namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// 本ツールがVRCAvatarDescriptorへ書くネイティブフェイストラッキングのBlendshapes Setの名前
    ///
    /// 他のパッケージ（Kx VRC NativeFT Manager など）が、ビルド後のDescriptorから本ツールのSetを見分けるための公開定数。
    /// 再ビルドで同名のSetを置き換える目印にも使うので、値は変えない
    /// </summary>
    public static class FaceTrackingSetNames
    {
        /// <summary>本ツールが書くSetの名前</summary>
        public const string Generator = "Kx VRC ARKit BlendShape Generator";
    }
}
