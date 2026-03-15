#!/usr/bin/env bash
set -euo pipefail

# ──────────────────────────────────────────────────────────────────────────────
# test-repos.sh — Run Parlance against curated public C# repositories
# ──────────────────────────────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "$(realpath "${BASH_SOURCE[0]}")")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PARLANCE="dotnet run --project $REPO_ROOT/src/Parlance.Cli --"
WORK_DIR=$(mktemp -d)
RESULTS_DIR="$WORK_DIR/results"
mkdir -p "$RESULTS_DIR"

# Colors
BOLD='\033[1m'
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
DIM='\033[2m'
RESET='\033[0m'

# ──────────────────────────────────────────────────────────────────────────────
# Repo list: URL | TIER (1=buildable, 2=syntax-only) | BUILD_PATH | SCAN_PATH
# BUILD_PATH is relative to repo root (solution or project file)
# SCAN_PATH is relative to repo root (directory to analyze)
# ──────────────────────────────────────────────────────────────────────────────
REPOS=(
    "davidfowl/TodoApi|1||."
    "jbogard/MediatR|1||src"
    "ardalis/CleanArchitecture|1||src"
    "mehmetozkaya/DotnetCrawler|2||."
)

# ──────────────────────────────────────────────────────────────────────────────
# Helpers
# ──────────────────────────────────────────────────────────────────────────────

separator() {
    echo -e "${CYAN}══════════════════════════════════════════════════════════════════${RESET}"
}

thin_separator() {
    echo -e "${DIM}──────────────────────────────────────────────────────────────────${RESET}"
}

section() {
    thin_separator
    echo -e "${BOLD}  $1${RESET}"
    thin_separator
}

timer_start() { TIMER_START=$(date +%s%N); }
timer_elapsed() {
    local end=$(date +%s%N)
    local ms=$(( (end - TIMER_START) / 1000000 ))
    echo "$(( ms / 1000 )).$(( (ms % 1000) / 100 ))s"
}

pass() { echo -e "  ${GREEN}PASS${RESET} $1"; }
fail() { echo -e "  ${RED}FAIL${RESET} $1"; }
skip() { echo -e "  ${YELLOW}SKIP${RESET} $1"; }
info() { echo -e "  ${DIM}$1${RESET}"; }

# ──────────────────────────────────────────────────────────────────────────────
# Summary tracking
# ──────────────────────────────────────────────────────────────────────────────
declare -a SUMMARY_NAMES=()
declare -a SUMMARY_TIERS=()
declare -a SUMMARY_SCORES=()
declare -a SUMMARY_DIAGNOSTICS=()
declare -a SUMMARY_BUILD_PRE=()
declare -a SUMMARY_BUILD_POST=()
declare -a SUMMARY_TIMES=()

# ──────────────────────────────────────────────────────────────────────────────
# Build Parlance first
# ──────────────────────────────────────────────────────────────────────────────
echo ""
separator
echo -e "${BOLD}  BUILDING PARLANCE${RESET}"
separator
timer_start
if dotnet build "$REPO_ROOT/src/Parlance.Cli/Parlance.Cli.csproj" -c Release --nologo -v q 2>&1 | tail -3; then
    pass "Parlance build ($(timer_elapsed))"
else
    fail "Parlance build failed — cannot continue"
    exit 1
fi
echo ""

# ──────────────────────────────────────────────────────────────────────────────
# Process each repo
# ──────────────────────────────────────────────────────────────────────────────
OVERALL_START=$(date +%s)

