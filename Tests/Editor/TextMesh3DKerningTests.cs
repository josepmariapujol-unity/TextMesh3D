using System.Collections.Generic;
using NUnit.Framework;
using Softgraph.TextMesh3D.Runtime;
using UnityEngine;
using static Softgraph.TextMesh3D.Tests.TextMesh3DTestUtility;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>
    /// Kerning values are checked against fontTools (independent of Typography), so both kerning sources are covered:
    /// GPOS 'kern' feature (Roboto, Helvetica, Arial) and the legacy 'kern' table (Futura, Garamond, Rockwell).
    /// Baskerville, Comic Sans and Times New Roman have no kerning data.
    /// </summary>
    class TextMesh3DKerningTests
    {
        const float k_Eps = 1e-4f;
        readonly List<TextMesh3DContainer> m_Created = new List<TextMesh3DContainer>();

        List<Transform> Layout(string text, string fontFile = "Roboto.ttf", bool kerning = true)
        {
            var c = CreateContainer(text, TextMesh3DContainer.OutputMode.Spline, size: 1f, font: LoadFont(FontPath(fontFile)), kerning: kerning);
            m_Created.Add(c);
            return Generate(c);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in m_Created) TextMesh3DTestUtility.Destroy(c);
            m_Created.Clear();
        }

        // Kerning in font units, from fontTools: AV, To, LT, Te
        [TestCase("Arial.ttf", -152, -227, -152, -227)]
        [TestCase("Baskerville.ttf", 0, 0, 0, 0)]
        [TestCase("ComicSans.TTF", 0, 0, 0, 0)]
        [TestCase("Futura.ttf", -36, -206, -112, -206)]
        [TestCase("Garamond.ttf", -231, -154, -111, -115)]
        [TestCase("Helvetica.ttf", -151, -227, -151, -227)]
        [TestCase("Roboto.ttf", -77, -208, -205, -89)]
        [TestCase("Rockwell.TTF", -123, -123, 0, -102)]
        [TestCase("Times New Roman.ttf", 0, 0, 0, 0)]
        public void Kerning_MatchesFontData(string fontFile, int av, int to, int lt, int te)
        {
            var typeface = LoadTypeface(FontPath(fontFile));
            float scale = 1f / typeface.UnitsPerEm;

            foreach (var (pair, kern) in new[] { ("AV", av), ("To", to), ("LT", lt), ("Te", te) })
            {
                var glyphs = Layout(pair, fontFile);
                float advance = typeface.GetAdvanceWidthFromGlyphIndex(typeface.GetGlyphIndex(pair[0])) * scale;
                Assert.AreEqual(advance + kern * scale, glyphs[1].localPosition.x, k_Eps, $"{fontFile} '{pair}'");
            }
        }

        // Expected glyph x positions at Size = 1 (em units), computed with fontTools from the font files.
        [TestCase("Roboto.ttf", "AVA", new[] { 0f, 0.6279f, 1.2383f })]
        [TestCase("Roboto.ttf", "To", new[] { 0f, 0.5054f })]
        [TestCase("Futura.ttf", "AVA", new[] { 0f, 0.6445f, 1.2451f })]
        [TestCase("Futura.ttf", "To", new[] { 0f, 0.4082f })]
        [TestCase("Garamond.ttf", "AVA", new[] { 0f, 0.5645f, 1.1416f })]
        [TestCase("Garamond.ttf", "To", new[] { 0f, 0.5396f })]
        public void Kerning_GlyphPositions(string fontFile, string text, float[] expectedX)
        {
            var glyphs = Layout(text, fontFile);
            for (int i = 0; i < expectedX.Length; i++)
                Assert.AreEqual(expectedX[i], glyphs[i].localPosition.x, 1e-3f, $"{fontFile} '{text}' glyph {i} ('{text[i]}')");
        }

        [Test]
        public void Kerning_Disabled_UsesPlainAdvance()
        {
            var typeface = LoadTypeface(RobotoPath);
            float advanceA = typeface.GetAdvanceWidthFromGlyphIndex(typeface.GetGlyphIndex('A')) / (float)typeface.UnitsPerEm;

            var kerned = Layout("AV");
            var plain = Layout("AV", kerning: false);

            Assert.AreEqual(advanceA, plain[1].localPosition.x, k_Eps);
            Assert.Less(kerned[1].localPosition.x, plain[1].localPosition.x, "AV should be tighter with kerning");
        }

        [Test]
        public void Kerning_LeavesUnkernedPairsAlone()
        {
            var kerned = Layout("HHH");
            var plain = Layout("HHH", kerning: false);
            for (int i = 0; i < 3; i++)
                Assert.AreEqual(plain[i].localPosition.x, kerned[i].localPosition.x, k_Eps);
        }

        [Test]
        public void Kerning_IsNotAppliedAcrossLineBreaksOrTabs()
        {
            var lines = Layout("A\nV");
            Assert.AreEqual(0f, lines[2].localPosition.x, k_Eps, "V starts a new line; no kerning with the A above");

            var tabbed = Layout("A\tV");
            var tabbedPlain = Layout("A\tV", kerning: false);
            Assert.AreEqual(tabbedPlain[2].localPosition.x, tabbed[2].localPosition.x, k_Eps);
        }

        [Test]
        public void Kerning_AccumulatesAlongAWord()
        {
            // Each kerned pair shifts everything after it, so the total shift is the sum of all pair adjustments.
            var typeface = LoadTypeface(RobotoPath);
            float scale = 1f / typeface.UnitsPerEm;
            var kerned = Layout("AVAT");
            var plain = Layout("AVAT", kerning: false);

            // From fontTools: AV -77, VA -75, AT -120
            Assert.AreEqual(plain[3].localPosition.x + (-77 - 75 - 120) * scale, kerned[3].localPosition.x, k_Eps);
        }

        [Test]
        public void Kerning_WorksInMeshModeAndGroupedOutput()
        {
            var separate = CreateContainer("AV", TextMesh3DContainer.OutputMode.Mesh);
            var grouped = CreateContainer("AV", TextMesh3DContainer.OutputMode.Mesh);
            m_Created.Add(separate);
            m_Created.Add(grouped);
            grouped.GroupOutput = true;

            var glyphs = Generate(separate);
            grouped.Generate();

            float right = glyphs[1].localPosition.x + GetMesh(glyphs[1]).bounds.max.x;
            Assert.AreEqual(right, grouped.GetComponent<MeshFilter>().sharedMesh.bounds.max.x, 1e-3f);
        }
    }
}
