using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Softgraph.TextMesh3D.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using static Softgraph.TextMesh3D.Tests.TextMesh3DTestUtility;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>Plane (XZ / XY) and Line Spacing.</summary>
    class TextMesh3DLayoutOptionsTests
    {
        const float k_Eps = 1e-3f;

        readonly List<TextMesh3DContainer> m_Created = new List<TextMesh3DContainer>();

        TextMesh3DContainer Create(string text, TextMesh3DContainer.OutputMode mode, TextMesh3DContainer.TextPlane plane, float extrusion = 0.5f)
        {
            var c = CreateContainer(text, mode, extrusion);
            c.Plane = plane;
            m_Created.Add(c);
            return c;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in m_Created) TextMesh3DTestUtility.Destroy(c);
            m_Created.Clear();
        }

        /// <summary>Bounds of all generated meshes (per glyph or grouped) in the container's local space.</summary>
        static Bounds LocalBounds(TextMesh3DContainer container)
        {
            var toLocal = container.transform.worldToLocalMatrix;
            bool any = false;
            var bounds = new Bounds();
            foreach (var mf in container.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = toLocal * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            Assert.IsTrue(any, "No geometry generated");
            return bounds;
        }

        // ---------- Plane ----------

        [Test]
        public void Plane_DefaultsToXZ()
        {
            var go = new GameObject("Default");
            try
            {
                var c = go.AddComponent<TextMesh3DContainer>();
                Assert.AreEqual(TextMesh3DContainer.TextPlane.XZ, c.Plane);
                Assert.AreEqual(1f, c.LineSpacing);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Plane_XY_StandsUp_FrontAtZeroDepthBackAlongPlusZ(bool group)
        {
            const float extrusion = 0.5f;
            var flat = Create("Hg", TextMesh3DContainer.OutputMode.Mesh, TextMesh3DContainer.TextPlane.XZ, extrusion);
            var upright = Create("Hg", TextMesh3DContainer.OutputMode.Mesh, TextMesh3DContainer.TextPlane.XY, extrusion);
            flat.GroupOutput = group;
            upright.GroupOutput = group;
            flat.Generate();
            upright.Generate();

            var f = LocalBounds(flat);
            var u = LocalBounds(upright);

            // Same layout along X; glyph height moves from Z to Y; extrusion moves from Y to Z.
            Assert.AreEqual(f.min.x, u.min.x, k_Eps);
            Assert.AreEqual(f.max.x, u.max.x, k_Eps);
            Assert.AreEqual(f.min.z, u.min.y, k_Eps, "Descender should go down -Y");
            Assert.AreEqual(f.max.z, u.max.y, k_Eps, "Ascender should go up +Y");
            Assert.AreEqual(0f, u.min.z, k_Eps, "Front face should sit at z = 0");
            Assert.AreEqual(extrusion, u.max.z, k_Eps, "Depth should go back along +Z");
        }

        [Test]
        public void Plane_XY_FrontFaceNormalsPointTowardsMinusZ()
        {
            var c = Create("H", TextMesh3DContainer.OutputMode.Mesh, TextMesh3DContainer.TextPlane.XY);
            c.GroupOutput = true;
            c.Generate();
            var mesh = c.GetComponent<MeshFilter>().sharedMesh;

            var v = mesh.vertices;
            var n = mesh.normals;
            var t = mesh.triangles;
            double frontArea = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], d = v[t[i + 2]];
                if (Mathf.Abs(a.z) > k_Eps || Mathf.Abs(b.z) > k_Eps || Mathf.Abs(d.z) > k_Eps) continue;

                var faceNormal = Vector3.Cross(b - a, d - a);
                Assert.Less(faceNormal.z, 0f, "Front cap triangles must face -Z (towards the default camera)");
                Assert.Less(n[t[i]].z, -0.5f, "Front cap vertex normals must point to -Z");
                frontArea += faceNormal.magnitude * 0.5;
            }
            Assert.Greater(frontArea, 0.0, "No front cap found at z = 0");
        }

        [Test]
        public void Plane_XY_SplinesLieOnXY()
        {
            var c = Create("B", TextMesh3DContainer.OutputMode.Spline, TextMesh3DContainer.TextPlane.XY);
            var glyphs = Generate(c);
            var splines = GetSplines(glyphs[0]);

            var toLocal = c.transform.worldToLocalMatrix * glyphs[0].localToWorldMatrix;
            float maxY = 0f;
            foreach (var s in splines.Splines)
            {
                for (int k = 0; k < s.Count; k++)
                {
                    var p = toLocal.MultiplyPoint3x4((Vector3)s[k].Position);
                    Assert.AreEqual(0f, p.z, k_Eps, "Spline contours should lie on the XY plane");
                    maxY = Mathf.Max(maxY, p.y);
                }
            }
            Assert.Greater(maxY, 0.5f, "Capital B should rise above the baseline along +Y");
        }

        [Test]
        public void Plane_Switching_RegeneratesCleanly()
        {
            var c = Create("AB", TextMesh3DContainer.OutputMode.Mesh, TextMesh3DContainer.TextPlane.XY);
            Generate(c);
            var xy = LocalBounds(c);

            c.Plane = TextMesh3DContainer.TextPlane.XZ;
            Generate(c);
            var xz = LocalBounds(c);

            Assert.AreEqual(Quaternion.identity, c.GeneratedRoot.transform.localRotation);
            Assert.AreEqual(Vector3.zero, c.GeneratedRoot.transform.localPosition);
            Assert.AreEqual(xy.size.y, xz.size.z, k_Eps);
            Assert.AreEqual(xy.size.z, xz.size.y, k_Eps);
        }

        // ---------- Line Spacing ----------

        [TestCase(2f)]
        [TestCase(0.5f)]
        [TestCase(0f)]
        public void LineSpacing_ScalesDistanceBetweenLines(float lineSpacing)
        {
            var reference = Create("A\nB", TextMesh3DContainer.OutputMode.Spline, TextMesh3DContainer.TextPlane.XZ);
            float defaultLine = Generate(reference)[2].localPosition.z;
            Assert.Less(defaultLine, 0f);

            var spaced = Create("A\nB", TextMesh3DContainer.OutputMode.Spline, TextMesh3DContainer.TextPlane.XZ);
            spaced.LineSpacing = lineSpacing;
            var glyphs = Generate(spaced);

            Assert.AreEqual(lineSpacing * defaultLine, glyphs[2].localPosition.z, k_Eps);
            Assert.AreEqual(0f, glyphs[2].localPosition.x, k_Eps);
        }

        [Test]
        public void LineSpacing_DoesNotAffectSingleLine()
        {
            var a = Create("AVATAR", TextMesh3DContainer.OutputMode.Spline, TextMesh3DContainer.TextPlane.XZ);
            var b = Create("AVATAR", TextMesh3DContainer.OutputMode.Spline, TextMesh3DContainer.TextPlane.XZ);
            b.LineSpacing = 3f;
            var ga = Generate(a);
            var gb = Generate(b);
            for (int i = 0; i < ga.Count; i++)
                Assert.AreEqual(ga[i].localPosition, gb[i].localPosition);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void LineSpacing_Invalid_LogsErrorAndKeepsOutput(float lineSpacing)
        {
            var c = Create("AB", TextMesh3DContainer.OutputMode.Mesh, TextMesh3DContainer.TextPlane.XZ);
            Generate(c);
            var before = c.GeneratedRoot;

            c.LineSpacing = lineSpacing;
            LogAssert.Expect(LogType.Error, new Regex("Line Spacing must be a finite number"));
            c.Generate();

            Assert.AreSame(before, c.GeneratedRoot, "Invalid settings must leave the previous output untouched");
        }
    }
}
