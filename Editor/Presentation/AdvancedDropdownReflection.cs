using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ARKitBlendShapeGenerator.Presentation
{
    /// <summary>
    /// UnityEditorのAdvancedDropdown内部APIへのアクセスを集約する互換層
    /// </summary>
    internal static class AdvancedDropdownReflection
    {
        private const BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly FieldInfo WindowInstanceField;
        private static readonly PropertyInfo MaximumSizeProperty;
        private static readonly MethodInfo SetSelectedIndexMethod;
        private static readonly Type WindowType;
        private static readonly PropertyInfo CloseOnSelectionProperty;
        private static readonly EventInfo SelectionChangedEvent;

        static AdvancedDropdownReflection()
        {
            WindowInstanceField = typeof(AdvancedDropdown).GetField("m_WindowInstance", MemberFlags);
            MaximumSizeProperty = typeof(AdvancedDropdown).GetProperty("maximumSize", MemberFlags);
            SetSelectedIndexMethod = typeof(AdvancedDropdownState).GetMethod(
                "SetSelectedIndex",
                MemberFlags,
                null,
                new[] { typeof(AdvancedDropdownItem), typeof(int) },
                null);

            WindowType = WindowInstanceField?.FieldType;
            CloseOnSelectionProperty = WindowType?.GetProperty("closeOnSelection", MemberFlags);
            SelectionChangedEvent = WindowType?.GetEvent("selectionChanged", MemberFlags);
        }

        internal static bool CanSetMaximumSize =>
            MaximumSizeProperty != null &&
            MaximumSizeProperty.CanWrite &&
            MaximumSizeProperty.PropertyType == typeof(Vector2);

        internal static bool CanSetSelectedIndex =>
            SetSelectedIndexMethod != null &&
            SetSelectedIndexMethod.ReturnType == typeof(void);

        internal static bool CanOverrideSelectionHandling =>
            WindowInstanceField != null &&
            WindowType != null &&
            typeof(EditorWindow).IsAssignableFrom(WindowType) &&
            CloseOnSelectionProperty != null &&
            CloseOnSelectionProperty.CanWrite &&
            CloseOnSelectionProperty.PropertyType == typeof(bool) &&
            SelectionChangedEvent != null &&
            SelectionChangedEvent.EventHandlerType == typeof(Action<AdvancedDropdownItem>);

        internal static bool TrySetMaximumSize(AdvancedDropdown dropdown, Vector2 value)
        {
            if (dropdown == null || !CanSetMaximumSize)
            {
                return false;
            }

            try
            {
                MaximumSizeProperty.SetValue(dropdown, value);
                return true;
            }
            catch (Exception exception) when (IsReflectionFailure(exception))
            {
                return false;
            }
        }

        internal static bool TrySetSelectedIndex(
            AdvancedDropdownState state,
            AdvancedDropdownItem root,
            int index)
        {
            if (state == null || root == null || index < 0 || !CanSetSelectedIndex)
            {
                return false;
            }

            try
            {
                SetSelectedIndexMethod.Invoke(state, new object[] { root, index });
                return true;
            }
            catch (Exception exception) when (IsReflectionFailure(exception))
            {
                return false;
            }
        }

        internal static bool TryOverrideSelectionHandling(
            AdvancedDropdown dropdown,
            Action<AdvancedDropdownItem> handler,
            out EditorWindow window)
        {
            window = null;
            if (dropdown == null || handler == null || !CanOverrideSelectionHandling)
            {
                return false;
            }

            bool changedCloseBehavior = false;
            try
            {
                window = WindowInstanceField.GetValue(dropdown) as EditorWindow;
                if (window == null)
                {
                    return false;
                }

                CloseOnSelectionProperty.SetValue(window, false);
                changedCloseBehavior = true;
                SelectionChangedEvent.AddEventHandler(window, handler);
                return true;
            }
            catch (Exception exception) when (IsReflectionFailure(exception))
            {
                if (changedCloseBehavior && window != null)
                {
                    RestoreCloseBehaviorOrClose(window);
                }

                window = null;
                return false;
            }
        }

        private static void RestoreCloseBehaviorOrClose(EditorWindow window)
        {
            try
            {
                CloseOnSelectionProperty.SetValue(window, true);
            }
            catch (Exception exception) when (IsReflectionFailure(exception))
            {
                window.Close();
            }
        }

        private static bool IsReflectionFailure(Exception exception)
        {
            return exception is ArgumentException ||
                   exception is TargetException ||
                   exception is TargetInvocationException ||
                   exception is MemberAccessException ||
                   exception is InvalidOperationException ||
                   exception is NotSupportedException;
        }
    }
}
