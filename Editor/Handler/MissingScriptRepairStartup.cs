using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ARKitBlendShapeGenerator.Infra;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Handler
{
    /// <summary>
    /// スクリプト参照の切れたコンポーネントを見つけ、利用者の同意を得て修復する。
    ///
    /// 参照が切れるのは、パッケージの入れ直しでスクリプトのGUIDが変わったときである
    /// （経緯はMissingScriptReferencesに書いている）。
    /// そこで、読み込み時にスクリプトのGUIDを前回調べたときのものと比べ、変わっていたときだけ
    /// プロジェクトを調べる。毎回の読み込みで全てのプレハブを読むわけにはいかないためである。
    ///
    /// 後から取り込んだプレハブに切れた参照が含まれる場合など、GUIDが変わらないまま
    /// 対象が増えることもあるので、メニューからも実行できるようにしている
    /// </summary>
    [InitializeOnLoad]
    internal static class MissingScriptRepairStartup
    {
        private const string MenuPath = "Tools/Kx VRC ARKit BlendShape Generator/Repair Missing Components";

        // 調べ終えたときのスクリプトのGUIDを、プロジェクトごとの利用者設定へ残す。
        // EditorPrefsはプロジェクトをまたいで共有されるため、ここには使えない
        private const string ScannedGuidKey = "com.qazx7412.kx-vrc-arkit-blendshape-generator.repair.scanned-guid";

        // ダイアログへ並べるファイルの上限。超えた分は件数だけ伝える
        private const int MaxListedPaths = 10;

        static MissingScriptRepairStartup()
        {
            // バッチ実行ではダイアログを出せず、答える相手もいない
            if (Application.isBatchMode)
            {
                return;
            }

            // 静的コンストラクタの時点ではAssetDatabaseへ問い合わせられないため、
            // エディタが落ち着いてから始める
            EditorApplication.delayCall += RunIfScriptGuidChanged;
        }

        private static void RunIfScriptGuidChanged()
        {
            // 再生中にファイルを書き換えるわけにはいかない。次の読み込みで改めて調べる
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var currentGuid = MissingScriptRepairer.FindCurrentGuid();
            if (currentGuid == null || currentGuid == EditorUserSettings.GetConfigValue(ScannedGuidKey))
            {
                return;
            }

            var scan = MissingScriptRepairer.Scan(S("repair.progress.scan"));
            if (scan.Cancelled)
            {
                // 最後まで調べていないので、次の読み込みでやり直す
                return;
            }

            // 修復するかどうかの答えを待たずに記録する。
            // 断られた場合に読み込みのたびに尋ね直すことはせず、以後はメニューに任せる
            EditorUserSettings.SetConfigValue(ScannedGuidKey, currentGuid);

            if (scan.ComponentCount > 0)
            {
                ConfirmAndRepair(scan);
            }
        }

        [MenuItem(MenuPath)]
        private static void RunFromMenu()
        {
            var scan = MissingScriptRepairer.Scan(S("repair.progress.scan"));
            if (scan.Cancelled)
            {
                return;
            }

            if (scan.CurrentGuid == null)
            {
                EditorUtility.DisplayDialog(S("dialog.title"), S("repair.dialog.script_not_found"), S("common.ok"));
                return;
            }

            if (scan.ComponentCount == 0)
            {
                EditorUtility.DisplayDialog(S("dialog.title"), S("repair.dialog.none"), S("common.ok"));
                return;
            }

            ConfirmAndRepair(scan);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateRunFromMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void ConfirmAndRepair(MissingScriptRepairer.ScanResult scan)
        {
            var message = S(
                "repair.dialog.confirm",
                scan.ComponentCount,
                scan.Paths.Count,
                FormatPaths(scan.Paths));

            if (!EditorUtility.DisplayDialog(
                    S("dialog.title"), message, S("repair.dialog.confirm.repair"), S("repair.dialog.confirm.later")))
            {
                return;
            }

            // 開いているシーンに未保存の変更があると、ファイルを書き換えた後の読み直しで失われる。
            // 保存するかどうかを先に決めてもらい、取りやめられたら何も書き換えない
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var repaired = MissingScriptRepairer.Repair(scan, S("repair.progress.repair"));

            // 元に戻すときの手がかりとして、書き換えたファイルと元のGUIDを残す
            Debug.Log("[ARKitGenerator] " + S(
                "repair.log.repaired",
                repaired.Count,
                string.Join(", ", scan.StaleGuids),
                scan.CurrentGuid,
                string.Join("\n", repaired)));

            EditorUtility.DisplayDialog(
                S("dialog.title"), S("repair.dialog.done", repaired.Count), S("common.ok"));
        }

        private static string FormatPaths(List<string> paths)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < paths.Count && i < MaxListedPaths; i++)
            {
                builder.Append(paths[i]).Append('\n');
            }

            if (paths.Count > MaxListedPaths)
            {
                builder.Append(S("repair.dialog.confirm.more", paths.Count - MaxListedPaths)).Append('\n');
            }

            return builder.ToString().TrimEnd('\n');
        }
    }
}
