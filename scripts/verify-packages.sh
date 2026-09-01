#!/usr/bin/env bash
# Verifies the packed analyzer package before it is published. An analyzer that ships under lib/
# instead of analyzers/dotnet/cs restores without error and then never runs, so this is checked
# explicitly rather than left to review.
set -euo pipefail

PACKAGE_DIR="${1:?usage: verify-packages.sh <package-directory>}"
ASSEMBLY="Chaos.BlazorAnalyzers.dll"

shopt -s nullglob
PACKAGES=("${PACKAGE_DIR}"/*.nupkg)
shopt -u nullglob

if [[ ${#PACKAGES[@]} -ne 1 ]]; then
    echo "::error::Expected exactly one .nupkg in '${PACKAGE_DIR}', found ${#PACKAGES[@]}."
    exit 1
fi

PACKAGE="${PACKAGES[0]}"
echo "Verifying $(basename "${PACKAGE}")"

CONTENTS="$(unzip -Z1 "${PACKAGE}")"
NUSPEC="$(unzip -p "${PACKAGE}" '*.nuspec')"

fail() {
    echo "::error::$1"
    exit 1
}

grep -qx "analyzers/dotnet/cs/${ASSEMBLY}" <<< "${CONTENTS}" \
    || fail "${ASSEMBLY} is missing from analyzers/dotnet/cs; the analyzer would never be loaded."

grep -q '^lib/' <<< "${CONTENTS}" \
    && fail "The package contains lib/ entries. An analyzer must not ship a compile-time reference."

grep -qx 'README.md' <<< "${CONTENTS}" \
    || fail "README.md is missing from the package."

grep -q '<developmentDependency>true</developmentDependency>' <<< "${NUSPEC}" \
    || fail "developmentDependency is not set; consumers would get a transitive dependency."

grep -q '<description>Package Description</description>' <<< "${NUSPEC}" \
    && fail "The nuspec still carries the default placeholder description."

grep -q '<tags>' <<< "${NUSPEC}" \
    || fail "The nuspec has no tags."

echo "Package layout OK"
