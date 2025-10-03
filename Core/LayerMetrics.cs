namespace LayerSync.Core
{
    /// <summary>
    /// Represents aggregated metrics for a single layer, including object count and total length of geometric entities.
    /// </summary>
    public struct LayerMetrics
    {
        /// <summary>
        /// The total number of objects on the layer.
        /// </summary>
        public int ObjectCount { get; set; }

        /// <summary>
        /// The cumulative length of all curve-based objects (lines, arcs, polylines, etc.) on the layer.
        /// </summary>
        public double TotalLength { get; set; }
    }
}