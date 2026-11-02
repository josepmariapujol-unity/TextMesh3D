using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    //Axis-Aligned-Bounding-Box, which is a rectangle in 2d space aligned along the x-y axis
    public struct AABB2
    {
        public Vector2 min;
        public Vector2 max;

        //We have a list with points and want to find the min and max values
        public AABB2(List<Vector2> points)
        {
            Vector2 p1 = points[0];

            float minX = p1.x;
            float maxX = p1.x;
            float minY = p1.y;
            float maxY = p1.y;

            for (int i = 1; i < points.Count; i++)
            {
                Vector2 p = points[i];

                if (p.x < minX)
                {
                    minX = p.x;
                }
                else if (p.x > maxX)
                {
                    maxX = p.x;
                }

                if (p.y < minY)
                {
                    minY = p.y;
                }
                else if (p.y > maxY)
                {
                    maxY = p.y;
                }
            }

            min = new Vector2(minX, minY);
            max = new Vector2(maxX, maxY);
        }
    }
}
