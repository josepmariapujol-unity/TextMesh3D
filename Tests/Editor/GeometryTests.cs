using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Softgraph.TextMesh3D.Runtime;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>Unit tests for the low-level geometry / triangulation code the mesh generator is built on.</summary>
    class GeometryTests
    {
        const float k_Eps = 1e-4f;

        // ---------- Geometry ----------

        [Test]
        public void IsTriangleOrientedClockwise_DetectsBothWindings()
        {
            Assert.IsTrue(Geometry.IsTriangleOrientedClockwise(new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0)));
            Assert.IsFalse(Geometry.IsTriangleOrientedClockwise(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1)));
        }

        [Test]
        public void IsPointLeftOnRightOfVector_ClassifiesPoints()
        {
            var a = new Vector2(0, 0);
            var b = new Vector2(1, 0);
            Assert.AreEqual(LeftOnRight.Left, Geometry.IsPointLeftOnRightOfVector(a, b, new Vector2(0, 1)));
            Assert.AreEqual(LeftOnRight.Right, Geometry.IsPointLeftOnRightOfVector(a, b, new Vector2(0, -1)));
            Assert.AreEqual(LeftOnRight.On, Geometry.IsPointLeftOnRightOfVector(a, b, new Vector2(0.5f, 0)));
        }

        // Regression: the "on the line" tolerance used to be absolute, so on short edges points that were clearly
        // to one side were classified as On. Dense outlines (Garamond "H") then triangulated into nothing.
        [TestCase(1f)]
        [TestCase(1e-3f)]
        [TestCase(1e-4f)]
        public void IsPointLeftOnRightOfVector_IsScaleIndependent(float scale)
        {
            var a = new Vector2(0, 0) * scale;
            var b = new Vector2(1, 0) * scale;
            Assert.AreEqual(LeftOnRight.Left, Geometry.IsPointLeftOnRightOfVector(a, b, new Vector2(0.5f, 0.1f) * scale));
            Assert.AreEqual(LeftOnRight.Right, Geometry.IsPointLeftOnRightOfVector(a, b, new Vector2(0.5f, -0.1f) * scale));
            Assert.AreEqual(LeftOnRight.On, Geometry.IsPointLeftOnRightOfVector(a, b, new Vector2(0.5f, 0f) * scale));
        }

        // Regression: crossing segments with a small cross product were treated as parallel.
        [TestCase(1f)]
        [TestCase(1e-3f)]
        [TestCase(1e-4f)]
        public void LineLine_DetectsCrossingOfShortSegments(float scale)
        {
            var a = new Edge2(new Vector2(0, 0) * scale, new Vector2(1, 1) * scale);
            var b = new Edge2(new Vector2(0, 1) * scale, new Vector2(1, 0) * scale);
            Assert.IsTrue(Intersections.LineLine(a, b, includeEndPoints: false));

            var parallel = new Edge2(new Vector2(0, 0.5f) * scale, new Vector2(1, 1.5f) * scale);
            Assert.IsFalse(Intersections.LineLine(a, parallel, includeEndPoints: false));
        }

        [TestCase(1f)]
        [TestCase(1e-3f)]
        public void PointCircle_IsScaleIndependent(float scale)
        {
            Vector2 a = new Vector2(0, 0) * scale, b = new Vector2(2, 0) * scale, c = new Vector2(0, 2) * scale; // center (1,1), r = 1.414
            Assert.AreEqual(IntersectionCases.IsInside, Intersections.PointCircle(a, b, c, new Vector2(1, 1.2f) * scale));
            Assert.AreEqual(IntersectionCases.NoIntersection, Intersections.PointCircle(a, b, c, new Vector2(1, 2.6f) * scale));
        }

        [Test]
        public void CalculateCircleCenter_IsEquidistantFromVertices([Values(false, true)] bool reverseWinding)
        {
            Vector2 a = new Vector2(0, 0), b = new Vector2(2, 0), c = new Vector2(0, 2);
            if (reverseWinding) (a, b) = (b, a);

            var center = Geometry.CalculateCircleCenter(a, b, c);

            Assert.AreEqual(1f, center.x, k_Eps);
            Assert.AreEqual(1f, center.y, k_Eps);
        }

        // ---------- Splines (glyph contours live in the XZ plane) ----------

        static Spline Polygon(bool clockwise)
        {
            var pts = new[] { new float3(0, 0, 0), new float3(1, 0, 0), new float3(1, 0, 1), new float3(0, 0, 1) }; // CCW seen from +Y
            if (clockwise) pts = pts.Reverse().ToArray();
            var spline = new Spline();
            foreach (var p in pts) spline.Add(new BezierKnot(p), TangentMode.Broken);
            spline.Closed = true;
            return spline;
        }

        [Test]
        public void IsClockWiseSpline_DetectsWinding()
        {
            Assert.IsFalse(TriangulationUtility.IsClockWiseSpline(Polygon(clockwise: false)));
            Assert.IsTrue(TriangulationUtility.IsClockWiseSpline(Polygon(clockwise: true)));
        }

        [Test]
        public void GetIndex_WrapsAtEnd()
        {
            Assert.AreEqual(0, TriangulationUtility.GetIndex(4, 4));
            Assert.AreEqual(3, TriangulationUtility.GetIndex(4, 3));
        }

        // ---------- Normalizer ----------

        [Test]
        public void Normalizer_MapsIntoUnitSquare_AndRoundTrips()
        {
            var points = new List<Vector2> { new Vector2(-10, 5), new Vector2(30, 5), new Vector2(10, 25) };
            var normalizer = new Normalizer2(points);

            var normalized = normalizer.Normalize(points);
            foreach (var p in normalized)
            {
                Assert.That(p.x, Is.InRange(-k_Eps, 1 + k_Eps));
                Assert.That(p.y, Is.InRange(-k_Eps, 1 + k_Eps));
            }

            for (int i = 0; i < points.Count; i++)
            {
                var back = normalizer.UnNormalize(normalized[i]);
                Assert.AreEqual(points[i].x, back.x, k_Eps);
                Assert.AreEqual(points[i].y, back.y, k_Eps);
            }
        }

        // ---------- Constrained Delaunay ----------

        static List<Vector2> Square(float min, float max, bool clockwise)
        {
            var pts = new List<Vector2> { new Vector2(min, min), new Vector2(max, min), new Vector2(max, max), new Vector2(min, max) }; // CCW
            if (clockwise) pts.Reverse();
            return pts;
        }

        static float Area(HashSet<Triangle2> triangles) =>
            triangles.Sum(t => Mathf.Abs((t.p2.x - t.p1.x) * (t.p3.y - t.p1.y) - (t.p3.x - t.p1.x) * (t.p2.y - t.p1.y)) * 0.5f);

        [Test]
        public void ConstrainedDelaunay_Square_ProducesTwoTrianglesCoveringArea()
        {
            var data = Delaunay.ConstrainedBySloan(null, Square(0, 1, clockwise: false), new HashSet<List<Vector2>>(), true, new HalfEdgeData2());
            var triangles = TransformBetweenDataStructures.HalfEdge2ToTriangle2(data);

            Assert.AreEqual(2, triangles.Count);
            Assert.AreEqual(1f, Area(triangles), k_Eps);
        }

        [Test]
        public void ConstrainedDelaunay_SquareWithHole_RemovesHoleTriangles()
        {
            var holes = new HashSet<List<Vector2>> { Square(0.25f, 0.75f, clockwise: true) };
            var data = Delaunay.ConstrainedBySloan(null, Square(0, 1, clockwise: false), holes, true, new HalfEdgeData2());
            var triangles = TransformBetweenDataStructures.HalfEdge2ToTriangle2(data);

            // Polygon with n=8 vertices and h=1 hole triangulates into n + 2h - 2 = 8 triangles.
            Assert.AreEqual(8, triangles.Count);
            Assert.AreEqual(0.75f, Area(triangles), k_Eps);
        }

        [Test]
        public void OrientTrianglesClockwise_MakesAllClockwise()
        {
            var input = new HashSet<Triangle2>
            {
                new Triangle2(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1)), // CCW
                new Triangle2(new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0)), // CW
            };

            foreach (var t in HelpMethods.OrientTrianglesClockwise(input))
                Assert.IsTrue(Geometry.IsTriangleOrientedClockwise(t.p1, t.p2, t.p3));
        }

        // ---------- GlyphSplineReader ----------

        static SplineContainer NewEmptyContainer(out GameObject go)
        {
            go = new GameObject("GlyphSplineReader Test");
            var container = go.AddComponent<SplineContainer>();
            container.RemoveSplineAt(0); // same as TextMesh3DContainer does
            return container;
        }

        [TestCase(false, TestName = "GlyphSplineReader_SquareContour_WithoutClosingLine")]
        [TestCase(true, TestName = "GlyphSplineReader_SquareContour_WithClosingLine")]
        public void GlyphSplineReader_SquareContour(bool explicitClosingLine)
        {
            var container = NewEmptyContainer(out var go);
            try
            {
                var reader = new GlyphSplineReader(container);
                reader.BeginRead(1);
                reader.MoveTo(0, 0);
                reader.LineTo(1, 0);
                reader.LineTo(1, 1);
                reader.LineTo(0, 1);
                if (explicitClosingLine) reader.LineTo(0, 0); // must be merged into the first knot, not duplicated
                reader.CloseContour();
                reader.EndRead();

                Assert.AreEqual(1, container.Splines.Count);
                var spline = container.Splines[0];
                Assert.IsTrue(spline.Closed);
                Assert.AreEqual(4, spline.Count);
                // Font Y maps to Unity Z; contours lie flat on the XZ plane.
                Assert.AreEqual(new float3(1, 0, 1), spline[2].Position);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void GlyphSplineReader_QuadraticCurve_SetsTangent()
        {
            var container = NewEmptyContainer(out var go);
            try
            {
                var reader = new GlyphSplineReader(container);
                reader.BeginRead(1);
                reader.MoveTo(0, 0);
                reader.Curve3(0.5f, 1f, 1f, 0f);
                reader.CloseContour();
                reader.EndRead();

                var spline = container.Splines[0];
                Assert.AreEqual(2, spline.Count);
                Assert.AreEqual(new float3(0.5f, 0, 1f), spline[0].TangentOut);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void GlyphSplineReader_MultipleContours_CreatesOneSplineEach()
        {
            var container = NewEmptyContainer(out var go);
            try
            {
                var reader = new GlyphSplineReader(container);
                reader.BeginRead(2);
                reader.MoveTo(0, 0); reader.LineTo(4, 0); reader.LineTo(4, 4); reader.LineTo(0, 4); reader.CloseContour();
                reader.MoveTo(1, 1); reader.LineTo(1, 3); reader.LineTo(3, 3); reader.LineTo(3, 1); reader.CloseContour();
                reader.EndRead();

                Assert.AreEqual(2, container.Splines.Count);
                Assert.IsFalse(TriangulationUtility.IsClockWiseSpline(container.Splines[0]));
                Assert.IsTrue(TriangulationUtility.IsClockWiseSpline(container.Splines[1]));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
