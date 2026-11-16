---
navigation.order: 20
description: Run the MDU Asset Server behind a Traefik that already serves other sites, and deploy every build of main from GitHub.
---

# Behind Traefik

On a server where Traefik already serves other sites, the container joins Traefik's Docker network instead of bringing its own proxy. The files are in [`Deploy/traefik`](https://github.com/capjan/mdu-assets/tree/main/Deploy/traefik):

| File | Purpose |
| --- | --- |
| `docker-compose.yml` | The service with Traefik labels: two routers, one per host name, both to port 8080, TLS from Traefik's certificate resolver |
| `deploy.sh` | Builds and starts one commit of `main`, then checks `/healthz` through Traefik |

The host names come from `UI_HOST` and `PUBLIC_HOST` in `.env`, so they stay on the server and out of the repository and the public Actions log. Adjust the entry point (`websecure`), the certificate resolver (`mytlschallenge`) and the network (`web`) to your Traefik. `MDU_ASSETS_TRUSTEDPROXIES` is the subnet of that network (`docker network inspect web`); every container in it could claim HTTPS and client addresses, so keep only your own services there.

```bash title="On the server"
mkdir -m 700 ~/container/mdu-assets && cd ~/container/mdu-assets
git clone https://github.com/capjan/mdu-assets.git source
cp source/Deploy/traefik/docker-compose.yml source/Deploy/traefik/deploy.sh .
umask 077 && printf 'UI_HOST=assets.example.com\nPUBLIC_HOST=img.example.com\nADMIN_USER=admin\nADMIN_PASSWORD=%s\n' "$(head -c 24 /dev/urandom | base64)" > .env
./deploy.sh "$(git -C source rev-parse origin/main)"
```

After the first start, sign in, change the password, and delete `ADMIN_PASSWORD` from `.env`.

## Automatic deployment from GitHub

The workflow [`deploy.yml`](https://github.com/capjan/mdu-assets/blob/main/.github/workflows/deploy.yml) deploys every successful build of `main`. Because the repository is public, it is built so that neither pull requests nor a leaked key can run code of their choice on the server:

- It runs only after the **Build** workflow succeeded for a push to `main` of this repository, or when started by hand. Builds of pull requests and forks never deploy.
- Its secrets are in the GitHub environment `production`, which only the branch `main` may use.
- The SSH key can do one thing: in `~/.ssh/authorized_keys` it has `restrict` (no shell, no forwarding) and the forced command `deploy.sh`, which accepts only `deploy <commit sha>`.
- The server builds the image itself from its read-only checkout of the public repository, and only for commits that are on `main`. No registry or GitHub credentials are on the server, and no server credentials are in the image.
- The host key is pinned in a secret, so the workflow never trusts an unknown server.

Set it up once. Create a key without a passphrase on your computer, install its public half on the server, and store the rest with the GitHub CLI:

```bash title="On your computer"
ssh-keygen -t ed25519 -N '' -C 'github-actions mdu-assets deploy' -f deploykey
```

```bash title="~/.ssh/authorized_keys on the server, one line"
restrict,command="/home/user/container/mdu-assets/deploy.sh" ssh-ed25519 AAAA… github-actions mdu-assets deploy
```

```bash title="On your computer"
echo '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' \
  | gh api -X PUT repos/OWNER/mdu-assets/environments/production --input -
gh api -X POST repos/OWNER/mdu-assets/environments/production/deployment-branch-policies -f name=main -f type=branch
gh secret set DEPLOY_SSH_KEY --env production < deploykey
ssh-keyscan -t ed25519 vps.example.com | gh secret set DEPLOY_KNOWN_HOSTS --env production
printf 'user@vps.example.com' | gh secret set DEPLOY_TARGET --env production
rm deploykey deploykey.pub
```

Compare the `ssh-keyscan` fingerprint with `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` on the server before you store it. To deploy by hand, run **Deploy** in the Actions tab or `gh workflow run deploy.yml`. To revoke the key, delete its line in `authorized_keys`.

The database schema is migrated on start. Public links never change between versions.
