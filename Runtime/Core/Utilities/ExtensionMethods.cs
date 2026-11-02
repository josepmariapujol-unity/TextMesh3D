using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    public static class ExtensionMethods
    {
        public static Vector2 ToVector2(this Vector3 v)
        {
            return new Vector2(v.x, v.z);
        }

        public static Vector3 ToVector3_Yis3D(this Vector2 v, float yPos = 0f)
        {
            return new Vector3(v.x, yPos, v.y);
        }
    }
}
