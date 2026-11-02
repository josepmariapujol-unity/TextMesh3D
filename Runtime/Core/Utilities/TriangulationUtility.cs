using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Softgraph.TextMesh3D.Runtime
{
    //Standardized methods that are the same for all
    public static class TriangulationUtility
    {
        public static bool IsClockWiseSpline(Spline spline)
        {
            float area = 0;
            int length = spline.Count;
            for (int i = 0; i < spline.Count; i++)
            {
                Vector3 va = spline[i].Position;
                Vector3 vb = spline[GetIndex(length, i + 1)].Position;
                float width = vb.x - va.x;
                float height = (vb.z + va.z) / 2f;
                area += width * height;
            }

            if (0 < area)
            {
                return true;
            }
            return false;
        }
        
        public static int GetIndex(int indexLength, int index)
        {
            if (index >= indexLength)
            {
                return 0;
            }
            return index;
        }
    }
}    

