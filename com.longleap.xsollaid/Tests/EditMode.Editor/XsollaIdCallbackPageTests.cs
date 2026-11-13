using System;
using NUnit.Framework;

namespace LongLeap.XsollaId.Editor.Tests
{
    public class XsollaIdCallbackPageTests
    {
        private const string Template = "var A = [" + XsollaIdCallbackPage.Placeholder + "];";

        [Test]
        public void Render_FillsOriginsInOrder()
        {
            string page = XsollaIdCallbackPage.Render(
                Template, new[] { "http://127.0.0.1:8081", "https://game.example.com" });

            Assert.AreEqual("var A = ['http://127.0.0.1:8081', 'https://game.example.com'];", page);
        }

        [Test]
        public void Render_DropsEmptyAndDuplicateEntries_AndTrims()
        {
            string page = XsollaIdCallbackPage.Render(
                Template, new[] { " https://a.example ", "", null, "   ", "https://a.example" });

            Assert.AreEqual("var A = ['https://a.example'];", page);
        }

        [Test]
        public void Render_NullOrEmptyList_LeavesEmptyArray()
        {
            Assert.AreEqual("var A = [];", XsollaIdCallbackPage.Render(Template, null));
            Assert.AreEqual("var A = [];", XsollaIdCallbackPage.Render(Template, new string[0]));
        }

        [Test]
        public void Render_EscapesQuotesBackslashesAndScriptTags()
        {
            string page = XsollaIdCallbackPage.Render(Template, new[] { @"a'b\c</script>" });

            Assert.AreEqual(@"var A = ['a\'b\\c\u003c/script\u003e'];", page);
        }

        [Test]
        public void Render_TemplateWithoutPlaceholder_Throws()
        {
            Assert.Throws<FormatException>(() => XsollaIdCallbackPage.Render("var A = [];", new[] { "x" }));
        }
    }
}
