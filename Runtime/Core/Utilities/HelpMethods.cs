using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    /// <summary>
    /// Utility methods shared across TextMesh3D components.
    /// </summary>
    public static class HelpMethods
    {
        /// <summary>
        /// Ensures all triangles in the set are oriented clockwise.
        /// If a triangle is not oriented clockwise, its winding order will be corrected.
        /// </summary>
        /// <param name="triangles">A hash set of 2D triangles (Triangle2).</param>
        /// <returns>A new hash set where all triangles are oriented clockwise.</returns>
        public static HashSet<Triangle2> OrientTrianglesClockwise(HashSet<Triangle2> triangles)
        {
            var corrected = new HashSet<Triangle2>();

            foreach (var triangle in triangles)
            {
                var t = triangle;

                if (!Geometry.IsTriangleOrientedClockwise(t.p1, t.p2, t.p3))
                    t.ChangeOrientation();

                corrected.Add(t);
            }

            return corrected;
        }
    }
}

/*
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
//Standardized methods that are the same for all
    public static class HelpMethods
    {
        //
        // Orient triangles so they have the correct orientation
        //
        public static HashSet<Triangle2> OrientTrianglesClockwise(HashSet<Triangle2> triangles)
        {
            //Convert to list or we will no be able to update the orientation
            List<Triangle2> trianglesList = new List<Triangle2>(triangles);

            for (int i = 0; i < trianglesList.Count; i++)
            {
                Triangle2 triangle = trianglesList[i];

                if (!Geometry.IsTriangleOrientedClockwise(triangle.p1, triangle.p2, triangle.p3))
                {
                    triangle.ChangeOrientation();
                    trianglesList[i] = triangle;
                }
            }

            //Back to hashset
            triangles = new HashSet<Triangle2>(trianglesList);

            return triangles;
        }
    }
}
*/
