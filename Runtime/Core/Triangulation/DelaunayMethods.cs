using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    //Methods for all delaunay algorithms
    public static class DelaunayMethods
    {
        //Test if we should flip an edge
        //a, b, c belongs to the triangle and d is the point on the other triangle
        //a-c is the edge, which is important so we can flip it, by making the edge b-d
        public static bool ShouldFlipEdge(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            bool shouldFlipEdge = false;

            //Use the circle test to test if we need to flip this edge
            //We should flip if d is inside a circle formed by a, b, c
            IntersectionCases intersectionCases = Intersections.PointCircle(a, b, c, d);

            if (intersectionCases == IntersectionCases.IsInside)
            {
                //Are these the two triangles forming a convex quadrilateral? Otherwise the edge cant be flipped
                if (Geometry.IsQuadrilateralConvex(a, b, c, d))
                {
                    //If the new triangle after a flip is not better, then dont flip
                    //This will also stop the algorithm from ending up in an endless loop
                    IntersectionCases intersectionCases2 = Intersections.PointCircle(b, c, d, a);

                    if (intersectionCases2 == IntersectionCases.IsOnEdge || intersectionCases2 == IntersectionCases.IsInside)
                    {
                        shouldFlipEdge = false;
                    }
                    else
                    {
                        shouldFlipEdge = true;
                    }
                }
            }

            return shouldFlipEdge;
        }

        //From "A fast algorithm for generating constrained delaunay..."
        //Is numerically stable
        //v1, v2 should belong to the edge we ant to flip
        //v1, v2, v3 are counter-clockwise
        //Is also checking if the edge can be swapped
        public static bool ShouldFlipEdgeStable(Vector2 v1, Vector2 v2, Vector2 v3, Vector2 vp)
        {
            float x13 = v1.x - v3.x;
            float x_23 = v2.x - v3.x;
            float x_1p = v1.x - vp.x;
            float x_2p = v2.x - vp.x;

            float y_13 = v1.y - v3.y;
            float y_23 = v2.y - v3.y;
            float y_1p = v1.y - vp.y;
            float y_2p = v2.y - vp.y;

            float cosA = x13 * x_23 + y_13 * y_23;
            float cosB = x_2p * x_1p + y_2p * y_1p;

            if (cosA >= 0f && cosB >= 0f)
            {
                return false;
            }
            if (cosA < 0f && cosB < 0)
            {
                return true;
            }

            float sinAb = (x13 * y_23 - x_23 * y_13) * cosB + (x_2p * y_1p - x_1p * y_2p) * cosA;

            if (sinAb < 0)
            {
                return true;
            }

            return false;
        }
    }
}
