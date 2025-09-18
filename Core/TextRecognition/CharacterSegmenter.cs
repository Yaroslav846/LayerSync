using Autodesk.AutoCAD.DatabaseServices;
using System.Collections.Generic;
using System.Linq;

namespace LayerSync.Core.TextRecognition
{
    /// <summary>
    /// Groups a list of curves into character clusters based on proximity.
    /// </summary>
    public class CharacterSegmenter
    {
        private readonly double _tolerance;

        /// <summary>
        /// Initializes the segmenter with a proximity tolerance.
        /// A good tolerance is often a fraction of the average text height.
        /// </summary>
        /// <param name="tolerance">The distance by which to expand bounding boxes when checking for neighbors.</param>
        public CharacterSegmenter(double tolerance = 0.5)
        {
            // NOTE: A fixed tolerance is a simplification. A more robust solution
            // would calculate this dynamically based on the average size of the input curves.
            // For example: tolerance = curves.Average(c => c.GeometricExtents.MaxPoint.Y - c.GeometricExtents.MinPoint.Y) * 0.2;
            _tolerance = tolerance;
        }

        /// <summary>
        /// Segments the input curves into a list of character clusters.
        /// </summary>
        /// <param name="curves">The raw list of curves selected by the user.</param>
        /// <returns>A list of CharacterCluster objects, where each object represents a potential character.</returns>
        public List<CharacterCluster> Segment(List<Curve> curves)
        {
            if (curves == null || curves.Count == 0)
            {
                return new List<CharacterCluster>();
            }

            // 1. Initialize each curve as its own cluster.
            // We filter out any curves that failed to produce a valid bounding box.
            var clusters = curves
                .Select(c => new CharacterCluster(c))
                .Where(c => c.BoundingBox.MinPoint != c.BoundingBox.MaxPoint)
                .ToList();

            // 2. Iteratively merge clusters that are close to each other.
            bool wasMergedInLastPass;
            do
            {
                wasMergedInLastPass = false;
                for (int i = 0; i < clusters.Count; i++)
                {
                    for (int j = clusters.Count - 1; j > i; j--) // Iterate backwards for safe removal
                    {
                        if (clusters[i].IsNearby(clusters[j], _tolerance))
                        {
                            // Merge cluster j into cluster i
                            clusters[i].MergeCluster(clusters[j]);

                            // Remove the now-merged cluster j
                            clusters.RemoveAt(j);

                            wasMergedInLastPass = true;
                        }
                    }
                }
            } while (wasMergedInLastPass); // 3. Repeat until no more merges occur in a full pass.

            return clusters;
        }
    }
}
