# Open Nevada domains

Open Nevada owns two complementary domains:

| Domain | Role | Intended behavior |
| --- | --- | --- |
| `opennevada.com` | Canonical public home | Product site, release links, and documentation entry point. |
| `opennv.org` | Short technical/community address | Permanent redirect to the canonical public home until a distinct technical portal is useful. |

## Safe first deployment

Do not point either name at a host until that host has a valid TLS certificate
and a minimal public page. Once a host is chosen, configure:

1. `opennevada.com` at the selected web host;
2. `www.opennevada.com` as a redirect to `https://opennevada.com`;
3. `opennv.org` and `www.opennv.org` as HTTPS 301 redirects to
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
