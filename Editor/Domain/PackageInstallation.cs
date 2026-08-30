using System;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// このパッケージがプロジェクトへどう置かれているか
    /// </summary>
    internal enum InstallLocation
    {
        /// <summary>判別できなかった</summary>
        Unknown,

        /// <summary>`Packages/`配下。VCC/ALCOMがvpm-manifest.jsonで版を管理している</summary>
        Vpm,

        /// <summary>`Assets/`配下。unitypackageから取り込まれ、版の管理者はいない</summary>
        Booth,
    }

    /// <summary>
    /// asmdefの位置からインストール形態とパッケージのルートを求める。
    ///
    /// 更新の案内先が形態ごとに違うため、まずどちらへ入っているかを知る必要がある。
    /// Unityへ問い合わせるのは呼び出し側で、ここでは受け取ったパスの解釈だけを行う
    /// （Localization.GetLocalizationRootと同じく、asmdefの位置を起点にする）
    /// </summary>
    internal static class PackageInstallation
    {
        private const string EditorDirectoryName = "Editor";

        /// <summary>
        /// 「ルート/Editor/なにか.asmdef」の形を前提に、ルートとインストール形態を取り出す。
        ///
        /// 形が合わないパスでは偽を返す。
        /// ルートが分からなければpackage.jsonも読めず、手元の版が確かめられない
        /// </summary>
        public static bool TryResolve(string editorAsmdefPath, out InstallLocation location, out string packageRoot)
        {
            location = InstallLocation.Unknown;
            packageRoot = null;

            if (string.IsNullOrWhiteSpace(editorAsmdefPath))
            {
                return false;
            }

            // Unityのパスは`/`区切りだが、環境によっては`\`が混ざったものが返る
            var segments = editorAsmdefPath.Trim().Replace('\\', '/').Split('/');

            // ルート・Editor・asmdefの3要素が最低限必要
            if (segments.Length < 3)
            {
                return false;
            }

            if (!string.Equals(segments[segments.Length - 2], EditorDirectoryName, StringComparison.Ordinal))
            {
                return false;
            }

            var rootSegments = new string[segments.Length - 2];
            Array.Copy(segments, rootSegments, rootSegments.Length);

            packageRoot = string.Join("/", rootSegments);
            location = ResolveLocation(rootSegments[0]);
            return true;
        }

        private static InstallLocation ResolveLocation(string topSegment)
        {
            switch (topSegment)
            {
                case "Packages":
                    return InstallLocation.Vpm;
                case "Assets":
                    return InstallLocation.Booth;
                default:
                    return InstallLocation.Unknown;
            }
        }
    }
}
