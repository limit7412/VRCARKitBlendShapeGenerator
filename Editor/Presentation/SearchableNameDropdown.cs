using System;
using UnityEditor;
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

        // EditorWindow.maxSizeの既定値を設定
        private static readonly Vector2 MaximumWindowSize = new Vector2(4000f, 400f);

        private readonly AdvancedDropdownState _state;
        private readonly Action<string> _onSelected;
        private EditorWindow _window;

        /// <summary>
        /// ハイライトする項目
        /// </summary>
        protected string CurrentValue { get; }

        protected SearchableNameDropdown(
            AdvancedDropdownState state,
            string currentValue,
            Action<string> onSelected) : base(state)
        {
            _state = state;
            _onSelected = onSelected;
            CurrentValue = currentValue;
            minimumSize = new Vector2(240f, 0f);

            AdvancedDropdownReflection.TrySetMaximumSize(this, MaximumWindowSize);
        }

        public void Open(Rect buttonRect)
        {
            TrySelectCurrentValue();

            Show(buttonRect);
            if (AdvancedDropdownReflection.TryOverrideSelectionHandling(
                    this,
                    OnWindowSelectionChanged,
                    out var window))
            {
                _window = window;
            }
        }

        private void TrySelectCurrentValue()
        {
            if (_state == null ||
                string.IsNullOrEmpty(CurrentValue) ||
                !AdvancedDropdownReflection.CanSetSelectedIndex)
            {
                return;
            }

            var root = BuildRoot();
            int index = IndexOfValue(root, CurrentValue);
            if (index < 0)
            {
                return;
            }
            AdvancedDropdownReflection.TrySetSelectedIndex(_state, root, index);
        }

        private static int IndexOfValue(AdvancedDropdownItem root, string value)
        {
            if (root == null)
            {
                return -1;
            }

            int index = 0;
            foreach (var child in root.children)
            {
                if (child is ValueItem valueItem && child.enabled && valueItem.Value == value)
                {
                    return index;
                }

                index++;
            }

            return -1;
        }

        /// <summary>
        /// 内部選択処理の引き継ぎに失敗した場合のフォールバック
        /// </summary>
        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (IsSelectable(item, out string value))
            {
                _onSelected?.Invoke(value);
            }
        }

        private static bool IsSelectable(AdvancedDropdownItem item, out string value)
        {
            if (item is ValueItem valueItem && item.enabled)
            {
                value = valueItem.Value;
                return true;
            }

            value = null;
            return false;
        }

        private void OnWindowSelectionChanged(AdvancedDropdownItem item)
        {
            if (!IsSelectable(item, out string value))
            {
                return;
            }

            _onSelected?.Invoke(value);
            _window?.Close();
            _window = null;

            GUIUtility.ExitGUI();
        }
    }
}
