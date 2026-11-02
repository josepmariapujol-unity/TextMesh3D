using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.Splines;
using Typography.OpenFont;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Softgraph.TextMesh3D.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Mesh/TextMesh3D - Text")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TextMesh3DContainer : MonoBehaviour
    {
        [Header("Enter prompt"), Multiline]
        public String m_Text;

        [SerializeField]
        public Font m_Font;

        [SerializeField]
        public Enum m_Output;

        [SerializeField]
        public float m_Extrusion;

        [SerializeField]
        public float m_Size;

        [SerializeField]
        public Material m_Material;

        [SerializeField]
        public float m_Spacing;

        [SerializeField]
        public bool m_GroupOutput;

        public enum Enum
        {
            Spline,
            Surface,
            Mesh
        }

        private GameObject objParentSpawn;
        private GameObject[] objToSpawn;
        private SplineContainer[] m_Spline;

        private Vector3[] vertices;
        private int[] triangles;

        private Vector3[][] outerVerticesLower;
        private Vector3[][] outerVerticesUpper;

        private Vector3[][] innerVerticesLower;
        private Vector3[][] innerVerticesUpper;

        private CombineInstance[] combineInstance;
        private List<float3>[] verticesListArray;

        [SerializeField][HideInInspector] private GameObject myText;

        // Raw bytes of m_Font's source file (.ttf/.otf). Unity's Font asset does not expose the
        // original file at runtime, so the editor caches it here to make generation work in builds.
        [SerializeField][HideInInspector] private byte[] m_FontData;
        [SerializeField][HideInInspector] private string m_FontDataSource;

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheFontData();
        }

        /// <summary>Editor only: reads the font file behind m_Font into m_FontData when the font changes.</summary>
        public void CacheFontData()
        {
            var path = m_Font != null ? AssetDatabase.GetAssetPath(m_Font) : null;

            if (string.IsNullOrEmpty(path))
            {
                if (m_FontData != null)
                {
                    m_FontData = null;
                    m_FontDataSource = null;
                    MarkDirty();
                }
                return;
            }

            if (m_FontData != null && m_FontData.Length > 0 && m_FontDataSource == path)
                return;

            var fullPath = Path.GetFullPath(path); // resolves Packages/... to the real location
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"TextMesh3D: font file not found at '{path}'.", this);
                return;
            }

            m_FontData = File.ReadAllBytes(fullPath);
            m_FontDataSource = path;
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (!Application.isPlaying)
                EditorUtility.SetDirty(this);
        }
