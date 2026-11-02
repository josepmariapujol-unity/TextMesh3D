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
            currentSpline = splineContainer.AddSpline();
        }

        public void EndRead()
        {
            Debug.Assert(currentSpline.Count == 0);
            splineContainer.RemoveSpline(currentSpline);
            currentSpline = null;
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

        public void Curve3(float x1, float y1, float x2, float y2)
        {
            currentKnot.TangentOut = new float3(x1, 0, y1) - currentKnot.Position;
            currentSpline.Add(currentKnot, TangentMode.Broken);
            MoveTo(x2, y2);
        }

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
            currentSpline = null;
            currentSpline = splineContainer.AddSpline();
        }

        private ISplineContainer splineContainer { get; set; }
        private BezierKnot currentKnot;
        private Spline currentSpline;
    }
}


