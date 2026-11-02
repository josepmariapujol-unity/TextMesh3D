using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    //And edge between two vertices in 2d space
    public class Edge2
    {
        public Vector2 p1;
        public Vector2 p2;

        public Edge2(Vector2 p1, Vector2 p2)
        {
            this.p1 = p1;
            this.p2 = p2;
        }
    }
}
