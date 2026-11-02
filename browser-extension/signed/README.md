# Firefox offline release

`appswitcher-firefox-1.1.0.xpi` is the Mozilla-signed, unlisted AppSwitcher Browser Bridge release, approved on 2026-11-02.

- Add-on ID: `browser@appswitcher.local`
- AMO add-on: `3115878`, file: `5198435`
- Version page: https://addons.mozilla.org/developers/addon/3115878/versions
- SHA-256: `FAC79D81477CC00F7FCE5C1E11C3C73AD4C755713D13CEE9BA6029D8FE81FE86`

Retrieved from the user's installed Firefox extension after their approval-page screenshot and installation confirmation. Firefox reports it active, not disabled, and `signedState: 2`. Every non-signature archive file matches the submitted unsigned package byte for byte.

The package builder selects the signed file matching `manifest.firefox.json`'s version by default and compares its complete payload against the generated Firefox extension before publishing. Changing extension source requires a new version and a newly signed XPI; do not edit this signed archive. The builder checks payload consistency and signature metadata; Firefox performs cryptographic signature verification during installation.

Copying this XPI into an offline release requires no store access or account on target machines. Updates are distributed with a new offline package.
