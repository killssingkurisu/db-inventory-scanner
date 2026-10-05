#!/bin/sh
# Type-checks the whole app with Mono on Linux, using stand-ins for the Windows Runtime OCR types.
# The real build is done on Windows by .github/workflows/build.yml.
set -e
cd "$(dirname "$0")/.."
mkdir -p build
mcs -langversion:7 -target:winexe -out:build/DbScanner-check.exe \
  -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.IO.Compression.dll \
  -resource:data/catalog.json,catalog.json \
  src/DbScanner/*.cs src/DbScanner/Core/*.cs src/DbScanner/Win/*.cs src/DbScanner/UI/*.cs tools/winrt-stubs/WinRtStubs.cs
echo "type-check OK"
