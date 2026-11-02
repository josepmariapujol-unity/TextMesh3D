using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    public static class PointTriangulationIntersection
    {
        public static HalfEdgeFace2 TriangulationWalk(
            Vector2 p,
            HalfEdgeFace2 startTriangle,
            HalfEdgeData2 triangulationData,
            List<HalfEdgeFace2> visitedTriangles = null)
        {
            HalfEdgeFace2 currentTriangle = startTriangle ?? GetRandomTriangle(triangulationData);
            if (currentTriangle == null)
            {
                Debug.LogWarning("Couldn't find start triangle when walking in triangulation.");
                return null;
            }

            visitedTriangles?.Add(currentTriangle);

            const int maxIterations = 1000000;
            for (int safety = 0; safety < maxIterations; safety++)
            {
                HalfEdge2 e1 = currentTriangle.edge;
                HalfEdge2 e2 = e1.nextEdge;
                HalfEdge2 e3 = e2.nextEdge;

                if (IsPointToTheRightOrOnLine(e1.prevEdge.v.position, e1.v.position, p))
                {
                    if (IsPointToTheRightOrOnLine(e2.prevEdge.v.position, e2.v.position, p))
                    {
                        if (IsPointToTheRightOrOnLine(e3.prevEdge.v.position, e3.v.position, p))
                        {
                            visitedTriangles?.Add(currentTriangle);
                            return currentTriangle;
                        }
                        currentTriangle = e3.oppositeEdge?.face;
                    }
                    else
                    {
                        currentTriangle = e2.oppositeEdge?.face;
                    }
                }
                else
                {
                    currentTriangle = e1.oppositeEdge?.face;
                }

                if (currentTriangle == null)
                {
                    Debug.LogWarning("Point walk exited mesh bounds.");
                    return null;
                }

                visitedTriangles?.Add(currentTriangle);
            }

            Debug.LogError("Stuck in endless loop when walking in triangulation.");
            return null;
        }

        private static HalfEdgeFace2 GetRandomTriangle(HalfEdgeData2 data)
        {
            int count = data.faces.Count;
            if (count == 0) return null;

            int index = Random.Range(0, count);
            int i = 0;
            foreach (var face in data.faces)
            {
                if (i++ == index)
                    return face;
            }
            return null;
        }

        private static bool IsPointToTheRightOrOnLine(Vector2 a, Vector2 b, Vector2 p)
        {
            var pos = Geometry.IsPointLeftOnRightOfVector(a, b, p);
            return pos == LeftOnRight.Right || pos == LeftOnRight.On;
        }
    }
}
