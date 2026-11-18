# Scan-to-BIM QA for Revit

A Revit add-in that checks an architectural, structural and MEP model against a linked point cloud (`.rcp` / `.rcs`), inside Revit, with the RVT and cloud you already have. Elements of linked RVTs (for example the STR and MEP models linked into the ARCH model) are checked too. No IFC export, no scan re-import.

For viewing, cutting, measuring and exporting the scan itself, use the Open Pointcloud Studio desktop application of this repository (official Windows build: `open-pointcloud-studio_2026.45.1_windows-x64.zip` on the [releases page](https://github.com/OpenAEC-Foundation/open-pointcloud-studio/releases)).

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
4. Choose the **point cloud** (or all clouds visible in the view) and the **categories**. **ARC / STR / MEP / All / None** set the usual categories of each discipline. Walls and floors flagged structural are reported as Structure. Keep **Check elements in linked models** on to check the STR/MEP links as well; **Selected elements only** limits the host elements to the selection.
5. Set **Tolerance (mm)** and **Missing threshold (mm)** separately (defaults 20 / 100).
6. **Run Check**.
7. Review:
   - **Elements** tab: filter by status and by discipline. The **Model** column shows Host or the linked file. Double-click or **Zoom to element** to select (linked elements too) and frame it.
   - **Suspected Missing** tab: double-click or **Zoom to location** to frame the cluster. Clusters next to an unconnected pipe/duct end are labelled "possible missing fitting" and select that element.
   - 3D view: heatmap (green 0 → yellow at tolerance → red at 2× tolerance) and magenta crosses for Suspected Missing. Toggle each layer in the window. The legend in the window follows the tolerance. The scan's own RGB colours mix with the heatmap: hide the cloud with **Temporary Hide** (HH) while reviewing; Run Check still reads it.
8. Fix the model, then **Run Check** again. **Export CSV** writes the element table and a `_missing.csv` with the clusters.

### Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| Tolerance | 20 mm | Element passes when P95 of absolute deviation ≤ tolerance |
| Missing threshold | 100 mm | Scan points farther than this from every model surface are unexplained. Also the largest deviation the check can measure: set it above the worst offset you expect |
| Point spacing | 5 mm | `averageDistance` for `PointCloudInstance.GetPoints` |
| Max points | 2,000,000 | Point budget for the region; read in slabs of ≤ 999,999 per call |
| Min points per element | 30 | Below this: Insufficient Scan Data |
| Min surface coverage | 25 % | Share of the element surface inside the box that has scan points on it. Below this: Insufficient Scan Data |
| Min points per missing cluster | 30 | Smaller groups are not reported. A DN80 elbow left out of a real model kept 39 points at this scan density; raise the value if site clutter floods the list |
| Insulation is the pipe/duct surface | on | Hosted insulation geometry is added to the host element, because the scanner sees the insulation. Off: insulation is ignored |
| Check elements in linked models | on | Elements of the chosen categories in linked RVTs are checked and listed with their file name |
| Linked models explain scan points | on | Other elements of linked RVTs count as context, so their surfaces are not flagged as missing |

## How it works

1. **Points.** The section box (`View3D.GetSectionBox()`, box frame + transform) is converted into six inward planes in **model** coordinates, passed to `PointCloudFilterFactory.CreateMultiPlaneFilter`, and read with `GetPoints` (≤ 999,999 points per call). The returned points are in the cloud's own coordinates (metres for `.rcs`), so they are moved to the model with `PointCloudInstance.GetTransform()`, which carries the 3.2808 scale, and tested exactly against the rotated box. Both facts were confirmed in Revit 2026.5. Revit internal feet are converted to mm only for thresholds and results.
2. **Geometry.** Target elements and context elements are collected from the view with a `BoundingBoxIntersectsFilter` on the box (inflated by the missing threshold). Their solids are triangulated at the finest Revit level of detail. A bounding volume hierarchy over the triangles prunes the search; **every deviation is the exact point-to-triangle distance to the element surface**, never a bounding-box distance.
3. **Attribution.** Each point goes to the nearest surface within the missing threshold. Target → evaluated for that element. Context (walls, slabs, other services, linked models) → explained, no status. None → unexplained.
4. **Axial end rule.** A point beyond either end of a straight pipe/duct (by more than the tolerance) is never attributed to it. This keeps the points of a missing elbow or tee off the adjacent pipe.
5. **Status.** Insufficient Scan Data when an element has too few points **or** too little surface coverage. Coverage uses the closest surface point of each attributed scan point, so an offset but well-scanned element is Fail, not Insufficient. Only the part of the element inside the section box counts. An occluded pipe is never Pass or Fail.
6. **Suspected Missing.** Unexplained points are binned in 100 mm voxels; only voxels with ≥ 5 points seed and grow clusters (26-neighbourhood), so scattered noise never forms one. Clusters under the minimum size are dropped. Each cluster is tied to the nearest unconnected connector within 600 mm.

## Version support

All five packages were compiled against the real `RevitAPI.dll` / `RevitAPIUI.dll` of each version (installed updates listed below), with zero warnings. **Revit 2026.5 was run on a real project**: an ACC cloud ARCH model with linked STR and MEP (CS) models and an `.rcs` scan.

| Revit | API compiled against | Runtime of the package | Compiled | Core tests | Run in Revit |
| --- | --- | --- | --- | --- | --- |
| 2023 | 23.0.0.0 | .NET Framework 4.8 | Yes | Core compiled for net48; tests run on .NET 8/10 | Not yet |
| 2024 | 24.2.0.0 | .NET Framework 4.8 | Yes | as above | Not yet |
| 2025 | 25.5.0.0 | .NET 10 | Yes | 24/24 on .NET 10 and .NET 8 | Not yet |
| **2026** | **26.5.0.0** | **.NET 10** | **Yes** | **24/24** | **Yes — 2026.5, results below** |
| 2027 | 27.3.0.0 | .NET 10 | Yes | 24/24 | Not yet |

Notes:

- The runtime was read from the `TargetFrameworkAttribute` of each `RevitAPI.dll`: Revit 2025.5, 2026.5 and 2027.3 run on .NET 10. Earlier 2025/2026 updates run on .NET 8. A package compiled against API 26.5 needs Revit 2026.5 or newer; for an older update, build with `build.ps1` on that machine (for a .NET 8 update add `-p:RevitTargetFramework=net8.0`).
- The add-in has no third-party dependencies: `ScanQa.Revit.dll` and `ScanQa.Core.dll` only. Revit asks once whether to load it (unsigned add-in).
- 2023, 2024, 2025 and 2027 use the same code paths, and the compiler checked every API call against each version's real API. Their runtime behaviour is still unverified.

## Revit 2026.5 results

Model: ARCH host (ACC cloud, workshared) with linked STR and CS models, one `.rcs` cloud, section box around a stair core (15 × 44 × 21 ft). Test elements were copied into the host and discarded afterwards without saving or synchronising.

| # | Check | Result |
| --- | --- | --- |
| 1 | Add-in loads | Pass — Scan QA tab, window opens and detects the section box |
| 2 | Point read | Pass after fix — 1,328,099 points, 3 reads, all inside the box, 0.3 s. First build read 0 points (see API facts) |
| 3 | Cloud transform | Pass — the `.rcs` instance transform carries a 3.2808 scale and a large offset; 20,000/20,000 transformed points fall in the box. Rotating the cloud instance was not tried (it would edit the shared model); rotation is covered by core tests |
| 4 | Rotated section box | Covered by core tests only; the project box was not rotated |
| 5 | Pipe on the scan | Pass — linked DN25 pipe P95 5.8 mm; the same pipe copied into the host P95 6.7 mm |
| 6 | Pipe moved 50 mm | Pass — same pipe moved 50 mm: Fail, P95 48.0 mm, 17 % within tolerance |
| 7 | Missing elbow | Pass with min cluster 30 or lower — DN80 PVC elbow removed: cluster of 39 points at the open end of the riser, labelled "possible missing fitting", confirmed visually. With the former default of 50 it was not reported |
| 8 | Occluded pipes | Pass — linked pipes with 3–27 points are Insufficient Scan Data, never Pass/Fail |
| 9 | Zoom | Pass — element and cluster zoom frame locally and select the element (host or linked) |
| 10 | Layer toggles | Pass — heatmap and Suspected Missing switch independently |
| 11 | Run again | Pass — results update in place, run after run |
| 12 | Performance | Pass — 1.33 M points, 84 elements (ARC+STR+MEP incl. links): about 3.5 s in total |

Full ARC + STR + MEP run on the same box: 72 elements (37 host, 47 linked) — 29 Pass, 23 Fail, 20 Insufficient — and 24 Suspected Missing clusters. Unmodelled site items show up as clusters, among them a red steel frame on the roof and a rainwater pipe.

### Revit API facts confirmed in 2026.5

- `PointCloudFilterFactory.CreateMultiPlaneFilter` takes its planes in **model** coordinates. `PointCloudInstance.GetPoints` returns points in the **cloud's** coordinates, so `GetTransform()` (scale included) must be applied to them.
- `GetPoints` rejects `numPoints` of 1,000,000; the add-in reads in slabs of at most 999,999.
- `GetPoints` returns `null` for a point cloud instance whose data is not loaded (seen on copied instances). The add-in reads again once and reports it in the run log.

## Known limitations

- **Missing fitting next to other geometry.** The axial end rule stops adjacent straight pipes/ducts from absorbing a missing fitting's points. A **wall, slab, or other context element within the missing threshold** of the fitting still explains part of it, so the cluster shrinks and can disappear when the surface is very close. Lowering the missing threshold keeps more points (test `MissingElbowNextToWall_KnownLimitation…`). A missing fitting next to an existing fitting (no axis) has the same limitation.
- **Short pipe pieces next to a missing fitting.** The axial end rule allows the tolerance as margin at each end, so a very short piece (69 mm in the test) still takes part of a missing fitting's points and can turn Fail. The remaining points must reach the cluster minimum.
- **Scan density.** Small-bore pipes and fittings often have few points (3–30 at this scan's density), so they come out as Insufficient Scan Data. That is the intended answer, not a failure of the check.
- **Site clutter** (furniture, temporary works, people) has no model surface and appears as Suspected Missing. Raise Min points per missing cluster to cut the list.
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
