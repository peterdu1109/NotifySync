#!/usr/bin/env bash
# memory.sh — append a timestamped note to obsidian/memory.md
# Usage: ./memory.sh <message...>
set -euo pipefail

VAULT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/obsidian"
MEMORY_FILE="$VAULT_DIR/memory.md"

mkdir -p "$VAULT_DIR"
[ -f "$MEMORY_FILE" ] || printf '# Memory\n\n' > "$MEMORY_FILE"

if [ "$#" -eq 0 ]; then
  printf 'Usage: %s <message>\n' "$0" >&2
  exit 1
fi

printf -- '- [%s] %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*" >> "$MEMORY_FILE"
