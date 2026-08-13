using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using ARKitBlendShapeGenerator.Presentation;

namespace ARKitBlendShapeGenerator.Tests
{
    public class SearchableNameDropdownTests
    {
        [Test]
        public void ReflectionContract_ResolvesSupportedMembers()
        {
            Assert.That(
                AdvancedDropdownReflection.CanSetMaximumSize,
                Is.True,
                "maximumSize was not found in Unity {0}",
                Application.unityVersion);
            Assert.That(
                AdvancedDropdownReflection.CanSetSelectedIndex,
                Is.True,
                "SetSelectedIndex was not found in Unity {0}",
                Application.unityVersion);
            Assert.That(
                AdvancedDropdownReflection.CanOverrideSelectionHandling,
                Is.True,
                "AdvancedDropdownWindow selection members were not found in Unity {0}",
                Application.unityVersion);
        }

        [Test]
        public void ReflectionCalls_SucceedForSupportedMembers()
        {
            var state = new AdvancedDropdownState();
            var dropdown = new TestDropdown(state);
            var root = dropdown.CreateRoot();

            Assert.That(
                AdvancedDropdownReflection.TrySetMaximumSize(dropdown, new Vector2(4000f, 400f)),
                Is.True,
                "maximumSize could not be set in Unity {0}",
                Application.unityVersion);
            Assert.That(
                AdvancedDropdownReflection.TrySetSelectedIndex(state, root, 0),
                Is.True,
                "SetSelectedIndex could not be invoked in Unity {0}",
                Application.unityVersion);
        }

        [Test]
        public void ReflectionCalls_OverrideSelectionHandlingForWindowInstance()
        {
            const BindingFlags memberFlags =
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var windowField = typeof(AdvancedDropdown).GetField("m_WindowInstance", memberFlags);
            Assert.That(windowField, Is.Not.Null);
            var windowType = windowField.FieldType;
            var closeOnSelection = windowType.GetProperty("closeOnSelection", memberFlags);
            Assert.That(closeOnSelection, Is.Not.Null);

            // 本番と同じくイベントとして解決する
            var selectionChanged = windowType.GetEvent("selectionChanged", memberFlags);
            Assert.That(selectionChanged, Is.Not.Null);
            Assert.That(
                selectionChanged.EventHandlerType,
                Is.EqualTo(typeof(Action<AdvancedDropdownItem>)));

            var dropdown = new TestDropdown(new AdvancedDropdownState());
            var window = ScriptableObject.CreateInstance(windowType) as EditorWindow;
            Assert.That(window, Is.Not.Null);

            try
            {
                windowField.SetValue(dropdown, window);
                int selectionCount = 0;
                Assert.That(
                    AdvancedDropdownReflection.TryOverrideSelectionHandling(
                        dropdown,
                        _ => selectionCount++,
                        out var configuredWindow),
                    Is.True,
                    "Selection handling could not be overridden in Unity {0}",
                    Application.unityVersion);
                Assert.That(configuredWindow, Is.SameAs(window));
                Assert.That(closeOnSelection.GetValue(window), Is.False);

                var handlerField = windowType.GetField(selectionChanged.Name, memberFlags);
                if (handlerField == null ||
                    handlerField.FieldType != selectionChanged.EventHandlerType)
                {
                    Assert.Ignore(
                        $"selectionChanged is not a field-like event in Unity {Application.unityVersion};" +
                        " handler invocation was not verified");
                }
                else
                {
                    var selectionHandler =
                        handlerField.GetValue(window) as Action<AdvancedDropdownItem>;
                    Assert.That(selectionHandler, Is.Not.Null);
                    selectionHandler(new AdvancedDropdownItem("Selected"));
                    Assert.That(selectionCount, Is.EqualTo(1));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private sealed class TestDropdown : SearchableNameDropdown
        {
            public TestDropdown(AdvancedDropdownState state)
                : base(state, "value", _ => { })
            {
            }

            public AdvancedDropdownItem CreateRoot()
            {
                return BuildRoot();
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Test");
                root.AddChild(new ValueItem("Value", "value") { id = 0 });
                return root;
            }
        }
    }
}
