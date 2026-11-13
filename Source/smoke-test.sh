#!/usr/bin/env bash
# Smoke test against a running server: health, upload with Basic Auth, and the public link.
# Usage: smoke-test.sh <base-url-of-ui> <base-url-of-public-links> <admin-user> <admin-password>
# Both URLs may point to the same address with different host names, for example
# http://127.0.0.1:8080 (UI) and http://localhost:8080 (public links).
set -euo pipefail
# IPv4 only: "localhost" may resolve to ::1, where a container port published on 127.0.0.1 does not answer.
curl() { command curl -4 "$@"; }
ui="$1"; public="$2"; user="$3"; password="$4"

for _ in $(seq 30); do curl -sf "$ui/healthz" >/dev/null && break; sleep 1; done
curl -sf "$ui/healthz"; echo

png=$(mktemp)
printf '\x89PNG\r\n\x1a\n\x00\x00\x00\x0dIHDR\x00\x00\x00\x02\x00\x00\x00\x03\x08\x02\x00\x00\x00\x00\x00\x00\x00' > "$png"

response=$(curl -sf -u "$user:$password" -H 'Content-Type: image/png' --data-binary "@$png" "$ui/api/v1/projects/default/assets?name=smoke.png")
echo "$response"
url=$(printf '%s' "$response" | sed -n 's/.*"url":"\([^"]*\)".*/\1/p')
case "$url" in "$public"/i/*) ;; *) echo "unexpected link: $url" >&2; exit 1 ;; esac

curl -sf -o /dev/null -w 'public link: %{http_code} %{content_type}\n' "$url"
cmp <(curl -sf "$url") "$png"
status=$(curl -s -o /dev/null -w '%{http_code}' -u "$user:wrong-password" --data-binary "@$png" "$ui/api/v1/projects/default/assets")
test "$status" = 401
echo "smoke test passed"
