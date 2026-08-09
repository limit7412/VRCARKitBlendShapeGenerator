using System.Collections.Generic;
using System.Linq;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator
{
    internal static class CustomMappingValidation
    {
        public static List<string> GetDuplicateArkitNames(List<CustomBlendShapeMapping> customMappings)
        {
            return GetDuplicateArkitNames(customMappings?.Select(mapping => mapping?.arkitName));
        }

        /// <summary>
        /// ARKit名の列挙から重複を抽出する。
        /// SerializedProperty等、コンポーネントの実体を経由せずに検証する場合に使用する。
        /// </summary>
        public static List<string> GetDuplicateArkitNames(IEnumerable<string> arkitNames)
        {
            var seen = new HashSet<string>();
            var duplicates = new HashSet<string>();

            if (arkitNames == null)
            {
                return new List<string>();
            }

            foreach (var name in arkitNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var arkitName = name.Trim();
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
                .Select(name => name.Trim())
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
