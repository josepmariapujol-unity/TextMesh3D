using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.TestTools;
using static Softgraph.TextMesh3D.Tests.TextMesh3DTestUtility;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>
    /// Bad input must never throw: generation logs a clear message and leaves the scene in a sane state.
    /// (The Unity Test Framework fails a test on any unexpected error log, so "no exception" is checked implicitly.)
    /// </summary>
    class TextMesh3DRobustnessTests
    {
        const float k_Eps = 1e-3f;
        readonly List<TextMesh3DContainer> m_Created = new List<TextMesh3DContainer>();

        TextMesh3DContainer Create(string text, TextMesh3DContainer.Enum mode = TextMesh3DContainer.Enum.Mesh)
        {
            var c = CreateContainer(text, mode);
            m_Created.Add(c);
            return c;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in m_Created) TextMesh3DTestUtility.Destroy(c);
            m_Created.Clear();
        }

        // ---------- Invalid settings keep the previous output ----------

        [Test]
        public void NullFont_LogsError_KeepsPreviousOutput()
        {
            var container = Create("A");
            Generate(container);

            container.m_Font = null;
            LogAssert.Expect(LogType.Error, new Regex("no font data"));
            container.Function();

            Assert.AreEqual(1, container.transform.childCount, "Previous output should be kept when the font is missing");
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidSize_LogsError_KeepsPreviousOutput(float size)
        {
            var container = Create("A");
            Generate(container);

            container.m_Size = size;
            LogAssert.Expect(LogType.Error, new Regex("Font Size must be greater than 0"));
            container.Function();

            Assert.AreEqual(1, container.transform.childCount);
        }

        [TestCase(-0.5f)]
        [TestCase(float.NaN)]
        public void InvalidExtrusion_LogsError(float extrusion)
        {
            var container = Create("A");
            container.m_Extrusion = extrusion;
            LogAssert.Expect(LogType.Error, new Regex("Extrusion must be 0 or greater"));
            container.Function();

            Assert.AreEqual(0, container.transform.childCount);
        }

        [Test]
        public void CorruptFontData_LogsError_DoesNotThrow()
        {
            var container = Create("A");
            var so = new SerializedObject(container);
            var data = so.FindProperty("m_FontData");
            data.arraySize = 16;
            for (int i = 0; i < 16; i++) data.GetArrayElementAtIndex(i).intValue = i * 7;
            // Same source path as the assigned font, so the editor cache keeps the corrupt bytes
            so.FindProperty("m_FontDataSource").stringValue = RobotoPath;
            so.ApplyModifiedPropertiesWithoutUndo();

            LogAssert.Expect(LogType.Error, new Regex("could not read font|is not a supported TrueType/OpenType font"));
            container.Function();

            Assert.AreEqual(0, container.transform.childCount);
        }

        // ---------- Text edge cases ----------

        [TestCase("")]
        [TestCase(null)]
        public void EmptyOrNullText_ClearsOutput(string text)
        {
            var container = Create("AB");
            container.m_GroupOutput = true;
            container.Function();
            Assert.IsNotNull(container.GetComponent<MeshFilter>().sharedMesh);

            container.m_Text = text;
            container.Function();

            Assert.AreEqual(0, container.transform.childCount);
            Assert.IsNull(container.GetComponent<MeshFilter>().sharedMesh);
        }

        [Test]
        public void LineBreak_StartsNewLine_TabAdvancesFourSpaces_ControlCharactersAreInvisible()
        {
            const float size = 1f;
            var glyphs = Generate(Create("A\nB\tC\rD", TextMesh3DContainer.Enum.Mesh));
            var typeface = LoadTypeface(RobotoPath);
            float scale = size / typeface.UnitsPerEm;
            float Advance(char c) => typeface.GetAdvanceWidthFromGlyphIndex(typeface.GetGlyphIndex(c)) * scale;
            float lineHeight = (typeface.Ascender - typeface.Descender + typeface.LineGap) * scale;

            Assert.AreEqual("Line Break", glyphs[1].name);
            Assert.AreEqual("Tab", glyphs[3].name);
            Assert.AreEqual("Control U+000D", glyphs[5].name);

            // B starts the second line
            Assert.AreEqual(0f, glyphs[2].localPosition.x, k_Eps);
            Assert.AreEqual(-lineHeight, glyphs[2].localPosition.z, k_Eps);
            // Tab = 4 spaces
            Assert.AreEqual(Advance('B') + 4f * Advance(' '), glyphs[4].localPosition.x, k_Eps);
            // \r takes no space
            Assert.AreEqual(glyphs[4].localPosition.x + Advance('C'), glyphs[6].localPosition.x, k_Eps);

            foreach (var i in new[] { 1, 3, 5 })
            {
                Assert.IsNull(GetMesh(glyphs[i]), $"{glyphs[i].name} should have no geometry");
                Assert.IsNull(glyphs[i].GetComponent<SplineContainer>());
            }
            foreach (var i in new[] { 0, 2, 4, 6 })
                Assert.IsNotNull(GetMesh(glyphs[i]), $"{glyphs[i].name} should have geometry");
        }

        [Test]
        public void GlyphWithoutOutline_HasNoMeshOrRenderer()
        {
            var glyphs = Generate(Create("A B")); // no-break space: a real glyph with no contours
            Assert.IsNull(glyphs[1].GetComponent<MeshFilter>());
            Assert.IsNull(glyphs[1].GetComponent<MeshRenderer>());
            Assert.IsNotNull(GetMesh(glyphs[2]));
        }

        [Test]
        public void CharacterMissingFromFont_LogsWarning_StillGenerates()
        {
            LogAssert.Expect(LogType.Warning, new Regex("has no glyph for: '中'"));
            var glyphs = Generate(Create("A中B"));

            Assert.AreEqual(3, glyphs.Count);
            Assert.IsNotNull(GetMesh(glyphs[2]), "Text after a missing character must still be generated");
        }

        // ---------- Editor entry points ----------

        [Test]
        public void CreateMenu_CreatesGeneratedTextWithDefaults()
        {
            Selection.activeGameObject = null;
            Assert.IsTrue(EditorApplication.ExecuteMenuItem("GameObject/3D Object/Text - TextMesh3D"));

            var go = Selection.activeGameObject;
            Assert.IsNotNull(go, "Menu should select the new object");
            var container = go.GetComponent<TextMesh3DContainer>();
            m_Created.Add(container);

            Assert.IsNotNull(container.m_Font, "Default font should be assigned");
            Assert.IsNotNull(container.m_Material, "Default material should be assigned");
            Assert.AreEqual(1, container.transform.childCount, "Text should be generated right away");
            Assert.AreEqual(container.m_Text.Length, container.transform.GetChild(0).childCount);
        }
    }
}
