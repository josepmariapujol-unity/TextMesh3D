using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    /// <summary>
    /// Static entry point for generating Delaunay and Constrained Delaunay triangulations.
    /// </summary>
    public static class Delaunay
    {
        /// <summary>
        /// Delaunay Triangulation (Incremental): Performs an incremental Delaunay triangulation using Sloan's algorithm.
        /// </summary>
        /// <param name="points">Set of 2D points to triangulate.</param>
        /// <param name="triangleData">Half-edge data structure to populate or reuse.</param>
        /// <returns>The updated HalfEdgeData2 structure.</returns>
        public static HalfEdgeData2 PointByPoint(HashSet<Vector2> points, HalfEdgeData2 triangleData)
        {
            return DelaunayIncrementalSloan.GenerateTriangulation(points, triangleData);
        }
        
        /// <summary>
        /// Constrained Delaunay Triangulation: Generates a constrained Delaunay triangulation using Sloan's method.
        /// 
        /// <para>Constraints:</para>
        /// - <paramref name="hull"/> must be counter-clockwise.
        /// - <paramref name="holes"/> must be clockwise.
        /// </summary>
        /// <param name="points">All sites (vertices).</param>
        /// <param name="hull">Outer boundary (CCW).</param>
        /// <param name="holes">Set of inner holes (CW).</param>
        /// <param name="shouldRemoveTriangles">If true, trims triangles outside hull or inside holes.</param>
        /// <param name="triangleData">Half-edge data structure to populate.</param>
        /// <returns>The updated HalfEdgeData2 structure.</returns>
        public static HalfEdgeData2 ConstrainedBySloan(
            HashSet<Vector2> points,
            List<Vector2> hull,
            HashSet<List<Vector2>> holes,
            bool shouldRemoveTriangles,
            HalfEdgeData2 triangleData)
        {
            ConstrainedDelaunaySloan.GenerateTriangulation(points, hull, holes, shouldRemoveTriangles, triangleData);
            return triangleData;
        }
    }
}