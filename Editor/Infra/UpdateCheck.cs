using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Infra
{
    /// <summary>
    /// 更新確認を行うかどうかについての利用者の選択。
    ///
    /// 値はEditorPrefsへそのまま入るため、既に選ばれているものと対応がずれないようにする
    /// </summary>
    internal enum UpdateCheckPreference
    {
        /// <summary>まだ選ばれていない。この状態では確認する</summary>
        Unset = 0,

        /// <summary>確認する</summary>
        Enabled = 1,

        /// <summary>確認しない</summary>
        Disabled = 2,
    }

    /// <summary>
    /// GitHubのreleasesから最新の安定版を調べ、手元より新しければ知らせる。
    ///
    /// 確認は既定で行う。始める前に尋ねる形だと、選ばずに閉じた利用者へは通知が届かず、
    /// 直っている不具合を踏み続けることになる。
    /// 送るのはリリース情報の取得要求だけで、確認は1日1回まで、失敗しても黙って次の機会へ回す。
    /// 更新の通知は無くても生成の妨げにならず、警告を積む価値が無い。
    ///
    /// 通信を望まない利用者はPreferencesから止められる。
    /// 止めた選択はEditorPrefsに残り、こちらから確認する側へ戻すことはない。
    ///
    /// 判断の材料はEditorPrefsに置くが、問い合わせるのはインスペクタの描画のたびになるため、
    /// 結果はメモリへ持ち、変化したときだけ読み直す
    /// </summary>
    internal static class UpdateCheck
    {
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/limit7412/VRCARKitBlendShapeGenerator/releases";

        /// <summary>最新の安定版を返すエンドポイント</summary>
        internal const string LatestReleaseApiUrl = ReleasesApiUrl + "/latest";

        /// <summary>
        /// 版を指定してリリースを引くエンドポイント。
        ///
        /// 自己更新は利用者が確かめた版を取りに行く。`latest`を引き直すと、知らせてから
        /// 実行するまでに次の版が出た場合に、確かめたものと違う版を入れることになる
        /// </summary>
        internal static string ReleaseByTagApiUrl(string tag)
        {
            return ReleasesApiUrl + "/tags/" + Uri.EscapeDataString(tag ?? string.Empty);
        }

        /// <summary>更新の入手先として案内するページ</summary>
        public const string ReleasesPageUrl =
            "https://github.com/limit7412/VRCARKitBlendShapeGenerator/releases/latest";

        private const string PreferenceKey = "ARKitBlendShapeGenerator.UpdateCheck.Preference";
        private const string LastAttemptKey = "ARKitBlendShapeGenerator.UpdateCheck.LastAttemptUtcTicks";
        private const string LatestTagKey = "ARKitBlendShapeGenerator.UpdateCheck.LatestTag";
        private const string DismissedTagKey = "ARKitBlendShapeGenerator.UpdateCheck.DismissedTag";
        private const string AnnouncedTagKey = "ARKitBlendShapeGenerator.UpdateCheck.AnnouncedTag";

        private const double CheckIntervalHours = 24.0;
        private const int RequestTimeoutSeconds = 15;

        // 前回の確認時刻はEditorPrefsにあり、描画のたびに読むと無駄が多い。
        // 1日1回の判定にこの粗さは影響しない
        private const double IntervalCheckThrottleSeconds = 5.0;

        private static bool _isStateCached;
        private static UpdateCheckPreference _cachedPreference;
        private static string _cachedPendingTag;

        private static bool _isRequestInFlight;
        private static double _nextIntervalCheckTime;

        /// <summary>確認の結果が変わったときに発火する。インスペクタの再描画に使う</summary>
        public static event Action ResultChanged;

        /// <summary>
        /// 更新を確認するか。
        ///
        /// 選ばれていない状態は確認する側へ倒すため、判定はこれを通す
        /// </summary>
        public static bool IsEnabled
        {
            get { return Preference != UpdateCheckPreference.Disabled; }
        }

        /// <summary>更新確認についての選択</summary>
        public static UpdateCheckPreference Preference
        {
            get
            {
                EnsureStateCached();
                return _cachedPreference;
            }
            set
            {
                EditorPrefs.SetInt(PreferenceKey, (int)value);

                // 選び直した直後は、間隔を待たずに確認できるようにする
                _nextIntervalCheckTime = 0.0;
                Invalidate();
            }
        }

        /// <summary>
        /// 通知すべき新しい版があればそのタグを返す。無ければnull。
        ///
        /// 利用者が確認を選んでおり、手元より新しい安定版が出ていて、
        /// その版を「通知しない」と選んでいない場合に限る
        /// </summary>
        public static string PendingUpdateTag
        {
            get
            {
                EnsureStateCached();
                return _cachedPendingTag;
            }
        }

        /// <summary>ポップアップで知らせた版。まだ無ければ空</summary>
        public static string AnnouncedTag
        {
            get { return EditorPrefs.GetString(AnnouncedTagKey, string.Empty); }
            set { EditorPrefs.SetString(AnnouncedTagKey, value ?? string.Empty); }
        }

        /// <summary>この版については以後知らせない</summary>
        public static void Dismiss(string tag)
        {
            EditorPrefs.SetString(DismissedTagKey, tag ?? string.Empty);
            Invalidate();
        }

        /// <summary>
        /// 必要なら最新の安定版を問い合わせる。
        ///
        /// インスペクタの描画から毎フレーム呼ばれるため、通信を始めるかどうかはここで絞る
        /// </summary>
        public static void PollIfDue()
        {
            if (!IsEnabled || _isRequestInFlight)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now < _nextIntervalCheckTime)
            {
                return;
            }

            _nextIntervalCheckTime = now + IntervalCheckThrottleSeconds;

            if (!IsIntervalElapsed())
            {
                return;
            }

            // 失敗も含めて次の機会を遅らせる。
            // 通信できない環境で描画のたびに要求を投げ続けるのを防ぐ
            EditorPrefs.SetString(LastAttemptKey, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            SendRequest();
        }

        private static void EnsureStateCached()
        {
            if (_isStateCached)
            {
                return;
            }

            _isStateCached = true;
            _cachedPreference = ReadPreference();
            _cachedPendingTag = ComputePendingTag(_cachedPreference);
        }

        private static void Invalidate()
        {
            _isStateCached = false;
            ResultChanged?.Invoke();
        }

        private static UpdateCheckPreference ReadPreference()
        {
            var stored = EditorPrefs.GetInt(PreferenceKey, (int)UpdateCheckPreference.Unset);
            return Enum.IsDefined(typeof(UpdateCheckPreference), stored)
                ? (UpdateCheckPreference)stored
                : UpdateCheckPreference.Unset;
        }

        private static string ComputePendingTag(UpdateCheckPreference preference)
        {
            if (preference == UpdateCheckPreference.Disabled)
            {
                return null;
            }

            var latest = EditorPrefs.GetString(LatestTagKey, string.Empty);
            if (string.IsNullOrEmpty(latest) || latest == EditorPrefs.GetString(DismissedTagKey, string.Empty))
            {
                return null;
            }

            return PackageVersion.IsUpdateAvailable(PackageLocation.Version, latest) ? latest : null;
        }

        private static bool IsIntervalElapsed()
        {
            var stored = EditorPrefs.GetString(LastAttemptKey, string.Empty);

            // NumberStyles.Noneは符号を認めないためticksは0以上になる。
            // 上限だけは、書き換えられた値でDateTimeが例外を投げないよう確かめる
            if (!long.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks > DateTime.MaxValue.Ticks)
            {
                return true;
            }

            // 保存された時刻が未来を指す場合（時計を戻したなど）も確認へ倒す。
            // そうしないと、その時刻を過ぎるまで確認が止まったままになる
            var elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            return elapsed.TotalHours >= CheckIntervalHours || elapsed < TimeSpan.Zero;
        }

        private static void SendRequest()
        {
            UnityWebRequest request = null;

            // 更新の通知はインスペクタの描画の途中で始まる。
            // 要求を組み立てられなくても、そこで例外を投げて設定UIごと止めるわけにはいかない
            try
            {
                request = UnityWebRequest.Get(LatestReleaseApiUrl);

                // User-Agentは指定しない。GitHubのAPIはこれの無い要求を拒むが、
                // UnityWebRequestが既定で付けるもので通る。
                // 上書きを認めないヘッダとして扱われる場合があり、指定する利点に見合わない
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.timeout = RequestTimeoutSeconds;

                _isRequestInFlight = true;
                request.SendWebRequest().completed += _ =>
                {
                    try
                    {
                        HandleResponse(request);
                    }
                    finally
                    {
                        request.Dispose();
                        _isRequestInFlight = false;
                    }
                };
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException)
            {
                _isRequestInFlight = false;
                request?.Dispose();
            }
        }

        private static void HandleResponse(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                return;
            }

            if (!TryParseTag(request.downloadHandler?.text, out var tag))
            {
                return;
            }

            if (tag == EditorPrefs.GetString(LatestTagKey, string.Empty))
            {
                return;
            }

            EditorPrefs.SetString(LatestTagKey, tag);
            Invalidate();
        }

        /// <summary>
        /// releases/latestの応答からタグを取り出す。
        ///
        /// このエンドポイントはプレリリースを除いて返すが、応答が想定と違ってもよいように、
        /// 安定版として解釈できないタグはここで落とす
        /// </summary>
        internal static bool TryParseTag(string json, out string tag)
        {
            return TryParseRelease(json, out tag, out _);
        }

        /// <summary>
        /// releases/latestの応答から、タグと添付されたアセットの一覧を取り出す。
        ///
        /// 自己更新はアセットの側も要るが、応答は通知と同じものなので解釈も1箇所にまとめる
        /// </summary>
        internal static bool TryParseRelease(string json, out string tag, out ReleaseAsset[] assets)
        {
            tag = null;
            assets = Array.Empty<ReleaseAsset>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            LatestReleaseResponse response;
            try
            {
                response = JsonUtility.FromJson<LatestReleaseResponse>(json);
            }
            catch (ArgumentException)
            {
                // JsonUtilityはJSONとして読めない文字列でArgumentExceptionを投げる
                return false;
            }

            if (response == null || string.IsNullOrWhiteSpace(response.tag_name))
            {
                return false;
            }

            // 版を指定して引く場合、`latest`の対象から外れた版もそのまま返る。
            // 不具合などで取り下げられた版を、古い知らせのまま入れるわけにはいかない
            if (response.prerelease || response.draft)
            {
                return false;
            }

            var candidate = response.tag_name.Trim();
            if (!PackageVersion.TryParse(candidate, out _))
            {
                return false;
            }

            tag = candidate;
            assets = ToReleaseAssets(response.assets);
            return true;
        }

        private static ReleaseAsset[] ToReleaseAssets(ReleaseAssetResponse[] responses)
        {
            if (responses == null)
            {
                return Array.Empty<ReleaseAsset>();
            }

            var assets = new List<ReleaseAsset>(responses.Length);
            foreach (var response in responses)
            {
                if (response == null || string.IsNullOrWhiteSpace(response.name))
                {
                    continue;
                }

                assets.Add(new ReleaseAsset(
                    response.name.Trim(),
                    response.browser_download_url?.Trim(),
                    response.digest?.Trim()));
            }

            return assets.ToArray();
        }

        [Serializable]
        private class LatestReleaseResponse
        {
            // JsonUtilityはフィールド名でJSONのキーと対応づけるため、APIの綴りに合わせる
            public string tag_name;
            public bool prerelease;
            public bool draft;
            public ReleaseAssetResponse[] assets;
        }

        [Serializable]
        private class ReleaseAssetResponse
        {
            public string name;
            public string browser_download_url;

            /// <summary>`sha256:`で始まるダイジェスト。付かない応答もある</summary>
            public string digest;
        }
    }
}
