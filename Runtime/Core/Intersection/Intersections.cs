using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    public enum IntersectionCases
    {
        IsInside,
        IsOnEdge,
        NoIntersection
    }

    public static class Intersections
    {
        public static bool LineLine(Edge2 a, Edge2 b, bool includeEndPoints)
        {
            //Double precision and scale-independent tolerances: with a fixed tolerance on the denominator,
            //short (but clearly crossing) segments were treated as parallel, so constraint edges of dense
            //outlines could not be inserted and the whole glyph was removed afterwards.
            double a1x = a.p1.x, a1y = a.p1.y, a2x = a.p2.x, a2y = a.p2.y;
            double b1x = b.p1.x, b1y = b.p1.y, b2x = b.p2.x, b2y = b.p2.y;

            double denominator = (b2y - b1y) * (a2x - a1x) - (b2x - b1x) * (a2y - a1y);

            double lengthA = System.Math.Sqrt((a2x - a1x) * (a2x - a1x) + (a2y - a1y) * (a2y - a1y));
            double lengthB = System.Math.Sqrt((b2x - b1x) * (b2x - b1x) + (b2y - b1y) * (b2y - b1y));

            if (System.Math.Abs(denominator) <= MathUtility.PARALLEL_RELATIVE_EPSILON * lengthA * lengthB)
                return false; // Lines are parallel or coincident

            double uA = ((b2x - b1x) * (a1y - b1y) - (b2y - b1y) * (a1x - b1x)) / denominator;
            double uB = ((a2x - a1x) * (a1y - b1y) - (a2y - a1y) * (a1x - b1x)) / denominator;

            double epsilon = MathUtility.SEGMENT_PARAM_EPSILON;

            if (includeEndPoints)
            {
                return (uA >= -epsilon && uA <= 1.0 + epsilon) && (uB >= -epsilon && uB <= 1.0 + epsilon);
            }
            else
            {
                return (uA > epsilon && uA < 1.0 - epsilon) && (uB > epsilon && uB < 1.0 - epsilon);
            }
        }

        public static IntersectionCases PointCircle(Vector2 a, Vector2 b, Vector2 c, Vector2 testPoint)
        {
            Vector2 center = Geometry.CalculateCircleCenter(a, b, c);
            float radiusSqr = (a - center).sqrMagnitude;
            float distSqr = (testPoint - center).sqrMagnitude;

            //Relative to the circle size; a fixed tolerance made every small triangle "on the circle"
            float tolerance = MathUtility.CIRCLE_RELATIVE_EPSILON * radiusSqr;

            if (distSqr < radiusSqr - tolerance)
                return IntersectionCases.IsInside;

            if (distSqr > radiusSqr + tolerance)
                return IntersectionCases.NoIntersection;

            return IntersectionCases.IsOnEdge;
        }
    }
}
