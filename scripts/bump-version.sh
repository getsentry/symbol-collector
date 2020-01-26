#!/bin/bash
set -eux

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd $SCRIPT_DIR/..

OLD_VERSION="$1"
NEW_VERSION="$2"

sed -i '' -e "1,/<Version>/ s!<Version>.*</Version>!<Version>$NEW_VERSION</Version>!" Directory.Build.props
sed -i '' -e "s/versionName=\(.*\)\"/versionName=\"$NEW_VERSION\"/g" src/SymbolCollector.Android/Properties/AndroidManifest.xml
sed -i '' -e "s/AssemblyVersion(\"\(.*\)\"/AssemblyVersion(\"$NEW_VERSION\"/g" src/SymbolCollector.Android/Properties/AssemblyInfo.cs
sed -i '' -e "s/AssemblyFileVersion(\"\(.*\)\"/AssemblyFileVersion(\"$NEW_VERSION\"/g" src/SymbolCollector.Android/Properties/AssemblyInfo.cs
