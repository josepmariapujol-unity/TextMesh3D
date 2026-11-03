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

        TextMesh3DContainer Create(string text, TextMesh3DContainer.OutputMode mode, float extrusion = 1f, float size = 1f, float spacing = 0f, Font font = null, int curveSegments = 3, bool kerning = true)
        {
            var c = CreateContainer(text, mode, extrusion, size, spacing, font, curveSegments, kerning);
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
            var glyphs = Generate(Create(glyph, TextMesh3DContainer.OutputMode.Mesh, extrusion));
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
            var low = GetMesh(Generate(Create("B", TextMesh3DContainer.OutputMode.Mesh, extrusion: 1f))[0]);
            var high = GetMesh(Generate(Create("B", TextMesh3DContainer.OutputMode.Mesh, extrusion: 3f))[0]);

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
            var glyphs = Generate(Create(glyph, TextMesh3DContainer.OutputMode.Surface));
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
            var surface = GetMesh(Generate(Create("B", TextMesh3DContainer.OutputMode.Surface))[0]);
            var solid = GetMesh(Generate(Create("B", TextMesh3DContainer.OutputMode.Mesh))[0]);

            MeasureAreas(surface, out var surfaceUp, out _, out _);
            MeasureAreas(solid, out var solidUp, out _, out _);
            Assert.AreEqual(solidUp, surfaceUp, solidUp * 0.01f);
        }

        // ---------- Spline mode ----------

        [TestCaseSource(nameof(k_Glyphs))]
        public void Spline_ContoursAreClosedAndFlat(string glyph)
        {
            var glyphs = Generate(Create(glyph, TextMesh3DContainer.OutputMode.Spline));
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
            var glyphs = Generate(Create("ABC D", TextMesh3DContainer.OutputMode.Spline));
            for (int i = 1; i < glyphs.Count; i++)
                Assert.Greater(glyphs[i].localPosition.x, glyphs[i - 1].localPosition.x, $"Glyph {i} is not right of glyph {i - 1}");
        }

        [Test]
        public void Layout_SpacesHaveNoGeometry()
        {
            var glyphs = Generate(Create("A B", TextMesh3DContainer.OutputMode.Mesh));
            Assert.IsTrue(IsSpace(glyphs[1]));
            Assert.IsNull(GetMesh(glyphs[1]));
        }

        [Test]
        public void Layout_SpacingAddsConstantOffsetPerGlyph()
        {
            const float spacing = 2.5f;
            var tight = Generate(Create("AVA", TextMesh3DContainer.OutputMode.Spline));
            var loose = Generate(Create("AVA", TextMesh3DContainer.OutputMode.Spline, spacing: spacing));

            for (int i = 0; i < tight.Count; i++)
                Assert.AreEqual(tight[i].localPosition.x + i * spacing, loose[i].localPosition.x, k_Eps);
        }

        [Test]
        public void Layout_SizeScalesUniformly()
        {
            var small = Generate(Create("AB", TextMesh3DContainer.OutputMode.Surface, size: 1f));
            var large = Generate(Create("AB", TextMesh3DContainer.OutputMode.Surface, size: 2f));

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
            // Kerning off: this test checks the plain advance widths (kerning has its own tests)
            var glyphs = Generate(Create(text, TextMesh3DContainer.OutputMode.Spline, size: size, font: LoadFont(path), kerning: false));

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
            var mesh = GetMesh(Generate(Create("H", TextMesh3DContainer.OutputMode.Surface, size: size, font: LoadFont(path)))[0]);

            // Cap height in Unity units must equal the font's cap height as a fraction of the em.
            float capHeightEm = typeface.GetGlyph(typeface.GetGlyphIndex('H')).Bounds.YMax / (float)typeface.UnitsPerEm;
            Assert.AreEqual(capHeightEm * size, mesh.bounds.max.z, k_Eps);

            // And every regular Latin font lands in a sane range (bundled fonts: 0.63 - 0.76 em).
            Assert.That(mesh.bounds.max.z, Is.InRange(0.55f * size, 0.85f * size), "Cap height should be ~0.7 em");
        }

        // Every bundled font x a spread of glyphs (straight, curved, holes). Garamond and Baskerville have dense
        // outlines that used to triangulate into empty meshes.
        static IEnumerable<TestCaseData> AllFontGlyphs()
        {
            var dir = System.IO.Path.GetFullPath(PackageRoot + "/Editor/Resources/Fonts");
            foreach (var file in System.IO.Directory.GetFiles(dir))
            {
                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf") continue;
                var name = System.IO.Path.GetFileName(file);
                foreach (var ch in "AHOBeg8aRS")
                    yield return new TestCaseData(name, ch.ToString()).SetName($"Surface_CoversGlyphOutline({name}, {ch})");
            }
        }

        [TestCaseSource(nameof(AllFontGlyphs))]
        public void Surface_CoversGlyphOutline(string fontFile, string glyph)
        {
            var mesh = GetMesh(Generate(Create(glyph, TextMesh3DContainer.OutputMode.Surface, size: 1f, font: LoadFont(FontPath(fontFile))))[0]);
            AssertValidMesh(mesh);

            MeasureAreas(mesh, out var up, out var down, out _);
            Assert.Greater(up, 0f, "Glyph triangulated into an empty surface");
            Assert.AreEqual(up, down, up * 0.01f);

            // The filled area must not exceed the glyph's bounding box (catches triangles leaking outside).
            var size = mesh.bounds.size;
            Assert.LessOrEqual(up, size.x * size.z * 1.001f);
        }

        // ---------- Holes / contour classification ----------

        // Expected = outlines - holes. Helvetica and Futura list the holes before the outline, which used to
        // make the mesher fill one counter of "B", "8", "&", "$". "%" used to give each ring the other's hole.
        [TestCase("Roboto.ttf", "H", 1)]
        [TestCase("Roboto.ttf", "O", 0)]
        [TestCase("Roboto.ttf", "B", -1)]
        [TestCase("Roboto.ttf", "i", 2)]
        [TestCase("Roboto.ttf", "8", -1)]
        [TestCase("Roboto.ttf", "%", 1)]
        [TestCase("Roboto.ttf", "@", 0)]
        [TestCase("Helvetica.ttf", "B", -1)]
        [TestCase("Helvetica.ttf", "8", -1)]
        [TestCase("Helvetica.ttf", "&", -1)]
        [TestCase("Helvetica.ttf", "$", -1)]
        [TestCase("Helvetica.ttf", "%", 1)]
        [TestCase("Futura.ttf", "B", -1)]
        [TestCase("Futura.ttf", "&", -1)]
        [TestCase("Futura.ttf", "%", 1)]
        [TestCase("ComicSans.TTF", "&", -1)]
        [TestCase("ComicSans.TTF", "$", -1)]
        [TestCase("Arial.ttf", "%", 1)]
        public void Surface_HasCorrectNumberOfHoles(string fontFile, string glyph, int outlinesMinusHoles)
        {
            var mesh = GetMesh(Generate(Create(glyph, TextMesh3DContainer.OutputMode.Surface, font: LoadFont(FontPath(fontFile))))[0]);
            Assert.AreEqual(outlinesMinusHoles, UpFacingEulerCharacteristic(mesh),
                "Euler characteristic of the cap (outlines - holes) is wrong: a hole was filled or assigned to the wrong outline");
        }

        [TestCase("B")]
        [TestCase("%")]
        public void Mesh_TopCapHasCorrectNumberOfHoles(string glyph)
        {
            var surface = GetMesh(Generate(Create(glyph, TextMesh3DContainer.OutputMode.Surface, font: LoadFont(FontPath("Helvetica.ttf"))))[0]);
            var solid = GetMesh(Generate(Create(glyph, TextMesh3DContainer.OutputMode.Mesh, font: LoadFont(FontPath("Helvetica.ttf"))))[0]);

            // Up-facing triangles of the solid are just the top cap, so it must match the surface.
            Assert.AreEqual(UpFacingEulerCharacteristic(surface), UpFacingEulerCharacteristic(solid));
        }

        // ---------- UVs / tangents ----------

        [TestCase(TextMesh3DContainer.OutputMode.Mesh)]
        [TestCase(TextMesh3DContainer.OutputMode.Surface)]
        public void Mesh_HasUVsNormalsAndTangents(TextMesh3DContainer.OutputMode mode)
        {
            var mesh = GetMesh(Generate(Create("Bg", mode, extrusion: 0.5f, size: 2f))[0]);

            Assert.AreEqual(mesh.vertexCount, mesh.uv.Length, "Missing UVs");
            Assert.AreEqual(mesh.vertexCount, mesh.normals.Length, "Missing normals");
            Assert.AreEqual(mesh.vertexCount, mesh.tangents.Length, "Missing tangents");
            foreach (var uv in mesh.uv)
                Assert.IsTrue(float.IsFinite(uv.x) && float.IsFinite(uv.y), $"Non-finite UV {uv}");
        }

        [Test]
        public void UVs_AreInEmUnits_SoTextureScaleIsIndependentOfSize()
        {
            var small = GetMesh(Generate(Create("H", TextMesh3DContainer.OutputMode.Surface, size: 1f))[0]);
            var large = GetMesh(Generate(Create("H", TextMesh3DContainer.OutputMode.Surface, size: 4f))[0]);

            var smallUv = small.uv;
            var largeUv = large.uv;
            Assert.AreEqual(smallUv.Length, largeUv.Length);

            float Extent(Vector2[] uvs) { float min = float.MaxValue, max = float.MinValue; foreach (var u in uvs) { min = Mathf.Min(min, u.y); max = Mathf.Max(max, u.y); } return max - min; }
            Assert.AreEqual(Extent(smallUv), Extent(largeUv), 1e-3f);
            Assert.That(Extent(smallUv), Is.InRange(0.5f, 0.9f), "Cap UV height should be the cap height in em (~0.7)");
        }

        // ---------- Curve quality ----------

        [Test]
        public void CurveSegments_IncreaseResolutionAndConvergeOnTheTrueShape()
        {
            var coarse = GetMesh(Generate(Create("O", TextMesh3DContainer.OutputMode.Surface, size: 10f, curveSegments: 1))[0]);
            var normal = GetMesh(Generate(Create("O", TextMesh3DContainer.OutputMode.Surface, size: 10f, curveSegments: 3))[0]);
            var fine = GetMesh(Generate(Create("O", TextMesh3DContainer.OutputMode.Surface, size: 10f, curveSegments: 12))[0]);

            Assert.Less(coarse.vertexCount, normal.vertexCount);
            Assert.Less(normal.vertexCount, fine.vertexCount);
            AssertValidMesh(fine);
            Assert.AreEqual(0, UpFacingEulerCharacteristic(fine), "'O' must keep exactly one hole at any resolution");

            // Chords cut corners off a curved ring, so the area changes less and less as resolution goes up.
            MeasureAreas(coarse, out var a1, out _, out _);
            MeasureAreas(normal, out var a3, out _, out _);
            MeasureAreas(fine, out var a12, out _, out _);
            Assert.Less(Mathf.Abs(a12 - a3), Mathf.Abs(a3 - a1));
        }

        [Test]
        public void Layout_SpaceUsesFontSpaceAdvance()
        {
            const float size = 2f;
            var typeface = LoadTypeface(RobotoPath);
            var glyphs = Generate(Create("A B", TextMesh3DContainer.OutputMode.Spline, size: size, kerning: false));

            float scale = size / typeface.UnitsPerEm;
            float spaceAdvance = typeface.GetAdvanceWidthFromGlyphIndex(typeface.GetGlyphIndex(' ')) * scale;
            Assert.Greater(spaceAdvance, 0f);
            Assert.AreEqual(spaceAdvance, glyphs[2].localPosition.x - glyphs[1].localPosition.x, k_Eps);
        }

        [Test]
        public void Layout_NarrowGlyphsAreNotOverlapping()
        {
            // Pen advance must be at least the ink width, plus right side bearing, for "i" and "l".
            var glyphs = Generate(Create("ilil", TextMesh3DContainer.OutputMode.Surface, size: 1f));
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
            var container = Create("Hi", TextMesh3DContainer.OutputMode.Mesh);
            container.Generate();
            container.Text = "Hello";
            var glyphs = Generate(container); // asserts a single generated root

            Assert.AreEqual(5, glyphs.Count);
        }

        // ---------- Undo / leaks / materials ----------

        [Test]
        public void Regenerate_CanBeUndone()
        {
            var container = Create("Hi", TextMesh3DContainer.OutputMode.Mesh);
            Undo.IncrementCurrentGroup();
            container.Generate();

            Undo.IncrementCurrentGroup();
            container.Text = "Hello";
            container.Generate();
            Assert.AreEqual(5, container.transform.GetChild(0).childCount);

            Undo.PerformUndo();

            Assert.AreEqual(1, container.transform.childCount, "Undo should restore exactly the previous output");
            var root = container.transform.GetChild(0);
            Assert.AreEqual(2, root.childCount);
            Assert.AreEqual("Glyph - H", root.GetChild(0).name);
            Assert.IsNotNull(GetMesh(root.GetChild(0)), "Restored glyph lost its mesh");
        }

        [Test]
        public void Generate_DoesNotLeakTemporaryMeshes()
        {
            var container = Create("AB", TextMesh3DContainer.OutputMode.Mesh);
            int before = Resources.FindObjectsOfTypeAll<Mesh>().Length;

            container.Generate();

            int created = Resources.FindObjectsOfTypeAll<Mesh>().Length - before;
            Assert.AreEqual(2, created, "Expected exactly one mesh per glyph; temporary wall/cap meshes must be destroyed");
        }

        [Test]
        public void Generate_UsesSharedMaterial_NoInstances()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PackageRoot + "/Editor/Resources/Materials/FontMaterial.mat");
            Assert.IsNotNull(material);
            var container = Create("A", TextMesh3DContainer.OutputMode.Mesh);
            container.Material = material;

            var glyphs = Generate(container);

            Assert.AreSame(material, glyphs[0].GetComponent<MeshRenderer>().sharedMaterial, "Renderer should reference the material asset, not a copy");
        }

        // ---------- Group Output ----------

        [TestCase(TextMesh3DContainer.OutputMode.Mesh)]
        [TestCase(TextMesh3DContainer.OutputMode.Surface)]
        public void GroupOutput_MergesAllGlyphsIntoRootMesh(TextMesh3DContainer.OutputMode mode)
        {
            const string text = "AB C";
            var separate = Generate(Create(text, mode));
            int expectedVertices = 0;
            float expectedRight = float.MinValue;
            foreach (var g in separate)
            {
                var m = GetMesh(g);
                if (m == null) continue;
                expectedVertices += m.vertexCount;
                expectedRight = Mathf.Max(expectedRight, g.localPosition.x + m.bounds.max.x);
            }

            var grouped = Create(text, mode);
            grouped.GroupOutput = true;
            grouped.Generate();

            Assert.AreEqual(0, grouped.transform.childCount, "Grouped output should not leave per-glyph objects");
            var mesh = grouped.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh);
            AssertValidMesh(mesh);
            Assert.AreEqual(expectedVertices, mesh.vertexCount);
            Assert.AreEqual(expectedRight, mesh.bounds.max.x, k_Eps, "Glyphs must keep their layout positions");
            Assert.AreEqual(mesh.vertexCount, mesh.uv.Length);
        }

        [Test]
        public void GroupOutput_TurnedOff_RestoresPerGlyphObjectsAndClearsRootMesh()
        {
            var container = Create("AB", TextMesh3DContainer.OutputMode.Mesh);
            container.GroupOutput = true;
            container.Generate();
            Assert.IsNotNull(container.GetComponent<MeshFilter>().sharedMesh);

            container.GroupOutput = false;
            var glyphs = Generate(container);

            Assert.AreEqual(2, glyphs.Count);
            Assert.IsNull(container.GetComponent<MeshFilter>().sharedMesh, "Old grouped mesh should be removed");
        }

        [Test]
        public void Generate_CachesFontBytesForPlayerBuilds()
        {
            var container = Create("A", TextMesh3DContainer.OutputMode.Spline);
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
