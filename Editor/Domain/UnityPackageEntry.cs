namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// unitypackageに入っているアセット1件。
    ///
    /// フォルダのエントリは中身を持たない
    /// </summary>
    internal readonly struct UnityPackageEntry
    {
        /// <summary>プロジェクトルートから見た取り込み先</summary>
        public string Pathname { get; }

        /// <summary>アセットの中身。フォルダならnull</summary>
        public byte[] Asset { get; }

        public UnityPackageEntry(string pathname, byte[] asset)
        {
            Pathname = pathname;
            Asset = asset;
        }
    }
}
