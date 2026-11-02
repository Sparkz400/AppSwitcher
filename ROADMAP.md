# Public roadmap

AppSwitcher is a usable unsigned beta. This roadmap distinguishes software maintenance from work that requires publisher accounts or real workstation access. Priorities are intentions, not delivery dates. Bugs and private security reports can change the order.

## Current foundation

- Versioned offline ZIP, per-user installer, checksums, dependency notices and rollback guidance.
- Windows application, browser, native UI, recovery and installer checks.
- Protected pull requests, C#/JavaScript CodeQL, secret scanning and push protection.
- Weekly grouped dependency proposals and automated publication of verified tagged betas.
- [Contribution guidance](CONTRIBUTING.md), [support](SUPPORT.md) and [community expectations](CODE_OF_CONDUCT.md).

## Next priorities

| Priority | Work | Completion evidence / dependency |
| --- | --- | --- |
| High | [Representative workstation acceptance #9](https://github.com/Sparkz400/AppSwitcher/issues/9) | Target displays, touch/gloves, kiosk recovery, reboot/power interruption and sustained workload; requires a workstation owner. |
| High | [Narrator and keyboard validation #10](https://github.com/Sparkz400/AppSwitcher/issues/10) | Actual speech/focus results in English and German; interactive Windows testing welcome. |
| High | [Windows publisher signing #11](https://github.com/Sparkz400/AppSwitcher/issues/11) | Owner provisions signing credentials, then downloaded signatures and installer behavior are verified. |
| Normal | [Browser-store distribution #12](https://github.com/Sparkz400/AppSwitcher/issues/12) | Publisher accounts, approvals and restart/pairing checks on the actual installed store packages. |

CI does not close hardware acceptance. Prepared store files do not mean store approval, and checksums do not mean publisher signing. Follow each issue for its current state; report a concrete defect separately instead of expanding a broad checklist indefinitely.

## Start contributing

These documentation tasks have bounded acceptance criteria and do not require signing accounts or changes to application behavior:

- [Explain three common setup problems #13](https://github.com/Sparkz400/AppSwitcher/issues/13).
- [Add a compact keyboard reference #14](https://github.com/Sparkz400/AppSwitcher/issues/14).

See all [good first issues](https://github.com/Sparkz400/AppSwitcher/labels/good%20first%20issue) and [help wanted](https://github.com/Sparkz400/AppSwitcher/labels/help%20wanted). Comment on an issue before a substantial contribution so others can coordinate. Maintainers review scope and evidence; there is no guaranteed response time.

## Propose later work

Cross-platform support, automatic application updates and a stable 1.0-style support commitment are not scheduled. Use a [feature request](https://github.com/Sparkz400/AppSwitcher/issues/new?template=feature_request.yml) to explain a concrete operator need and deployment constraints. Preserve the offline installation and explicit update/rollback model when proposing distribution changes.
