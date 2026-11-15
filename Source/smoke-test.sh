#!/usr/bin/env bash
# Smoke test against a running server: health, upload with Basic Auth, the public link, and libvips (preview, ?w=).
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
# A real 4x3 PNG without metadata: the server decodes uploads with libvips and keeps such files byte for byte.
printf '\x89\x50\x4e\x47\x0d\x0a\x1a\x0a\x00\x00\x00\x0d\x49\x48\x44\x52\x00\x00\x00\x04\x00\x00\x00\x03\x08\x02\x00\x00\x00\x3b\x96\x39\x91\x00\x00\x00\x09\x70\x48\x59\x73\x00\x00\x03\xe8\x00\x00\x03\xe8\x01\xb5\x7b\x52\x6b\x00\x00\x00\x28\x49\x44\x41\x54\x08\x99\x05\xc1\x31\x01\x00\x00\x08\xc3\xb0\x3a\x43\xc5\xbc\xf4\xc3\xcf\x4c\x60\x8d\x04\x70\x70\xf1\x10\xe2\xc4\x8d\x17\xa1\x4e\xdd\x7a\xf5\x01\xe3\xb6\x0e\x14\x0b\x7d\x52\x59\x00\x00\x00\x00\x49\x45\x4e\x44\xae\x42\x60\x82' > "$png"

response=$(curl -sf -u "$user:$password" -H 'Content-Type: image/png' --data-binary "@$png" "$ui/api/v1/projects/default/assets?name=smoke.png")
echo "$response"
url=$(printf '%s' "$response" | sed -n 's/.*"url":"\([^"]*\)".*/\1/p')
case "$url" in "$public"/i/*) ;; *) echo "unexpected link: $url" >&2; exit 1 ;; esac

curl -sf -o /dev/null -w 'public link: %{http_code} %{content_type}\n' "$url"
cmp <(curl -sf "$url") "$png"

# libvips in the program folder: the preview and a smaller width are made on demand.
id=$(printf '%s' "$response" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p')
curl -sf -u "$user:$password" -o /dev/null -w 'preview: %{http_code} %{content_type}\n' "$ui/api/v1/assets/$id/thumbnail"
test "$(curl -sf -u "$user:$password" -o /dev/null -w '%{content_type}' "$ui/api/v1/assets/$id/thumbnail")" = image/webp
curl -sf -o /dev/null -w 'width 320: %{http_code} %{content_type}\n' -H 'Accept: image/webp' "$url?w=320"
status=$(curl -s -o /dev/null -w '%{http_code}' -u "$user:wrong-password" --data-binary "@$png" "$ui/api/v1/projects/default/assets")
test "$status" = 401
echo "smoke test passed"
