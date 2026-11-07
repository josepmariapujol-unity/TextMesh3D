using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using Typography.OpenFont;

namespace Softgraph.TextMesh3D.Runtime
{
    public class GlyphSplineReader : IGlyphTranslator
    {
        public GlyphSplineReader(ISplineContainer splineContainer)
        {
            this.splineContainer = splineContainer;
        }

        /// <summary>Reads the outlines into a plain list (no SplineContainer component or GameObject needed).</summary>
        public GlyphSplineReader(List<Spline> splines)
        {
            splineList = splines;
        }

        public void Read(Glyph glyph, float scale = 1)
        {
            if (glyph.IsCffGlyph)
            {
                this.Read(null, glyph.GetCff1GlyphData(), scale);
            }
            else
            {
                this.Read(glyph.GlyphPoints, glyph.EndPoints, scale);
            }
        }

        public void BeginRead(int contourCount)
        {
            currentSpline = AddSpline();
        }

        public void EndRead()
        {
            Debug.Assert(currentSpline.Count == 0);
            RemoveSpline(currentSpline);
            currentSpline = null;
        }

        private Spline AddSpline()
        {
            if (splineList == null) return splineContainer.AddSpline();
            var spline = new Spline();
            splineList.Add(spline);
            return spline;
        }

        private void RemoveSpline(Spline spline)
        {
            if (splineList == null) splineContainer.RemoveSpline(spline);
            else splineList.Remove(spline);
        }

        public void MoveTo(float x0, float y0)
        {
            currentKnot = new BezierKnot(new float3(x0, 0, y0));
        }

        public void LineTo(float x1, float y1)
        {
            currentSpline.Add(currentKnot, TangentMode.Broken);
            MoveTo(x1, y1);
        }

        /// <summary>
        /// Quadratic Bézier (TrueType) from the current point P0 through control Q1 to P2, stored as the exactly
        /// equivalent cubic: control points P0 + ⅔(Q1 − P0) and P2 + ⅔(Q1 − P2).
        /// </summary>
        public void Curve3(float x1, float y1, float x2, float y2)
        {
            var control = new float3(x1, 0, y1);
            currentKnot.TangentOut = k_QuadraticToCubic * (control - currentKnot.Position);
            currentSpline.Add(currentKnot, TangentMode.Broken);
            MoveTo(x2, y2);
            currentKnot.TangentIn = k_QuadraticToCubic * (control - currentKnot.Position);
        }

        private const float k_QuadraticToCubic = 2f / 3f;

        public void Curve4(float x1, float y1, float x2, float y2, float x3, float y3)
        {
            currentKnot.TangentOut = new float3(x1, 0, y1) - currentKnot.Position;
            currentSpline.Add(currentKnot, TangentMode.Broken);
            MoveTo(x3, y3);
            currentKnot.TangentIn = new float3(x2, 0, y2) - currentKnot.Position;
        }

        public void CloseContour()
        {
            if (currentSpline.Count > 0 && math.distancesq(currentKnot.Position, currentSpline[0].Position) < math.EPSILON)
            {
                BezierKnot firstKnot = currentSpline[0];
                firstKnot.TangentIn = currentKnot.TangentIn;
                currentSpline.SetKnot(0, firstKnot);
            }
            else
            {
                currentSpline.Add(currentKnot, TangentMode.Broken);
            }
            currentSpline.Closed = true;
            currentSpline = AddSpline();
        }

        private ISplineContainer splineContainer { get; set; }
        private readonly List<Spline> splineList;
        private BezierKnot currentKnot;
        private Spline currentSpline;
    }
}


