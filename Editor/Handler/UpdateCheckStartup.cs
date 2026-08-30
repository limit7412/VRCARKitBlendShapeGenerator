using UnityEditor;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;
using ARKitBlendShapeGenerator.Infra;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Handler
{
    /// <summary>
    /// 読み込み時に更新を確認し、booth版で新しい版が出ていれば一度だけポップアップで知らせる。
    ///
    /// 確認のためにインスペクタを開かせるわけにはいかないので、契機はエディタの読み込みに置く。
    /// 通信するかどうかと1日1回までの間隔はUpdateCheckが持つ判断のままで、ここは契機だけを足す。
    ///
    /// ポップアップは版ごとに一度きりで、二度目以降はインスペクタのボタンに任せる
    /// </summary>
    [InitializeOnLoad]
    internal static class UpdateCheckStartup
    {
        static UpdateCheckStartup()
        {
            // バッチ実行ではダイアログを出せず、待つ相手もいない
            if (Application.isBatchMode)
            {
                return;
            }

            // 静的コンストラクタの時点ではAssetDatabaseもEditorPrefsも触りにくいため、
            // エディタが落ち着いてから始める
            EditorApplication.delayCall += Begin;
        }

        private static void Begin()
        {
            ReportCompletedUpdate();

            // 応答は起動後しばらくして返る。届いた時点で改めて見る
            UpdateCheck.ResultChanged += Announce;
            UpdateCheck.PollIfDue();

            // 前回の確認結果が残っていることもあるため、応答を待たずに一度見る
            Announce();
        }

        private static void Announce()
        {
            // 再生へ入る操作の途中でダイアログを挟むと、手を止めさせることになる。
            // 版ごとに一度きりの知らせなので、落ち着いた場面まで待ってよい
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var tag = UpdateCheck.PendingUpdateTag;
            if (!UpdateAnnouncement.ShouldAnnounce(PackageLocation.Location, tag, UpdateCheck.AnnouncedTag))
            {
                return;
            }

            // 出したことを先に記録する。ここで何を選ばれても、この版で再び出すことはない
            UpdateCheck.AnnouncedTag = tag;

            if (!SelfUpdater.IsSupported)
            {
                // フォルダが動かされている場合、取り込み先が手元と合わないため入れ直しを案内する
                if (EditorUtility.DisplayDialog(
                        S("dialog.title"),
                        S("update.available.booth", tag),
                        S("common.ok"),
                        S("update.dismiss")))
                {
                    return;
                }

                UpdateCheck.Dismiss(tag);
                return;
            }

            switch (EditorUtility.DisplayDialogComplex(
                        S("dialog.title"),
                        S("update.announce.message", tag),
                        S("update.announce.update"),
                        S("update.announce.later"),
                        S("update.dismiss")))
            {
                case 0:
                    SelfUpdater.Run(tag);
                    break;
                case 1:
                    // インスペクタのボタンから改めて更新できる
                    break;
                default:
                    UpdateCheck.Dismiss(tag);
                    break;
            }
        }

        /// <summary>
        /// 取り込みを要求した版が入ったかを確かめ、結果を知らせる。
        ///
        /// 取り込みはアセンブリの読み直しを起こし、要求した側のコードはそこで消える。
        /// 完了を知らせられるのは読み直しの後になる
        /// </summary>
        private static void ReportCompletedUpdate()
        {
            var requested = SessionState.GetString(SelfUpdater.PendingCompletionKey, string.Empty);
            if (string.IsNullOrEmpty(requested))
            {
                return;
            }

            SessionState.EraseString(SelfUpdater.PendingCompletionKey);

            if (PackageLocation.Version == requested)
            {
                EditorPrefs.DeleteKey(SelfUpdater.BackupPathKey);
                Debug.Log("[ARKitGenerator] " + S("update.completed", requested));
                return;
            }

            // 取り込みが途中で終わった場合に備えて、退避先を知らせる
            var backup = EditorPrefs.GetString(SelfUpdater.BackupPathKey, string.Empty);
            Debug.LogWarning("[ARKitGenerator] " + S("update.incomplete", requested, backup));
        }
    }
}
