using System.Collections;
using NUnit.Framework;
using Softgraph.TextMesh3D.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>Generation while the game is running (uses Destroy instead of DestroyImmediate).</summary>
    class TextMesh3DPlayModeTests
    {
        GameObject m_Go;

        [TearDown]
        public void TearDown()
        {
            if (m_Go != null) Object.Destroy(m_Go);
        }

        [UnityTest]
        public IEnumerator Generate_InPlayMode_ProducesMeshesAndReplacesOldOutput()
        {
            m_Go = new GameObject("TextMesh3D PlayMode Test");
            var container = m_Go.AddComponent<TextMesh3DContainer>();
#if UNITY_EDITOR
            container.Font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Packages/com.softgraph.textmesh3d/Editor/Resources/Fonts/Roboto.ttf");
#endif
            Assert.IsNotNull(container.Font, "Test font not found");
            container.Text = "Ab";
            container.Output = TextMesh3DContainer.OutputMode.Mesh;
            container.Size = 1f;
            container.Extrusion = 1f;

            container.Generate();
            yield return null;
            container.Text = "Abc";
            container.Generate();
            yield return null; // Destroy() is deferred to end of frame

            Assert.AreEqual(1, container.transform.childCount, "Old output was not destroyed");
            var root = container.transform.GetChild(0);
            Assert.AreEqual(3, root.childCount);
            foreach (Transform glyph in root)
            {
                var mf = glyph.GetComponent<MeshFilter>();
                Assert.IsNotNull(mf);
                Assert.IsNotNull(mf.sharedMesh);
                Assert.Greater(mf.sharedMesh.vertexCount, 0);
            }
        }

        [UnityTest]
        public IEnumerator Regenerate_InPlayMode_DoesNotLeakMeshes()
        {
            m_Go = new GameObject("TextMesh3D PlayMode Leak Test");
            var container = m_Go.AddComponent<TextMesh3DContainer>();
#if UNITY_EDITOR
            container.Font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Packages/com.softgraph.textmesh3d/Editor/Resources/Fonts/Roboto.ttf");
#endif
            container.Text = "Abc";
            container.Output = TextMesh3DContainer.OutputMode.Mesh;
            container.Size = 1f;
            container.Extrusion = 1f;

            container.Generate();
            yield return null;
            int baseline = Resources.FindObjectsOfTypeAll<Mesh>().Length;

            for (int i = 0; i < 5; i++)
            {
                container.Generate();
                yield return null; // Destroy() runs at end of frame
            }

            Assert.AreEqual(baseline, Resources.FindObjectsOfTypeAll<Mesh>().Length, "Regenerating should replace meshes, not accumulate them");
        }
    }
}
