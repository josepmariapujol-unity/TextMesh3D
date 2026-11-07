using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Softgraph.TextMesh3D.Editor;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using static Softgraph.TextMesh3D.Tests.TextMesh3DTestUtility;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>Glyph meshes are built once per glyph and settings, and shared by repeated letters and later Generate() calls.</summary>
    class TextMesh3DMeshCacheTests
    {
        const string k_Folder = "Assets/TextMesh3DMeshCacheTests";
        readonly List<TextMesh3DContainer> m_Created = new List<TextMesh3DContainer>();

        TextMesh3DContainer Create(string text, TextMesh3DContainer.OutputMode mode = TextMesh3DContainer.OutputMode.Mesh)
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
            if (AssetDatabase.IsValidFolder(k_Folder)) AssetDatabase.DeleteAsset(k_Folder);
        }

        [TestCase(TextMesh3DContainer.OutputMode.Mesh)]
        [TestCase(TextMesh3DContainer.OutputMode.Surface)]
        public void RepeatedLetters_ShareOneMesh(TextMesh3DContainer.OutputMode mode)
        {
            var c = Create("AAA", mode);
            var glyphs = Generate(c);

            Assert.AreEqual(1, c.GlyphMeshBuildCount, "'A' should be triangulated once");
            Assert.AreSame(GetMesh(glyphs[0]), GetMesh(glyphs[1]));
            Assert.AreSame(GetMesh(glyphs[0]), GetMesh(glyphs[2]));
        }

        [Test]
        public void ChangingText_OnlyBuildsNewGlyphs()
        {
            var c = Create("Score 1");
            Generate(c);
            int built = c.GlyphMeshBuildCount; // S c o r e, space, 1

            c.Text = "Score 12";
            var glyphs = Generate(c);

            Assert.AreEqual(built + 1, c.GlyphMeshBuildCount, "Only '2' is new");
            Assert.IsNotNull(GetMesh(glyphs[7]));
        }

        [Test]
        public void SettingsThatDontChangeGlyphShapes_ReuseMeshes()
        {
            var c = Create("AVA\nTo");
            var first = GetMesh(Generate(c)[0]);
            int built = c.GlyphMeshBuildCount;

            c.Spacing = 0.3f;
            c.Kerning = false;
            c.LineSpacing = 2f;
            c.Material = null;
            var glyphs = Generate(c);

            Assert.AreEqual(built, c.GlyphMeshBuildCount);
            Assert.AreSame(first, GetMesh(glyphs[0]));
        }

        static IEnumerable<TestCaseData> ShapeChanges()
        {
            yield return new TestCaseData(new System.Action<TextMesh3DContainer>(c => c.Size = 2f)).SetName("Rebuilds_WhenSizeChanges");
            yield return new TestCaseData(new System.Action<TextMesh3DContainer>(c => c.Extrusion = 3f)).SetName("Rebuilds_WhenExtrusionChanges");
            yield return new TestCaseData(new System.Action<TextMesh3DContainer>(c => c.CurveSegments = 8)).SetName("Rebuilds_WhenCurveSegmentsChange");
            yield return new TestCaseData(new System.Action<TextMesh3DContainer>(c => c.Plane = TextMesh3DContainer.TextPlane.XY)).SetName("Rebuilds_WhenPlaneChanges");
            yield return new TestCaseData(new System.Action<TextMesh3DContainer>(c => c.Output = TextMesh3DContainer.OutputMode.Surface)).SetName("Rebuilds_WhenOutputChanges");
            yield return new TestCaseData(new System.Action<TextMesh3DContainer>(c => c.Font = LoadFont(FontPath("Baskerville.ttf")))).SetName("Rebuilds_WhenFontChanges");
        }

        [TestCaseSource(nameof(ShapeChanges))]
        public void SettingsThatChangeGlyphShapes_Rebuild(System.Action<TextMesh3DContainer> change)
        {
            var c = Create("O");
            var before = GetMesh(Generate(c)[0]);
            var beforeBounds = before.bounds;

            change(c);
            var after = GetMesh(Generate(c)[0]);

            Assert.AreNotSame(before, after);
            Assert.AreEqual(2, c.GlyphMeshBuildCount);
            Assert.AreEqual(beforeBounds, before.bounds, "A cached mesh must never be modified (undo may still show it)");
        }

        [Test]
        public void MeshOutput_GlyphObjectsHaveNoSplineContainer()
        {
            var glyphs = Generate(Create("AB"));
            foreach (var g in glyphs) Assert.IsNull(g.GetComponent<SplineContainer>());
        }

        [Test]
        public void GroupOutput_UsesCacheAcrossGenerates()
        {
            var c = Create("Score 1");
            c.GroupOutput = true;
            c.Generate();
            int built = c.GlyphMeshBuildCount;
            int vertices = c.GetComponent<MeshFilter>().sharedMesh.vertexCount;

            c.Text = "Score 1";
            c.Generate();

            Assert.AreEqual(built, c.GlyphMeshBuildCount);
            Assert.AreEqual(0, c.transform.childCount);
            Assert.AreEqual(vertices, c.GetComponent<MeshFilter>().sharedMesh.vertexCount);
        }

        [Test]
        public void DestroyedCachedMesh_IsRebuilt()
        {
            var c = Create("A");
            Object.DestroyImmediate(GetMesh(Generate(c)[0]));

            var mesh = GetMesh(Generate(c)[0]);

            Assert.IsTrue(mesh != null, "A destroyed cached mesh must be rebuilt, not reused");
            Assert.Greater(mesh.vertexCount, 0);
        }

        [Test]
        public void Undo_RestoresOutputWithLiveMeshes()
        {
            var c = Create("AB");
            Undo.IncrementCurrentGroup();
            c.Generate();
            Undo.IncrementCurrentGroup();

            c.Extrusion = 3f; // invalidates the cache
            c.Generate();
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();

            foreach (Transform g in c.GeneratedRoot.transform)
            {
                var mesh = GetMesh(g);
                Assert.IsTrue(mesh != null, "Meshes of the restored output must not have been destroyed");
                Assert.Greater(mesh.vertexCount, 0);
            }
        }

        [Test]
        public void SaveAsPrefab_StoresSharedMeshOnce()
        {
            if (!AssetDatabase.IsValidFolder(k_Folder)) AssetDatabase.CreateFolder("Assets", "TextMesh3DMeshCacheTests");
            var c = Create("AAB");
            c.Generate();
            var path = k_Folder + "/Shared.prefab";

            var prefab = TextMesh3DPrefabExporter.SaveAsPrefab(c, path);

            var embedded = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Mesh>().ToArray();
            Assert.AreEqual(2, embedded.Length, "One mesh for 'A' (used twice) and one for 'B'");
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Assert.AreSame(filters[0].sharedMesh, filters[1].sharedMesh);
        }
    }
}
