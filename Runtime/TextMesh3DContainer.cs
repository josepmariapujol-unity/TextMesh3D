using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Typography.OpenFont;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Softgraph.TextMesh3D.Runtime
{
    /// <summary>
    /// Converts a string into 3D text using the glyph outlines of a TrueType/OpenType font:
    /// splines, flat surfaces or extruded meshes. One child object per character, or one merged mesh
    /// on this object when Group Output is enabled.
    /// </summary>
    /// <remarks>
    /// This file: settings, public API and layout. See TextMesh3DContainer.FontData.cs (font loading) and
    /// TextMesh3DContainer.Meshing.cs (outline classification, triangulation, mesh building).
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Mesh/TextMesh3D - Text")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public partial class TextMesh3DContainer : MonoBehaviour
    {
        public enum OutputMode
        {
            Spline,
            Surface,
            Mesh
        }

        /// <summary>The plane the text is laid out on.</summary>
        public enum TextPlane
        {
            /// <summary>Lying flat on the XZ plane, extruded up along +Y.</summary>
            XZ,
            /// <summary>Standing up on the XY plane, facing -Z (the default camera direction), extruded back along +Z.</summary>
            XY
        }

        // Serialized names (m_*) are kept from earlier versions so existing scenes keep their values.
        [SerializeField, Multiline, Tooltip("Text to generate. Supports line breaks (\\n) and tabs.")]
        private string m_Text;

        [SerializeField, Tooltip("TrueType (.ttf) or OpenType (.otf) font.")]
        private Font m_Font;

        [SerializeField, Tooltip("Spline: outlines only. Surface: flat filled shapes. Mesh: extruded solids.")]
        private OutputMode m_Output;

        [SerializeField, Min(0f), Tooltip("Mesh only: depth of the extrusion in Unity units.")]
        private float m_Extrusion;

        [SerializeField, Min(0.0001f), Tooltip("Em size in Unity units. Same visual size for every font, regardless of its UnitsPerEm.")]
        private float m_Size;

        [SerializeField, Tooltip("Surface/Mesh only: material for the generated meshes.")]
        private Material m_Material;

        [SerializeField, Tooltip("Extra space added after every character, in Unity units (can be negative).")]
        private float m_Spacing;

        [SerializeField, Tooltip("Apply the font's kerning, e.g. tighter \"AV\" and \"To\" pairs.")]
        private bool m_Kerning = true;

        [SerializeField, Tooltip("Surface/Mesh only: merge all glyphs into one mesh on this object instead of one child object per glyph.")]
        private bool m_GroupOutput;

        [SerializeField, Range(1, 32), Tooltip("Points per curved outline segment. Higher gives smoother curves and more triangles.")]
        private int m_CurveSegments = 3;

        [SerializeField, Tooltip("XZ: lying flat, extruded up (+Y). XY: standing up facing -Z (the default camera), extruded back along +Z.")]
        private TextPlane m_Plane = TextPlane.XZ;

        [SerializeField, Tooltip("Distance between lines, as a multiple of the font's line height (1 = the font's default).")]
        private float m_LineSpacing = 1f;

        [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("m_GeneratedRoot")]
        private GameObject m_GeneratedRoot;

        /// <summary>One character of the laid-out text, before any objects or meshes are created.</summary>
        private struct GlyphPlacement
        {
            public char character;
            public int glyphIndex;    // -1 for control characters (\n, \t, ...), which have no glyph
            public Vector3 position;  // pen position on the internal XZ layout (before the plane is applied)
        }

        public string Text { get => m_Text; set => m_Text = value; }
        public Font Font { get => m_Font; set => m_Font = value; }
        public OutputMode Output { get => m_Output; set => m_Output = value; }
        public float Extrusion { get => m_Extrusion; set => m_Extrusion = value; }
        public float Size { get => m_Size; set => m_Size = value; }
        public Material Material { get => m_Material; set => m_Material = value; }
        public float Spacing { get => m_Spacing; set => m_Spacing = value; }
        public bool Kerning { get => m_Kerning; set => m_Kerning = value; }
        public bool GroupOutput { get => m_GroupOutput; set => m_GroupOutput = value; }
        public int CurveSegments { get => m_CurveSegments; set => m_CurveSegments = value; }
        public TextPlane Plane { get => m_Plane; set => m_Plane = value; }
        public float LineSpacing { get => m_LineSpacing; set => m_LineSpacing = value; }

        /// <summary>The object holding the generated per-character children (null when grouped or not generated).</summary>
        public GameObject GeneratedRoot => m_GeneratedRoot;

        [Obsolete("Use Generate() instead.")]
        public void Function() => Generate();

        /// <summary>Regenerates the output from the current settings. In edit mode this is one undo step.</summary>
        public void Generate()
        {
#if UNITY_EDITOR
            // In edit mode, one Generate is one undo step: it restores the previous output (and grouped mesh).
            bool recordUndo = !Application.isPlaying;
            int undoGroup = -1;
            if (recordUndo)
            {
                // Turn the caller's pending Undo.RecordObject changes (e.g. "RecordObject, then Text = ...") into undo
                // records first. A complete snapshot of an object discards its pending RecordObject diff, so without
                // this the edit that triggered Generate (live preview, editor scripts) could not be undone.
                Undo.FlushUndoRecordObjects();

                // Cache the font file before the snapshot below. It is derived data (re-read whenever it doesn't match
                // the Font asset), so it doesn't need its own undo record.
                CacheFontData();

                Undo.SetCurrentGroupName(k_UndoName);
                undoGroup = Undo.GetCurrentGroup();
                // Complete snapshots, not RecordObject: RecordObject only diffs at the next undo flush, and
                // Undo.DestroyObjectImmediate (removing the previous output) flushes immediately, before
                // m_GeneratedRoot and the grouped mesh are reassigned. Those later changes were then never recorded,
                // so Undo removed the new output but left m_GeneratedRoot pointing at it instead of the old one.
                Undo.RegisterCompleteObjectUndo(this, k_UndoName);
                if (TryGetComponent<MeshFilter>(out var rootFilter)) Undo.RegisterCompleteObjectUndo(rootFilter, k_UndoName);
                if (TryGetComponent<MeshRenderer>(out var rootRenderer)) Undo.RegisterCompleteObjectUndo(rootRenderer, k_UndoName);
            }
#endif
            try
            {
                if (!ValidateSettings())
                {
                    // keep the previous output untouched
                }
                else if (string.IsNullOrEmpty(m_Text))
                {
                    ClearPreviousOutput();
                }
                else
                {
                    var typeface = LoadTypeface();
                    if (typeface != null) BuildOutput(typeface); // on failure the previous output stays
                }
            }
            catch (Exception e)
            {
                // Never leave the editor in a broken state; in edit mode the partial result can be undone.
                Debug.LogError($"TextMesh3D: generation failed: {e.Message}", this);
                Debug.LogException(e, this);
            }
            finally
            {
#if UNITY_EDITOR
                if (recordUndo)
                {
                    if (m_GeneratedRoot) Undo.RegisterCreatedObjectUndo(m_GeneratedRoot, k_UndoName);
                    Undo.CollapseUndoOperations(undoGroup);
                }
#endif
            }
        }

        private bool ValidateSettings()
        {
            if (!(m_Size > 0f) || float.IsInfinity(m_Size))
            {
                Debug.LogError($"TextMesh3D: Size must be greater than 0 (is {m_Size}).", this);
                return false;
            }
            if (!(m_Extrusion >= 0f) || float.IsInfinity(m_Extrusion))
            {
                Debug.LogError($"TextMesh3D: Extrusion must be 0 or greater (is {m_Extrusion}).", this);
                return false;
            }
            if (float.IsNaN(m_Spacing) || float.IsInfinity(m_Spacing))
            {
                Debug.LogError($"TextMesh3D: Spacing must be a finite number (is {m_Spacing}).", this);
                return false;
            }
            if (float.IsNaN(m_LineSpacing) || float.IsInfinity(m_LineSpacing))
            {
                Debug.LogError($"TextMesh3D: Line Spacing must be a finite number (is {m_LineSpacing}).", this);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Maps the internal layout (text on XZ, glyph up = +Z, extrusion +Y) to the chosen plane, in this object's space.
        /// XY: rotated -90° about X so glyph up becomes +Y and the extrusion points to -Z, then shifted by the extrusion
        /// depth so the front face sits at z = 0 facing -Z and the depth goes back along +Z.
        /// </summary>
        private Matrix4x4 GetPlaneMatrix()
        {
            if (m_Plane != TextPlane.XY) return Matrix4x4.identity;
            float depth = m_Output == OutputMode.Mesh ? m_Extrusion : 0f;
            return Matrix4x4.TRS(new Vector3(0f, 0f, depth), Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
        }

        /// <summary>
        /// Lays out the text, removes the previous output and builds the new one. The plane orientation is written
        /// into the output itself (positions, mesh vertices, spline knots), so it doesn't depend on any transform.
        /// </summary>
        private void BuildOutput(Typeface typeface)
        {
            var scale = GetFontScale(typeface, m_Size);
            var placements = LayoutText(typeface, scale);
            var plane = GetPlaneMatrix();

            ClearPreviousOutput();

            if (m_Output == OutputMode.Spline)
            {
                BuildSplineObjects(typeface, scale, placements, plane);
                return;
            }

            PrepareMeshCache(typeface);
            if (m_GroupOutput) BuildGroupedMesh(typeface, scale, placements, plane);
            else BuildMeshObjects(typeface, scale, placements, plane);
        }

        /// <summary>The generated root with one child object per character, in text order.</summary>
        private GameObject[] CreateCharacterObjects(List<GlyphPlacement> placements, Matrix4x4 plane)
        {
            m_GeneratedRoot = new GameObject(m_Font != null ? m_Font.name : "TextMesh3D");
            m_GeneratedRoot.transform.SetParent(transform, false);

            var objects = new GameObject[placements.Count];
            for (int i = 0; i < placements.Count; i++)
            {
                objects[i] = new GameObject(GetCharacterObjectName(placements[i].character));
                objects[i].transform.SetParent(m_GeneratedRoot.transform, false);
                objects[i].transform.localPosition = plane.MultiplyPoint3x4(placements[i].position);
            }
            return objects;
        }

        /// <summary>Spline output: one SplineContainer per glyph, with one closed spline per outline.</summary>
        private void BuildSplineObjects(Typeface typeface, float scale, List<GlyphPlacement> placements, Matrix4x4 plane)
        {
            var objects = CreateCharacterObjects(placements, plane);
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i].glyphIndex < 0) continue; // control characters have no outline

                var container = objects[i].AddComponent<SplineContainer>();
                container.RemoveSplineAt(0);
                new GlyphSplineReader(container).Read(typeface.GetGlyph((ushort)placements[i].glyphIndex), scale);

                if (m_Plane != TextPlane.XZ)
                {
                    foreach (var spline in container.Splines) RotateSpline(spline, plane.rotation);
                }
            }
        }

        /// <summary>Surface/Mesh output: one object per character; repeated glyphs share one cached mesh.</summary>
        private void BuildMeshObjects(Typeface typeface, float scale, List<GlyphPlacement> placements, Matrix4x4 plane)
        {
            var objects = CreateCharacterObjects(placements, plane);
            for (int i = 0; i < placements.Count; i++)
            {
                var mesh = GetGlyphMesh(typeface, placements[i], scale);
                if (mesh == null) continue; // spaces, line breaks, empty glyphs

                objects[i].AddComponent<MeshFilter>().sharedMesh = mesh;
                // sharedMaterial: .material would instantiate (and leak) a copy in edit mode
                objects[i].AddComponent<MeshRenderer>().sharedMaterial = m_Material;
            }
        }

        private static void RotateMesh(Mesh mesh, Quaternion rotation)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = rotation * vertices[i];
            for (int i = 0; i < normals.Length; i++) normals[i] = rotation * normals[i];
            for (int i = 0; i < tangents.Length; i++)
            {
                var v = rotation * new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                tangents[i] = new Vector4(v.x, v.y, v.z, tangents[i].w); // w (handedness) is unchanged by a rotation
            }
            mesh.vertices = vertices;
            if (normals.Length > 0) mesh.normals = normals;
            if (tangents.Length > 0) mesh.tangents = tangents;
            mesh.RecalculateBounds();
        }

        private static void RotateSpline(Spline spline, Quaternion rotation)
        {
            quaternion r = rotation;
            for (int k = 0; k < spline.Count; k++)
            {
                var knot = spline[k];
                // Knot rotations are identity, so tangents are in spline space and rotate like positions.
                spline.SetKnot(k, new BezierKnot(
                    math.mul(r, knot.Position),
                    math.mul(r, knot.TangentIn),
                    math.mul(r, knot.TangentOut),
                    knot.Rotation));
            }
        }

        private const string k_UndoName = "Generate TextMesh3D";

        /// <summary>
        /// Removes the previous output: the generated child hierarchy and any grouped mesh on this object.
        /// In edit mode this goes through Undo, so the meshes are kept for undo/redo (Unity frees unreferenced
        /// ones when the scene is saved). While playing, the meshes are destroyed so regenerating doesn't leak.
        /// </summary>
        private void ClearPreviousOutput()
        {
            TryGetComponent<MeshFilter>(out var rootFilter);
            var rootMesh = rootFilter != null ? rootFilter.sharedMesh : null;
            bool ownsRootMesh = rootMesh != null && IsGeneratedMesh(rootMesh);

            if (Application.isPlaying)
            {
                if (m_GeneratedRoot)
                {
                    foreach (var mf in m_GeneratedRoot.GetComponentsInChildren<MeshFilter>(true))
                    {
                        // Cached glyph meshes are reused by the next output; the cache frees them when invalidated.
                        if (mf.sharedMesh != null && IsGeneratedMesh(mf.sharedMesh) && !IsCachedMesh(mf.sharedMesh)) Destroy(mf.sharedMesh);
                    }
                    Destroy(m_GeneratedRoot);
                }
                if (ownsRootMesh) Destroy(rootMesh);
            }
#if UNITY_EDITOR
            else if (m_GeneratedRoot)
            {
                Undo.DestroyObjectImmediate(m_GeneratedRoot);
            }
#endif
            else if (m_GeneratedRoot)
            {
                DestroyImmediate(m_GeneratedRoot);
            }

            m_GeneratedRoot = null;
            if (ownsRootMesh) rootFilter.sharedMesh = null;
        }

        // Only meshes this component created (in memory) may be destroyed; never a mesh asset the user assigned.
        private static bool IsGeneratedMesh(Mesh mesh)
        {
#if UNITY_EDITOR
            return !EditorUtility.IsPersistent(mesh);
#else
            return true;
#endif
        }

        /// <summary>
        /// Positions every character with the font's advance widths, kerning, line breaks and tabs. Pure layout:
        /// creates no objects or meshes, so it is cheap to run on every Generate().
        /// </summary>
        private List<GlyphPlacement> LayoutText(Typeface typeface, float scale)
        {
            var placements = new List<GlyphPlacement>(m_Text.Length);
            var pen = Vector3.zero;

            // scale: m_Size is the em size in Unity units, so text has the same size for any font regardless of UnitsPerEm.
            var lineHeight = GetLineHeight(typeface) * scale * m_LineSpacing;
            var spaceAdvance = GetAdvanceWidth(typeface, typeface.GetGlyphIndex(' ')) * scale;
            var missing = new List<char>();

            var kerning = m_Kerning ? GetKerning(typeface) : null;
            int previousGlyph = -1;     // glyph index of the previous character, -1 when the run was broken
            int previousPlacement = -1; // its index in placements

            for (int i = 0; i < m_Text.Length; i++)
            {
                char ch = m_Text[i];

                //Control characters have no glyph: \n starts a new line (text runs along +X, lines go down -Z),
                //\t advances 4 spaces, others take no space. Without this they were drawn as "missing glyph" boxes.
                if (ch == '\n' || ch == '\t' || char.IsControl(ch) || char.IsSurrogate(ch))
                {
                    placements.Add(new GlyphPlacement { character = ch, glyphIndex = -1, position = pen });
                    if (ch == '\n')
                    {
                        pen.x = 0f;
                        pen.z -= lineHeight;
                    }
                    else if (ch == '\t')
                    {
                        pen.x += 4f * spaceAdvance + m_Spacing;
                    }
                    previousGlyph = -1; // no kerning across lines, tabs or control characters
                    continue;
                }

                ushort glyphIndex = typeface.GetGlyphIndex(ch);
                if (glyphIndex == 0 && !char.IsWhiteSpace(ch) && !missing.Contains(ch))
                {
                    missing.Add(ch); // still drawn as the font's "missing glyph" (.notdef) shape
                }

                //Kerning: adjust the previous glyph's advance (and any placement offsets) for this pair, e.g. "AV", "To"
                var offset = Vector3.zero;
                float extraAdvance = 0f;
                if (kerning != null && previousGlyph >= 0)
                {
                    var pair = kerning.GetAdjustment((ushort)previousGlyph, glyphIndex);
                    if (!pair.IsZero)
                    {
                        pen.x += pair.FirstAdvance * scale;
                        var previous = placements[previousPlacement];
                        previous.position += new Vector3(pair.FirstOffsetX * scale, 0f, pair.FirstOffsetY * scale);
                        placements[previousPlacement] = previous;
                        offset = new Vector3(pair.SecondOffsetX * scale, 0f, pair.SecondOffsetY * scale);
                        extraAdvance = pair.SecondAdvance * scale;
                    }
                }

                placements.Add(new GlyphPlacement { character = ch, glyphIndex = glyphIndex, position = pen + offset });

                //Move the pen by the font's advance width.
                //The advance includes the side bearings, so it works for narrow glyphs and spaces alike.
                pen.x += GetAdvanceWidth(typeface, glyphIndex) * scale + m_Spacing + extraAdvance;
                previousGlyph = glyphIndex;
                previousPlacement = placements.Count - 1;
            }

            if (missing.Count > 0)
            {
                var fontName = m_Font != null ? m_Font.name : "TextMesh3D";
                Debug.LogWarning($"TextMesh3D: font '{fontName}' has no glyph for: {string.Join(" ", missing.Select(c => $"'{c}' (U+{(int)c:X4})"))}", this);
            }
            return placements;
        }

        private static string GetCharacterObjectName(char ch)
        {
            switch (ch)
            {
                case ' ': return "Space";
                case '\n': return "Line Break";
                case '\t': return "Tab";
            }
            if (char.IsControl(ch) || char.IsSurrogate(ch)) return $"Control U+{(int)ch:X4}";
            return "Glyph - " + ch;
        }

        /// <summary>Baseline-to-baseline distance in font units.</summary>
        private static float GetLineHeight(Typeface typeface)
        {
            float height = typeface.Ascender - typeface.Descender + typeface.LineGap;
            return height > 0f ? height : (typeface.UnitsPerEm > 0 ? typeface.UnitsPerEm * 1.2f : 1200f);
        }

        /// <summary>Font units to Unity units: one em (UnitsPerEm font units) becomes <paramref name="size"/> units.</summary>
        internal static float GetFontScale(Typeface typeface, float size)
        {
            var unitsPerEm = typeface.UnitsPerEm > 0 ? typeface.UnitsPerEm : 1000f;
            return size / unitsPerEm;
        }

        /// <summary>Horizontal advance in font units, from the font's hmtx table.</summary>
        internal static float GetAdvanceWidth(Typeface typeface, ushort glyphIndex)
        {
            float advance = typeface.GetAdvanceWidthFromGlyphIndex(glyphIndex);
            if (advance > 0f) return advance;

            // Some fonts report 0 for a missing glyph; fall back to a quarter em so text doesn't collapse.
            return typeface.UnitsPerEm > 0 ? typeface.UnitsPerEm * 0.25f : 250f;
        }

        // DestroyImmediate is only valid in edit mode; use Destroy while playing.
        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
