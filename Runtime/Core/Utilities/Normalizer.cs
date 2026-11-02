using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    /// <summary>
    /// Handles normalization and unnormalization of 2D point data within a unit square [0,1]².
    /// Helps reduce floating point errors in geometric algorithms.
    /// </summary>
    public class Normalizer2
    {
        private readonly float dMax;
        private readonly AABB2 boundingBox;

        /// <summary>
        /// Constructs a Normalizer from a list of points. All normalization is based on this bounding box.
        /// </summary>
        public Normalizer2(List<Vector2> points)
        {
            boundingBox = new AABB2(points);
            dMax = CalculateDMax(boundingBox);
        }

        /// <summary>
        /// Calculates the maximum side length of the bounding box (used for normalization).
        /// </summary>
        private float CalculateDMax(AABB2 box)
        {
            return Mathf.Max(box.max.x - box.min.x, box.max.y - box.min.y);
        }

        //
        // Normalize
        //

        /// <summary>
        /// Normalizes a point into the unit square based on the bounding box.
        /// </summary>
        public Vector2 Normalize(Vector2 point)
        {
            return new Vector2(
                (point.x - boundingBox.min.x) / dMax,
                (point.y - boundingBox.min.y) / dMax
            );
        }

        /// <summary>
        /// Normalizes a list of 2D points.
        /// </summary>
        public List<Vector2> Normalize(List<Vector2> points)
        {
            List<Vector2> normalized = new(points.Count);
            foreach (var p in points)
                normalized.Add(Normalize(p));
            return normalized;
        }

        /// <summary>
        /// Normalizes a HashSet of 2D points (optional).
        /// </summary>
        public HashSet<Vector2> Normalize(HashSet<Vector2> points)
        {
            var normalized = new HashSet<Vector2>();
            foreach (var p in points)
                normalized.Add(Normalize(p));
            return normalized;
        }

        //
        // Unnormalize
        //

        /// <summary>
        /// Reverts a normalized point to its original position.
        /// </summary>
        public Vector2 UnNormalize(Vector2 point)
        {
            return new Vector2(
                point.x * dMax + boundingBox.min.x,
                point.y * dMax + boundingBox.min.y
            );
        }

        /// <summary>
        /// Unnormalizes a HalfEdgeData2 by adjusting all vertex positions.
        /// </summary>
        public HalfEdgeData2 UnNormalize(HalfEdgeData2 data)
        {
            foreach (var v in data.vertices)
                v.position = UnNormalize(v.position);
            return data;
        }
    }
}
/*
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Softgraph.TextMesh3D.Runtime
{
    //To avoid floating point precision issues, it's common to normalize all data to range 0-1
    public class Normalizer2
    {
        private float dMax;
        private AABB2 boundingBox;

        public Normalizer2(List<Vector2> points)
        {
            boundingBox = new AABB2(points);
            dMax = CalculateDMax(boundingBox);
        }

        //From "A fast algorithm for constructing Delaunay triangulations in the plane" by Sloan
        //boundingBox is the rectangle that covers all original points before normalization
        public float CalculateDMax(AABB2 boundingBox)
        {
            float dMax = Mathf.Max(boundingBox.max.x - boundingBox.min.x, boundingBox.max.y - boundingBox.min.y);

            return dMax;
        }

        //
        // Normalize stuff
        //

        public Vector2 Normalize(Vector2 point)
        {
            float x = (point.x - boundingBox.min.x) / dMax;
            float y = (point.y - boundingBox.min.y) / dMax;

            Vector2 pNormalized = new Vector2(x, y);

            return pNormalized;
        }

        public List<Vector2> Normalize(List<Vector2> points)
        {
            List<Vector2> normalizedPoints = new List<Vector2>();

            foreach (Vector2 p in points)
            {
                normalizedPoints.Add(Normalize(p));
            }

            return normalizedPoints;
        }

        //
        // UnNormalize stuff
        //

        public Vector2 UnNormalize(Vector2 point)
        {
            float x = (point.x * dMax) + boundingBox.min.x;
            float y = (point.y * dMax) + boundingBox.min.y;
            Vector2 pUnNormalized = new Vector2(x, y);

            return pUnNormalized;
        }

        //HalfEdgeData2
        public HalfEdgeData2 UnNormalize(HalfEdgeData2 data)
        {
            foreach (HalfEdgeVertex2 v in data.vertices)
            {
                Vector2 vUnNormalized = UnNormalize(v.position);
                v.position = vUnNormalized;
            }

            return data;
        }
    }
}
*/
