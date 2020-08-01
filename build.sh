#! /bin/bash
set -e
dotnet test -c Release Bson.HilbertIndex.Test/Bson.HilbertIndex.Test.csproj
dotnet pack -c Release Bson.HilbertIndex/Bson.HilbertIndex.csproj -o ./out