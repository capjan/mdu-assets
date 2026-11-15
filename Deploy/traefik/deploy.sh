#!/bin/sh
# Builds and starts one commit of main. Installed next to docker-compose.yml on the server and used as the forced
# command of the deploy key, so the key can do nothing else: SSH_ORIGINAL_COMMAND must be "deploy <commit sha>".
# By hand: ./deploy.sh <commit sha>
set -eu
umask 077
dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)

request=${SSH_ORIGINAL_COMMAND:-deploy ${1:-}}
case "$request" in
  "deploy "*) sha=${request#deploy } ;;
  *) echo "usage: deploy <commit sha of main>" >&2; exit 2 ;;
esac
if ! printf '%s' "$sha" | grep -Eqx '[0-9a-f]{40}'; then
  echo "usage: deploy <commit sha of main>" >&2
  exit 2
fi

exec 9>"$dir/.deploy.lock"
if ! flock -n 9; then
  echo "Another deployment is running." >&2
  exit 75
fi

# Only commits that are on main of the public repository are built; the checkout has no credentials.
cd "$dir/source"
git fetch --quiet origin main
if ! git merge-base --is-ancestor "$sha" origin/main; then
  echo "$sha is not on main." >&2
  exit 1
fi
git -c advice.detachedHead=false checkout --quiet --detach "$sha"

cd "$dir"
docker compose build --pull
docker compose up -d --remove-orphans
docker image prune --force >/dev/null

# Asks the new container through Traefik on this host, so routing and TLS are checked too. The host name stays out of
# the output, which ends up in the public Actions log.
host=$(sed -n 's/^UI_HOST=//p' .env | tr -d "\"'")
for _ in $(seq 1 30); do
  if [ "$(curl -fsS --resolve "$host:443:127.0.0.1" "https://$host/healthz" 2>/dev/null)" = Healthy ]; then
    printf '%s\n' "$sha" > "$dir/deployed"
    echo "Deployed $sha"
    exit 0
  fi
  sleep 2
done
echo "/healthz did not answer Healthy after the deployment." >&2
docker compose logs --tail 50 mdu-assets >&2
exit 1
