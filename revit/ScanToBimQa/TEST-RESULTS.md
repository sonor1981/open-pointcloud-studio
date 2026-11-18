# Test and build results

Run: 2026-11-18 on Linux x64 (Ubuntu 24.04), .NET SDK 10.0.115, base tag v2026.45.1 (322c955).

## Core tests on .NET 10

```
PASS  AcceptanceTests.AcceptanceSuite_AllRequiredCasesInOneRotatedCloud (252 ms)
PASS  ClassificationTests.ElementOutsideRegion_IsNotReported (11 ms)
PASS  ClassificationTests.OccludedPipe_IsInsufficientNotPassOrFail (17 ms)
PASS  ClassificationTests.PipeExtendingOutsideSectionBox_CoverageCountsOnlyInside (15 ms)
PASS  ClassificationTests.PipeOffset40mm_Fails (20 ms)
PASS  ClassificationTests.PipeWithinTolerance_Passes (15 ms)
PASS  ClassificationTests.ScatteredNoise_GivesNoMissingCluster (17 ms)
PASS  ClassificationTests.Settings_AreValidated (0 ms)
PASS  GeometryTests.Bvh_NearestMatchesBruteForce (9 ms)
PASS  GeometryTests.CloudFilterPlanes_AgreeWithBoxInCloudCoordinates (3 ms)
PASS  GeometryTests.RotatedSectionBox_ContainmentUsesLocalFrame (0 ms)
PASS  GeometryTests.Transform_InverseRoundTrip (0 ms)
PASS  GeometryTests.Triangle_ClosestPointMatchesBruteForce (9 ms)
PASS  GeometryTests.Units_FeetAndMillimetres (0 ms)
PASS  MissingFittingTests.MissingElbow_IsReportedAsClusterAtOpenEnds_AndPipesStillPass (30 ms)
PASS  MissingFittingTests.MissingElbow_NaiveNearestSurfaceLosesPointsToAdjacentPipes (48 ms)
PASS  MissingFittingTests.MissingElbow_SmallerThresholdKeepsMorePointsAwayFromWall (85 ms)
PASS  MissingFittingTests.MissingElbowNextToWall_KnownLimitation_WallWithinThresholdAbsorbsPoints (65 ms)
PASS  MultiDisciplineTests.WallsSlabColumnsBeams_ClassifiedPerDiscipline (175 ms)
PASS  PresentationTests.Heatmap_ColoursFollowTolerance (1 ms)
PASS  PresentationTests.ResultFilter_AndCsv (2 ms)
PASS  PresentationTests.ZoomTargets_FrameElementAndCluster (22 ms)
PASS  TransformTests.RotatedTranslatedCloud_AndRotatedSectionBox_GiveSameResultAsModelFrame (70 ms)
PASS  TransformTests.WrongTransform_WouldBeDetected (2 ms)
24/24 tests passed
```

## Core tests on .NET 8

```
PASS  AcceptanceTests.AcceptanceSuite_AllRequiredCasesInOneRotatedCloud (346 ms)
PASS  ClassificationTests.ElementOutsideRegion_IsNotReported (12 ms)
PASS  ClassificationTests.OccludedPipe_IsInsufficientNotPassOrFail (18 ms)
PASS  ClassificationTests.PipeExtendingOutsideSectionBox_CoverageCountsOnlyInside (14 ms)
PASS  ClassificationTests.PipeOffset40mm_Fails (23 ms)
PASS  ClassificationTests.PipeWithinTolerance_Passes (18 ms)
PASS  ClassificationTests.ScatteredNoise_GivesNoMissingCluster (16 ms)
PASS  ClassificationTests.Settings_AreValidated (0 ms)
PASS  GeometryTests.Bvh_NearestMatchesBruteForce (7 ms)
PASS  GeometryTests.CloudFilterPlanes_AgreeWithBoxInCloudCoordinates (2 ms)
PASS  GeometryTests.RotatedSectionBox_ContainmentUsesLocalFrame (0 ms)
PASS  GeometryTests.Transform_InverseRoundTrip (0 ms)
PASS  GeometryTests.Triangle_ClosestPointMatchesBruteForce (6 ms)
PASS  GeometryTests.Units_FeetAndMillimetres (0 ms)
PASS  MissingFittingTests.MissingElbow_IsReportedAsClusterAtOpenEnds_AndPipesStillPass (26 ms)
PASS  MissingFittingTests.MissingElbow_NaiveNearestSurfaceLosesPointsToAdjacentPipes (48 ms)
PASS  MissingFittingTests.MissingElbow_SmallerThresholdKeepsMorePointsAwayFromWall (86 ms)
PASS  MissingFittingTests.MissingElbowNextToWall_KnownLimitation_WallWithinThresholdAbsorbsPoints (87 ms)
PASS  MultiDisciplineTests.WallsSlabColumnsBeams_ClassifiedPerDiscipline (187 ms)
PASS  PresentationTests.Heatmap_ColoursFollowTolerance (3 ms)
PASS  PresentationTests.ResultFilter_AndCsv (2 ms)
PASS  PresentationTests.ZoomTargets_FrameElementAndCluster (38 ms)
PASS  TransformTests.RotatedTranslatedCloud_AndRotatedSectionBox_GiveSameResultAsModelFrame (121 ms)
PASS  TransformTests.WrongTransform_WouldBeDetected (5 ms)
24/24 tests passed
```

## Add-in builds

Compiled against the RevitAPI.dll / RevitAPIUI.dll of each installed version; 0 errors, 0 warnings.

| Revit | RevitAPI | Target framework | Package | SHA-256 |
| --- | --- | --- | --- | --- |
| 2023 | 23.0.0.0 | net48 | ScanToBimQa-Revit2023.zip | `e439c08157af75be…` |
| 2024 | 24.2.0.0 | net48 | ScanToBimQa-Revit2024.zip | `f9da75af545b63b0…` |
| 2025 | 25.5.0.0 | net10.0 | ScanToBimQa-Revit2025.zip | `4e17250510acc41a…` |
| 2026 | 26.5.0.0 | net10.0 | ScanToBimQa-Revit2026.zip | `d5685aeef7c48f79…` |
| 2027 | 27.3.0.0 | net10.0 | ScanToBimQa-Revit2027.zip | `d6d83e1e6e6d0c13…` |

## Run in Revit 2026.5

See "Revit 2026.5 results" in README.md: checklist items 1–3 and 5–12 passed on a real ACC project (item 7 with the minimum cluster size of 30, now the default); item 4 (rotated section box) is covered by core tests only. Revit 2023, 2024, 2025 and 2027 were compiled but not run.
