---
navigation.order: 70
description: Use the MDU Asset Server from MDU with mdu assets upload and mdu lint, from ShareX, and from scripts.
---

# Integrations

## MDU

[MDU](https://github.com/capjan/mdu) 6.10 and later can upload images to an asset server and check the links to it. MDU uses an asset server only when a site configures one in its `mdu.yml`:

```yaml title="docs/mdu.yml"
assets:
  server: https://assets.example.com
  public: https://img.example.com
  project: docs
```

The token comes from the environment, never from `mdu.yml`:

```bash
export MDU_ASSETS_TOKEN=mdua_…
```

### Upload

```bash
mdu assets upload screenshot.png --tag setup --alt "Settings page"
```

> [!RESULT]
> `![Settings page](https://img.example.com/i/k3Fq9xZ2aB/screenshot.webp)`

The command prints one snippet per file, so it can be piped, for example into the clipboard with `| pbcopy`. `--format url` or `--format html` prints the other snippets, `-` reads the file from standard input, and a file that is already in the project prints its existing link.

### Check links

`mdu lint` asks the server about every link on the public host. A link to an image that is unknown or in the trash is an error with the rule `MDU032`. When the server cannot be reached, lint only warns; `mdu lint --offline` skips the check. The check needs no token.

See [assets](https://capjan.github.io/mdu/Usage/assets.html) in the MDU documentation for all options.

## ShareX

On Windows, [ShareX](https://getsharex.com/) uploads every screenshot and puts the Markdown snippet into the clipboard. Create a token on the [Account](Usage/Account.md#integrations) page, download the ShareX configuration offered there, and double-click it to import it.

## Scripts

Any tool that can send an HTTP request can upload. The **Account** page offers a curl command for the chosen project and token; the [API](API.md#upload) describes all parameters.

```bash title="Upload and copy the Markdown on macOS"
curl -sS -H "Authorization: Bearer $MDU_ASSETS_TOKEN" -H "Content-Type: image/png" --data-binary @screenshot.png \
  "https://assets.example.com/api/v1/projects/docs/assets?name=screenshot.png" \
  | sed -n 's/.*"markdown":"\([^"]*\)".*/\1/p' | pbcopy
```
