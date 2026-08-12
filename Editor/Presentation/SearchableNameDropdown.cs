using System;
using System.Reflection;
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

        // 内部のcloseOnSelectionを無効化し閉じる処理を独自に行う
        private const BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly FieldInfo WindowInstanceField =
            typeof(AdvancedDropdown).GetField("m_WindowInstance", MemberFlags);

        // 項目数に応じて変化するサイズの上限を設定
        private static readonly PropertyInfo MaximumSizeProperty =
            typeof(AdvancedDropdown).GetProperty("maximumSize", MemberFlags);

        // EditorWindow.maxSizeの既定値を設定
        private static readonly Vector2 MaximumWindowSize = new Vector2(4000f, 400f);

        // 現在値の項目を開いた時点でハイライトする
        private static readonly MethodInfo SetSelectedIndexMethod =
            typeof(AdvancedDropdownState).GetMethod(
                "SetSelectedIndex",
                MemberFlags,
                null,
                new[] { typeof(AdvancedDropdownItem), typeof(int) },
                null);

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

            TrySetMaximumSize();
        }

        private void TrySetMaximumSize()
        {
            if (MaximumSizeProperty == null ||
                !MaximumSizeProperty.CanWrite ||
                MaximumSizeProperty.PropertyType != typeof(Vector2))
            {
                return;
            }

            MaximumSizeProperty.SetValue(this, MaximumWindowSize);
        }

        public void Open(Rect buttonRect)
        {
            TrySelectCurrentValue();

            Show(buttonRect);
            TryTakeOverSelectionHandling();
        }

        private void TrySelectCurrentValue()
        {
            if (SetSelectedIndexMethod == null || _state == null || string.IsNullOrEmpty(CurrentValue))
            {
                return;
            }

            var root = BuildRoot();
            int index = IndexOfValue(root, CurrentValue);
            if (index < 0)
            {
                return;
            }
            SetSelectedIndexMethod.Invoke(_state, new object[] { root, index });
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
                if (child is ValueItem valueItem && valueItem.Value == value)
                {
                    return index;
                }

                index++;
            }

            return -1;
        }

        /// <summary>
        /// TryTakeOverSelectionHandling失敗時のフォールバック
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

        private void TryTakeOverSelectionHandling()
        {
            if (!(WindowInstanceField?.GetValue(this) is EditorWindow window))
            {
                return;
            }

            var windowType = window.GetType();
            var closeOnSelection = windowType.GetProperty("closeOnSelection", MemberFlags);
            var selectionChanged = windowType.GetEvent("selectionChanged", MemberFlags);

            // プロパティ不一致時のフォールバック
            if (closeOnSelection == null ||
                !closeOnSelection.CanWrite ||
                closeOnSelection.PropertyType != typeof(bool) ||
                selectionChanged == null ||
                selectionChanged.EventHandlerType != typeof(Action<AdvancedDropdownItem>))
            {
                return;
            }

            _window = window;
            closeOnSelection.SetValue(window, false);
            selectionChanged.AddEventHandler(
                window,
                new Action<AdvancedDropdownItem>(OnWindowSelectionChanged));
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
