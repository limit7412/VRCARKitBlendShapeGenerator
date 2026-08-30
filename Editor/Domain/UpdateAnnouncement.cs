namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// 新しい版が出ていることをポップアップで知らせるかどうかの判断。
    ///
    /// 知らせるのは版ごとに一度だけで、二度目以降はインスペクタのボタンに任せる。
    /// 作業の途中に割り込む形になるため、同じ版で繰り返し出すわけにはいかない。
    /// 更にその先の版が出たときは、その版について改めて一度だけ出す
    /// </summary>
    internal static class UpdateAnnouncement
    {
        /// <param name="location">手元のインストール形態</param>
        /// <param name="pendingTag">知らせるべき新しい版。無ければnull</param>
        /// <param name="announcedTag">既にポップアップで知らせた版。まだ無ければnullか空</param>
        public static bool ShouldAnnounce(InstallLocation location, string pendingTag, string announcedTag)
        {
            // VPM版の更新はVCC/ALCOMが担うため、こちらから割り込まない
            if (location != InstallLocation.Booth)
            {
                return false;
            }

            if (string.IsNullOrEmpty(pendingTag))
            {
                return false;
            }

            return pendingTag != announcedTag;
        }
    }
}
