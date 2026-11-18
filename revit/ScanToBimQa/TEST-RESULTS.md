# Test and build results

Run: 2026-11-18 on Linux x64 (Ubuntu 24.04), .NET SDK 10.0.115, base tag v2026.45.1 (322c955).

## Core tests on .NET 10

```
PASS  AcceptanceTests.AcceptanceSuite_AllRequiredCasesInOneRotatedCloud (266 ms)
PASS  ClassificationTests.ElementOutsideRegion_IsNotReported (11 ms)
PASS  ClassificationTests.OccludedPipe_IsInsufficientNotPassOrFail (19 ms)
PASS  ClassificationTests.PipeExtendingOutsideSectionBox_CoverageCountsOnlyInside (16 ms)
PASS  ClassificationTests.PipeOffset40mm_Fails (20 ms)
PASS  ClassificationTests.PipeWithinTolerance_Passes (15 ms)
PASS  ClassificationTests.ScatteredNoise_GivesNoMissingCluster (17 ms)
PASS  ClassificationTests.Settings_AreValidated (0 ms)
PASS  GeometryTests.Bvh_NearestMatchesBruteForce (8 ms)
PASS  GeometryTests.CloudFilterPlanes_AgreeWithBoxInCloudCoordinates (3 ms)
PASS  GeometryTests.RotatedSectionBox_ContainmentUsesLocalFrame (0 ms)
PASS  GeometryTests.Transform_InverseRoundTrip (0 ms)
PASS  GeometryTests.Triangle_ClosestPointMatchesBruteForce (7 ms)
PASS  GeometryTests.Units_FeetAndMillimetres (0 ms)
PASS  MissingFittingTests.MissingElbow_IsReportedAsClusterAtOpenEnds_AndPipesStillPass (26 ms)
PASS  MissingFittingTests.MissingElbow_NaiveNearestSurfaceLosesPointsToAdjacentPipes (43 ms)
PASS  MissingFittingTests.MissingElbow_SmallerThresholdKeepsMorePointsAwayFromWall (79 ms)
PASS  MissingFittingTests.MissingElbowNextToWall_KnownLimitation_WallWithinThresholdAbsorbsPoints (65 ms)
PASS  PresentationTests.Heatmap_ColoursFollowTolerance (1 ms)
PASS  PresentationTests.ResultFilter_AndCsv (4 ms)
PASS  PresentationTests.ZoomTargets_FrameElementAndCluster (23 ms)
PASS  TransformTests.RotatedTranslatedCloud_AndRotatedSectionBox_GiveSameResultAsModelFrame (80 ms)
PASS  TransformTests.WrongTransform_WouldBeDetected (2 ms)
23/23 tests passed
```

## Core tests on .NET 8

```
PASS  AcceptanceTests.AcceptanceSuite_AllRequiredCasesInOneRotatedCloud (226 ms)
PASS  ClassificationTests.ElementOutsideRegion_IsNotReported (11 ms)
PASS  ClassificationTests.OccludedPipe_IsInsufficientNotPassOrFail (17 ms)
PASS  ClassificationTests.PipeExtendingOutsideSectionBox_CoverageCountsOnlyInside (13 ms)
PASS  ClassificationTests.PipeOffset40mm_Fails (19 ms)
PASS  ClassificationTests.PipeWithinTolerance_Passes (14 ms)
PASS  ClassificationTests.ScatteredNoise_GivesNoMissingCluster (15 ms)
PASS  ClassificationTests.Settings_AreValidated (0 ms)
PASS  GeometryTests.Bvh_NearestMatchesBruteForce (7 ms)
PASS  GeometryTests.CloudFilterPlanes_AgreeWithBoxInCloudCoordinates (2 ms)
PASS  GeometryTests.RotatedSectionBox_ContainmentUsesLocalFrame (0 ms)
PASS  GeometryTests.Transform_InverseRoundTrip (0 ms)
PASS  GeometryTests.Triangle_ClosestPointMatchesBruteForce (7 ms)
PASS  GeometryTests.Units_FeetAndMillimetres (0 ms)
PASS  MissingFittingTests.MissingElbow_IsReportedAsClusterAtOpenEnds_AndPipesStillPass (24 ms)
PASS  MissingFittingTests.MissingElbow_NaiveNearestSurfaceLosesPointsToAdjacentPipes (41 ms)
PASS  MissingFittingTests.MissingElbow_SmallerThresholdKeepsMorePointsAwayFromWall (79 ms)
PASS  MissingFittingTests.MissingElbowNextToWall_KnownLimitation_WallWithinThresholdAbsorbsPoints (63 ms)
PASS  PresentationTests.Heatmap_ColoursFollowTolerance (2 ms)
PASS  PresentationTests.ResultFilter_AndCsv (3 ms)
PASS  PresentationTests.ZoomTargets_FrameElementAndCluster (22 ms)
PASS  TransformTests.RotatedTranslatedCloud_AndRotatedSectionBox_GiveSameResultAsModelFrame (72 ms)
PASS  TransformTests.WrongTransform_WouldBeDetected (2 ms)
23/23 tests passed
```

## Add-in builds

Compiled against the RevitAPI.dll / RevitAPIUI.dll of each installed version; 0 errors, 0 warnings.

| Revit | RevitAPI | Target framework | Package |
| --- | --- | --- | --- |
| 2023 | 23.0.0.0 | net48 | ScanToBimQa-Revit2023.zip (50219 bytes) |
| 2024 | 24.2.0.0 | net48 | ScanToBimQa-Revit2024.zip (50215 bytes) |
| 2025 | 25.5.0.0 | net10.0 | ScanToBimQa-Revit2025.zip (50514 bytes) |
| 2026 | 26.5.0.0 | net10.0 | ScanToBimQa-Revit2026.zip (50516 bytes) |
| 2027 | 27.3.0.0 | net10.0 | ScanToBimQa-Revit2027.zip (50513 bytes) |

## Not verified here

Nothing has run inside Revit. See the verification checklist in README.md. The net48 build of the core is compiled but its tests were run on .NET 8 and .NET 10 only.
