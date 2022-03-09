#!/bin/sh -e

dotnet restore
dotnet build --configuration Release --no-restore
dotnet LusidFeatureReporter/bin/Release/netcoreapp3.1/LusidFeatureReporter.dll -a Lusid.Sdk.Examples -n  Lusid.Sdk.Examples -o features.txt
dotnet test -v n Lusid.Sdk.Examples