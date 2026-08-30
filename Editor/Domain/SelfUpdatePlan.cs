using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// booth版の自己更新について、Unityへ触れずに決められることをまとめる。
    ///
    /// どのアセットを取りに行くか、更新してよい置かれ方か、取り込みの前に何を消すか。
    /// いずれも失敗すると利用者のプロジェクトを壊すため、規則をここへ集めて検証できるようにする
    /// </summary>
    internal static class SelfUpdatePlan
    {
        /// <summary>
        /// unitypackageが自身の中に持つ取り込み先。
        ///
        /// 取り込みはこのパスへ向かうため、手元のフォルダがここに無い場合、
        /// 更新は既存のフォルダを置き換えず、canonicalな位置へもう一組を作ってしまう
        /// </summary>
        public const string InstallRoot = "Assets/AtelierKairox/VRCARKitBlendShapeGenerator";

        private const string BoothAssetPrefix = "VRCARKitBlendShapeGenerator_";
        private const string BoothAssetSuffix = ".zip";
        private const string TagVersionPrefix = "v";

        /// <summary>
        /// このパッケージのunitypackageなら必ず持つ取り込み先。
        ///
        /// アセンブリの定義とマニフェストが欠けたものは、少なくともこのパッケージではない
        /// </summary>
        private static readonly string[] RequiredPathnames =
        {
            InstallRoot,
            InstallRoot + "/package.json",
            InstallRoot + "/Editor/ARKitBlendShapeGenerator.Editor.asmdef",
            InstallRoot + "/Runtime/ARKitBlendShapeGenerator.Runtime.asmdef",
        };

        /// <summary>
        /// タグから、配布物の名前に使われるバージョンを取り出す。
        ///
        /// release.ymlは先頭の`v`を落としてから資材を作るため、`v0.2.0`のタグからは
        /// `VRCARKitBlendShapeGenerator_0.2.0.zip`ができる。同じ形にしないと取りに行く名前が外れる
        /// </summary>
        public static string AssetVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return tag;
            }

            var trimmed = tag.Trim();
            return trimmed.StartsWith(TagVersionPrefix, StringComparison.Ordinal)
                ? trimmed.Substring(TagVersionPrefix.Length)
                : trimmed;
        }

        /// <summary>そのタグのリリースに添付されるbooth用zipの名前</summary>
        public static string BoothAssetName(string tag)
        {
            return string.Format(
                CultureInfo.InvariantCulture, "{0}{1}{2}", BoothAssetPrefix, AssetVersion(tag), BoothAssetSuffix);
        }

        /// <summary>
        /// 自己更新を行える置かれ方かどうか。
        ///
        /// booth版であっても、フォルダが動かされていれば行わない。
        /// 取り込み先はunitypackageの側で決まっており、手元の位置へは追従しないため、
        /// 実行すると同じアセンブリがプロジェクトに二組できてコンパイルが通らなくなる
        /// </summary>
        public static bool CanSelfUpdate(InstallLocation location, string packageRoot)
        {
            return location == InstallLocation.Booth
                && string.Equals(Normalize(packageRoot), InstallRoot, StringComparison.Ordinal);
        }

        /// <summary>
        /// 取得すべきアセットを名前で選ぶ。見つからなければ偽を返す。
        ///
        /// 同じリリースにはVPM用zipも並ぶため、拡張子だけでは選べない
        /// </summary>
        public static bool TrySelectBoothAsset(IEnumerable<ReleaseAsset> assets, string tag, out ReleaseAsset selected)
        {
            selected = default;

            if (assets == null || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            var expected = BoothAssetName(tag);
            foreach (var asset in assets)
            {
                if (!string.Equals(asset.Name, expected, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(asset.DownloadUrl))
                {
                    return false;
                }

                selected = asset;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 取り込もうとしている中身が、このパッケージのものかどうか。
        ///
        /// 消すファイルは「新しい版に無いもの」として選ぶため、中身の欠けたunitypackageや
        /// 別のパッケージのものを受け入れると、手元のほとんどが消える。
        /// ダイジェストは配布されたファイルそのものとは一致するので、この防ぎ方にはならない
        /// </summary>
        public static bool IsExpectedPackage(IEnumerable<string> packagedPathnames)
        {
            if (packagedPathnames == null)
            {
                return false;
            }

            var packaged = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pathname in packagedPathnames)
            {
                var normalized = Normalize(pathname);
                if (normalized == null)
                {
                    continue;
                }

                // 取り込み先がフォルダの外を指すものが混ざっていれば、このパッケージではない
                if (normalized != InstallRoot
                    && !normalized.StartsWith(InstallRoot + "/", StringComparison.Ordinal))
                {
                    return false;
                }

                packaged.Add(normalized);
            }

            foreach (var required in RequiredPathnames)
            {
                if (!packaged.Contains(required))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 取り込みの前に消すアセットを選ぶ。
        ///
        /// unitypackageの取り込みは追加と上書きしかしないため、前の版にあって新しい版で
        /// 無くなったファイルが残る。クラスを消したりリネームした版へ更新すると、
        /// 残ったファイルがコンパイルエラーを起こす。
        ///
        /// 手元にしか無いファイルは、利用者が自分で置いたものである可能性もあるが、
        /// パッケージのフォルダの中に限る。
        /// </summary>
        /// <param name="installedAssetPaths">手元のフォルダにあるアセットのパス（.metaを除く）</param>
        /// <param name="packagedPathnames">新しいunitypackageが持つ取り込み先のパス</param>
        public static IReadOnlyList<string> SelectObsoleteAssets(
            IEnumerable<string> installedAssetPaths,
            IEnumerable<string> packagedPathnames)
        {
            var obsolete = new List<string>();

            if (installedAssetPaths == null)
            {
                return obsolete;
            }

            var packaged = new HashSet<string>(StringComparer.Ordinal);
            if (packagedPathnames != null)
            {
                foreach (var pathname in packagedPathnames)
                {
                    var normalized = Normalize(pathname);
                    if (normalized != null)
                    {
                        packaged.Add(normalized);
                    }
                }
            }

            // 新しい版が1件も読めなかった場合に手元を消し尽くさないよう、空の一覧は使わない
            if (packaged.Count == 0)
            {
                return obsolete;
            }

            foreach (var path in installedAssetPaths)
            {
                var normalized = Normalize(path);
                if (normalized == null || packaged.Contains(normalized))
                {
                    continue;
                }

                obsolete.Add(normalized);
            }

            obsolete.Sort(StringComparer.Ordinal);
            return obsolete;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return path.Trim().Replace('\\', '/').TrimEnd('/');
        }
    }
}
