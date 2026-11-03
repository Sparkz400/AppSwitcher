# Public roadmap

AppSwitcher is an unsigned release candidate. Stable Windows releases will also support unsigned distribution with no paid signing requirement. This roadmap distinguishes software maintenance from work that requires real workstation access or optional publisher accounts. Priorities are intentions, not delivery dates. Bugs and private security reports can change the order.

## Current foundation

- Versioned offline ZIP, per-user installer, checksums, SPDX inventory, dependency notices and rollback guidance.
- Windows application, browser, native UI, recovery and installer checks.
- Protected pull requests, C#/JavaScript CodeQL, secret scanning and push protection.
- Weekly grouped dependency proposals, signed build records and automated publication of immutable verified betas.
- [Contribution guidance](CONTRIBUTING.md), [support](SUPPORT.md) and [community expectations](CODE_OF_CONDUCT.md).

## Next priorities

The [3 November release audit](docs/RELEASE_AUDIT_2026-11-03.md) found additional runtime and settings defects. Follow the [first stable release TODO](docs/RELEASE_TODO.md) for reproductions, acceptance criteria, UI simplification and release gates before treating the beta as stable.

| Priority | Work | Completion evidence / dependency |
| --- | --- | --- |
| High | [Representative workstation acceptance #9](https://github.com/Sparkz400/AppSwitcher/issues/9) | Target displays, keyboard/visible focus, visual feedback without sound, touch/gloves, kiosk recovery, reboot/power interruption and sustained workload; requires a workstation owner. |
| Optional | [Narrator speech validation #10](https://github.com/Sparkz400/AppSwitcher/issues/10) | Spoken English/German screen-reader results remain unverified. Final workstations have no audio output, so speech testing is outside this release scope; keyboard checks remain required under workstation acceptance. |
| Optional | [Windows publisher signing #11](https://github.com/Sparkz400/AppSwitcher/issues/11) | Deferred under the zero-cost release policy; unsigned stable releases are supported. No certificate purchase is required. |
| Optional | [Browser-store distribution #12](https://github.com/Sparkz400/AppSwitcher/issues/12) | Publisher accounts, approvals and restart/pairing checks on the actual installed store packages; paid store distribution is not required for release. |

CI does not close hardware acceptance. Prepared store files do not mean store approval, and checksums do not mean publisher signing. Follow each issue for its current state; report a concrete defect separately instead of expanding a broad checklist indefinitely.

## Start contributing

The initial documentation tasks are covered by [troubleshooting](docs/TROUBLESHOOTING.md) ([#13](https://github.com/Sparkz400/AppSwitcher/issues/13)) and the [keyboard reference](docs/KEYBOARD.md) ([#14](https://github.com/Sparkz400/AppSwitcher/issues/14)). Small, reproducible corrections to these guides are welcome.

See all [good first issues](https://github.com/Sparkz400/AppSwitcher/labels/good%20first%20issue) and [help wanted](https://github.com/Sparkz400/AppSwitcher/labels/help%20wanted). Comment on an issue before a substantial contribution so others can coordinate. Maintainers review scope and evidence; there is no guaranteed response time.

## Propose later work

Cross-platform support, automatic application updates and a stable-release support commitment are not scheduled. Use a [feature request](https://github.com/Sparkz400/AppSwitcher/issues/new?template=feature_request.yml) to explain a concrete operator need and deployment constraints. Preserve the offline installation and explicit update/rollback model when proposing distribution changes.
