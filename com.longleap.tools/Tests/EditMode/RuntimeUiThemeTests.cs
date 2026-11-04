using LongLeap.Tools.UI;
using NUnit.Framework;
using UnityEngine;

namespace LongLeap.Tools.Tests
{
    /// <summary>RuntimeUi reads its palette and sizes from the current theme.</summary>
    public sealed class RuntimeUiThemeTests
    {
        private RuntimeUiTheme _theme;

        [SetUp]
        public void SetUp() => _theme = ScriptableObject.CreateInstance<RuntimeUiTheme>();

        [TearDown]
        public void TearDown()
        {
            RuntimeUi.Theme = null;
            Object.DestroyImmediate(_theme);
        }

        [Test]
        public void Palette_ComesFromAssignedTheme()
        {
            _theme.Accent = Color.green;
            _theme.RowHeight = 80f;
            _theme.LabelFontSize = 40;

            RuntimeUi.Theme = _theme;

            Assert.AreEqual(Color.green, RuntimeUi.Accent);
            Assert.AreEqual(80f, RuntimeUi.RowHeight);
            Assert.AreEqual(40, RuntimeUi.LabelFontSize);
        }

        [Test]
        public void Theme_WithoutAssignment_IsNeverNull()
        {
            RuntimeUi.Theme = null;

            Assert.IsNotNull(RuntimeUi.Theme);
        }

        // A slider, not a button: the button caption is TextMeshPro, and without TMP Essentials
        // its component may open the import window in Edit Mode.
        [Test]
        public void CreateSlider_UsesThemeColors()
        {
            _theme.Text = Color.red;
            _theme.SliderTrack = Color.blue;
            RuntimeUi.Theme = _theme;

            var root = new GameObject("Root", typeof(RectTransform));
            try
            {
                var slider = RuntimeUi.CreateSlider("Slider", root.transform, Color.white);

                Assert.AreEqual(Color.red, ((UnityEngine.UI.Image)slider.targetGraphic).color);
                Assert.AreEqual(Color.blue, slider.transform.Find("Background").GetComponent<UnityEngine.UI.Image>().color);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