#endif

        private Typeface LoadTypeface()
        {
#if UNITY_EDITOR
            CacheFontData();
#endif
            if (m_FontData == null || m_FontData.Length == 0)
            {
                Debug.LogError("TextMesh3D: no font data. Assign a .ttf/.otf font in the editor before generating.", this);
                return null;
            }

            using (var stream = new MemoryStream(m_FontData, false))
            {
                return new OpenFontReader().Read(stream);
            }
        }

        // DestroyImmediate is only valid in edit mode; use Destroy while playing.
        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        public void Function()
        {
            switch (m_Output)
            {
                case Enum.Spline:
                    DoSpline();
                    break;
                case Enum.Surface:
                    DoSurface();
                    break;
                case Enum.Mesh:
                    DoMesh();
                    break;
            }
        }

        private bool DoSpline()
        {
            Typeface typeface = LoadTypeface();
            if (typeface == null) return false;

            var fontName = m_Font != null ? m_Font.name : "TextMesh3D";
            if (myText) SafeDestroy(myText);
            myText = new GameObject(fontName);
            myText.transform.SetParent(transform, false);

            objToSpawn = new GameObject[m_Text.Length];
            m_Spline = new SplineContainer[m_Text.Length];

            var advanceWidth = float3.zero;

            var scale = 0.01f * m_Size;
            var spaceWidth = 446f;

            for (int i = 0; i < objToSpawn.Length; i++)
            {
                //Adding gameObject with a name
                if (m_Text[i].ToString() == " ")
                {
                    objToSpawn[i] = new GameObject("Space");
                }
                else
                {
                    objToSpawn[i] = new GameObject("Glyph - " + m_Text[i]);
                }
                objToSpawn[i].transform.parent = myText.transform;

                //Adding SplineContainer Component
                objToSpawn[i].AddComponent<SplineContainer>();

                var container = objToSpawn[i].GetComponent<SplineContainer>();
                container.RemoveSplineAt(0);

                //Adding Glyph to gameObjects
                Glyph glyph = typeface.GetGlyph(typeface.GetGlyphIndex(m_Text[i]));
                GlyphSplineReader glyphSplineReader = new GlyphSplineReader(container);
                glyphSplineReader.Read(glyph, scale);

                //Set Position of gameObjects with advanceWidth
                objToSpawn[i].transform.localPosition = new Vector3(advanceWidth.x, advanceWidth.y, advanceWidth.z);
                if (m_Text[i].ToString() == " ")
                {
                    advanceWidth.x += spaceWidth * scale + m_Spacing;  //Spacing for Space characters (446*2)
                }
                else
                {
                    advanceWidth.x += glyph.Bounds.XMax * scale + m_Spacing;
                }
            }
            return true;
        }

        private void DoSurface()
        {
            if (!DoSpline()) return;

            for (int i = 0; i < objToSpawn.Length; i++)
            {
                if (m_Text[i].ToString() == " ")
                {
                    continue;
                }

                m_Spline[i] = objToSpawn[i].GetComponent<SplineContainer>();
                objToSpawn[i].AddComponent<MeshFilter>();
                objToSpawn[i].AddComponent<MeshRenderer>().material = m_Material;

                RemoveSingleKnot(i);
                var branchNumber = m_Spline[i].Splines.Count;

                if (branchNumber > 1)
                {
                    var polygon = m_Spline[i].Splines[0];               //first branch
                    var point = m_Spline[i].Splines[1][0].Position;     //first knot second branch

                    if (!IsPointInsidePolygon(point, polygon))
                    {
                        m_Spline[i].AddSpline(m_Spline[i].Splines[0]);        //Mostly e-a-o-R-p
                        m_Spline[i].RemoveSpline(m_Spline[i].Splines[0]);
                    }
                }

                // If outer spline is clockwise we will reverse the whole SplineContainer, else nothing happen since its already counter clockwise by default
                ReturnCounterClockWiseSpline(i, TriangulationUtility.IsClockWiseSpline(m_Spline[i].Splines[0]));

                List<bool> listHolesBool = new List<bool>();
                var outerBranchNumber = 0;
                //Calculate the number of outer splines. For example: "i" has 2 outer branches (both branches), "B" has 1 (exterior)
                //Calculate list for clockwise or counter clockwise
                for (int j = 0; j < branchNumber; j++)
                {
                    if (!TriangulationUtility.IsClockWiseSpline(m_Spline[i].Splines[j]))
                    {
                        outerBranchNumber++;
                        listHolesBool.Add(false);
                    }
                    else
                    {
                        listHolesBool.Add(true);
                    }
                }

                //top(s) + bottom(s) meshes
                combineInstance = new CombineInstance[outerBranchNumber + outerBranchNumber];     //*2 for the mesh on the top
                verticesListArray = new List<float3>[branchNumber];

                GameObject empty = new GameObject();
                var localToWorldMatrix = empty.transform.localToWorldMatrix;

                for (int j = 0; j < branchNumber; j++)
                {
                    var knotNumber = m_Spline[i].Splines[j].Count;      //knots x layers
                    verticesListArray[j] = new List<float3>();
                    CreateDetailedVerticesListWhenTangentsExist(i, j, knotNumber, out verticesListArray[j]);
                }

                DoFillMesh(branchNumber, listHolesBool, outerBranchNumber, localToWorldMatrix, 0, 0);

                SafeDestroy(empty);

                Mesh combinedMesh = new Mesh();
                combinedMesh.name = objToSpawn[i].name;
                combinedMesh.CombineMeshes(combineInstance, true);
                objToSpawn[i].GetComponent<MeshFilter>().mesh = combinedMesh;

                SafeDestroy(objToSpawn[i].GetComponent<SplineContainer>());
            }
        }

        private void DoMesh()
        {
            if (!DoSpline()) return;

            //Run for every string character and continue for empty spaces
            for (int i = 0; i < objToSpawn.Length; i++)
            {
                if (m_Text[i].ToString() == " ")
                {
                    continue;
                }

                m_Spline[i] = objToSpawn[i].GetComponent<SplineContainer>();
                objToSpawn[i].AddComponent<MeshFilter>();
                objToSpawn[i].AddComponent<MeshRenderer>().material = m_Material;

                RemoveSingleKnot(i);

                //Number of branches in one spline i,o,p = 2; B = 3
                var branchNumber = m_Spline[i].Splines.Count;

                if (branchNumber > 1)
                {
                    var polygon = m_Spline[i].Splines[0];               //first branch
                    var point = m_Spline[i].Splines[1][0].Position;     //first knot second branch

                    if (!IsPointInsidePolygon(point, polygon))
                    {
                        m_Spline[i].AddSpline(m_Spline[i].Splines[0]);        //Mostly e-a-o-R-p
                        m_Spline[i].RemoveSpline(m_Spline[i].Splines[0]);
                    }
                }

                // If outer spline is clockwise we will reverse the whole SplineContainer, else nothing happen since its already counter clockwise by default
                ReturnCounterClockWiseSpline(i, TriangulationUtility.IsClockWiseSpline(m_Spline[i].Splines[0]));

                List<bool> listHolesBool = new List<bool>();
                var outerBranchNumber = 0;
                //Calculate the number of outer splines. For example: "i" has 2 outer branches (both branches), "B" has 1 (exterior)
                //Calculate list for clockwise or counter clockwise
                for (int j = 0; j < branchNumber; j++)
                {
                    if (!TriangulationUtility.IsClockWiseSpline(m_Spline[i].Splines[j]))
                    {
                        outerBranchNumber++;
                        listHolesBool.Add(false);
                    }
                    else
                    {
                        listHolesBool.Add(true);
                    }
                }

                var innerBranchNumber = branchNumber - outerBranchNumber;

                //Number of outer vertices
                outerVerticesLower = new Vector3[outerBranchNumber][];
                outerVerticesUpper = new Vector3[outerBranchNumber][];

                //Number of inner vertices or holes
                innerVerticesLower = new Vector3[innerBranchNumber][];
                innerVerticesUpper = new Vector3[innerBranchNumber][];

                //Lateral(s) + top(s) + bottom(s) meshes
                combineInstance = new CombineInstance[branchNumber + outerBranchNumber + outerBranchNumber];     //*2 for the mesh on the top
                verticesListArray = new List<float3>[branchNumber];

                GameObject empty = new GameObject();
                var localToWorldMatrix = empty.transform.localToWorldMatrix;

                DoLateralMeshing(branchNumber, i, localToWorldMatrix);

                DoFillMesh(branchNumber, listHolesBool, outerBranchNumber, localToWorldMatrix, m_Extrusion, branchNumber);

                SafeDestroy(empty);

                Mesh combinedMesh = new Mesh();
                combinedMesh.name = objToSpawn[i].name;
                combinedMesh.CombineMeshes(combineInstance, true);
                combinedMesh.RecalculateNormals();
                objToSpawn[i].GetComponent<MeshFilter>().mesh = combinedMesh;

                SafeDestroy(objToSpawn[i].GetComponent<SplineContainer>());
            }
        }

        private void RemoveSingleKnot(int i)
        {
            //Some fonts have 1 single knot at the beginning so we dont want to mesh it then removing
            if (m_Spline[i].Splines[0].Count == 1)
            {
                m_Spline[i].RemoveSpline(m_Spline[i].Splines[0]);
            }
        }

        private void DoLateralMeshing(int branchNumber, int i, Matrix4x4 localToWorldMatrix)
        {
            var outerMeshRunningNumber = 0;
            var innerMeshRunningNumber = 0;
            for (int j = 0; j < branchNumber; j++)
            {
                var extrusionNumber = 2;                               //layers: bottom and top
                var knotNumber = m_Spline[i].Splines[j].Count;      //knots x layers

                verticesListArray[j] = new List<float3>();

                CreateDetailedVerticesListWhenTangentsExist(i, j, knotNumber, out verticesListArray[j]);

                //Adding extra knots(splines) from points when Tangents exist for detailed meshing
                knotNumber = verticesListArray[j].Count;

                //For triangulation purposes we need to duplicate vertices. One normal vector for vertices
                var knotNumberDoubleVertices = knotNumber * 2;

                vertices = new Vector3[knotNumberDoubleVertices * 2]; // why so many? every lateral face needs double vertices due to normals plus frontal meshing
                triangles = new int[FindTotalTrianglesExtrusion(knotNumber)];

                //Setting the number outer vertices
                if (!TriangulationUtility.IsClockWiseSpline(m_Spline[i].Splines[j]))
                {
                    outerVerticesLower[outerMeshRunningNumber] = new Vector3[knotNumberDoubleVertices];
                    outerVerticesUpper[outerMeshRunningNumber] = new Vector3[knotNumberDoubleVertices];
                    outerMeshRunningNumber++;
                }
                else
                {
                    innerVerticesLower[innerMeshRunningNumber] = new Vector3[knotNumberDoubleVertices];
                    innerVerticesUpper[innerMeshRunningNumber] = new Vector3[knotNumberDoubleVertices];
                    innerMeshRunningNumber++;
                }
                for (int k = 0; k < knotNumber; k++)
                {
                    var index = k * 2;

                    vertices[index] = verticesListArray[j][GetIndex(knotNumber, k)];
                    vertices[index + 1] = verticesListArray[j][GetIndex(knotNumber, k + 1)];

                    vertices[index + knotNumberDoubleVertices] = verticesListArray[j][GetIndex(knotNumber, k)] + new float3(0, m_Extrusion, 0);
                    vertices[index + 1 + knotNumberDoubleVertices] = verticesListArray[j][GetIndex(knotNumber, k + 1)] + new float3(0, m_Extrusion, 0);
                }

                TriangulateLateralFaces(extrusionNumber, knotNumber);

                combineInstance[j].mesh = new Mesh();
                combineInstance[j].mesh.vertices = vertices;
                combineInstance[j].mesh.triangles = triangles;
                combineInstance[j].mesh.RecalculateNormals();
                combineInstance[j].transform = localToWorldMatrix;
            }
        }

        private void DoFillMesh(int branchNumber, List<bool> listHolesBool, int outerBranchNumber, Matrix4x4 localToWorldMatrix, float extrusion, int branchNumberMesh)
        {
            var c = 0;
            for (int j = 0; j < branchNumber; j++)
            {
                if (listHolesBool[j] == false)
                {
                    //Hull
                    List<Vector3> hullPoints = new List<Vector3>();

                    // hullPoints.Clear(); //Clean for safety
                    for (int k = 0; k < verticesListArray[j].Count; k++)
                    {
                        hullPoints.Add(verticesListArray[j][k]);
                    }

                    List<Vector2> hullPoints2D = hullPoints.Select(x1 => x1.ToVector2()).ToList();

                    //Holes
                    HashSet<List<Vector2>> allHolePoints2D = new HashSet<List<Vector2>>();
                    List<Vector3> holePoints = new List<Vector3>();
                    holePoints.Clear();

                    for (int k = 0; k < (listHolesBool.Count - j); k++)
                    {
                        if (j + k + 1 < listHolesBool.Count && listHolesBool[j + k + 1])
                        {
                            for (int w = 0; w < verticesListArray[j + k + 1].Count; w++)
                            {
                                holePoints.Add(verticesListArray[j + k + 1][w]);
                            }

                            List<Vector2> holePoints2D = holePoints.Select(x1 => x1.ToVector2()).ToList();
                            allHolePoints2D.Add(holePoints2D);
                            holePoints.Clear(); //Clean for safety
                        }
                    }

                    //Normalize to range 0-1
                    //We should use all points, including the constraints because the hole may be outside of the random points
                    List<Vector2> allPoints = new List<Vector2>();
                    allPoints.AddRange(hullPoints2D);

                    foreach (List<Vector2> hole in allHolePoints2D)
                    {
                        allPoints.AddRange(hole);
                    }

                    Normalizer2 normalizer = new Normalizer2(allPoints);
                    List<Vector2> hullPoints2DNormalized = normalizer.Normalize(hullPoints2D);
                    HashSet<List<Vector2>> allHolePoints2DNormalized = new HashSet<List<Vector2>>();

                    foreach (List<Vector2> hole in allHolePoints2D)
                    {
                        List<Vector2> holeNormalized = normalizer.Normalize(hole);
                        allHolePoints2DNormalized.Add(holeNormalized);
                    }

                    // Generate the triangulation -  Constrained delaunay
                    HalfEdgeData2 triangleDataNormalized = Delaunay.ConstrainedBySloan(null, hullPoints2DNormalized, allHolePoints2DNormalized, shouldRemoveTriangles: true, new HalfEdgeData2());

                    //UnNormalize
                    HalfEdgeData2 triangleData = normalizer.UnNormalize(triangleDataNormalized);
                    //From half-edge to triangle
                    HashSet<Triangle2> triangles2D = TransformBetweenDataStructures.HalfEdge2ToTriangle2(triangleData);

                    //From triangulation to mesh
                    //Make sure the triangles have the correct orientation
                    triangles2D = HelpMethods.OrientTrianglesClockwise(triangles2D);
                    //From 2d to 3d
                    HashSet<Triangle3> triangles3D = new HashSet<Triangle3>();

                    foreach (Triangle2 t in triangles2D)
                    {
                        triangles3D.Add(new Triangle3(t.p1.ToVector3_Yis3D(), t.p2.ToVector3_Yis3D(), t.p3.ToVector3_Yis3D()));
                    }

                    Mesh meshArrayFront = TransformBetweenDataStructures.Triangle3ToCompressedMesh(triangles3D);

                    //Mesh meshLower = new Mesh();
                    Vector3[] verticesLower = new Vector3[meshArrayFront.vertices.Length];
                    int[] trianglesLower = new int[meshArrayFront.triangles.Length];

                    //Mesh meshUpper = new Mesh();
                    Vector3[] verticesUpper = new Vector3[meshArrayFront.vertices.Length];
                    int[] trianglesUpper = new int[meshArrayFront.triangles.Length];

                    for (int k = 0; k < meshArrayFront.vertices.Length; k++)
                    {
                        verticesLower[k] = meshArrayFront.vertices[k];
                        verticesUpper[k] = meshArrayFront.vertices[k] + new Vector3(0,extrusion,0);
                    }

                    for (int k = 0; k < meshArrayFront.triangles.Length; k+=3)
                    {
                        trianglesLower[k + 0] = meshArrayFront.triangles[k + 0];
                        trianglesLower[k + 1] = meshArrayFront.triangles[k + 2];
                        trianglesLower[k + 2] = meshArrayFront.triangles[k + 1];
                    }

                    trianglesUpper = meshArrayFront.triangles;

                    combineInstance[branchNumberMesh + c].mesh = new Mesh();
                    combineInstance[branchNumberMesh + c].mesh.vertices = verticesUpper;
                    combineInstance[branchNumberMesh + c].mesh.triangles = trianglesUpper;
                    combineInstance[branchNumberMesh + c].mesh.RecalculateNormals();
                    combineInstance[branchNumberMesh + c].transform = localToWorldMatrix;

                    combineInstance[branchNumberMesh + outerBranchNumber + c].mesh = new Mesh();
                    combineInstance[branchNumberMesh + outerBranchNumber + c].mesh.vertices = verticesLower;
                    combineInstance[branchNumberMesh + outerBranchNumber + c].mesh.triangles = trianglesLower;
                    combineInstance[branchNumberMesh + outerBranchNumber + c].mesh.RecalculateNormals();
                    combineInstance[branchNumberMesh + outerBranchNumber + c].transform = localToWorldMatrix;

                    c++;
                }
            }
        }

        bool IsPointInsidePolygon(float3 point, Spline polygon)
        {
            int count = 0;
            int numVertices = polygon.Count;

            for (int i = 0, j = numVertices - 1; i < numVertices; j = i++)
            {
                if ((polygon[i].Position.z > point.z != polygon[j].Position.z > point.z) &&
                    (point.x < (polygon[j].Position.x - polygon[i].Position.x) * (point.z - polygon[i].Position.z) / (polygon[j].Position.z - polygon[i].Position.z) + polygon[i].Position.x))
                {
                    count++;
                }
            }

            return count % 2 == 1;
        }

        void ReturnCounterClockWiseSpline(int i, bool isClockWise)
        {
            if (isClockWise)
            {
                var branchNumber = m_Spline[i].Splines.Count;
                for (int y = 0; y < branchNumber; y++)
                {
                    List<BezierKnot> listSpline = new List<BezierKnot>();

                    var counterBranch = m_Spline[i].Splines[y].Count;
                    //read
                    for (int z = 0; z < counterBranch; z++)
                    {
                        var bezierKnot = new BezierKnot(
                            m_Spline[i].Splines[y][counterBranch - 1 - z].Position,
                            m_Spline[i].Splines[y][counterBranch - 1 - z].TangentOut,
                            m_Spline[i].Splines[y][counterBranch - 1 - z].TangentIn,
                            quaternion.identity
                        );
                        listSpline.Add(bezierKnot);
                    }
                    //write
                    for (int z = 0; z < counterBranch; z++)
                    {
                        m_Spline[i].Splines[y][z] = listSpline[z];
                    }
                    listSpline.Clear();
                }
            }
        }

        void CreateDetailedVerticesListWhenTangentsExist(int i, int j, int knotNumber, out List<float3> verticesList)
        {
            verticesList = new List<float3>();
            for (int k = 0; k < knotNumber; k++)
            {
                if (m_Spline[i].Splines[j][k].TangentOut.x != 0 ||
                    m_Spline[i].Splines[j][k].TangentOut.y != 0 ||
                    m_Spline[i].Splines[j][k].TangentOut.z != 0 ||
                    m_Spline[i].Splines[j][TriangulationUtility.GetIndex(knotNumber, k + 1)].TangentIn.x != 0 ||
                    m_Spline[i].Splines[j][TriangulationUtility.GetIndex(knotNumber, k + 1)].TangentIn.y != 0 ||
                    m_Spline[i].Splines[j][TriangulationUtility.GetIndex(knotNumber, k + 1)].TangentIn.z != 0)
                {
                    BezierCurve curve = new BezierCurve(m_Spline[i].Splines[j][k], m_Spline[i].Splines[j][TriangulationUtility.GetIndex(knotNumber, k + 1)]);

                    var div = 3;
                    for (int w = 0; w < div; w++)
                    {
                        float t = (float) w / div;
                        verticesList.Add(CurveUtility.EvaluatePosition(curve, t));
                    }
                }
                else
                {
                    verticesList.Add(m_Spline[i].Splines[j][k].Position);
                }
            }
        }

        void TriangulateLateralFaces(int extrusionNumber, int knotNumber)
        {
            for (int i = 0; i < extrusionNumber - 1; i++)
            {
                for (int j = 0; j < knotNumber; j++)
                {
                    var index = j * 6 + 0 + i * knotNumber * 6;

                    triangles[index + 0] = j*2;
                    triangles[index + 1] = j*2+knotNumber*2;
                    triangles[index + 2] = j*2+knotNumber*2+1;

                    triangles[index + 3] = j*2;
                    triangles[index + 4] = j*2+knotNumber*2+1;
                    triangles[index + 5] = j*2+1;
                }
            }
        }

        int FindTotalTrianglesExtrusion(int knots)
        {
            return knots * 2 * 3;
        }

        private static int GetIndex(int indexLength, int index)
        {
            return index >= indexLength ? 0 : index;
        }
    }
}
