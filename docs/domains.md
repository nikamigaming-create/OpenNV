# Open Nevada domains

Open Nevada owns two complementary domains:

| Domain | Role | Intended behavior |
| --- | --- | --- |
| `opennevada.com` | Canonical public home | **Live.** Product site, release links, and documentation entry point. |
| `opennv.org` | Short technical/community address | **Live.** HTTPS 301 redirect to the canonical public home. |

## Production status

The canonical host serves the Open Nevada static site over HTTPS. The short
domain is proxied through Cloudflare and redirects to the canonical hostname
before an origin is contacted.

`www.opennevada.com` and `www.opennv.org` are not configured yet. If they are
added later, make both permanent redirects to `https://opennevada.com`.

## Deployment guardrails

For future host or redirect changes, retain these constraints:

1. `opennevada.com` remains the selected public web host;
2. `www.opennevada.com` redirects to `https://opennevada.com`;
3. `opennv.org` and `www.opennv.org` remain HTTPS 301 redirects to
   `https://opennevada.com`.

Keep GitHub Releases as the download authority until the site has an automated
release feed. A domain must not present a download as official unless it points
to a tagged OpenNV release with checksums and source information.

## Registrar baseline

- Enable two-factor authentication, registrar lock, privacy protection, and
  auto-renewal for both names.
- Enable DNSSEC when the selected DNS provider supports it end-to-end.
- Do not publish MX, SPF, DKIM, or DMARC records until an actual mail provider
  is configured; an unmonitored address looks official but is unsafe.
- Keep the public site branded as **Open Nevada** and the compact technical
  identifier as **OpenNV / ONV**.

The domains are product identity only. They do not change the requirement that
players provide game files, DLC, conversions, and mod archives they are legally
allowed to use.
