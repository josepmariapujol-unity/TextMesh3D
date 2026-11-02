using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    public struct Triangle2
    {
        public Vector2 p1;
        public Vector2 p2;
        public Vector2 p3;

        public Triangle2(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            this.p1 = p1;
            this.p2 = p2;
            this.p3 = p3;
        }

        public void ChangeOrientation()
        {
            //Swap two vertices
            (p1, p2) = (p2, p1);
        }
    }

    public struct Triangle3
    {
        //Corners
        public Vector3 p1;
        public Vector3 p2;
        public Vector3 p3;

        public Triangle3(Vector3 p1, Vector3 p2, Vector3 p3)
        {
            this.p1 = p1;
            this.p2 = p2;
            this.p3 = p3;
        }
    }
}
