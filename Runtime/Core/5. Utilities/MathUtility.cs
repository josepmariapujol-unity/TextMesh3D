using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    public static class MathUtility
    {
        //The value we use to avoid floating point precision issues
        //http://sandervanrossen.blogspot.com/2009/12/realtime-csg-part-1.html
        //Unity has a built-in Mathf.Epsilon;
        //But it's better to use our own so we can test different values
        public const float EPSILON = 0.00001f;

        // Scale-independent tolerances for the triangulation predicates. Absolute tolerances on determinants
        // break down for dense outlines (many short edges), so these are relative to the lengths involved.

        /// <summary>A point is "on" a line if its distance to it is below this fraction of the segment length.</summary>
        public const double SIDE_RELATIVE_EPSILON = 1e-7;

        /// <summary>Two segments are parallel if |cross(dirA, dirB)| is below this fraction of |A|*|B|.</summary>
        public const double PARALLEL_RELATIVE_EPSILON = 1e-12;

        /// <summary>Tolerance on the segment parameters (0..1) when intersecting two segments.</summary>
        public const double SEGMENT_PARAM_EPSILON = 1e-7;

        /// <summary>A point is "on" a circumcircle if |dist² - r²| is below this fraction of r².</summary>
        public const float CIRCLE_RELATIVE_EPSILON = 1e-6f;

        //Clamp list indices
        //Will even work if index is larger/smaller than listSize, so can loop multiple times
        public static int ClampListIndex(int index, int listSize)
        {
            index = ((index % listSize) + listSize) % listSize;

            return index;
        }


        // Returns the determinant of the 2x2 matrix defined as
        // | x1 x2 |
        // | y1 y2 |
        //det(a_normalized, b_normalized) = sin(alpha) so it's similar to the dot product
        //Vector alignment dot det
        //Same:            1   0
        //Perpendicular:   0  -1
        //Opposite:       -1   0
        //Perpendicular:   0   1
        public static float Det2(float x1, float x2, float y1, float y2)
        {
            return x1 * y2 - y1 * x2;
        }


    }
}
