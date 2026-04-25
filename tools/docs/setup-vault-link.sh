#!/usr/bin/env bash
# Create the docs/superpowers symlink that points into the contributor's
# Parlance vault folder. Run once after cloning. The symlink itself is
# gitignored, so each contributor maintains their own.
#
# Vault location is read from PARLANCE_VAULT_PATH. Set it in your environment
# or in a `.env` file at the repo root (see `.env.example`).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [[ -f "$REPO_ROOT/.env" ]]; then
    set -a
    # shellcheck disable=SC1091
    source "$REPO_ROOT/.env"
    set +a
fi

if [[ -z "${PARLANCE_VAULT_PATH:-}" ]]; then
    echo "error: PARLANCE_VAULT_PATH is not set" >&2
    echo "       set it in your environment or create $REPO_ROOT/.env (see .env.example)" >&2
    exit 1
fi

VAULT="$PARLANCE_VAULT_PATH"

if [[ ! -d "$VAULT" ]]; then
    echo "error: vault not found: $VAULT" >&2
    echo "       fix PARLANCE_VAULT_PATH" >&2
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
