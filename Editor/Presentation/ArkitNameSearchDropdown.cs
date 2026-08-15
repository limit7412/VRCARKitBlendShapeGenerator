using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Presentation
{
    /// <summary>
    /// カスタムマッピングのARKit名を検索して選択するドロップダウン
    /// 有効なマッピングどうしで同一ARKit名は設定できないため、選ぶと重複になる名前は
    /// 一覧から隠さずグレーアウトして選択不可にする
    /// </summary>
    internal sealed class ArkitNameSearchDropdown : SearchableNameDropdown
    {
        private readonly IReadOnlyList<string> _arkitNames;
        private readonly ICollection<string> _blockedNames;

        public ArkitNameSearchDropdown(
            AdvancedDropdownState state,
            IReadOnlyList<string> arkitNames,
            ICollection<string> blockedNames,
            string currentValue,
            Action<string> onSelected) : base(state, currentValue, onSelected)
        {
            _arkitNames = arkitNames ?? new List<string>();
            _blockedNames = blockedNames ?? new HashSet<string>();
        }

        internal static string BuildButtonLabel(string current)
        {
            // 設定値は加工せずそのまま見せる（Trimして表示すると、実際に生成される名前と食い違う）
            return string.IsNullOrWhiteSpace(current) ? S("arkit_name.placeholder") : current;
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

                bool isUsed = _blockedNames.Contains(arkitName);
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
