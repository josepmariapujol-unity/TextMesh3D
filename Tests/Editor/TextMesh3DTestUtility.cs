using System.Collections.Generic;
using NUnit.Framework;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>Shared helpers: build a TextMesh3DContainer, generate, and inspect / clean up the output.</summary>
    static class TextMesh3DTestUtility
    {
        public const string PackageRoot = "Packages/com.softgraph.textmesh3d";
        public const string RobotoPath = PackageRoot + "/Editor/Resources/Fonts/Roboto.ttf";

        public static Font LoadRoboto()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(RobotoPath);
            Assert.IsNotNull(font, $"Test font missing at {RobotoPath}");
            return font;
        }

        public static TextMesh3DContainer CreateContainer(
            string text,
            TextMesh3DContainer.Enum mode,
            float extrusion = 1f,
            float size = 1f,
            float spacing = 0f)
        {
            var go = new GameObject("TextMesh3D Test");
            var container = go.AddComponent<TextMesh3DContainer>();
            container.m_Text = text;
            container.m_Font = LoadRoboto();
            container.m_Output = mode;
            container.m_Extrusion = extrusion;
            container.m_Size = size;
            container.m_Spacing = spacing;
            return container;
        }

        /// <summary>Generates and returns the glyph objects (children of the generated root), in text order.</summary>
        public static List<Transform> Generate(TextMesh3DContainer container)
        {
            container.Function();
            Assert.AreEqual(1, container.transform.childCount, "Expected exactly one generated root under the container.");
            var root = container.transform.GetChild(0);
            var glyphs = new List<Transform>();
            foreach (Transform child in root) glyphs.Add(child);
            Assert.AreEqual(container.m_Text.Length, glyphs.Count, "Expected one glyph object per character.");
            return glyphs;
        }

        public static bool IsSpace(Transform glyph) => glyph.name == "Space";

        public static Mesh GetMesh(Transform glyph)
        {
            var mf = glyph.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        /// <summary>Destroys the container, everything it generated, and the generated meshes.</summary>
        public static void Destroy(TextMesh3DContainer container)
        {
            if (container == null) return;
            foreach (var mf in container.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null && !EditorUtility.IsPersistent(mf.sharedMesh))
                    Object.DestroyImmediate(mf.sharedMesh);
            }
            Object.DestroyImmediate(container.gameObject);
        }

        // --- Geometry measurements (order-independent, so they don't depend on HashSet iteration order) ---

        /// <summary>Sum of triangle areas, split by whether the face normal points up (+Y), down (-Y) or sideways.</summary>
        public static void MeasureAreas(Mesh mesh, out float up, out float down, out float side)
        {
            up = down = side = 0f;
            var v = mesh.vertices;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var cross = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                float area = cross.magnitude * 0.5f;
                if (area <= 0f) continue;
                float ny = cross.y / (2f * area);
                if (ny > 0.5f) up += area;
                else if (ny < -0.5f) down += area;
                else side += area;
            }
        }

        public static float TotalArea(Mesh mesh)
        {
            MeasureAreas(mesh, out var up, out var down, out var side);
            return up + down + side;
        }

        public static SplineContainer GetSplines(Transform glyph) => glyph.GetComponent<SplineContainer>();
    }
}
