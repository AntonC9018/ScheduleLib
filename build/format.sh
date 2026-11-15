#!/usr/bin/env bash
# Apply/check the shared JetBrains formatting profile; see docs/formatting.md.
set -euo pipefail
python3 "$(dirname "${BASH_SOURCE[0]}")/format.py" "$@"
