#!/usr/bin/env bash
# Validates the release version supplied to the release workflow and reports whether it is a
# prerelease. Expects RELEASE_VERSION in the environment and writes to GITHUB_OUTPUT when set.
set -euo pipefail

VERSION="${RELEASE_VERSION:-}"

if [[ -z "${VERSION}" ]]; then
    echo "::error::RELEASE_VERSION is empty."
    exit 1
fi

# MAJOR.MINOR.PATCH with an optional prerelease suffix, for example 1.2.3 or 1.2.3-dev.4
SEMVER='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$'

if [[ ! "${VERSION}" =~ ${SEMVER} ]]; then
    echo "::error::'${VERSION}' is not a valid version. Expected MAJOR.MINOR.PATCH with an optional -prerelease suffix."
    exit 1
fi

if [[ "${VERSION}" == *-* ]]; then
    PRERELEASE=true
else
    PRERELEASE=false
fi

echo "Version:    ${VERSION}"
echo "Prerelease: ${PRERELEASE}"

if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    {
        echo "version=${VERSION}"
        echo "prerelease=${PRERELEASE}"
    } >> "${GITHUB_OUTPUT}"
fi
