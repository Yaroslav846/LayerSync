using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Linq;

namespace LayerSync.Core.TextRecognition
{
    /// <summary>
    /// Extracts a feature vector from a CharacterCluster by normalizing it and rasterizing it onto a grid.
    /// </summary>
    public class CharacterFeatureExtractor
    {
        private readonly int _gridWidth;
        private readonly int _gridHeight;

        public CharacterFeatureExtractor(int gridWidth = 8, int gridHeight = 10)
        {
            _gridWidth = gridWidth;
            _gridHeight = gridHeight;
        }

        public bool[] ExtractFeatures(CharacterCluster cluster)
        {
            var features = new bool[_gridWidth * _gridHeight];
            var box = cluster.BoundingBox;

            if (box.MinPoint == box.MaxPoint)
            {
                return features; // Cannot process a zero-size cluster
            }

            var boxSize = box.MaxPoint - box.MinPoint;
            double scale = Math.Max(boxSize.X, boxSize.Y);
            if (scale < 1e-6) return features; // Avoid division by zero

            var cellWidth = 1.0 / _gridWidth;
            var cellHeight = 1.0 / _gridHeight;
            double hitThreshold = (cellWidth + cellHeight) / 4.0; // A point is a "hit" if a curve is within this normalized distance

            for (int y = 0; y < _gridHeight; y++)
            {
                for (int x = 0; x < _gridWidth; x++)
                {
                    // Find the center of the grid cell in normalized coordinates (0 to 1)
                    var normalizedCellCenter = new Point2d(
                        (x + 0.5) * cellWidth,
                        (y + 0.5) * cellHeight
                    );

                    // Transform this normalized point back into the original drawing coordinates
                    var originalCoordPoint = new Point3d(
                        box.MinPoint.X + (normalizedCellCenter.X * scale),
                        box.MinPoint.Y + (normalizedCellCenter.Y * scale),
                        0
                    );

                    // Check if this point is close to any curve in the cluster
                    bool isHit = false;
                    foreach (var curve in cluster.Curves)
                    {
                        try
                        {
                            var closestPoint = curve.GetClosestPointTo(originalCoordPoint, false);
                            double distance = closestPoint.DistanceTo(originalCoordPoint);

                            // Compare distance in normalized space
                            if (distance / scale < hitThreshold)
                            {
                                isHit = true;
                                break;
                            }
                        }
                        catch (Exception) { /* Some curves might fail GetClosestPointTo */ }
                    }

                    // The feature array is stored row-by-row, but we fill it "bottom-up" to match reading order.
                    features[(_gridHeight - 1 - y) * _gridWidth + x] = isHit;
                }
            }

            return features;
        }
    }
}
