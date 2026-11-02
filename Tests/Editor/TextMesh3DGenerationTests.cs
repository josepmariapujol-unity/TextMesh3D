using System.Collections.Generic;
using NUnit.Framework;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEngine;
using static Softgraph.TextMesh3D.Tests.TextMesh3DTestUtility;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>
    /// Invariants that must hold for any correct output, regardless of implementation details.
    /// These should stay green through refactors; if one goes red, generation broke.
    /// </summary>
    class TextMesh3DGenerationTests
    {
        const float k_Eps = 1e-3f;

        // A spread of shapes: holes (A B O o e 8 @ Q R), multiple outers (i ? %), curves, straight lines, digits.
        static string[] k_Glyphs => new[] { "A", "B", "O", "o", "e", "g", "i", "8", "R", "Q", "W", "1", "?", "&", "@", "%" };

        readonly List<TextMesh3DContainer> m_Created = new List<TextMesh3DContainer>();

        TextMesh3DContainer Create(string text, TextMesh3DContainer.Enum mode, float extrusion = 1f, float size = 1f, float spacing = 0f, Font font = null)
        {
            var c = CreateContainer(text, mode, extrusion, size, spacing, font);
            m_Created.Add(c);
            return c;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in m_Created) TextMesh3DTestUtility.Destroy(c);
            m_Created.Clear();
        }

        // ---------- Mesh mode ----------

        [TestCaseSource(nameof(k_Glyphs))]
        public void Mesh_IsValidClosedExtrusion(string glyph)
        {
            const float extrusion = 2f;
            var glyphs = Generate(Create(glyph, TextMesh3DContainer.Enum.Mesh, extrusion));
            var mesh = GetMesh(glyphs[0]);

            AssertValidMesh(mesh);

            var b = mesh.bounds;
            Assert.AreEqual(0f, b.min.y, k_Eps, "Bottom cap should sit at y = 0");
            Assert.AreEqual(extrusion, b.max.y, k_Eps, "Top cap should sit at y = extrusion");

            MeasureAreas(mesh, out var up, out var down, out var side);
            Assert.Greater(up, 0f, "Top cap is empty");
            Assert.AreEqual(up, down, up * 0.01f, "Top and bottom caps should have the same area");
            Assert.Greater(side, 0f, "Side walls are empty");
        }

        [Test]
        public void Mesh_ExtrusionScalesOnlyHeight()
        {
            var low = GetMesh(Generate(Create("B", TextMesh3DContainer.Enum.Mesh, extrusion: 1f))[0]);
            var high = GetMesh(Generate(Create("B", TextMesh3DContainer.Enum.Mesh, extrusion: 3f))[0]);

            Assert.AreEqual(low.bounds.size.x, high.bounds.size.x, k_Eps);
            Assert.AreEqual(low.bounds.size.z, high.bounds.size.z, k_Eps);
            Assert.AreEqual(3f * low.bounds.size.y, high.bounds.size.y, k_Eps);

            MeasureAreas(low, out var lowUp, out _, out var lowSide);
            MeasureAreas(high, out var highUp, out _, out var highSide);
            Assert.AreEqual(lowUp, highUp, lowUp * 0.01f, "Cap area must not depend on extrusion");
            Assert.AreEqual(3f * lowSide, highSide, lowSide * 0.03f, "Wall area should scale with extrusion");
        }

        // ---------- Surface mode ----------

        [TestCaseSource(nameof(k_Glyphs))]
        public void Surface_IsFlatAndNonEmpty(string glyph)
        {
            var glyphs = Generate(Create(glyph, TextMesh3DContainer.Enum.Surface));
            var mesh = GetMesh(glyphs[0]);

            AssertValidMesh(mesh);
            Assert.AreEqual(0f, mesh.bounds.size.y, k_Eps, "Surface output should be flat");

            MeasureAreas(mesh, out var up, out var down, out _);
            Assert.Greater(up, 0f);
            Assert.AreEqual(up, down, up * 0.01f, "Surface should be double-sided with equal areas");
        }

        [Test]
        public void Surface_AreaMatchesMeshCap()
        {
            var surface = GetMesh(Generate(Create("B", TextMesh3DContainer.Enum.Surface))[0]);
            var solid = GetMesh(Generate(Create("B", TextMesh3DContainer.Enum.Mesh))[0]);

            MeasureAreas(surface, out var surfaceUp, out _, out _);
            MeasureAreas(solid, out var solidUp, out _, out _);
            Assert.AreEqual(solidUp, surfaceUp, solidUp * 0.01f);
        }

        // ---------- Spline mode ----------

        [TestCaseSource(nameof(k_Glyphs))]
        public void Spline_ContoursAreClosedAndFlat(string glyph)
        {
            var glyphs = Generate(Create(glyph, TextMesh3DContainer.Enum.Spline));
            var splines = GetSplines(glyphs[0]);

            Assert.IsNotNull(splines);
            Assert.Greater(splines.Splines.Count, 0, "Glyph produced no contours");
            foreach (var s in splines.Splines)
            {
                Assert.IsTrue(s.Closed, "Every glyph contour should be closed");
                Assert.Greater(s.Count, 0);
                for (int k = 0; k < s.Count; k++)
                    Assert.AreEqual(0f, s[k].Position.y, k_Eps, "Contours should lie on the XZ plane");
            }
        }

        // ---------- Layout ----------

        [Test]
        public void Layout_GlyphsAdvanceLeftToRight()
        {
            var glyphs = Generate(Create("ABC D", TextMesh3DContainer.Enum.Spline));
            for (int i = 1; i < glyphs.Count; i++)
                Assert.Greater(glyphs[i].localPosition.x, glyphs[i - 1].localPosition.x, $"Glyph {i} is not right of glyph {i - 1}");
        }

        [Test]
        public void Layout_SpacesHaveNoGeometry()
        {
            var glyphs = Generate(Create("A B", TextMesh3DContainer.Enum.Mesh));
            Assert.IsTrue(IsSpace(glyphs[1]));
            Assert.IsNull(GetMesh(glyphs[1]));
        }

        [Test]
        public void Layout_SpacingAddsConstantOffsetPerGlyph()
        {
            const float spacing = 2.5f;
            var tight = Generate(Create("AVA", TextMesh3DContainer.Enum.Spline));
            var loose = Generate(Create("AVA", TextMesh3DContainer.Enum.Spline, spacing: spacing));

            for (int i = 0; i < tight.Count; i++)
                Assert.AreEqual(tight[i].localPosition.x + i * spacing, loose[i].localPosition.x, k_Eps);
        }

        [Test]
        public void Layout_SizeScalesUniformly()
        {
            var small = Generate(Create("AB", TextMesh3DContainer.Enum.Surface, size: 1f));
            var large = Generate(Create("AB", TextMesh3DContainer.Enum.Surface, size: 2f));

            Assert.AreEqual(2f * small[1].localPosition.x, large[1].localPosition.x, k_Eps);
            var s = GetMesh(small[0]).bounds.size;
            var l = GetMesh(large[0]).bounds.size;
            Assert.AreEqual(2f * s.x, l.x, k_Eps);
            Assert.AreEqual(2f * s.z, l.z, k_Eps);
        }

        // ---------- Font metrics (UnitsPerEm / advance width) ----------

        // Roboto & co. use 2048 units per em, Baskerville uses 1000.
        static string[] k_Fonts => new[] { "Roboto.ttf", "Baskerville.ttf", "Arial.ttf", "Garamond.ttf" };

        [TestCaseSource(nameof(k_Fonts))]
        public void Layout_PenAdvancesByFontAdvanceWidth(string fontFile)
        {
            const string text = "Hil lo W.";
            const float size = 3f;
            var path = FontPath(fontFile);
            var typeface = LoadTypeface(path);
            var glyphs = Generate(Create(text, TextMesh3DContainer.Enum.Spline, size: size, font: LoadFont(path)));

            float scale = size / typeface.UnitsPerEm;
            float expectedX = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                Assert.AreEqual(expectedX, glyphs[i].localPosition.x, k_Eps, $"'{text[i]}' (index {i}) is misplaced");
                expectedX += typeface.GetAdvanceWidthFromGlyphIndex(typeface.GetGlyphIndex(text[i])) * scale;
            }
        }

        [TestCaseSource(nameof(k_Fonts))]
        public void Size_IsEmBased_IndependentOfUnitsPerEm(string fontFile)
        {
            const float size = 1f;
            var path = FontPath(fontFile);
            var typeface = LoadTypeface(path);
            var mesh = GetMesh(Generate(Create("H", TextMesh3DContainer.Enum.Surface, size: size, font: LoadFont(path)))[0]);

            // Cap height in Unity units must equal the font's cap height as a fraction of the em.
            float capHeightEm = typeface.GetGlyph(typeface.GetGlyphIndex('H')).Bounds.YMax / (float)typeface.UnitsPerEm;
            Assert.AreEqual(capHeightEm * size, mesh.bounds.max.z, k_Eps);

            // And every regular Latin font lands in a sane range (bundled fonts: 0.63 - 0.76 em).
            Assert.That(mesh.bounds.max.z, Is.InRange(0.55f * size, 0.85f * size), "Cap height should be ~0.7 em");
        }

        [Test]
        public void Layout_SpaceUsesFontSpaceAdvance()
        {
            const float size = 2f;
            var typeface = LoadTypeface(RobotoPath);
            var glyphs = Generate(Create("A B", TextMesh3DContainer.Enum.Spline, size: size));

            float scale = size / typeface.UnitsPerEm;
            float spaceAdvance = typeface.GetAdvanceWidthFromGlyphIndex(typeface.GetGlyphIndex(' ')) * scale;
            Assert.Greater(spaceAdvance, 0f);
            Assert.AreEqual(spaceAdvance, glyphs[2].localPosition.x - glyphs[1].localPosition.x, k_Eps);
        }

        [Test]
        public void Layout_NarrowGlyphsAreNotOverlapping()
        {
            // Pen advance must be at least the ink width, plus right side bearing, for "i" and "l".
            var glyphs = Generate(Create("ilil", TextMesh3DContainer.Enum.Surface, size: 1f));
            for (int i = 0; i + 1 < glyphs.Count; i++)
            {
                var inkRight = glyphs[i].localPosition.x + GetMesh(glyphs[i]).bounds.max.x;
                var nextInkLeft = glyphs[i + 1].localPosition.x + GetMesh(glyphs[i + 1]).bounds.min.x;
                Assert.Greater(nextInkLeft, inkRight, $"Glyph {i + 1} overlaps or touches glyph {i}");
            }
        }

        // ---------- Regeneration / font data ----------

        [Test]
        public void Regenerate_ReplacesPreviousOutput()
        {
            var container = Create("Hi", TextMesh3DContainer.Enum.Mesh);
            container.Function();
            container.m_Text = "Hello";
            var glyphs = Generate(container); // asserts a single generated root

            Assert.AreEqual(5, glyphs.Count);
        }

        [Test]
        public void Generate_CachesFontBytesForPlayerBuilds()
        {
            var container = Create("A", TextMesh3DContainer.Enum.Spline);
            Generate(container);

            var so = new SerializedObject(container);
            var data = so.FindProperty("m_FontData");
            Assert.IsNotNull(data, "m_FontData field missing");
            Assert.Greater(data.arraySize, 0, "Font bytes were not cached; generation would fail in a player build");
        }

        // ---------- helpers ----------

        static void AssertValidMesh(Mesh mesh)
        {
            Assert.IsNotNull(mesh, "Glyph has no mesh");
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;

            Assert.Greater(vertices.Length, 0, "Mesh has no vertices");
            Assert.Greater(triangles.Length, 0, "Mesh has no triangles");
            Assert.AreEqual(0, triangles.Length % 3);

            foreach (var v in vertices)
                Assert.IsTrue(float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z), $"Non-finite vertex {v}");
            foreach (var i in triangles)
                Assert.That(i, Is.InRange(0, vertices.Length - 1), "Triangle index out of range");
        }
    }
}
