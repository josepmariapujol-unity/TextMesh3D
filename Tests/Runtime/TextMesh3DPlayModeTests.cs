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
            container.m_Font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Packages/com.softgraph.textmesh3d/Editor/Resources/Fonts/Roboto.ttf");
#endif
            Assert.IsNotNull(container.m_Font, "Test font not found");
            container.m_Text = "Ab";
            container.m_Output = TextMesh3DContainer.Enum.Mesh;
            container.m_Size = 1f;
            container.m_Extrusion = 1f;

            container.Function();
            yield return null;
            container.m_Text = "Abc";
            container.Function();
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
    }
}
