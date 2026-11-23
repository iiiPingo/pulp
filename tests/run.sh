#!/bin/sh
# Builds the font + zoom tests with Mono and runs it (Linux/macOS). Needs mono-devel.
set -e
cd "$(dirname "$0")/.."
mkdir -p tests/out
res=""
for f in src/fonts/*.ttf; do res="$res -resource:$f,Pulp.Fonts.$(basename "$f")"; done
mcs -nologo -out:tests/out/FontTests.exe -r:System.Drawing.dll src/Fonts.cs src/Zoom.cs tests/FontTests.cs $res
mono tests/out/FontTests.exe
