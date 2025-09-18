using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;

namespace LayerSync.Core.TextRecognition
{
    /// <summary>
    /// Represents a cluster of geometric curves that are likely to form a single character.
    /// </summary>
    public class CharacterCluster
    {
        public List<Curve> Curves { get; }
        public Extents3d BoundingBox { get; private set; }

        public CharacterCluster(Curve initialCurve)
        {
            Curves = new List<Curve> { initialCurve };
            try
            {
                // Use the precise geometric extents if available
                BoundingBox = (Extents3d)initialCurve.GeometricExtents;
            }
            catch (Exception)
            {
                // Fallback for curves that might not have extents available (e.g., not in database yet)
                // This creates a simple box from start to end, which is better than nothing.
                if (initialCurve.StartPoint != null && initialCurve.EndPoint != null)
                {
                    BoundingBox = new Extents3d(initialCurve.StartPoint, initialCurve.EndPoint);
                }
                else
                {
                    // If all else fails, create an invalid extent that can be updated later.
                    BoundingBox = new Extents3d(Point3d.Origin, Point3d.Origin);
                }
            }
        }

        /// <summary>
        /// Adds a curve to the cluster and expands the bounding box to include it.
        /// </summary>
        public void AddCurve(Curve curve)
        {
            Curves.Add(curve);
            try
            {
                BoundingBox.AddExtents(curve.GeometricExtents);
            }
            catch (Exception) { /* Ignore curves that fail to provide extents */ }
        }

        /// <summary>
        /// Merges another cluster into this one.
        /// </summary>
        public void MergeCluster(CharacterCluster other)
        {
            Curves.AddRange(other.Curves);
            BoundingBox.AddExtents(other.BoundingBox);
        }

        /// <summary>
        /// Checks if a given curve is close enough to this cluster to be considered part of it.
        /// </summary>
        public bool IsNearby(Curve curve, double tolerance)
        {
            try
            {
                var curveBox = curve.GeometricExtents;

                // Create expanded versions of both bounding boxes
                var expandedThisBox = new Extents3d(
                    new Point3d(BoundingBox.MinPoint.X - tolerance, BoundingBox.MinPoint.Y - tolerance, 0),
                    new Point3d(BoundingBox.MaxPoint.X + tolerance, BoundingBox.MaxPoint.Y + tolerance, 0)
                );

                // Check for intersection on the XY plane. Z-values are ignored for 2D text recognition.
                return expandedThisBox.MinPoint.X <= curveBox.MaxPoint.X &&
                       expandedThisBox.MaxPoint.X >= curveBox.MinPoint.X &&
                       expandedThisBox.MinPoint.Y <= curveBox.MaxPoint.Y &&
                       expandedThisBox.MaxPoint.Y >= curveBox.MinPoint.Y;
            }
            catch (Exception)
            {
                return false; // Cannot determine proximity if extents are unavailable.
            }
        }

        /// <summary>
        /// Checks if another cluster is close enough to this cluster to be merged.
        /// </summary>
        public bool IsNearby(CharacterCluster otherCluster, double tolerance)
        {
            try
            {
                var otherBox = otherCluster.BoundingBox;

                // Create expanded versions of both bounding boxes
                var expandedThisBox = new Extents3d(
                    new Point3d(BoundingBox.MinPoint.X - tolerance, BoundingBox.MinPoint.Y - tolerance, 0),
                    new Point3d(BoundingBox.MaxPoint.X + tolerance, BoundingBox.MaxPoint.Y + tolerance, 0)
                );

                // Check for intersection on the XY plane.
                return expandedThisBox.MinPoint.X <= otherBox.MaxPoint.X &&
                       expandedThisBox.MaxPoint.X >= otherBox.MinPoint.X &&
                       expandedThisBox.MinPoint.Y <= otherBox.MaxPoint.Y &&
                       expandedThisBox.MaxPoint.Y >= otherBox.MinPoint.Y;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
