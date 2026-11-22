#!/usr/bin/env bash
# List files that exist on only one of the two long-lived branches.
#
# The branches diverge by design: 1.7 carries the Compatibility contract layer and its
# features, 1.6 carries the Prepatcher-era files. Run this before landing a shared change:
# a file that appears on only one branch is a prompt to check whether the change landed on
# both, not automatically a mistake.
#
# Usage: build/parity-report.sh [base] [other]   (default: master experimental-1.7)
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

BASE="${1:-master}"
OTHER="${2:-experimental-1.7}"

for ref in "$BASE" "$OTHER"; do
  if ! git rev-parse --verify --quiet "$ref" >/dev/null; then
    echo "parity-report: missing ref $ref (fetch both branches first)" >&2
    exit 2
  fi
done

TMP="$(mktemp -d)"
trap 'rm -rf -- "$TMP"' EXIT

git ls-tree -r --name-only "$BASE" | sort > "$TMP/base"
git ls-tree -r --name-only "$OTHER" | sort > "$TMP/other"

comm -23 "$TMP/base" "$TMP/other" > "$TMP/only-base"
comm -13 "$TMP/base" "$TMP/other" > "$TMP/only-other"

echo "parity-report: $BASE vs $OTHER"
echo
echo "Only on $BASE ($(wc -l < "$TMP/only-base")):"
sed 's/^/  /' "$TMP/only-base"
echo
echo "Only on $OTHER ($(wc -l < "$TMP/only-other")):"
sed 's/^/  /' "$TMP/only-other"
echo
echo "Review new entries above. Known-intentional sets live in the workspace"
echo "docs/branch-parity.md; this script reports, it does not fail on expected differences."
