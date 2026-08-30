using UnityEditor;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;
using ARKitBlendShapeGenerator.Infra;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Presentation
{
    /// <summary>
    /// 更新の確認についての問いかけと、新しい版が出ているときの案内をインスペクタの先頭へ描く。
    ///
    /// 案内の文面はインストール形態で変わる。
    /// VPM版の版数はVCC/ALCOMがvpm-manifest.jsonで管理しているため、
    /// こちらでファイルを置き換えると管理側の記録と実態がずれる。
    /// booth版には版を管理する主体がおらず、入れ直しが更新の手段になる
    /// </summary>
    internal static class UpdateNotice
    {
        public static void Draw()
        {
            switch (UpdateCheck.Preference)
            {
                case UpdateCheckPreference.Unset:
                    DrawConsent();
                    break;
                case UpdateCheckPreference.Enabled:
                    UpdateCheck.PollIfDue();
                    DrawAvailableUpdate();
                    break;
            }
        }

        private static void DrawConsent()
        {
            EditorGUILayout.HelpBox(S("update.consent.description"), MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(S("update.consent.enable")))
            {
                UpdateCheck.Preference = UpdateCheckPreference.Enabled;
            }

            if (GUILayout.Button(S("update.consent.disable")))
            {
                UpdateCheck.Preference = UpdateCheckPreference.Disabled;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }

        private static void DrawAvailableUpdate()
        {
            var tag = UpdateCheck.PendingUpdateTag;
            if (tag == null)
            {
                return;
            }

            EditorGUILayout.HelpBox(DescribeUpdate(tag), MessageType.Info);

            EditorGUILayout.BeginHorizontal();

            // booth版には版を管理する主体がいないため、ここから入れ替えまで行う。
            // VPM版はVCC/ALCOMの記録とずれるので案内にとどめる
            if (SelfUpdater.IsSupported)
            {
                using (new EditorGUI.DisabledScope(SelfUpdater.IsRunning))
                {
                    if (GUILayout.Button(S("update.run")))
                    {
                        RunWithConfirmation(tag);
                    }
                }
            }
            else if (GUILayout.Button(S("update.open_releases")))
            {
                Application.OpenURL(UpdateCheck.ReleasesPageUrl);
            }

            if (GUILayout.Button(S("update.dismiss")))
            {
                UpdateCheck.Dismiss(tag);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }

        /// <summary>
        /// 取り込みはUndoできず、手元の改変も上書きする。
        /// 押し間違いで走らないよう、実行の前に一度確かめる
        /// </summary>
        private static void RunWithConfirmation(string tag)
        {
            if (!EditorUtility.DisplayDialog(
                    S("dialog.title"),
                    S("update.confirm", tag, SelfUpdatePlan.InstallRoot),
                    S("update.run"),
                    S("common.cancel")))
            {
                return;
            }

            SelfUpdater.Run(tag);
        }

        private static string DescribeUpdate(string tag)
        {
            switch (PackageLocation.Location)
            {
                case InstallLocation.Vpm:
                    return S("update.available.vpm", tag);
                case InstallLocation.Booth:
                    return SelfUpdater.IsSupported
                        ? S("update.available.booth_updatable", tag)
                        : S("update.available.booth", tag);
                default:
                    return S("update.available.unknown", tag);
            }
        }
    }

    /// <summary>
    /// 更新確認の入切をPreferencesからも変えられるようにする。
    ///
    /// インスペクタの問いかけで「確認しない」を選ぶと、以後その問いかけは出ない。
    /// 選び直す場所がここになる
    /// </summary>
    internal static class UpdateCheckSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Preferences/ARKit BlendShape Generator", SettingsScope.User)
            {
                guiHandler = _ =>
                {
                    var enabled = UpdateCheck.Preference == UpdateCheckPreference.Enabled;
                    var changed = EditorGUILayout.Toggle(S("update.settings.enabled"), enabled);
                    if (changed != enabled)
                    {
                        UpdateCheck.Preference = changed
                            ? UpdateCheckPreference.Enabled
                            : UpdateCheckPreference.Disabled;
                    }

                    EditorGUILayout.HelpBox(S("update.settings.description"), MessageType.None);
                },
            };
        }
    }
}
