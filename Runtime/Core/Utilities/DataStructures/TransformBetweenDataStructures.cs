using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    public static class TransformBetweenDataStructures
    {
        public static HalfEdgeData2 Triangle2ToHalfEdge2(HashSet<Triangle2> triangles, HalfEdgeData2 data)
        {
            //Make sure the triangles have the same orientation, which is clockwise
            triangles = HelpMethods.OrientTrianglesClockwise(triangles);
            
            //Fill the data structure
            foreach (Triangle2 t in triangles)
            {
                HalfEdgeVertex2 v1 = new HalfEdgeVertex2(t.p1);
                HalfEdgeVertex2 v2 = new HalfEdgeVertex2(t.p2);
                HalfEdgeVertex2 v3 = new HalfEdgeVertex2(t.p3);

                //The vertices the edge points to
                HalfEdge2 he1 = new HalfEdge2(v1);
                HalfEdge2 he2 = new HalfEdge2(v2);
                HalfEdge2 he3 = new HalfEdge2(v3);

                he1.nextEdge = he2;
                he2.nextEdge = he3;
                he3.nextEdge = he1;

                he1.prevEdge = he3;
                he2.prevEdge = he1;
                he3.prevEdge = he2;

                //The vertex needs to know of an edge going from it
                v1.edge = he2;
                v2.edge = he3;
                v3.edge = he1;

                //The face the half-edge is connected to
                HalfEdgeFace2 face = new HalfEdgeFace2(he1);

                //Each edge needs to know of the face connected to this edge
                he1.face = face;
                he2.face = face;
                he3.face = face;
                
                //Add everything to the lists
                data.edges.Add(he1);
                data.edges.Add(he2);
                data.edges.Add(he3);

                data.faces.Add(face);

                data.vertices.Add(v1);
                data.vertices.Add(v2);
                data.vertices.Add(v3);
            }


            //Step 4. Find the half-edges going in the opposite direction of each edge we have 
            //Is there a faster way to do this because this is the bottleneck?
            foreach (HalfEdge2 e in data.edges)
            {
                HalfEdgeVertex2 goingToVertex = e.v;
                HalfEdgeVertex2 goingFromVertex = e.prevEdge.v;

                foreach (HalfEdge2 eOther in data.edges)
                {
                    //Dont compare with itself
                    if (e == eOther)
                    {
                        continue;
                    }

                    //Is this edge going between the vertices in the opposite direction
                    if (goingFromVertex.position.Equals(eOther.v.position) && goingToVertex.position.Equals(eOther.prevEdge.v.position))
                    {
                        e.oppositeEdge = eOther;

                        break;
                    }
                }
            }
            return data;
        }

        
        //
        // Half-edge to triangle if we know the half-edge consists of triangles
        //
        public static HashSet<Triangle2> HalfEdge2ToTriangle2(HalfEdgeData2 data)
        {
            if (data == null)
            {
                return null;
            }
        
            HashSet<Triangle2> triangles = new HashSet<Triangle2>();

            foreach (HalfEdgeFace2 face in data.faces)
            {
                Vector2 p1 = face.edge.v.position;
                Vector2 p2 = face.edge.nextEdge.v.position;
                Vector2 p3 = face.edge.nextEdge.nextEdge.v.position;

                Triangle2 t = new Triangle2(p1, p2, p3);

                triangles.Add(t);
            }

            return triangles;
        }


        //
        // Triangle to Unity mesh
        //

        //Version 1. Check that each vertex exists only once in the final mesh
        //Make sure the triangles have the correct orientation
        public static Mesh Triangle3ToCompressedMesh(HashSet<Triangle3> triangles)
        {
            if (triangles == null)
            {
                return null;
            }

            //Step 2. Create the list with unique vertices
            //A hashset will make it fast to check if a vertex already exists in the collection
            HashSet<Vector3> uniqueVertices = new HashSet<Vector3>();

            foreach (Triangle3 t in triangles)
            {
                Vector3 v1 = t.p1;
                Vector3 v2 = t.p2;
                Vector3 v3 = t.p3;

                if (!uniqueVertices.Contains(v1))
                {
                    uniqueVertices.Add(v1);
                }
                if (!uniqueVertices.Contains(v2))
                {
                    uniqueVertices.Add(v2);
                }
                if (!uniqueVertices.Contains(v3))
                {
                    uniqueVertices.Add(v3);
                }
            }

            //Create the list with all vertices
            List<Vector3> meshVertices = new List<Vector3>(uniqueVertices);


            //Step3. Create the list with all triangles by using the unique vertices
            List<int> meshTriangles = new List<int>();

            //Use a dictionary to quickly find which positon in the list a Vector3 has
            Dictionary<Vector3, int> vector2Positons = new Dictionary<Vector3, int>();

            for (int i = 0; i < meshVertices.Count; i++)
            {
                vector2Positons.Add(meshVertices[i], i);
            }

            foreach (Triangle3 t in triangles)
            {
                Vector3 v1 = t.p1;
                Vector3 v2 = t.p2;
                Vector3 v3 = t.p3;

                meshTriangles.Add(vector2Positons[v1]);
                meshTriangles.Add(vector2Positons[v2]);
                meshTriangles.Add(vector2Positons[v3]);
            }


            //Step4. Create the final mesh
            Mesh mesh = new Mesh();

            //From MyVector3 to Vector3
            Vector3[] meshVerticesArray = new Vector3[meshVertices.Count];

            for (int i = 0; i < meshVerticesArray.Length; i++)
            {
                Vector3 v = meshVertices[i];

                meshVerticesArray[i] = new Vector3(v.x, v.y, v.z);
            }
            
            mesh.vertices = meshVerticesArray;
            mesh.triangles = meshTriangles.ToArray();

            return mesh;
        }
    }
}
