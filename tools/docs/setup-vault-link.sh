#!/usr/bin/env bash
# Create the docs/superpowers symlink that points into the contributor's
# Parlance vault folder. Run once after cloning. The symlink itself is
# gitignored, so each contributor maintains their own.
#
# Vault location: $PARLANCE_VAULT_PATH if set, otherwise the default below.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VAULT_DEFAULT="/mnt/c/Users/doug/ObsidianVault/NotesMain/20-Projects/Parlance"
VAULT="${PARLANCE_VAULT_PATH:-$VAULT_DEFAULT}"

if [[ ! -d "$VAULT" ]]; then
    echo "error: vault not found: $VAULT" >&2
    echo "       set PARLANCE_VAULT_PATH or fix the default in this script" >&2
    exit 1
fi

mkdir -p "$VAULT/Superpowers"

target="$REPO_ROOT/docs/superpowers"
if [[ -L "$target" ]]; then
    current="$(readlink "$target")"
    if [[ "$current" == "$VAULT/Superpowers" ]]; then
        echo "symlink already correct: $target -> $current"
        exit 0
    fi
    echo "replacing existing symlink: $target -> $current"
    rm "$target"
elif [[ -e "$target" ]]; then
    echo "error: $target exists and is not a symlink — refusing to overwrite" >&2
    exit 1
fi

ln -s "$VAULT/Superpowers" "$target"
echo "created: $target -> $VAULT/Superpowers"
