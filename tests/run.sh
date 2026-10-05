#!/bin/sh
# Builds the scanner core and runs the checks with Mono (needs mono-devel, libgdiplus and tesseract).
set -e
cd "$(dirname "$0")/.."
mkdir -p build
mcs -langversion:7 -target:library -out:build/DbScanner.Core.dll -r:System.Core.dll src/DbScanner/Core/*.cs
mcs -langversion:7 -out:build/tests.exe -r:build/DbScanner.Core.dll -r:System.Drawing.dll -r:System.Core.dll tests/TestRunner.cs
MONO_PATH=build mono build/tests.exe .
