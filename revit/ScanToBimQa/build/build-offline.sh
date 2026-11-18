#!/usr/bin/env bash
# Builds and tests off Windows (Linux/macOS) with copied reference assemblies. No NuGet access is needed.
#
#   REVIT_REFS=<dir with 2023/ 2024/ ... each holding RevitAPI.dll and RevitAPIUI.dll>
#   NETFX_REFS=<dir holding .NETFramework/v4.8/...>   (for Revit 2023/2024)
#   WINFORMS_REFS=<dir holding System.Windows.Forms.dll etc. from Microsoft.WindowsDesktop.App.Ref>
#   build/build-offline.sh [versions...]
#
# The Revit and Windows reference assemblies are not part of the repository (Autodesk/Microsoft licences);
# copy them from a machine where Revit and the .NET SDK are installed.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSIONS=("${@:-2023 2024 2025 2026 2027}")
CFG="-p:RestoreConfigFile=$PWD/build/nuget.offline.config -p:NetFxRefRoot=${NETFX_REFS:-} -p:WinFormsRefDir=${WINFORMS_REFS:-}"

dotnet run --project tests/ScanQa.Core.Tests -c Release $CFG -p:CoreTargetFrameworks=net10.0 | tee dist-test-net10.log
dotnet run --project tests/ScanQa.Core.Tests -c Release $CFG -p:CoreTargetFrameworks=net8.0 -p:TestTargetFramework=net8.0 | tee dist-test-net8.log

for v in ${VERSIONS[@]}; do
  dotnet build src/ScanQa.Revit -c Release $CFG -p:RevitVersion=$v -p:RevitApiDir="$REVIT_REFS/$v"
done
