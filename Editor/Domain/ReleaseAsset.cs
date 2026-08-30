namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// リリースに添付されたファイル1件。
    ///
    /// 自己更新はここからbooth用zipを選んで取得する
    /// </summary>
    internal readonly struct ReleaseAsset
    {
        public string Name { get; }
        public string DownloadUrl { get; }

        /// <summary>
        /// GitHubがアセットごとに返すダイジェスト。`sha256:`で始まる。
        ///
        /// 古いリリースや応答の形によっては付かないため、空になりうる
        /// </summary>
        public string Digest { get; }

        public ReleaseAsset(string name, string downloadUrl, string digest)
        {
            Name = name;
            DownloadUrl = downloadUrl;
            Digest = digest;
        }
    }
}
