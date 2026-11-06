using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Typography.OpenFont;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Softgraph.TextMesh3D.Runtime
{
    // Meshing: outline classification (outlines vs holes), triangulation of caps, side walls, UVs and Group Output.
    public partial class TextMesh3DContainer
    {
        /// <summary>One closed outline of a glyph, flattened to a polygon on the XZ plane.</summary>
        private sealed class Contour
        {
            public Spline spline;
            public List<Vector3> points;   // sampled outline, XZ plane
            public float signedArea;       // shoelace area in (x, z); > 0 = counter-clockwise
            public bool isHole;
            public int parent = -1;        // for holes: index of the outline that contains it
        }

        private void BuildGlyphMeshes(bool extrude)
        {
            float height = extrude ? m_Extrusion : 0f;

            for (int i = 0; i < m_GlyphObjects.Length; i++)
            {
                var splineContainer = m_GlyphObjects[i].GetComponent<SplineContainer>();
                var contours = BuildContours(splineContainer);

                // Spaces, line breaks, tabs, empty glyphs: nothing to mesh
                if (contours.Count == 0)
                {
                    SafeDestroy(splineContainer);
                    continue;
                }

                m_GlyphObjects[i].AddComponent<MeshFilter>();
                // sharedMaterial: .material would instantiate (and leak) a copy in edit mode
                m_GlyphObjects[i].AddComponent<MeshRenderer>().sharedMaterial = m_Material;

                // Order: walls, then top caps, then bottom caps (same order as before the refactor).
                var walls = new List<Mesh>();
                var tops = new List<Mesh>();
                var bottoms = new List<Mesh>();

                if (extrude)
                {
                    foreach (var contour in contours)
                    {
                        walls.Add(BuildWalls(contour.points, height));
                    }
                }

                for (int c = 0; c < contours.Count; c++)
                {
                    if (contours[c].isHole) continue;

                    var holes = new List<Contour>();
                    foreach (var other in contours)
                    {
                        if (other.isHole && other.parent == c) holes.Add(other);
                    }

                    // A malformed outline must not abort the whole text: skip its caps (walls are kept) and report it.
                    try
                    {
                        BuildCaps(contours[c], holes, height, out var top, out var bottom);
                        if (top != null) tops.Add(top);
                        if (bottom != null) bottoms.Add(bottom);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"TextMesh3D: could not fill an outline of '{m_Text[i]}' ({m_GlyphObjects[i].name}): {e.Message}", m_GlyphObjects[i]);
                    }
                }

                var parts = new List<Mesh>(walls.Count + tops.Count + bottoms.Count);
                parts.AddRange(walls);
                parts.AddRange(tops);
                parts.AddRange(bottoms);

                var combined = Combine(parts, m_GlyphObjects[i].name, recalculateNormals: extrude);
                m_GlyphObjects[i].GetComponent<MeshFilter>().sharedMesh = combined;

                SafeDestroy(splineContainer);
            }

            if (m_GroupOutput)
            {
                GroupIntoRootMesh();
            }
        }

        /// <summary>
        /// Group Output: merges every glyph into one mesh on this object's MeshFilter / MeshRenderer and removes
        /// the per-glyph objects. One object and one draw call for the whole text.
        /// </summary>
        private void GroupIntoRootMesh()
        {
            var glyphMeshes = new List<Mesh>();
            var instances = new List<CombineInstance>();
            int vertexCount = 0;
            var plane = GetPlaneMatrix(); // the grouped mesh has no root object to carry the orientation

            for (int i = 0; i < m_GlyphObjects.Length; i++)
            {
                var filter = m_GlyphObjects[i] != null ? m_GlyphObjects[i].GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null) continue;

                glyphMeshes.Add(filter.sharedMesh);
                vertexCount += filter.sharedMesh.vertexCount;
                instances.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    transform = plane * Matrix4x4.Translate(m_GlyphObjects[i].transform.localPosition),
                });
            }

            var grouped = new Mesh { name = m_Text };
            if (vertexCount > 65535)
            {
                grouped.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            grouped.CombineMeshes(instances.ToArray(), true);

            var rootFilter = GetComponent<MeshFilter>();
            var rootRenderer = GetComponent<MeshRenderer>();
            if (rootFilter == null) rootFilter = gameObject.AddComponent<MeshFilter>();
            if (rootRenderer == null) rootRenderer = gameObject.AddComponent<MeshRenderer>();
            rootFilter.sharedMesh = grouped;
            rootRenderer.sharedMaterial = m_Material;

            // The per-glyph objects were created in this same call, so they can go without an undo record.
            foreach (var mesh in glyphMeshes) SafeDestroy(mesh);
            SafeDestroy(m_GeneratedRoot);
            m_GeneratedRoot = null;
            m_GlyphObjects = new GameObject[0];
        }

        /// <summary>
        /// Samples every spline into a polygon and classifies it by nesting depth: a contour inside an odd number
        /// of other contours is a hole. This does not depend on contour order or on the font's winding convention
        /// (some fonts list holes before the outline, which used to fill holes in e.g. Helvetica "B" and "8").
        /// Outlines are oriented counter-clockwise and holes clockwise, as the triangulator expects.
        /// </summary>
        private List<Contour> BuildContours(SplineContainer container)
        {
            var contours = new List<Contour>();
            foreach (var spline in container.Splines)
            {
                // Some fonts emit single-knot "contours"; they enclose nothing.
                if (spline.Count < 2) continue;

                var points = SampleSpline(spline);
                var area = SignedArea(points);
                if (points.Count < 3 || Mathf.Abs(area) <= float.Epsilon) continue;

                contours.Add(new Contour { spline = spline, points = points, signedArea = area });
            }

            for (int i = 0; i < contours.Count; i++)
            {
                int depth = 0;
                for (int j = 0; j < contours.Count; j++)
                {
                    if (i != j && IsPointInsidePolygon(contours[i].points[0], contours[j].points)) depth++;
                }
                contours[i].isHole = depth % 2 == 1;
            }

            // A hole belongs to the smallest outline that contains it.
            for (int i = 0; i < contours.Count; i++)
            {
                if (!contours[i].isHole) continue;

                float bestArea = float.MaxValue;
                for (int j = 0; j < contours.Count; j++)
                {
                    if (contours[j].isHole) continue;
                    float area = Mathf.Abs(contours[j].signedArea);
                    if (area < bestArea && IsPointInsidePolygon(contours[i].points[0], contours[j].points))
                    {
                        bestArea = area;
                        contours[i].parent = j;
                    }
                }
            }

            foreach (var contour in contours)
            {
                bool counterClockwise = contour.signedArea > 0f;
                if (counterClockwise == contour.isHole)
                {
                    ReverseSpline(contour.spline);
                    contour.points = SampleSpline(contour.spline);
                    contour.signedArea = SignedArea(contour.points);
                }
            }

            return contours;
        }

        /// <summary>Flattens a spline: straight segments keep their knot, curved segments get m_CurveSegments points.</summary>
        private List<Vector3> SampleSpline(Spline spline)
        {
            int knotCount = spline.Count;
            int segments = Mathf.Max(1, m_CurveSegments);
            var points = new List<Vector3>(knotCount * segments);

            for (int k = 0; k < knotCount; k++)
            {
                var knot = spline[k];
                var next = spline[TriangulationUtility.GetIndex(knotCount, k + 1)];

                bool isCurve = !knot.TangentOut.Equals(float3.zero) || !next.TangentIn.Equals(float3.zero);
                if (isCurve)
                {
                    var curve = new BezierCurve(knot, next);
                    for (int w = 0; w < segments; w++)
                    {
                        float t = (float)w / segments;
                        points.Add(CurveUtility.EvaluatePosition(curve, t));
                    }
                }
                else
                {
                    points.Add(knot.Position);
                }
            }
            return points;
        }

        /// <summary>Shoelace area on the XZ plane, treating (x, z) as 2D (x, y). Positive means counter-clockwise.</summary>
        private static float SignedArea(List<Vector3> points)
        {
            double area = 0;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                area += (double)points[j].x * points[i].z - (double)points[i].x * points[j].z;
            }
            return (float)(area * 0.5);
        }

        private static bool IsPointInsidePolygon(Vector3 point, List<Vector3> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.z > point.z) != (b.z > point.z) &&
                    point.x < (b.x - a.x) * (point.z - a.z) / (b.z - a.z) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static void ReverseSpline(Spline spline)
        {
            int count = spline.Count;
            var reversed = new BezierKnot[count];
            for (int z = 0; z < count; z++)
            {
                var source = spline[count - 1 - z];
                reversed[z] = new BezierKnot(source.Position, source.TangentOut, source.TangentIn, quaternion.identity);
            }
            for (int z = 0; z < count; z++)
            {
                spline[z] = reversed[z];
            }
        }

        // UVs are measured in em units, so a texture tiles the same way at every text size.
        private float UVScale => Mathf.Abs(m_Size) > 1e-6f ? 1f / m_Size : 1f;

        /// <summary>Side walls of one contour: a quad per edge, with its own vertices so edges stay sharp.</summary>
        private Mesh BuildWalls(List<Vector3> points, float height)
        {
            int n = points.Count;
            var up = new Vector3(0f, height, 0f);
            float uvScale = UVScale;

            var verts = new Vector3[n * 4];
            var uvs = new Vector2[n * 4];
            var tris = new int[n * 6];

            float distance = 0f;
            for (int k = 0; k < n; k++)
            {
                var a = points[k];
                var b = points[TriangulationUtility.GetIndex(n, k + 1)];
                float nextDistance = distance + Vector3.Distance(a, b);

                int index = k * 2;
                verts[index] = a;
                verts[index + 1] = b;
                verts[index + 2 * n] = a + up;
                verts[index + 1 + 2 * n] = b + up;

                // U runs along the outline, V up the extrusion.
                uvs[index] = new Vector2(distance * uvScale, 0f);
                uvs[index + 1] = new Vector2(nextDistance * uvScale, 0f);
                uvs[index + 2 * n] = new Vector2(distance * uvScale, height * uvScale);
                uvs[index + 1 + 2 * n] = new Vector2(nextDistance * uvScale, height * uvScale);

                int t = k * 6;
                tris[t + 0] = index;
                tris[t + 1] = index + 2 * n;
                tris[t + 2] = index + 2 * n + 1;
                tris[t + 3] = index;
                tris[t + 4] = index + 2 * n + 1;
                tris[t + 5] = index + 1;

                distance = nextDistance;
            }

            var mesh = new Mesh { vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>Triangulates one outline with its holes into a top cap (at y = height) and a bottom cap (y = 0).</summary>
        private void BuildCaps(Contour outline, List<Contour> holes, float height, out Mesh top, out Mesh bottom)
        {
            List<Vector2> hullPoints2D = outline.points.Select(p => p.ToVector2()).ToList();

            var allHolePoints2D = new HashSet<List<Vector2>>();
            foreach (var hole in holes)
            {
                allHolePoints2D.Add(hole.points.Select(p => p.ToVector2()).ToList());
            }

            //Normalize to range 0-1, using all points including the holes
            var allPoints = new List<Vector2>(hullPoints2D);
            foreach (var hole in allHolePoints2D) allPoints.AddRange(hole);

            var normalizer = new Normalizer2(allPoints);
            var hullNormalized = normalizer.Normalize(hullPoints2D);
            var holesNormalized = new HashSet<List<Vector2>>();
            foreach (var hole in allHolePoints2D) holesNormalized.Add(normalizer.Normalize(hole));

            //Constrained delaunay, then back to glyph space
            HalfEdgeData2 triangleDataNormalized = Delaunay.ConstrainedBySloan(null, hullNormalized, holesNormalized, shouldRemoveTriangles: true, new HalfEdgeData2());
            HalfEdgeData2 triangleData = normalizer.UnNormalize(triangleDataNormalized);
            HashSet<Triangle2> triangles2D = HelpMethods.OrientTrianglesClockwise(TransformBetweenDataStructures.HalfEdge2ToTriangle2(triangleData));

            var triangles3D = new HashSet<Triangle3>();
            foreach (Triangle2 t in triangles2D)
            {
                triangles3D.Add(new Triangle3(t.p1.ToVector3_Yis3D(), t.p2.ToVector3_Yis3D(), t.p3.ToVector3_Yis3D()));
            }

            Mesh front = TransformBetweenDataStructures.Triangle3ToCompressedMesh(triangles3D);
            var frontVertices = front.vertices;
            var frontTriangles = front.triangles;
            SafeDestroy(front);

            float uvScale = UVScale;
            var up = new Vector3(0f, height, 0f);
            var verticesTop = new Vector3[frontVertices.Length];
            var verticesBottom = new Vector3[frontVertices.Length];
            var uvs = new Vector2[frontVertices.Length];
            for (int k = 0; k < frontVertices.Length; k++)
            {
                verticesBottom[k] = frontVertices[k];
                verticesTop[k] = frontVertices[k] + up;
                uvs[k] = new Vector2(frontVertices[k].x, frontVertices[k].z) * uvScale; // planar mapping, em units
            }

            var trianglesBottom = new int[frontTriangles.Length];
            for (int k = 0; k < frontTriangles.Length; k += 3)
            {
                trianglesBottom[k + 0] = frontTriangles[k + 0];
                trianglesBottom[k + 1] = frontTriangles[k + 2];
                trianglesBottom[k + 2] = frontTriangles[k + 1];
            }

            top = new Mesh { vertices = verticesTop, uv = uvs, triangles = frontTriangles };
            top.RecalculateNormals();

            bottom = new Mesh { vertices = verticesBottom, uv = uvs, triangles = trianglesBottom };
            bottom.RecalculateNormals();
        }

        /// <summary>Merges the parts into one mesh with normals and tangents, and frees the temporary parts.</summary>
        private static Mesh Combine(List<Mesh> parts, string name, bool recalculateNormals)
        {
            var instances = new CombineInstance[parts.Count];
            int vertexCount = 0;
            for (int k = 0; k < parts.Count; k++)
            {
                instances[k].mesh = parts[k];
                instances[k].transform = Matrix4x4.identity;
                vertexCount += parts[k].vertexCount;
            }

            var combined = new Mesh { name = name };
            if (vertexCount > 65535)
            {
                combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            combined.CombineMeshes(instances, true);
            if (recalculateNormals)
            {
                combined.RecalculateNormals();
            }
            combined.RecalculateTangents();

            foreach (var part in parts) SafeDestroy(part);
            return combined;
        }
    }
}
