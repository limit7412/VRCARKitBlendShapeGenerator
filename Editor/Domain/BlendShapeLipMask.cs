namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// 唇の境界線を基準にした上下の適用範囲
    ///
    /// 左右分割（BlendShapeSide）がX座標の固定平面で分けるのに対し、上下は唇の高さが
    /// メッシュごとに違うため、口領域の検出結果（MouthRegionContext）から境界線を求めて分ける
    /// </summary>
    internal enum BlendShapeLipMask
    {
        All,
        Upper,
        Lower,
    }
}
