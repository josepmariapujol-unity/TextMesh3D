using System.Collections.Generic;
using System.IO;
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

        public static string FontPath(string fileName) => PackageRoot + "/Editor/Resources/Fonts/" + fileName;

        public static Font LoadFont(string assetPath)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
            Assert.IsNotNull(font, $"Test font missing at {assetPath}");
            return font;
        }

        public static Font LoadRoboto() => LoadFont(RobotoPath);

        /// <summary>Parses the font file directly, independent of the code under test, to get reference metrics.</summary>
        public static Typography.OpenFont.Typeface LoadTypeface(string assetPath)
        {
            using (var stream = File.OpenRead(Path.GetFullPath(assetPath)))
                return new Typography.OpenFont.OpenFontReader().Read(stream);
        }

        public static TextMesh3DContainer CreateContainer(
            string text,
            TextMesh3DContainer.OutputMode mode,
            float extrusion = 1f,
            float size = 1f,
            float spacing = 0f,
            Font font = null,
            int curveSegments = 3)
        {
            var go = new GameObject("TextMesh3D Test");
            var container = go.AddComponent<TextMesh3DContainer>();
            container.CurveSegments = curveSegments;
            container.Text = text;
            container.Font = font != null ? font : LoadRoboto();
            container.Output = mode;
            container.Extrusion = extrusion;
            container.Size = size;
            container.Spacing = spacing;
            return container;
        }

        /// <summary>Generates and returns the glyph objects (children of the generated root), in text order.</summary>
        public static List<Transform> Generate(TextMesh3DContainer container)
        {
            container.Generate();
            Assert.AreEqual(1, container.transform.childCount, "Expected exactly one generated root under the container.");
            var root = container.transform.GetChild(0);
            var glyphs = new List<Transform>();
            foreach (Transform child in root) glyphs.Add(child);
            Assert.AreEqual(container.Text.Length, glyphs.Count, "Expected one glyph object per character.");
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
            // Accumulate in double so the sums don't depend on triangle order (keeps snapshots stable).
            double sumUp = 0, sumDown = 0, sumSide = 0;
            var v = mesh.vertices;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var cross = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                float area = cross.magnitude * 0.5f;
                if (area <= 0f) continue;
                float ny = cross.y / (2f * area);
                if (ny > 0.5f) sumUp += area;
                else if (ny < -0.5f) sumDown += area;
                else sumSide += area;
            }
            up = (float)sumUp;
            down = (float)sumDown;
            side = (float)sumSide;
        }

        /// <summary>
        /// Euler characteristic V - E + F of the up-facing triangles (vertices welded by position).
        /// For a correctly filled flat glyph it equals (number of outlines) - (number of holes):
        /// "H" = 1, "O" = 0, "B" = -1, "i" = 2, "%" = 1. A filled-in or misplaced hole changes it.
        /// </summary>
        public static int UpFacingEulerCharacteristic(Mesh mesh)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            var vertexIds = new Dictionary<Vector3, int>();
            var edges = new HashSet<(int, int)>();
            int faces = 0;

            int Id(Vector3 p)
            {
                if (!vertexIds.TryGetValue(p, out var id)) vertexIds[p] = id = vertexIds.Count;
                return id;
            }

            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (Vector3.Cross(b - a, c - a).y <= 0f) continue;

                int ia = Id(a), ib = Id(b), ic = Id(c);
                edges.Add(ia < ib ? (ia, ib) : (ib, ia));
                edges.Add(ib < ic ? (ib, ic) : (ic, ib));
                edges.Add(ic < ia ? (ic, ia) : (ia, ic));
                faces++;
            }
            return vertexIds.Count - edges.Count + faces;
        }

        public static float TotalArea(Mesh mesh)
        {
            MeasureAreas(mesh, out var up, out var down, out var side);
            return up + down + side;
        }

        public static SplineContainer GetSplines(Transform glyph) => glyph.GetComponent<SplineContainer>();
    }
}
