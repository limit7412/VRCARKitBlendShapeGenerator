using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Presentation
{
    /// <summary>
    /// カスタムマッピングのARKit名を検索して選択するドロップダウン
    /// 同一ARKit名は複数のマッピングに設定できないため、他のマッピングで使用済みの名前は
    /// 一覧から隠さずグレーアウトして選択不可にする
    /// </summary>
    internal sealed class ArkitNameSearchDropdown : SearchableNameDropdown
    {
        private readonly IReadOnlyList<string> _arkitNames;
        private readonly ICollection<string> _usedByOtherMappings;

        public ArkitNameSearchDropdown(
            AdvancedDropdownState state,
            IReadOnlyList<string> arkitNames,
            ICollection<string> usedByOtherMappings,
            Action<string> onSelected) : base(state, onSelected)
        {
            _arkitNames = arkitNames ?? new List<string>();
            _usedByOtherMappings = usedByOtherMappings ?? new HashSet<string>();
        }

        internal static string BuildButtonLabel(string current)
        {
            return string.IsNullOrWhiteSpace(current) ? S("arkit_name.placeholder") : current.Trim();
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(S("arkit_name.dropdown.title"));
            int nextId = 0;

            foreach (var arkitName in _arkitNames)
            {
                if (string.IsNullOrEmpty(arkitName))
                {
                    continue;
                }

                bool isUsed = _usedByOtherMappings.Contains(arkitName);
                root.AddChild(new ValueItem(isUsed ? arkitName + S("arkit_name.used_suffix") : arkitName, arkitName)
                {
                    id = nextId++,
                    enabled = !isUsed
                });
            }

            return root;
        }
    }
}
