#!/usr/bin/env bash
# Publish vault docs into the repo.
#
# Walks the Parlance vault folder, finds every *.md file with a
# `parlance_publish: <repo-relative-path>` key in its YAML frontmatter, and
# writes the body to that repo path with a generated-marker comment prepended.
# Repo docs are overwritten unconditionally — vault is the source of truth.
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

published=0
while IFS= read -r -d '' file; do
    publish_path="$(awk '
        BEGIN { in_fm = 0; fm_count = 0 }
        /^---$/ {
            fm_count++
            if (fm_count == 1) { in_fm = 1; next }
            if (fm_count == 2) { exit }
        }
        in_fm && /^parlance_publish:[[:space:]]*/ {
            sub(/^parlance_publish:[[:space:]]*/, "")
            gsub(/^[[:space:]]+|[[:space:]]+$/, "")
            print
            exit
        }
    ' "$file")"

    [[ -z "$publish_path" ]] && continue

    vault_rel="${file#"$VAULT/"}"
    target="$REPO_ROOT/$publish_path"
    mkdir -p "$(dirname "$target")"

    {
        printf -- "<!-- generated from 20-Projects/Parlance/%s — edit in vault, run tools/docs/publish.sh -->\n\n" "$vault_rel"
        awk '
            BEGIN { in_fm = 0; fm_done = 0; started = 0 }
            /^---$/ {
                if (!in_fm && !fm_done) { in_fm = 1; next }
                if (in_fm) { in_fm = 0; fm_done = 1; next }
            }
            fm_done {
                if (!started && /^[[:space:]]*$/) next
                started = 1
                print
            }
        ' "$file"
    } > "$target"

    printf "  %s -> %s\n" "$vault_rel" "$publish_path"
    published=$((published + 1))
done < <(find "$VAULT" -type f -name '*.md' -print0)

echo ""
echo "$published document(s) published from $VAULT"
