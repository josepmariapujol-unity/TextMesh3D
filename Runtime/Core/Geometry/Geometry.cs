using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    //Help enum in case we need to return something else than a bool
    public enum LeftOnRight
    {
        Left, On, Right
    }

    public static class Geometry
    {
        //
        // Calculate the center of circle in 2d space given three coordinates
        //
        //From the book "Geometric Tools for Computer Graphics"
        public static Vector2 CalculateCircleCenter(Vector2 a, Vector2 b, Vector2 c)
        {
            //Make sure the triangle a-b-c is counterclockwise
            if (!IsTriangleOrientedClockwise(a, b, c))
            {
                //Swap two vertices to change orientation
                (a, b) = (b, a);
            }

            //The area of the triangle
            float X_1 = b.x - a.x;
            float X_2 = c.x - a.x;
            float Y_1 = b.y - a.y;
            float Y_2 = c.y - a.y;
            float A = 0.5f * MathUtility.Det2(X_1, Y_1, X_2, Y_2);

            float l10Square = Vector2.SqrMagnitude(b - a);
            float l20Square = Vector2.SqrMagnitude(c - a);
            float one_divided_by_4A = 1f / (4f * A);

            float x = a.x + one_divided_by_4A * ((Y_2 * l10Square) - (Y_1 * l20Square));
            float y = a.y + one_divided_by_4A * ((X_1 * l20Square) - (X_2 * l10Square));
            Vector2 center = new Vector2(x, y);

            return center;
        }

        //
        // Is a triangle in 2d space oriented clockwise or counter-clockwise
        //
        //https://math.stackexchange.com/questions/1324179/how-to-tell-if-3-connected-points-are-connected-clockwise-or-counter-clockwise
        //https://en.wikipedia.org/wiki/Curve_orientation
        public static bool IsTriangleOrientedClockwise(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            bool isClockWise = true;
            float determinant = p1.x * p2.y + p3.x * p1.y + p2.x * p3.y - p1.x * p3.y - p3.x * p2.y - p2.x * p1.y;

            if (determinant > 0f)
            {
                isClockWise = false;
            }

            return isClockWise;
        }

        //
        // Does a point p lie to the left, to the right, or on a vector going from a to b
        //
        //Same as above but we want to figure out if we are on the vector
        //Use this if we might en up on the line, which has a low probability in a game, but may happen in some cases
        //Where is c in relation to a-b
        public static LeftOnRight IsPointLeftOnRightOfVector(Vector2 a, Vector2 b, Vector2 p)
        {
            //det(a - p, b - p) in double precision
            double x1 = (double)a.x - p.x;
            double x2 = (double)a.y - p.y;
            double y1 = (double)b.x - p.x;
            double y2 = (double)b.y - p.y;
            double relationValue = x1 * y2 - y1 * x2;

            //The determinant equals |a-b| * distance(p, line), so compare against a tolerance proportional to |a-b|.
            //A fixed tolerance made short edges "swallow" nearby points, which produced inverted triangles
            //and empty meshes for fonts with dense outlines (e.g. Garamond "H").
            double dx = (double)b.x - a.x;
            double dy = (double)b.y - a.y;
            double epsilon = MathUtility.SIDE_RELATIVE_EPSILON * System.Math.Sqrt(dx * dx + dy * dy);

            //To the right
            if (relationValue < -epsilon)
            {
                return LeftOnRight.Right;
            }
            //To the left
            if (relationValue > epsilon)
            {
                return LeftOnRight.Left;
            }
            //= 0 -> on the line
            return LeftOnRight.On;
        }

        //
        // Is a quadrilateral convex? Assume no 3 points are colinear and the shape doesnt look like an hourglass
        //
        //A quadrilateral is a polygon with four edges (or sides) and four vertices or corners
        public static bool IsQuadrilateralConvex(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            bool isConvex = false;

            //Convex if the convex hull includes all 4 points - will require just 4 determinant operations
            //In this case we dont kneed to know the order of the points, which is better
            //We could split it up into triangles, but still messy because of interior/exterior angles
            //Another version is if we know the edge between the triangles that form a quadrilateral
            //then we could measure the 4 angles of the edge, add them together (2 and 2) to get the interior angle
            //But it will still require 8 magnitude operations which is slow
            //From: https://stackoverflow.com/questions/2122305/convex-hull-of-4-points
            bool abc = IsTriangleOrientedClockwise(a, b, c);
            bool abd = IsTriangleOrientedClockwise(a, b, d);
            bool bcd = IsTriangleOrientedClockwise(b, c, d);
            bool cad = IsTriangleOrientedClockwise(c, a, d);

            if (abc && abd && bcd & !cad)
            {
                isConvex = true;
            }
            else if (abc && abd && !bcd & cad)
            {
                isConvex = true;
            }
            else if (abc && !abd && bcd & cad)
            {
                isConvex = true;
            }
            //The opposite sign, which makes everything inverted
            else if (!abc && !abd && !bcd & cad)
            {
                isConvex = true;
            }
            else if (!abc && !abd && bcd & !cad)
            {
                isConvex = true;
            }
            else if (!abc && abd && !bcd & !cad)
            {
                isConvex = true;
            }

            return isConvex;
        }
    }
}
