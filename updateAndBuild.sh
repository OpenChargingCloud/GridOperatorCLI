#!/bin/bash
#
# Pull everything and build it.

set -e

cd "$(dirname "$0")"

git pull --ff-only
git submodule update --init --recursive
git submodule foreach git checkout master
git submodule foreach git pull
# No npm here: the build runs "npm ci" itself whenever the frontend's
# package.json or package-lock.json changed (see libs/GridOperator/GridOperator/GridOperator.csproj).
#dotnet build GridOperatorCLI.slnx --configuration Release
dotnet build GridOperatorCLI.slnx
