# Scan-to-BIM QA for Revit

A Revit add-in that checks a modelled MEP model against a linked point cloud (`.rcp` / `.rcs`), inside Revit, with the RVT and cloud you already have. No IFC export, no scan re-import.

It answers two questions for the area inside the section box of a 3D view:

1. **How far is each element off?** Distance from every scan point to the true element surface, per element: points used, % within tolerance, P95 and max absolute deviation, status **Pass / Fail / Insufficient Scan Data**.
2. **Where is scan data with no model?** Clusters of scan points with no model surface within the missing threshold: **Suspected Missing**, for a reviewer to confirm.

Results show as a per-point heatmap and a Suspected Missing layer in the 3D view, plus a QA table with filter, select and zoom.

## Install

### From a package

1. Close Revit.
2. Unzip `ScanToBimQa-Revit2026.zip` into `%AppData%\Autodesk\Revit\Addins\2026\`. You get `ScanToBimQa.addin` and the folder `ScanToBimQa\`.
3. Start Revit and accept the add-in prompt. A **Scan QA** tab appears.

Use the package that matches your Revit version. See [Version support](#version-support) for the minimum update of each package.

### From source (Windows)

Requires the .NET 10 SDK, plus the .NET Framework 4.8 developer pack for Revit 2023/2024.

```powershell
cd revit\ScanToBimQa
.\build\build.ps1 -Versions 2026 -Install   # test, build against your installed Revit 2026, copy to Addins
.\build\build.ps1                           # every installed Revit version 2023–2027
```

`build.ps1` compiles against the `RevitAPI.dll` of your own install folder, so the package always matches your Revit update. Packages land in `dist\`.

Off Windows, `build/build-offline.sh` builds with copied reference assemblies (see the script header). Autodesk and Microsoft reference assemblies are not stored in this repository.

## Run a check

1. Open the RVT with the point cloud linked (Insert > Point Cloud).
2. Open a 3D view. Turn on **Section Box** and fit it around the area to check. A rotated section box is fine.
3. **Scan QA > Scan-to-BIM QA**. The window is modeless; keep it open while you work.
4. Choose the **point cloud** (or all clouds visible in the view), the **categories** (default: pipes, pipe fittings, ducts, duct fittings), and optionally **Selected elements only**.
5. Set **Tolerance (mm)** and **Missing threshold (mm)** separately (defaults 20 / 100).
6. **Run Check**.
7. Review:
   - **Elements** tab: filter Problems / All / Fail / Insufficient / Pass. Double-click or **Zoom to element** to select and frame it.
   - **Suspected Missing** tab: double-click or **Zoom to location** to frame the cluster. Clusters next to an unconnected pipe/duct end are labelled "possible missing fitting" and select that element.
   - 3D view: heatmap (green 0 → yellow at tolerance → red at 2× tolerance) and magenta crosses for Suspected Missing. Toggle each layer in the window. The legend in the window follows the tolerance.
8. Fix the model, then **Run Check** again. **Export CSV** writes the element table and a `_missing.csv` with the clusters.

### Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| Tolerance | 20 mm | Element passes when P95 of absolute deviation ≤ tolerance |
| Missing threshold | 100 mm | Scan points farther than this from every model surface are unexplained. Also the largest deviation the check can measure: set it above the worst offset you expect |
| Point spacing | 5 mm | `averageDistance` for `PointCloudInstance.GetPoints` |
| Max points | 2,000,000 | Point budget for the region; read in slabs of ≤ 1,000,000 per call |
| Min points per element | 30 | Below this: Insufficient Scan Data |
| Min surface coverage | 25 % | Share of the element surface inside the box that has scan points on it. Below this: Insufficient Scan Data |
| Min points per missing cluster | 50 | Smaller groups are not reported |
| Insulation is the pipe/duct surface | on | Hosted insulation geometry is added to the host element, because the scanner sees the insulation. Off: insulation is ignored |
| Linked models explain scan points | on | Elements of linked RVTs (architecture, structure) count as context so walls and slabs are not flagged |

## How it works

1. **Points.** The section box (`View3D.GetSectionBox()`, box frame + transform) is converted into six inward planes in the cloud's own coordinates (inverse of `PointCloudInstance.GetTransform()`), passed to `PointCloudFilterFactory.CreateMultiPlaneFilter`, and read with `GetPoints`. Returned points are moved to the model with the instance transform and tested exactly against the rotated box. Revit internal feet are converted to mm only for thresholds and results.
2. **Geometry.** Target elements and context elements are collected from the view with a `BoundingBoxIntersectsFilter` on the box (inflated by the missing threshold). Their solids are triangulated at the finest Revit level of detail. A bounding volume hierarchy over the triangles prunes the search; **every deviation is the exact point-to-triangle distance to the element surface**, never a bounding-box distance.
3. **Attribution.** Each point goes to the nearest surface within the missing threshold. Target → evaluated for that element. Context (walls, slabs, other services, linked models) → explained, no status. None → unexplained.
4. **Axial end rule.** A point beyond either end of a straight pipe/duct (by more than the tolerance) is never attributed to it. This keeps the points of a missing elbow or tee off the adjacent pipe.
5. **Status.** Insufficient Scan Data when an element has too few points **or** too little surface coverage. Coverage uses the closest surface point of each attributed scan point, so an offset but well-scanned element is Fail, not Insufficient. Only the part of the element inside the section box counts. An occluded pipe is never Pass or Fail.
6. **Suspected Missing.** Unexplained points are binned in 100 mm voxels; only voxels with ≥ 5 points seed and grow clusters (26-neighbourhood), so scattered noise never forms one. Clusters under the minimum size are dropped. Each cluster is tied to the nearest unconnected connector within 600 mm.

## Version support

All five packages were compiled against the real `RevitAPI.dll` / `RevitAPIUI.dll` of each version (installed updates listed below), with zero warnings. **None has been run inside Revit yet.**

| Revit | API compiled against | Runtime of the package | Compiled | Core tests | Run in Revit |
| --- | --- | --- | --- | --- | --- |
| 2023 | 23.0.0.0 | .NET Framework 4.8 | Yes | Core compiled for net48; tests run on .NET 8/10 | Not yet |
| 2024 | 24.2.0.0 | .NET Framework 4.8 | Yes | as above | Not yet |
| 2025 | 25.5.0.0 | .NET 10 | Yes | 23/23 on .NET 10 and .NET 8 | Not yet |
| **2026** | **26.5.0.0** | **.NET 10** | **Yes** | **23/23** | **Not yet — see checklist** |
| 2027 | 27.3.0.0 | .NET 10 | Yes | 23/23 | Not yet |

Notes:

- The runtime was read from the `TargetFrameworkAttribute` of each `RevitAPI.dll`: Revit 2025.5, 2026.5 and 2027.3 run on .NET 10. Earlier 2025/2026 updates run on .NET 8. A package compiled against API 26.5 needs Revit 2026.5 or newer; for an older update, build with `build.ps1` on that machine (for a .NET 8 update add `-p:RevitTargetFramework=net8.0`).
- The add-in has no third-party dependencies: `ScanQa.Revit.dll` and `ScanQa.Core.dll` only.

## Verification checklist for Revit 2026

Compile-time checks prove the API calls exist with these signatures. These behaviours can only be confirmed in Revit:

| # | Check | Pass criterion |
| --- | --- | --- |
| 1 | Add-in loads | **Scan QA** tab appears, no load error |
| 2 | Point read with identity cloud transform | Run log shows points "inside the section box" ≈ points returned |
| 3 | Point read with a moved/rotated cloud (Move/Rotate the cloud instance) | Heatmap overlays the cloud; no "fallback" note in the run log. If the note appears, the filter planes are expected in model coordinates — the fallback handles it, but report it |
| 4 | Rotated section box | Only points inside the rotated box are coloured |
| 5 | Pipe on the scan | Pass, green heatmap |
| 6 | Pipe moved 50 mm | Fail, P95 ≈ 50 mm, red heatmap |
| 7 | Delete an elbow between two pipes | Suspected Missing cluster at the elbow, labelled possible missing fitting; both pipes still Pass |
| 8 | Pipe hidden behind another service | Insufficient Scan Data |
| 9 | Zoom to element / location | Element selected and framed in the checked view; cluster framed |
| 10 | Layer toggles | Heatmap and Suspected Missing switch on/off independently |
| 11 | Fix model → Run Check | Results update without closing the window |
| 12 | Performance | 2 M points in a typical plant room in under ~30 s (core alone: 6.3 M points / 300 pipes in 4.2 s on 2 cores) |

## Known limitations

- **Missing fitting next to other geometry.** The axial end rule stops adjacent straight pipes/ducts from absorbing a missing fitting's points. A **wall, slab, or other context element within the missing threshold** of the fitting still explains part of it, so the cluster shrinks and can disappear when the surface is very close. Lowering the missing threshold keeps more points (test `MissingElbowNextToWall_KnownLimitation…`). A missing fitting next to an existing fitting (no axis) has the same limitation.
- **Large deviations.** An element offset by more than the missing threshold gets no points (Insufficient) and its scan shows as Suspected Missing next to it. Raise the threshold to measure it.
- **Tessellation.** Distances are to Revit's triangulated surface. On large round ducts the chord error of the triangulation adds a small bias.
- **Linked models** count only as context; their elements are not checked and not filtered by view visibility. Rooms, spaces, areas, zones and masses are never used as context.
- **Heatmap points** are drawn as 1-pixel points (DirectContext3D has no point size); regions above 600,000 evaluated points are decimated for display only. The legend is in the QA window, not in the view.
- Linked point clouds inside linked RVTs are not read.

## Tests

The core engine is Revit-independent and runs anywhere:

```bash
dotnet run --project tests/ScanQa.Core.Tests -c Release
```

The suite builds synthetic models and scans: a pipe within tolerance, a pipe offset on purpose, a missing elbow between two pipes (with and without the axial rule, and next to a wall), occluded pipes, a rotated and translated cloud read through a rotated section box, scattered noise, coverage clipped to the box, zoom frames, filters and CSV. `AcceptanceTests` combines the five required cases in one rotated cloud. Latest results: [TEST-RESULTS.md](TEST-RESULTS.md).

## Layout

| Path | Content |
| --- | --- |
| `src/ScanQa.Core` | Geometry, BVH, QA engine, heatmap, zoom frames, CSV. No Revit references (net48 / net8.0 / net10.0) |
| `src/ScanQa.Revit` | Add-in: point read, geometry extraction, DirectContext3D layers, QA window, zoom |
| `tests/ScanQa.Core.Tests` | Self-contained test runner (no test package, runs offline) |
| `build/` | `build.ps1` (Windows), `build-offline.sh` (other systems) |

## Licence and reuse

LGPL-3.0-or-later, as the point-cloud core of this repository ([LICENSE.md](../../LICENSE.md)).

No code from Open Pointcloud Studio is reused. Its components are Rust; Revit already reads `.rcp`/`.rcs` itself (which Open Pointcloud Studio does not read), and the check needs Revit's model geometry, so nothing in the Rust core solves a need of this add-in without adding a native dependency.
