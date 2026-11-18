using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ScanQa.Core.Geometry;

namespace ScanQa.Core.Qa
{
    public readonly struct Rgb
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
    }

    public sealed class LegendEntry
    {
        public string Label { get; set; } = "";
        public Rgb Color { get; set; }
    }

    /// <summary>Deviation colour ramp keyed to the tolerance: green 0, yellow at tolerance, red at 2× tolerance and beyond.</summary>
    public static class Heatmap
    {
        public static readonly Rgb Green = new Rgb(0, 176, 80);
        public static readonly Rgb Yellow = new Rgb(255, 214, 0);
        public static readonly Rgb Red = new Rgb(220, 20, 20);
        public static readonly Rgb Missing = new Rgb(230, 0, 230);

        public static Rgb ForDeviation(double deviationMm, double toleranceMm)
        {
            if (double.IsNaN(deviationMm)) return Missing;
            double t = Math.Max(0, deviationMm / toleranceMm);
            if (t <= 1) return Lerp(Green, Yellow, t);
            if (t <= 2) return Lerp(Yellow, Red, t - 1);
            return Red;
        }

        public static IReadOnlyList<LegendEntry> Legend(double toleranceMm)
        {
            string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            return new[]
            {
                new LegendEntry { Label = "0 mm", Color = ForDeviation(0, toleranceMm) },
                new LegendEntry { Label = $"{F(toleranceMm / 2)} mm", Color = ForDeviation(toleranceMm / 2, toleranceMm) },
                new LegendEntry { Label = $"{F(toleranceMm)} mm (tolerance)", Color = ForDeviation(toleranceMm, toleranceMm) },
                new LegendEntry { Label = $"{F(toleranceMm * 1.5)} mm", Color = ForDeviation(toleranceMm * 1.5, toleranceMm) },
                new LegendEntry { Label = $"≥ {F(toleranceMm * 2)} mm", Color = ForDeviation(toleranceMm * 2, toleranceMm) },
                new LegendEntry { Label = "Suspected Missing", Color = Missing },
            };
        }

        private static Rgb Lerp(Rgb a, Rgb b, double t) => new Rgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Boxes to frame in the view when the user zooms to an element or a cluster.</summary>
    public static class ZoomTargets
    {
        /// <summary>Padding: 25 % of the largest dimension, at least <paramref name="minPaddingMm"/>.</summary>
        public static Aabb Frame(Aabb target, double minPaddingMm = 300)
        {
            var size = target.Size;
            double largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
            double pad = Math.Max(largest * 0.25, Units.MmToFeet(minPaddingMm));
            return target.Inflate(pad);
        }

        public static Aabb ForElement(ElementResult r) => Frame(r.Bounds);
        public static Aabb ForCluster(MissingCluster c) => Frame(c.Bounds);
    }

    public enum StatusFilter
    {
        All,
        Problems,
        Fail,
        Insufficient,
        Pass,
    }

    public static class ResultFilter
    {
        public static IEnumerable<ElementResult> Apply(IEnumerable<ElementResult> rows, StatusFilter filter)
        {
            switch (filter)
            {
                case StatusFilter.Problems: return rows.Where(r => r.Status != QaStatus.Pass);
                case StatusFilter.Fail: return rows.Where(r => r.Status == QaStatus.Fail);
                case StatusFilter.Insufficient: return rows.Where(r => r.Status == QaStatus.InsufficientScanData);
                case StatusFilter.Pass: return rows.Where(r => r.Status == QaStatus.Pass);
                default: return rows;
            }
        }

        /// <summary>Problems first (Fail by P95 descending, then Insufficient), then Pass.</summary>
        public static IEnumerable<ElementResult> DefaultOrder(IEnumerable<ElementResult> rows) =>
            rows.OrderBy(r => r.Status == QaStatus.Fail ? 0 : r.Status == QaStatus.InsufficientScanData ? 1 : 2)
                .ThenByDescending(r => double.IsNaN(r.P95Mm) ? 0 : r.P95Mm);

        /// <summary>Null or empty discipline keeps every row.</summary>
        public static IEnumerable<ElementResult> ByDiscipline(IEnumerable<ElementResult> rows, string discipline) =>
            string.IsNullOrEmpty(discipline) ? rows : rows.Where(r => r.Discipline == discipline);

        public static string StatusText(QaStatus s) =>
            s == QaStatus.InsufficientScanData ? "Insufficient Scan Data" : s.ToString();
    }

    public static class CsvExport
    {
        public static string Elements(IEnumerable<ElementResult> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Model,Discipline,ElementId,Category,Name,Points,PercentWithinTolerance,P95Mm,MaxMm,Coverage,Status,Reason");
            foreach (var r in rows)
            {
                sb.Append(Q(r.Source)).Append(',')
                    .Append(Q(r.Discipline)).Append(',')
                    .Append(r.ElementId).Append(',')
                    .Append(Q(r.Category)).Append(',')
                    .Append(Q(r.Name)).Append(',')
                    .Append(r.PointCount).Append(',')
                    .Append(N(r.PercentWithinTolerance)).Append(',')
                    .Append(N(r.P95Mm)).Append(',')
                    .Append(N(r.MaxMm)).Append(',')
                    .Append(N(r.Coverage)).Append(',')
                    .Append(Q(ResultFilter.StatusText(r.Status))).Append(',')
                    .Append(Q(r.Reason)).AppendLine();
            }
            return sb.ToString();
        }

        public static string Clusters(IEnumerable<MissingCluster> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ClusterId,Points,ExtentMm,CentroidXmm,CentroidYmm,CentroidZmm,MinXmm,MinYmm,MinZmm,MaxXmm,MaxYmm,MaxZmm,NearOpenEndOfElementId,Note");
            foreach (var c in rows)
            {
                sb.Append(c.Id).Append(',').Append(c.PointCount).Append(',').Append(N(c.ExtentMm)).Append(',')
                    .Append(N(Units.FeetToMm(c.Centroid.X))).Append(',')
                    .Append(N(Units.FeetToMm(c.Centroid.Y))).Append(',')
                    .Append(N(Units.FeetToMm(c.Centroid.Z))).Append(',')
                    .Append(N(Units.FeetToMm(c.Bounds.Min.X))).Append(',').Append(N(Units.FeetToMm(c.Bounds.Min.Y))).Append(',')
                    .Append(N(Units.FeetToMm(c.Bounds.Min.Z))).Append(',').Append(N(Units.FeetToMm(c.Bounds.Max.X))).Append(',')
                    .Append(N(Units.FeetToMm(c.Bounds.Max.Y))).Append(',').Append(N(Units.FeetToMm(c.Bounds.Max.Z))).Append(',')
                    .Append(c.NearOpenEndOfElementId?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                    .Append(Q(c.Note)).AppendLine();
            }
            return sb.ToString();
        }

        private static string N(double v) => double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
