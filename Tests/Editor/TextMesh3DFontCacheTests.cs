using System.Collections.Generic;
using NUnit.Framework;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEngine;
using static Softgraph.TextMesh3D.Tests.TextMesh3DTestUtility;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>The font file is parsed once and reused, and the cache never serves a stale font.</summary>
    class TextMesh3DFontCacheTests
    {
        readonly List<TextMesh3DContainer> m_Created = new List<TextMesh3DContainer>();

        TextMesh3DContainer Create(string text)
        {
            var c = CreateContainer(text, TextMesh3DContainer.OutputMode.Spline);
            m_Created.Add(c);
            return c;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in m_Created) TextMesh3DTestUtility.Destroy(c);
            m_Created.Clear();
        }

        [Test]
        public void Generate_ParsesFontOnlyOnce()
        {
            var container = Create("AV");
            container.Generate();
            container.Text = "Hello";
            container.Generate();
            container.Size = 2f;
            container.Kerning = false;
            container.Generate();

            Assert.AreEqual(1, container.TypefaceParseCount, "Changing text or settings must not re-parse the font");
        }

        [Test]
        public void ChangingFont_ReparsesAndUsesNewFont()
        {
            var container = Create("H");
            Generate(container);
            int robotoKnots = KnotCount(container); // Roboto "H" is straight lines only, Baskerville's has curved serifs

            container.Font = LoadFont(FontPath("Baskerville.ttf"));
            Generate(container);

            Assert.AreEqual(2, container.TypefaceParseCount, "A new font must be parsed");
            Assert.AreNotEqual(robotoKnots, KnotCount(container), "Output must come from the new font, not the cached one");
        }

        [Test]
        public void SwitchingBackToPreviousFont_UsesCorrectFont()
        {
            var container = Create("H");
            Generate(container);
            int robotoKnots = KnotCount(container);

            container.Font = LoadFont(FontPath("Baskerville.ttf"));
            Generate(container);
            container.Font = LoadRoboto();
            Generate(container);

            Assert.AreEqual(robotoKnots, KnotCount(container), "Switching back must give Roboto output again");
        }

        [Test]
        public void UndoFontChange_UsesRestoredFont()
        {
            var container = Create("H");
            Generate(container);
            int robotoKnots = KnotCount(container);

            Undo.IncrementCurrentGroup();
            Undo.RecordObject(container, "Change font");
            container.Font = LoadFont(FontPath("Baskerville.ttf"));
            Generate(container); // caches Baskerville's bytes and typeface; joins the same undo group
            Undo.IncrementCurrentGroup();

            Undo.PerformUndo(); // restores Roboto and its font bytes (a new array, so the typeface cache must miss)
            Generate(container);

            Assert.AreEqual(LoadRoboto(), container.Font);
            Assert.AreEqual(robotoKnots, KnotCount(container), "After undo the output must use Roboto, not the cached Baskerville");
        }

        [Test]
        public void ManyContainers_EachParseOnce()
        {
            var a = Create("A");
            var b = Create("B");
            for (int i = 0; i < 3; i++) { a.Generate(); b.Generate(); }

            Assert.AreEqual(1, a.TypefaceParseCount);
            Assert.AreEqual(1, b.TypefaceParseCount);
        }

        static int KnotCount(TextMesh3DContainer container)
        {
            var glyph = container.transform.GetChild(0).GetChild(0);
            int total = 0;
            foreach (var spline in glyph.GetComponent<UnityEngine.Splines.SplineContainer>().Splines) total += spline.Count;
            return total;
        }
    }
}
