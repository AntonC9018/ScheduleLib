#!/usr/bin/env bash
# Apply/check EditorConfig braces and full Roslyn/ReSharper layout.
# CR diagnostics are deliberately not auto-fixed; see docs/formatting.md.
set -euo pipefail

readonly SAFE_STYLE_DIAGNOSTICS=(IDE0011 IDE0005)
readonly DEFAULT_TARGET="All.sln"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT

usage() {
    cat <<'USAGE'
Usage: build/format.sh [options] [-- <extra dotnet style args>]

Options:
  --check                 Verify only; never write. Exits non-zero when a file
                          would change. Safe to run on the whole solution.
  --project <file>        Project or solution to operate on (default: All.sln).
  --path <p> [--path <p>] Limit the run to these files or folders, relative to
                          the repository root. Repeatable.
  --yes                   Allow an unscoped apply run. Without it, an apply run
                          with neither --project nor --path is refused, because
                          it rewrites every file in the repository.
  -h, --help              Show this help.

Examples:
  build/format.sh --check
  build/format.sh --check --path src/ScheduleLib/ScheduleLib.Dates
  build/format.sh --project src/MainCli/MainCli.csproj
  build/format.sh --path src/MainCli -- --verbosity diagnostic

Notes:
  Requires the .NET SDK, Python 3.9+, and Bash. The pinned JetBrains tool is restored
  automatically. Only IDE0011 and IDE0005 style fixes run; CR rules stay manual.
USAGE
}

CHECK=0
ALLOW_ALL=0
TARGET="$DEFAULT_TARGET"
declare -a INCLUDES=()
declare -a EXTRA=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --check) CHECK=1; shift ;;
        --yes) ALLOW_ALL=1; shift ;;
        --project) TARGET="${2:?--project requires a value}"; shift 2 ;;
        --path) INCLUDES+=("${2:?--path requires a value}"); shift 2 ;;
        -h|--help) usage; exit 0 ;;
        --) shift; EXTRA+=("$@"); break ;;
        *) echo "format.sh: unknown option '$1'" >&2; usage >&2; exit 2 ;;
    esac
done

SCOPED=0
if [[ ${#INCLUDES[@]} -gt 0 || "$TARGET" != "$DEFAULT_TARGET" ]]; then
    SCOPED=1
fi

if [[ $CHECK -eq 0 && $SCOPED -eq 0 && $ALLOW_ALL -eq 0 ]]; then
    cat >&2 <<'MSG'
format.sh: refusing to reformat the entire repository.

The current tree carries pre-existing formatting debt. Scope the run to the files
you actually touched, or pass --check to inspect without writing:

  build/format.sh --check
  build/format.sh --path src/MainCli
  build/format.sh --project src/MainCli/MainCli.csproj

Pass --yes if a repository-wide rewrite is genuinely intended.
MSG
    exit 2
fi

declare -a COMMON=(--verbosity minimal)
if [[ $CHECK -eq 1 ]]; then
    COMMON+=(--verify-no-changes)
fi
if [[ ${#INCLUDES[@]} -gt 0 ]]; then
    # `dotnet format --include` treats each argument as a glob pattern matched
    # against the working directory, and a bare directory name matches nothing
    # at all - it silently checks zero files and still reports success. Expand
    # real directories to `<dir>/**` so a scoped check cannot pass vacuously.
    for include in "${INCLUDES[@]}"; do
        if [[ -d "$ROOT/$include" ]]; then
            include="${include%/}/**"
        elif [[ "$include" != *'*'* && ! -f "$ROOT/$include" ]]; then
            echo "format.sh: --path '$include' matches no file or directory under $ROOT" >&2
            exit 2
        fi
        COMMON+=(--include "$include")
    done
fi
if [[ ${#EXTRA[@]} -gt 0 ]]; then
    COMMON+=("${EXTRA[@]}")
fi

cd "$ROOT"

STATUS=0

run() {
    local pass="$1"; shift
    local label="$pass"
    [[ $CHECK -eq 1 ]] && label="${pass} (check)"
    echo "==> dotnet format ${label} -> ${TARGET}"

    local rc=0
    dotnet format "$pass" "$TARGET" "$@" || rc=$?

    # In apply mode a failing pass is a real error and stops the run. In check
    # mode a non-zero exit just means "these files would change", so both passes
    # still run and the results are aggregated at the end.
    if [[ $CHECK -eq 0 && $rc -ne 0 ]]; then
        return "$rc"
    fi
    if [[ $rc -ne 0 ]]; then
        STATUS=1
    fi
    return 0
}

# IDE0011 adds required braces; IDE0005 removes redundant usings. No CR fixer runs.
run style --diagnostics "${SAFE_STYLE_DIAGNOSTICS[@]}" "${COMMON[@]}"

# JetBrains supports both the standard and ReSharper-specific EditorConfig keys.
# It formats mirrored source files only; check mode never writes repository files.
dotnet tool restore --verbosity quiet
declare -a LAYOUT=(--target "$TARGET")
if [[ $CHECK -eq 1 ]]; then
    LAYOUT+=(--check)
fi
for include in "${INCLUDES[@]}"; do
    LAYOUT+=(--path "$include")
done
layout_rc=0
python3 "$ROOT/build/format-layout.py" "${LAYOUT[@]}" || layout_rc=$?
if [[ $layout_rc -eq 2 || ($CHECK -eq 0 && $layout_rc -ne 0) ]]; then
    exit "$layout_rc"
fi
if [[ $layout_rc -ne 0 ]]; then
    STATUS=1
fi

if [[ $CHECK -eq 1 && $STATUS -ne 0 ]]; then
    echo
    echo "format: formatting differences found (nothing was written)."
    echo "Re-run without --check, scoped to the same --path/--project, to apply."
fi

exit "$STATUS"
