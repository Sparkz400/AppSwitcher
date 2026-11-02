# Security policy

Please report vulnerabilities privately using [GitHub's private vulnerability report form](https://github.com/Sparkz400/AppSwitcher-public/security/advisories/new). Do not post PIN hashes, customer configurations, browser profiles, credentials or exploit details in a public issue. If that form is unavailable, open an issue asking the maintainer to restore the private reporting route, without technical vulnerability details.

Include the AppSwitcher version/source commit, Windows and browser versions, affected component, a minimal synthetic reproduction, expected impact and any mitigation. Remove customer URLs, names, paths and secrets from logs and screenshots. Reports are reviewed on a best-effort basis; this community project has no guaranteed response time or commercial support contract. Coordinate public disclosure with the maintainer after a fix or mitigation is available.

During beta, security fixes target the latest beta release and `main`. Previous beta versions receive no separate maintenance branch. Upgrade after testing the new version on a representative workstation. See [maintenance policy](MAINTENANCE.md).

## Trust boundaries

- An AppSwitcher PIN protects application controls. It does not protect against someone who can modify the user's files, run code as that Windows user, administer the PC, or replace the executable. Use Windows account and filesystem controls for those boundaries.
- Configuration can launch programs and optional hooks. Treat imported configurations and backups as executable deployment instructions. Inspect their changes before applying them; never import an unknown file on a production workstation.
- The browser bridge uses a current-user pipe and registered extension origins. A compromised Windows user remains outside its protection boundary. Browser profiles and local journals contain identifiers and configured website addresses; protect them like other local configuration.
- Kiosk options require site-specific testing and an independent administrator escape/recovery method. AppSwitcher is not a safety controller or a substitute for machine safety functions.
- The first beta is unsigned. Download only the intended release and verify its checksums under your organization's approval process. Checksums are integrity evidence, not publisher authentication.

Release checks use locked packages, fail on NuGet vulnerability warnings, compare dependency notices with the actual payload, and retain test evidence. A clean automated scan is not proof that every vulnerability has been found.
