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

        private readonly Action<string> _onSelected;
        private EditorWindow _window;

        protected SearchableNameDropdown(AdvancedDropdownState state, Action<string> onSelected) : base(state)
        {
            _onSelected = onSelected;
            minimumSize = new Vector2(240f, 0f);
        }

        public void Open(Rect buttonRect)
        {
            Show(buttonRect);
            TryTakeOverSelectionHandling();
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
