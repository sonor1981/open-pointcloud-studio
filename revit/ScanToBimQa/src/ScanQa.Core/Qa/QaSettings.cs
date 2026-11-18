using System;

namespace ScanQa.Core.Qa
{
    /// <summary>All lengths in millimetres.</summary>
    public sealed class QaSettings
    {
        /// <summary>Accepted absolute deviation. An element passes when its P95 deviation is within this value.</summary>
        public double ToleranceMm { get; set; } = 20;

        /// <summary>
        /// Scan points farther than this from every model surface are "unexplained" and feed the Suspected
        /// Missing layer. It is also the largest deviation that can be measured on an element: a point beyond
        /// it is never attributed to an element. Must be larger than <see cref="ToleranceMm"/>.
        /// </summary>
        public double MissingThresholdMm { get; set; } = 100;

        /// <summary>
        /// How far beyond the end of a straight pipe/duct a scan point may lie and still be attributed to it.
        /// Null uses <see cref="ToleranceMm"/>. Keeps the points of a missing fitting off the adjacent pipe.
        /// </summary>
        public double? AxialEndMarginMm { get; set; }

        /// <summary>Use the axial end rule for straight elements. Disable only to reproduce the naive nearest-surface result.</summary>
        public bool UseAxialEndRule { get; set; } = true;

        /// <summary>Percentile used for the pass/fail decision.</summary>
        public double DecisionPercentile { get; set; } = 95;

        /// <summary>Fewer attributed points than this gives Insufficient Scan Data.</summary>
        public int MinPoints { get; set; } = 30;

        /// <summary>Lower share of the element surface (inside the region) with scan points gives Insufficient Scan Data.</summary>
        public double MinCoverage { get; set; } = 0.25;

        /// <summary>Cell size for the coverage measure.</summary>
        public double CoverageCellMm { get; set; } = 50;

        /// <summary>Voxel size for grouping unexplained points.</summary>
        public double ClusterCellMm { get; set; } = 100;

        /// <summary>A voxel needs this many unexplained points to take part in a cluster (rejects scattered noise).</summary>
        public int MinPointsPerCell { get; set; } = 5;

        /// <summary>Smallest reported Suspected Missing cluster.</summary>
        public int MinClusterPoints { get; set; } = 30;

        /// <summary>Search distance from a cluster to an unconnected element end.</summary>
        public double OpenEndSearchMm { get; set; } = 600;

        public double EffectiveAxialMarginMm => AxialEndMarginMm ?? ToleranceMm;

        public void Validate()
        {
            if (ToleranceMm <= 0) throw new ArgumentException("Tolerance must be greater than 0 mm.");
            if (MissingThresholdMm <= ToleranceMm)
                throw new ArgumentException("Missing threshold must be greater than the tolerance.");
            if (DecisionPercentile <= 0 || DecisionPercentile > 100)
                throw new ArgumentException("Decision percentile must be in (0, 100].");
            if (MinCoverage < 0 || MinCoverage > 1) throw new ArgumentException("Minimum coverage must be between 0 and 1.");
            if (CoverageCellMm <= 0 || ClusterCellMm <= 0) throw new ArgumentException("Cell sizes must be greater than 0 mm.");
            if (MinPoints < 1 || MinPointsPerCell < 1 || MinClusterPoints < 1)
                throw new ArgumentException("Point count limits must be at least 1.");
        }
    }
}
