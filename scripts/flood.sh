#!/usr/bin/env bash
# Posts N contact messages (default 100) to the form with curl, 20 at a time, and counts the
# status codes. This is what a bot that doesn't run JavaScript does: it needs no browser.
# Usage: scripts/flood.sh [base-url] [count]
set -euo pipefail

base="${1:-http://localhost:5200}"
count="${2:-100}"

start=$(date +%s%N)
seq 1 "$count" | xargs -P 20 -I{} curl -s -o /dev/null -w '%{http_code}\n' \
  --data-urlencode "name=Bot {}" \
  --data-urlencode "email=bot{}@example.com" \
  --data-urlencode "message=Buy cheap watches, offer {}" \
  "$base/contact" | sort | uniq -c | sed 's/^ *//' |
  while read -r n code; do echo "HTTP $code: $n posts"; done
end=$(date +%s%N)
ms=$(((end - start) / 1000000))
printf 'Done in %d.%03d s\n' $((ms / 1000)) $((ms % 1000))
