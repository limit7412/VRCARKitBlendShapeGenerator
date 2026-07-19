using System.Collections.Generic;
using System.Linq;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator
{
    internal static class CustomMappingValidation
    {
        public static List<string> GetDuplicateArkitNames(List<CustomBlendShapeMapping> customMappings)
        {
            var seen = new HashSet<string>();
            var duplicates = new HashSet<string>();

            if (customMappings == null)
            {
                return new List<string>();
            }

            foreach (var mapping in customMappings)
            {
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.arkitName))
                {
                    continue;
                }

                var arkitName = mapping.arkitName.Trim();
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
