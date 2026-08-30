using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
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

        /// <summary>退避先の場所。失敗したときに案内する</summary>
        internal const string BackupPathKey = "ARKitBlendShapeGenerator.SelfUpdate.BackupPath";

        private const int RequestTimeoutSeconds = 60;
        private const string UnityPackageExtension = ".unitypackage";
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

            _isRunning = true;

            // アセットのURLは応答ごとに変わりうるため、確認時のものを覚えず、その場で取り直す
            Send(() => UnityWebRequest.Get(UpdateCheck.LatestReleaseApiUrl), request =>
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
            IReadOnlyList<string> packagedPathnames;

            try
            {
                ExtractUnityPackage(archivePath, unityPackagePath);

                using (var stream = File.OpenRead(unityPackagePath))
                {
                    packagedPathnames = UnityPackageContents.ReadPathnames(stream);
                }
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                Delete(unityPackagePath);
                Fail(S("update.error.archive", exception.Message));
                return;
            }

            var obsolete = SelfUpdatePlan.SelectObsoleteAssets(EnumerateInstalledAssets(), packagedPathnames);

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

            // ここから先はプロジェクトのファイルを書き換える。
            // 途中でアセンブリが読み直されると取り込みまで辿り着けないため、reloadを止めておく
            EditorApplication.LockReloadAssemblies();
            try
            {
                DeleteObsolete(obsolete);

                EditorPrefs.SetString(BackupPathKey, backupPath);
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

        private static void DeleteObsolete(IReadOnlyList<string> obsolete)
        {
            if (obsolete.Count == 0)
            {
                return;
            }

            // .metaはAssetDatabaseが一緒に始末する
            var failed = new List<string>();
            AssetDatabase.DeleteAssets(obsolete.ToArray(), failed);

            // 消せなかったファイルが残ってもコンパイルが通らなくなるだけで、取り込みは進む。
            // 何が残ったかは分かるようにしておく
            foreach (var path in failed)
            {
                Debug.LogWarning(string.Format(
                    CultureInfo.InvariantCulture, "[ARKitGenerator] {0} を削除できませんでした", path));
            }
        }

        /// <summary>手元のフォルダにあるアセットを、プロジェクトからの相対パスで並べる</summary>
        private static IEnumerable<string> EnumerateInstalledAssets()
        {
            var root = PackageLocation.Root;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                yield break;
            }

            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
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
