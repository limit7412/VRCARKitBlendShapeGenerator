using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Presentation
{
    /// <summary>
    /// ソースBlendShapeを検索して選択するドロップダウン
    /// </summary>
    internal sealed class BlendShapeSearchDropdown : SearchableNameDropdown
    {
        internal enum SourceValueState
        {
            Empty,
            Found,
            Missing
        }

        private readonly IReadOnlyList<string> _blendShapeNames;
        private readonly string _missingValue;

        public BlendShapeSearchDropdown(
            AdvancedDropdownState state,
            IReadOnlyList<string> blendShapeNames,
            string missingValue,
            Action<string> onSelected) : base(state, onSelected)
        {
            _blendShapeNames = blendShapeNames ?? new List<string>();
            _missingValue = missingValue;
        }

        internal static SourceValueState ResolveState(string current, ICollection<string> availableBlendShapes)
        {
            if (string.IsNullOrEmpty(current))
            {
                return SourceValueState.Empty;
            }

            return availableBlendShapes != null && availableBlendShapes.Contains(current)
                ? SourceValueState.Found
                : SourceValueState.Missing;
        }

        internal static string BuildButtonLabel(string current, ICollection<string> availableBlendShapes)
        {
            switch (ResolveState(current, availableBlendShapes))
            {
                case SourceValueState.Empty:
                    return S("source.placeholder");
                case SourceValueState.Missing:
                    return current + S("source.not_found_suffix");
                default:
                    return current;
            }
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(S("source.dropdown.title"));

            int nextId = 0;

            root.AddChild(new ValueItem(S("source.placeholder"), string.Empty) { id = nextId++ });

            if (!string.IsNullOrEmpty(_missingValue))
            {
                root.AddChild(new ValueItem(_missingValue + S("source.not_found_suffix"), _missingValue)
                {
                    id = nextId++,
                    enabled = false
                });
            }

            foreach (var blendShapeName in _blendShapeNames)
            {
                if (string.IsNullOrEmpty(blendShapeName))
                {
                    continue;
                }

                root.AddChild(new ValueItem(blendShapeName, blendShapeName) { id = nextId++ });
            }

            return root;
        }
    }
}
