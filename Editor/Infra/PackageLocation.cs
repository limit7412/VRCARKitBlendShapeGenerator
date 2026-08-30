using System;
using System.IO;
using UnityEditor.Compilation;
using UnityEngine;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Infra
{
    /// <summary>
    /// 手元に入っているパッケージの形態と版を、asmdefの位置を起点に調べる。
    ///
    /// 解決の規則はPackageInstallationが持ち、ここはUnityへの問い合わせとファイルの読み取りを担う。
    /// 結果はドメインリロードまで変わらないため、一度求めたら使い回す
    /// </summary>
    internal static class PackageLocation
    {
        private const string EditorAssemblyName = "ARKitBlendShapeGenerator.Editor";
        private const string ManifestFileName = "package.json";

        private static bool _resolved;
        private static InstallLocation _location;
        private static string _version;

        /// <summary>インストール形態。判別できなければUnknown</summary>
        public static InstallLocation Location
        {
            get
            {
                Resolve();
                return _location;
            }
        }

        /// <summary>package.jsonが持つ手元の版。読めなければnull</summary>
        public static string Version
        {
            get
            {
                Resolve();
                return _version;
            }
        }

        private static void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;

            var asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(EditorAssemblyName);
            if (!PackageInstallation.TryResolve(asmdefPath, out _location, out var packageRoot))
            {
                return;
            }

            _version = ReadVersion(packageRoot);
        }

        private static string ReadVersion(string packageRoot)
        {
            // リリース時にrelease.ymlがタグでversionを書き換えてからzipへ入れるため、
            // 配布物のpackage.jsonは常にそのリリースの版を持つ
            try
            {
                var manifestPath = Path.Combine(packageRoot, ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    return null;
                }

                return TryParseVersion(File.ReadAllText(manifestPath), out var version) ? version : null;
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException
                || exception is ArgumentException
                || exception is NotSupportedException)
            {
                // 版が読めなくても更新の通知が出なくなるだけで、生成そのものには影響しない
                return null;
            }
        }

        /// <summary>package.jsonのversionを取り出す。読めない形なら偽を返す</summary>
        internal static bool TryParseVersion(string manifestJson, out string version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                return false;
            }

            try
            {
                var manifest = JsonUtility.FromJson<PackageManifest>(manifestJson);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.version))
                {
                    return false;
                }

                version = manifest.version.Trim();
                return true;
            }
            catch (ArgumentException)
            {
                // JsonUtilityはJSONとして読めない文字列でArgumentExceptionを投げる
                return false;
            }
        }

        [Serializable]
        private class PackageManifest
        {
            // JsonUtilityはフィールド名でJSONのキーと対応づけるため、package.jsonの綴りに合わせる
            public string version;
        }
    }
}
