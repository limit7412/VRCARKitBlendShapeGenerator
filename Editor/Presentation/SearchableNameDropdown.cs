using System;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ARKitBlendShapeGenerator.Presentation
{
    internal abstract class SearchableNameDropdown : AdvancedDropdown
    {
        protected sealed class ValueItem : AdvancedDropdownItem
        {
            public readonly string Value;

            public ValueItem(string label, string value) : base(label)
            {
                Value = value;
            }
        }

        private readonly Action<string> _onSelected;

        protected SearchableNameDropdown(AdvancedDropdownState state, Action<string> onSelected) : base(state)
        {
            _onSelected = onSelected;
            minimumSize = new Vector2(240f, 0f);
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is ValueItem valueItem)
            {
                _onSelected?.Invoke(valueItem.Value);
            }
        }
    }
}