for entry in "${REPOS[@]}"; do
    IFS='|' read -r repo tier build_path scan_path <<< "$entry"
    name=$(basename "$repo")
    clone_dir="$WORK_DIR/$name"
    repo_results="$RESULTS_DIR/$name"
    mkdir -p "$repo_results"

    echo ""
    separator
    echo -e "${BOLD}  REPO: $repo  ${DIM}(tier $tier)${RESET}"
    separator

    # ── Clone ──
    timer_start
    if git clone --depth 1 "https://github.com/$repo.git" "$clone_dir" 2>/dev/null; then
        pass "Clone ($(timer_elapsed))"
    else
        fail "Clone failed — skipping"
        SUMMARY_NAMES+=("$repo")
        SUMMARY_TIERS+=("$tier")
        SUMMARY_SCORES+=("--")
        SUMMARY_DIAGNOSTICS+=("--")
        SUMMARY_BUILD_PRE+=("--")
        SUMMARY_BUILD_POST+=("--")
        SUMMARY_TIMES+=("--")
        continue
    fi

    file_count=$(find "$clone_dir" -name "*.cs" | wc -l)
    info "$file_count .cs files found"

    # ── Build (Tier 1 only) ──
    build_pre="skip"
    if [[ "$tier" == "1" ]]; then
        section "PRE-FIX BUILD"
        timer_start
        build_target="$clone_dir"
        if [[ -n "$build_path" ]]; then
            build_target="$clone_dir/$build_path"
        fi

        # Try to find a solution or project
        if [[ -z "$build_path" ]]; then
            sln=$(find "$clone_dir" -maxdepth 2 -name "*.sln" | head -1)
            if [[ -n "$sln" ]]; then
                build_target="$sln"
            fi
        fi

        if dotnet restore "$build_target" --nologo -v q 2>&1 | tail -2; then
            if dotnet build "$build_target" --no-restore --nologo -v q 2>&1 | tail -3; then
                build_pre="pass"
                pass "Build ($(timer_elapsed))"
            else
                build_pre="fail"
                fail "Build failed ($(timer_elapsed))"
            fi
        else
            build_pre="fail"
            fail "Restore failed ($(timer_elapsed))"
        fi
    else
        skip "Build (tier 2 — syntax-only)"
    fi

    # ── Analyze (text) ──
    section "ANALYZE"
    scan_dir="$clone_dir/$scan_path"
    timer_start

    set +e
    analyze_output=$($PARLANCE analyze "$scan_dir" --format text 2>&1)
    analyze_exit=$?
    set -e

    echo "$analyze_output"
    echo "$analyze_output" > "$repo_results/analyze-text.txt"

    analyze_time=$(timer_elapsed)

    # Extract score from output
    score=$(echo "$analyze_output" | grep -oP 'Score:\s+\K[0-9]+' | tail -1 || echo "--")
    diag_summary=$(echo "$analyze_output" | grep -iP '(error|warning|suggestion|info)' | tail -4 || echo "")

    if [[ $analyze_exit -eq 0 ]]; then
        pass "Analyze completed (exit $analyze_exit, ${analyze_time})"
    elif [[ $analyze_exit -eq 2 ]]; then
        fail "No .cs files found (exit 2)"
    else
        info "Analyze exit code: $analyze_exit (${analyze_time})"
    fi

    # ── Analyze (JSON) ──
    set +e
    $PARLANCE analyze "$scan_dir" --format json > "$repo_results/analyze.json" 2>&1
    set -e
    info "JSON results saved to $repo_results/analyze.json"

    # ── Fix dry-run ──
    section "FIX PREVIEW (dry-run)"
    timer_start

    set +e
    fix_output=$($PARLANCE fix "$scan_dir" 2>&1)
    fix_exit=$?
    set -e

    echo "$fix_output"
    echo "$fix_output" > "$repo_results/fix-preview.txt"

    fix_count=$(echo "$fix_output" | grep -cP '(PARL|CA|RCS|IDE)\d+' || echo "0")

    if [[ $fix_exit -eq 0 ]]; then
        pass "Fix preview ($(timer_elapsed))"
    else
        fail "Fix preview exit code: $fix_exit ($(timer_elapsed))"
    fi

    # ── Fix apply + rebuild (Tier 1, build passed only) ──
    build_post="skip"
    if [[ "$tier" == "1" && "$build_pre" == "pass" ]]; then
        section "FIX APPLY + REBUILD"
        timer_start

        set +e
        apply_output=$($PARLANCE fix "$scan_dir" --apply 2>&1)
        apply_exit=$?
        set -e

        echo "$apply_output"
        echo "$apply_output" > "$repo_results/fix-apply.txt"

        if [[ $apply_exit -eq 0 ]]; then
            pass "Fix applied ($(timer_elapsed))"

            # Rebuild
            timer_start
            if dotnet build "$build_target" --no-restore --nologo -v q 2>&1 | tail -3; then
                build_post="pass"
                pass "Post-fix build ($(timer_elapsed))"
            else
                build_post="fail"
                fail "Post-fix build BROKEN ($(timer_elapsed))"
            fi
        else
            build_post="error"
            fail "Fix apply failed (exit $apply_exit)"
        fi
    else
        if [[ "$tier" == "2" ]]; then
            skip "Fix apply (tier 2)"
        elif [[ "$build_pre" == "fail" ]]; then
            skip "Fix apply (pre-fix build failed)"
        fi
    fi

    # ── Track summary ──
    SUMMARY_NAMES+=("$repo")
    SUMMARY_TIERS+=("$tier")
    SUMMARY_SCORES+=("$score")
    SUMMARY_DIAGNOSTICS+=("$diag_summary")
    SUMMARY_BUILD_PRE+=("$build_pre")
    SUMMARY_BUILD_POST+=("$build_post")

done

# ──────────────────────────────────────────────────────────────────────────────
# Final Summary
# ──────────────────────────────────────────────────────────────────────────────
OVERALL_ELAPSED=$(( $(date +%s) - OVERALL_START ))

echo ""
echo ""
separator
echo -e "${BOLD}  SUMMARY${RESET}"
separator
echo ""

printf "  ${BOLD}%-35s %5s %6s %8s %10s${RESET}\n" "REPO" "TIER" "SCORE" "PRE-BLD" "POST-BLD"
thin_separator

for i in "${!SUMMARY_NAMES[@]}"; do
    pre="${SUMMARY_BUILD_PRE[$i]}"
    post="${SUMMARY_BUILD_POST[$i]}"

    # Colorize
    [[ "$pre" == "pass" ]] && pre="${GREEN}pass${RESET}" || { [[ "$pre" == "fail" ]] && pre="${RED}fail${RESET}" || pre="${DIM}$pre${RESET}"; }
    [[ "$post" == "pass" ]] && post="${GREEN}pass${RESET}" || { [[ "$post" == "fail" ]] && post="${RED}fail${RESET}" || post="${DIM}$post${RESET}"; }

    printf "  %-35s %5s %6s %8b %10b\n" "${SUMMARY_NAMES[$i]}" "${SUMMARY_TIERS[$i]}" "${SUMMARY_SCORES[$i]}" "$pre" "$post"
done

echo ""
info "Total time: ${OVERALL_ELAPSED}s"
info "Work dir: $WORK_DIR"
info "Results: $RESULTS_DIR"
echo ""
