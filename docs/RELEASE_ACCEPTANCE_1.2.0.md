# 1.2.0 publication decision and verification scope

On 3 November 2026, the repository owner requested that the prepared unsigned 1.2.0 package be put fully on GitHub and published, after receiving the completed local verification results and the remaining acceptance limits. This records authorization to publish the open-source software with those limits disclosed. It is not approval to deploy an untested workstation, and unperformed checks are not recorded as passed.

## Completed software evidence

The clean local package at `9f604edb203771c1a05fef80b17670b6b8bac954` passed 273 application tests, 32 browser/helper tests, 10,089 native UI assertions with zero binding warnings, 15 bundled-setup assertions, process/recovery checks and 23 installer lifecycle checks. The dependency audit reported no vulnerable dependencies across the four projects. All eight release assets, all 894 payload files, source manifests, checksums and the SPDX inventory were verified. AppSwitcher-owned Windows binaries and the installer were unsigned; the Mozilla-signed Firefox 1.1.0 payload was unchanged.

The exact local 1.2.0 installer also passed 19 offline VM checks as a standard user, upgrading the published RC to 1.2.0. All installed payload hashes matched; configuration bytes and three existing browser-host registrations/files were preserved. The VM network was restored and test processes were closed. Commands and test drivers were used, without Computer Use tools.

The [earlier published-RC VM qualification](VALIDATION.md#offline-windows-vm-qualification--3-november-2026) also covers permanent signed Firefox installation/restart, Chrome/Edge persistent installation and two unattended offline restarts each, native reconnect, unrelated-tab preservation, copied-profile conflicts, installer rollback and a 30-minute synthetic sustained run. These records identify the revisions actually tested; they are not physical workstation acceptance or a full-shift result.

The publication preparation changes release policy, tests and documentation only; application, native-host and extension implementation remains the tested 1.2.0 source. The public assets are built and checked again by GitHub Windows verification at the final main revision. The release notes link that exact run and CodeQL run. Local package hashes are not substituted for the GitHub-built asset hashes.

## Deferred checks and deployment limits

| Record | Status for publication | Remaining work |
| --- | --- | --- |
| Workstation | Deferred by the owner's publication instruction | Actual monitor/DPI/docking combinations, touch/gloves where used, keyboard and visible feedback, live contrast changes, kiosk escape, reboot/autostart and power-loss recovery, cold startup and representative full-shift workload on the intended workstation. |
| Offline browsers | Deferred only for the unperformed installation walkthrough and site-specific deployment | Chrome/Edge native folder-picker/manual installation walkthrough. Persistent unpacked installation state and unattended restarts passed through command-driven browser APIs. Qualify the exact deployed browser/profile/policy combination at the site. |

The [workstation acceptance record](WORKSTATION_ACCEPTANCE.md) remains unaccepted until its applicable checks are actually performed and recorded. The final workstations have no audio output; Narrator speech is outside their acceptance scope. Windows publisher signing and browser-store publication are optional under the owner's zero-cost distribution decision.

## Publication controls retained

Publishing requires protected-branch integration, successful Windows verification and CodeQL at the exact main commit, no blocking serious scanner findings, verified source/package manifests and checksums, SPDX inventory, build/SBOM attestations, matching server asset digests and immutable release verification. The workflow publishes the already tested CI assets. Existing releases are not replaced. The versioned authorization in `release-readiness.json` permits only the explicitly listed deferred deployment checks; it cannot satisfy or skip these CI/security/integrity controls.
