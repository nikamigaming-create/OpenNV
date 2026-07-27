# Publishing the Open Nevada site on Cloudflare Pages

The repository contains a dependency-free static site in [`site/`](../site/).
Cloudflare Pages should publish that one directory without a build framework.

## Current production state

- `https://opennevada.com` is live and serves the Open Nevada landing page.
- `https://opennv.org` is live and permanently redirects to the canonical URL.
- The `www` hostnames are intentionally unassigned for now; add them only as
  permanent redirects to the apex hostname.

## Project settings

In Cloudflare, create a **Pages** project from
`nikamigaming-create/OpenNV` with these exact settings:

| Field | Value |
| --- | --- |
| Production branch | `main` |
| Framework preset | None |
| Build command | `exit 0` |
| Build output directory | `site` |
| Root directory | `/` |

`site/index.html` is the entry point. Every push to `main` becomes a production
deployment after the GitHub connection is authorized; non-main branches receive
preview deployments.

## Domains

Use **one** public Pages project:

1. Add `opennevada.com` as the canonical custom domain in the project's
   **Custom domains** screen.
2. Add `www.opennevada.com` only if it will redirect to the apex or serve the
   same project.
3. Configure a permanent redirect from `opennv.org` and `www.opennv.org` to
   `https://opennevada.com`, preserving the path and query string.

Attach a domain through the Pages project before creating manual DNS records.
For a zone already on Cloudflare, Pages can create the necessary DNS record;
creating a CNAME first can leave the hostname unassociated with the project.

This keeps Open Nevada's public identity canonical while retaining `opennv.org`
as the short technical/community address.
