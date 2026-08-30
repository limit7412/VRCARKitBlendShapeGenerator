using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using ARKitBlendShapeGenerator.Domain;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Infra
{
    /// <summary>
    /// booth版を新しい版のunitypackageで置き換える。
    ///
    /// 手順を分けているのは、プロジェクトのファイルへ触れるのを最後に寄せるため。
    /// 取得と検証と展開を先に済ませ、どれかが失敗した場合は手元に手を付けずに終わる。
    ///
    /// 取り込みは自分自身を差し替える。要求した時点でエディタのアセンブリが読み直されるため、
    /// 以降の処理をここへ書いても実行されない。完了の知らせはSessionStateへ残し、
    /// 読み込み後にUpdateCheckStartupが拾う
    /// </summary>
    internal static class SelfUpdater
    {
        /// <summary>取り込みを要求した版。読み込み後に完了を知らせるために置く</summary>
        internal const string PendingCompletionKey = "ARKitBlendShapeGenerator.SelfUpdate.PendingTag";

        /// <summary>
        /// 退避先の場所。失敗したときに案内する。
        ///
        /// EditorPrefsではなくSessionStateへ置く。前者はプロジェクトを跨いで共有されるため、
        /// 複数のプロジェクトで同じ頃に更新すると、互いの退避先を上書きしてしまう
        /// </summary>
        internal const string BackupPathKey = "ARKitBlendShapeGenerator.SelfUpdate.BackupPath";

        private const int RequestTimeoutSeconds = 60;
        private const string UnityPackageExtension = ".unitypackage";
        private const string ManifestFileName = "package.json";
        private const string DigestPrefix = "sha256:";

        private static bool _isRunning;

        /// <summary>更新の実行中。ボタンを二重に押されても始めない</summary>
        public static bool IsRunning
        {
            get { return _isRunning; }
        }

        /// <summary>この置かれ方で自己更新を行えるか</summary>
        public static bool IsSupported
        {
            get { return SelfUpdatePlan.CanSelfUpdate(PackageLocation.Location, PackageLocation.Root); }
        }

        /// <summary>
        /// 指定した版へ更新する。
        ///
        /// 呼ぶ前に利用者の同意を取ること。取り込みはUndoできない
        /// </summary>
        public static void Run(string tag)
        {
            if (_isRunning || string.IsNullOrEmpty(tag))
            {
                return;
            }

            if (!IsSupported)
            {
                Fail(S("update.error.unsupported"));
                return;
            }

            // 再生中にスクリプトを差し替えるわけにはいかない
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Fail(S("update.error.playing"));
                return;
            }

            _isRunning = true;

            // 版を指定して引く。`latest`を引き直すと、知らせてから実行するまでに次の版が
            // 出た場合に、確かめたものと違う版が入る。
            // アセットのURLは応答ごとに変わりうるため、確認時のものは覚えずここで取り直す
            Send(() => UnityWebRequest.Get(UpdateCheck.ReleaseByTagApiUrl(tag)), request =>
            {
                if (!UpdateCheck.TryParseRelease(request.downloadHandler?.text, out var latest, out var assets)
                    || latest != tag)
                {
                    Fail(S("update.error.release_missing", tag));
                    return;
                }

                if (!SelfUpdatePlan.TrySelectBoothAsset(assets, tag, out var asset))
                {
                    Fail(S("update.error.asset_missing", SelfUpdatePlan.BoothAssetName(tag)));
                    return;
                }

                Download(asset, tag);
            });
        }

        private static void Download(ReleaseAsset asset, string tag)
        {
            var archivePath = FileUtil.GetUniqueTempPathInProject() + ".zip";

            Send(
                () =>
                {
                    var request = UnityWebRequest.Get(asset.DownloadUrl);
                    request.downloadHandler = new DownloadHandlerFile(archivePath);
                    return request;
                },
                _ =>
                {
                    try
                    {
                        Install(archivePath, asset, tag);
                    }
                    finally
                    {
                        Delete(archivePath);
                    }
                });
        }

        private static void Install(string archivePath, ReleaseAsset asset, string tag)
        {
            if (!TryVerifyDigest(archivePath, asset.Digest))
            {
                Fail(S("update.error.digest"));
                return;
            }

            var unityPackagePath = FileUtil.GetUniqueTempPathInProject() + UnityPackageExtension;
            IReadOnlyList<UnityPackageEntry> entries;

            try
            {
                ExtractUnityPackage(archivePath, unityPackagePath);

                using (var stream = File.OpenRead(unityPackagePath))
                {
                    entries = UnityPackageContents.Read(stream);
                }
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                Delete(unityPackagePath);
                Fail(S("update.error.archive", exception.Message));
                return;
            }

            var packagedPathnames = new List<string>(entries.Count);
            foreach (var entry in entries)
            {
                packagedPathnames.Add(entry.Pathname);
            }

            // 中身の欠けたものや別のパッケージを受け入れると、消すファイルの選択が破滅的に外れる
            if (!SelfUpdatePlan.IsExpectedPackage(packagedPathnames))
            {
                Delete(unityPackagePath);
                Fail(S("update.error.contents"));
                return;
            }

            // 名前だけ合った別の版が添付されていることもある。
            // ダイジェストはそのファイル自体と一致するため、中身のマニフェストで確かめる
            if (!IsExpectedVersion(entries, tag))
            {
                Delete(unityPackagePath);
                Fail(S("update.error.version", tag));
                return;
            }

            IReadOnlyList<string> obsolete;
            try
            {
                // 走査はここで動く。権限の無いフォルダなどに当たると例外が出る
                obsolete = SelfUpdatePlan.SelectObsoleteAssets(EnumerateInstalledAssets(), packagedPathnames);
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                Delete(unityPackagePath);
                Fail(S("update.error.scan", exception.Message));
                return;
            }

            string backupPath;
            try
            {
                backupPath = Backup();
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                Delete(unityPackagePath);
                Fail(S("update.error.backup", exception.Message));
                return;
            }

            // 開始から取得までの間に再生へ入られることがある。
            // ファイルへ触る直前にもう一度確かめる
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Delete(unityPackagePath);
                Fail(S("update.error.playing"));
                return;
            }

            // 控えの場所は、この先で中断した場合にも辿れるよう先に残す
            SessionState.SetString(BackupPathKey, backupPath);

            // ここから先はプロジェクトのファイルを書き換える。
            // 途中でアセンブリが読み直されると取り込みまで辿り着けないため、reloadを止めておく

            EditorApplication.LockReloadAssemblies();
            try
            {
                var failed = DeleteObsolete(obsolete);
                if (failed.Count > 0)
                {
                    // 消せなかった古いファイルが新しい版と同居するとコンパイルが通らない。
                    // 取り込みへ進むと、版だけが新しくなって壊れた状態が残る
                    Delete(unityPackagePath);
                    Fail(S("update.error.delete", string.Join(", ", failed), backupPath));
                    return;
                }

                SessionState.SetString(PendingCompletionKey, tag);

                // 取り込みを要求した時点で、この先のコードは差し替えの対象になる
                AssetDatabase.ImportPackage(unityPackagePath, false);
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
                _isRunning = false;
            }
        }

        /// <summary>同梱されたpackage.jsonが、取りに行った版のものかどうか</summary>
        private static bool IsExpectedVersion(IReadOnlyList<UnityPackageEntry> entries, string tag)
        {
            var manifestPath = SelfUpdatePlan.InstallRoot + "/" + ManifestFileName;

            foreach (var entry in entries)
            {
                if (!string.Equals(entry.Pathname, manifestPath, StringComparison.Ordinal) || entry.Asset == null)
                {
                    continue;
                }

                var manifest = Encoding.UTF8.GetString(entry.Asset);
                return PackageLocation.TryParseVersion(manifest, out var version)
                    && PackageVersion.IsSameVersion(version, tag);
            }

            return false;
        }

        /// <summary>古い版にしか無いアセットを消し、消せなかったものを返す</summary>
        private static IReadOnlyList<string> DeleteObsolete(IReadOnlyList<string> obsolete)
        {
            var failed = new List<string>();
            if (obsolete.Count == 0)
            {
                return failed;
            }

            // .metaはAssetDatabaseが一緒に始末する
            AssetDatabase.DeleteAssets(obsolete.ToArray(), failed);

            foreach (var path in failed)
            {
                Debug.LogWarning(string.Format(
                    CultureInfo.InvariantCulture, "[ARKitGenerator] {0} を削除できませんでした", path));
            }

            return failed;
        }

        /// <summary>
        /// 手元のフォルダにあるアセットを、プロジェクトからの相対パスで並べる。
        ///
        /// フォルダも含める。新しい版で無くなったフォルダを残すと、`.meta`ごと残った
        /// 空のフォルダが、同じGUIDを持つ移動後のフォルダとぶつかる
        /// </summary>
        private static IEnumerable<string> EnumerateInstalledAssets()
        {
            var root = PackageLocation.Root;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                yield break;
            }

            foreach (var path in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            {
                yield return path.Replace('\\', '/');
            }

            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                // .metaはアセットではなく、消すときもAssetDatabaseが一緒に始末する
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return path.Replace('\\', '/');
            }
        }

        private static string Backup()
        {
            var backupPath = FileUtil.GetUniqueTempPathInProject();
            FileUtil.CopyFileOrDirectory(PackageLocation.Root, backupPath);
            return Path.GetFullPath(backupPath);
        }

        private static void ExtractUnityPackage(string archivePath, string destination)
        {
            using (var stream = File.OpenRead(archivePath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ZipArchiveEntry found = null;
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.EndsWith(UnityPackageExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (found != null)
                    {
                        throw new InvalidDataException("zipに.unitypackageが複数入っています");
                    }

                    found = entry;
                }

                if (found == null)
                {
                    throw new InvalidDataException("zipに.unitypackageが入っていません");
                }

                using (var source = found.Open())
                using (var target = File.Create(destination))
                {
                    source.CopyTo(target);
                }
            }
        }

        /// <summary>
        /// ダイジェストと突き合わせる。
        ///
        /// 応答がダイジェストを持たない場合は照合できないが、取得はHTTPSで行っており、
        /// 照合できないことを理由に更新を止めるほどではない
        /// </summary>
        private static bool TryVerifyDigest(string archivePath, string digest)
        {
            if (string.IsNullOrEmpty(digest) || !digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var expected = digest.Substring(DigestPrefix.Length).Trim();

            try
            {
                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(archivePath))
                {
                    var actual = BitConverter.ToString(sha256.ComputeHash(stream))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();

                    return string.Equals(actual, expected.ToLowerInvariant(), StringComparison.Ordinal);
                }
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                return false;
            }
        }

        /// <summary>
        /// 要求を組み立てて送り、成功した場合だけ続きへ渡す。
        ///
        /// 組み立てで失敗した場合もここで止める。投げっぱなしにすると、
        /// 実行中の印が立ったままになり、以後ボタンが効かなくなる
        /// </summary>
        private static void Send(Func<UnityWebRequest> create, Action<UnityWebRequest> onSuccess)
        {
            UnityWebRequest request = null;

            try
            {
                request = create();
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Accept", "application/vnd.github+json");

                request.SendWebRequest().completed += _ =>
                {
                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            Fail(S("update.error.download", request.error));
                            return;
                        }

                        onSuccess(request);
                    }
                    finally
                    {
                        request.Dispose();
                    }
                };
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException)
            {
                request?.Dispose();
                Fail(S("update.error.download", exception.Message));
            }
        }

        private static void Fail(string message)
        {
            _isRunning = false;

            Debug.LogWarning("[ARKitGenerator] " + message);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(S("dialog.title"), message, S("common.ok"));
            }
        }

        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                // 一時ファイルを消せなくても更新の成否は変わらない
                Debug.LogWarning(string.Format(
                    CultureInfo.InvariantCulture, "[ARKitGenerator] {0}: {1}", path, exception.Message));
            }
        }

        private static bool IsFileFailure(Exception exception)
        {
            return exception is IOException
                || exception is UnauthorizedAccessException
                || exception is ArgumentException
                || exception is NotSupportedException
                || exception is InvalidDataException;
        }
    }
}
