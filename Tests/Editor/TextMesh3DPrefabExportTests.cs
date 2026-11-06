using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Softgraph.TextMesh3D.Editor;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.TestTools;
using static Softgraph.TextMesh3D.Tests.TextMesh3DTestUtility;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>Save as Prefab: baked, self-contained prefabs with the meshes stored inside the .prefab file.</summary>
    class TextMesh3DPrefabExportTests
    {
        const string k_Folder = "Assets/TextMesh3DExportTests";

        readonly List<TextMesh3DContainer> m_Created = new List<TextMesh3DContainer>();

        TextMesh3DContainer Create(string text, TextMesh3DContainer.OutputMode mode)
        {
            var c = CreateContainer(text, mode);
            m_Created.Add(c);
            return c;
        }

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(k_Folder)) AssetDatabase.CreateFolder("Assets", "TextMesh3DExportTests");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in m_Created) TextMesh3DTestUtility.Destroy(c);
            m_Created.Clear();
            AssetDatabase.DeleteAsset(k_Folder);
        }

        static Mesh[] Meshes(GameObject go) =>
            go.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Where(m => m != null).ToArray();

        static Mesh[] EmbeddedMeshes(string path) =>
            AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Mesh>().ToArray();

        [Test]
        public void Mesh_SavesEveryGlyphMeshInsideThePrefab()
        {
            var container = Create("AB C", TextMesh3DContainer.OutputMode.Mesh);
            container.Generate();
            var path = k_Folder + "/Mesh.prefab";

            var prefab = TextMesh3DPrefabExporter.SaveAsPrefab(container, path);

            Assert.IsNotNull(prefab);
            Assert.IsNull(prefab.GetComponentInChildren<TextMesh3DContainer>(true), "The prefab must be baked (no TextMesh3D component)");

            var source = Meshes(container.gameObject);
            var saved = Meshes(prefab);
            Assert.AreEqual(3, source.Length, "A, B and C have meshes; the space has none");
            Assert.AreEqual(source.Length, saved.Length);
            for (int i = 0; i < saved.Length; i++)
            {
                Assert.AreEqual(path, AssetDatabase.GetAssetPath(saved[i]), "Meshes must be stored inside the prefab");
                Assert.AreNotSame(source[i], saved[i], "The prefab must own a copy, not the scene mesh");
                Assert.AreEqual(source[i].vertexCount, saved[i].vertexCount);
                Assert.AreEqual(source[i].triangles.Length, saved[i].triangles.Length);
            }
            Assert.AreEqual(3, EmbeddedMeshes(path).Length);
        }

        [Test]
        public void Mesh_KeepsGlyphLayoutAndMaterial()
        {
            var container = Create("AV", TextMesh3DContainer.OutputMode.Mesh);
            container.Material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Temp Material" };
            container.Generate();
            var path = k_Folder + "/Layout.prefab";

            var prefab = TextMesh3DPrefabExporter.SaveAsPrefab(container, path);

            var sourceGlyphs = container.GeneratedRoot.transform;
            var savedGlyphs = prefab.transform.GetChild(0);
            Assert.AreEqual(sourceGlyphs.childCount, savedGlyphs.childCount);
            for (int i = 0; i < sourceGlyphs.childCount; i++)
            {
                Assert.AreEqual(sourceGlyphs.GetChild(i).name, savedGlyphs.GetChild(i).name);
                Assert.AreEqual(sourceGlyphs.GetChild(i).localPosition, savedGlyphs.GetChild(i).localPosition, "Kerning / layout must be kept");
            }

            // An in-memory material is embedded too, so the prefab doesn't lose it
            var material = savedGlyphs.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
            Assert.IsNotNull(material);
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(material));
            Object.DestroyImmediate(container.Material);
        }

        [Test]
        public void GroupOutput_SavesTheMergedMeshOnTheRoot()
        {
            var container = Create("Hello", TextMesh3DContainer.OutputMode.Surface);
            container.GroupOutput = true;
            container.Generate();
            var path = k_Folder + "/Grouped.prefab";

            var prefab = TextMesh3DPrefabExporter.SaveAsPrefab(container, path);

            Assert.AreEqual(0, prefab.transform.childCount);
            var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh);
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(mesh));
            Assert.AreEqual(container.GetComponent<MeshFilter>().sharedMesh.vertexCount, mesh.vertexCount);
            Assert.IsNotNull(prefab.GetComponent<MeshRenderer>());
        }

        [Test]
        public void Spline_SavesSplinesWithoutMeshes()
        {
            var container = Create("Hi", TextMesh3DContainer.OutputMode.Spline);
            container.Generate();
            var path = k_Folder + "/Spline.prefab";

            var prefab = TextMesh3DPrefabExporter.SaveAsPrefab(container, path);

            var source = container.GeneratedRoot.GetComponentsInChildren<SplineContainer>(true);
            var saved = prefab.GetComponentsInChildren<SplineContainer>(true);
            Assert.AreEqual(source.Length, saved.Length);
            for (int i = 0; i < saved.Length; i++)
            {
                Assert.AreEqual(source[i].Splines.Count, saved[i].Splines.Count);
                Assert.AreEqual(source[i].Splines.Sum(s => s.Count), saved[i].Splines.Sum(s => s.Count));
            }
            Assert.AreEqual(0, EmbeddedMeshes(path).Length);
        }

        [Test]
        public void Prefab_StaysValidAfterSourceIsRegeneratedAndDeleted()
        {
            var container = Create("AB", TextMesh3DContainer.OutputMode.Mesh);
            container.Generate();
            var path = k_Folder + "/Independent.prefab";
            var counts = Meshes(container.gameObject).Select(m => m.vertexCount).ToArray();

            TextMesh3DPrefabExporter.SaveAsPrefab(container, path);
            container.Text = "Z";
            container.Generate();
            TextMesh3DTestUtility.Destroy(container);
            m_Created.Clear();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            CollectionAssert.AreEqual(counts, Meshes(prefab).Select(m => m.vertexCount).ToArray());
        }

        [Test]
        public void SavingAgain_ReplacesMeshesInsteadOfAccumulating()
        {
            var container = Create("ABC", TextMesh3DContainer.OutputMode.Mesh);
            container.Generate();
            var path = k_Folder + "/Overwrite.prefab";
            TextMesh3DPrefabExporter.SaveAsPrefab(container, path);

            container.Text = "A";
            container.Generate();
            var prefab = TextMesh3DPrefabExporter.SaveAsPrefab(container, path);

            Assert.AreEqual(1, EmbeddedMeshes(path).Length);
            Assert.AreEqual(1, Meshes(prefab).Length);
        }

        [Test]
        public void NoOutput_LogsErrorAndSavesNothing()
        {
            var container = Create("A", TextMesh3DContainer.OutputMode.Mesh); // not generated
            var path = k_Folder + "/Empty.prefab";

            LogAssert.Expect(LogType.Error, new Regex("nothing to save"));
            Assert.IsNull(TextMesh3DPrefabExporter.SaveAsPrefab(container, path));
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void PathOutsideAssets_LogsError()
        {
            var container = Create("A", TextMesh3DContainer.OutputMode.Mesh);
            container.Generate();

            LogAssert.Expect(LogType.Error, new Regex("must be inside Assets/"));
            Assert.IsNull(TextMesh3DPrefabExporter.SaveAsPrefab(container, "Packages/foo.prefab"));
        }

        [Test]
        public void DefaultFileName_IsSingleLineAndFileSafe()
        {
            var container = Create("Line 1\nA/B:C", TextMesh3DContainer.OutputMode.Mesh);
            Assert.AreEqual("Line 1 A_B_C", TextMesh3DPrefabExporter.GetDefaultFileName(container));
        }
    }
}
