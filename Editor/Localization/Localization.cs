using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using nadena.dev.ndmf.localization;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// NDMFのローカライズ機構を利用した文言管理
    /// 翻訳ファイルは Editor/Localization/*.po に配置する
    /// </summary>
    internal static class Localization
    {
        private const string DefaultLanguage = "ja-jp";
        private const string FallbackLocalizationRoot =
            "Packages/com.qazx7412.kx-vrc-arkit-blendshape-generator/Editor/Localization";

        private static readonly string[] SupportedLanguages = { "ja-jp", "en-us" };

        internal static readonly Localizer Localizer = new Localizer(DefaultLanguage, LoadLocalizationAssets);

        private static List<LocalizationAsset> LoadLocalizationAssets()
        {
            var assets = new List<LocalizationAsset>();
            var root = GetLocalizationRoot();

            foreach (var language in SupportedLanguages)
            {
                var asset = AssetDatabase.LoadAssetAtPath<LocalizationAsset>($"{root}/{language}.po");
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }

        private static string GetLocalizationRoot()
        {
            // Assets/配下・Packages/配下のどちらに置かれても解決できるよう、asmdefの位置から辿る
            var asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                "ARKitBlendShapeGenerator.Editor");
            if (string.IsNullOrEmpty(asmdefPath))
            {
                return FallbackLocalizationRoot;
            }

            var directory = Path.GetDirectoryName(asmdefPath);
            if (string.IsNullOrEmpty(directory))
            {
                return FallbackLocalizationRoot;
            }

            return directory.Replace('\\', '/') + "/Localization";
        }

        internal static string S(string key)
        {
            return Localizer.GetLocalizedString(key);
        }

        internal static string S(string key, params object[] args)
        {
            return string.Format(S(key), args);
        }

        internal static GUIContent G(string key)
        {
            return Localizer.TryGetLocalizedString(key + ".tooltip", out var tooltip)
                ? new GUIContent(S(key), tooltip)
                : new GUIContent(S(key));
        }
    }
}
