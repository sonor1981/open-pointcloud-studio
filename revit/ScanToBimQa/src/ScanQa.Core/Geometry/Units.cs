namespace ScanQa.Core.Geometry
{
    /// <summary>Revit stores lengths in decimal feet. All QA thresholds and results are in millimetres.</summary>
    public static class Units
    {
        public const double MmPerFoot = 304.8;

        public static double MmToFeet(double mm) => mm / MmPerFoot;
        public static double FeetToMm(double feet) => feet * MmPerFoot;
    }
}
