#!/bin/bash
set -e
cd "$(dirname "$0")/Tests/WorkbenchGroups.Tests"
/home/deck/.dotnet/dotnet test "$@"
