using System.Collections.Generic;
using System.Linq;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Domain
{
    internal static class CustomMappingValidation
    {
        /// <summary>
        /// 有効なマッピングのARKit名から重複を抽出する。
        ///
        /// 無効なマッピングは生成側（CollectCustomMappings）が読み飛ばすため、重複として数えない。
        /// これにより、同じARKit名の別定義を無効にしたまま取り置きできる。
        /// </summary>
        public static List<string> GetDuplicateArkitNames(List<CustomBlendShapeMapping> customMappings)
        {
            return GetDuplicateArkitNames(
                customMappings?
                    .Where(mapping => mapping != null && mapping.enabled)
                    .Select(mapping => mapping.arkitName));
        }

        /// <summary>
        /// ARKit名の列挙から重複を抽出する。
        /// SerializedProperty等、コンポーネントの実体を経由せずに検証する場合に使用する。
        /// 無効なマッピングは呼び出し側で除いてから渡す。
        ///
        /// 名前は一切加工せず完全一致で比較する。生成側も同じ規則で名前を扱うため、
        /// ここだけTrimすると「重複扱いなのに別々に生成される」といった食い違いが生じる。
        /// 空白のみは未設定として無視する。
        /// </summary>
        public static List<string> GetDuplicateArkitNames(IEnumerable<string> arkitNames)
        {
            var seen = new HashSet<string>();
            var duplicates = new HashSet<string>();

            if (arkitNames == null)
            {
                return new List<string>();
            }

            foreach (var arkitName in arkitNames)
            {
                if (string.IsNullOrWhiteSpace(arkitName))
                {
                    continue;
                }

                if (!seen.Add(arkitName))
                {
                    duplicates.Add(arkitName);
                }
            }

            return duplicates.OrderBy(name => name).ToList();
        }

        public static bool HasDuplicateArkitNames(
            List<CustomBlendShapeMapping> customMappings,
            out List<string> duplicates)
        {
            duplicates = GetDuplicateArkitNames(customMappings);
            return duplicates.Count > 0;
        }

        public static string BuildDuplicateMessage(IEnumerable<string> duplicateArkitNames)
        {
            if (duplicateArkitNames == null)
            {
                return S("validation.duplicate.generic");
            }

            var names = duplicateArkitNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .OrderBy(name => name)
                .ToList();

            if (names.Count == 0)
            {
                return S("validation.duplicate.generic");
            }

            return S("validation.duplicate.message", string.Join(", ", names));
        }
    }
}
