# Beta software validation

Recorded on 2026-11-02. [Public beta v1.2.0-beta.1](https://github.com/Sparkz400/AppSwitcher-public/releases/tag/v1.2.0-beta.1) was built at clean public commit `5ebac9b9dcc538349e3ec143375635cf031f316d`; its [Windows CI run](https://github.com/Sparkz400/AppSwitcher-public/actions/runs/41513508379) passed. The released artifacts remain tied to that tag; later documentation updates do not replace them. The evidence below covers the implemented beta features and distribution mechanism; it does not establish production workstation acceptance.

| Check | Observed result | Boundary |
| --- | --- | --- |
| Application regression suite | 253 passing .NET tests | Synthetic service/configuration scenarios |
| Browser/driver regression suite | 32 passing Node tests, including unrelated-profile rejection | Fault-injection and isolated broker tests |
| Native UI | 9,949 assertions in public release CI; 9,948 in the local milestone, zero binding warnings/errors | Native controls, keyboard, UI Automation and simulated contrast palettes; checks enumerate visible controls, not actual Narrator speech or glove use |
| Startup recovery | Damaged configuration recovered only after backup PIN and confirmation; original bytes preserved; launcher stayed closed | Isolated real startup host |
| Installer | 23 checks pass: fresh install, app/bridge lock rejection, upgrade, rollback, all payload hashes, uninstall and unchanged production configuration | Unique product ID and disposable installation, actual complete payload |
| Archive integrity | All 879 payload files and 35 store-bundle files match manifest hashes; payload lengths match; no extra/duplicate archive files; all three asset checksums match; clean source `5ebac9b9`; installer status `NotSigned` | Independently checked after downloading the final public CI artifacts and before release publication |
| Dependency notices | All 13 exact dependencies/runtime entries match; unknown/stale inventory tests reject | License inventory and integrity, not an independent legal opinion |
| Firefox submission lint | Zero errors, warnings and notices for unsigned upload version 1.1.1 | Automated add-on lint, not store approval or Mozilla signing |

A separate private-repository run at the identical initial source tree timed out during fresh installer startup before any payload-copy log entry or completed assertion. Its setup log opened more than two minutes after launch. The retained failed report is not counted as a pass. The test harness subsequently allows a bounded five minutes per setup process while retaining all 23 assertions and the workflow's overall 30-minute limit; this changes no released application or installer behavior. The public release run above passed the original three-minute bound.

## Sustained real-host run

The isolated UI-check host ran for **1,801.41 seconds**, recorded **177 samples**, opened/closed settings throughout, and survived **five forced host-process terminations** followed by restart. Its independently launched managed application kept the same PID throughout. All health checks completed; the highest sampled request response was **11.89 ms** and peak private memory was **217.15 MiB**. The host includes test dependencies and these figures are not production-EXE performance guarantees or a long-term leak verdict.

The tested AppSwitcher assembly SHA-256 was `DE78052441377B90D1604DA449CA9CD0C3C6597598673A5601F356EDF3121BFF`. The report retains per-sample host PID, memory, handles, threads and CPU time. Only the synthetic host was terminated; no Windows reboot, physical power loss or machine policy was applied. The final public CI runs the current source separately.

## Visible browser checks

Chrome **157.0.8103.26**, Edge **156.0.4314.58** and Firefox **159.0** each passed 20 minimize/activate cycles through the real native host in isolated profiles. Chrome/Edge preserved the original document and unsaved in-page value, left unrelated same-URL tabs alone, and reconnected after deliberate host disconnection. Firefox also verified its session ownership value, recovery after marker removal and preservation of the unrelated tab during close.

Bundled Chromium **156.0.8078.4** additionally passed a full close/reopen with restored tab IDs, activation of the existing owned tab, close of only the owned pair and preservation of the unrelated tab. The dedicated session helper attaches without navigating restored tabs: the generic CLI opener can navigate one to `about:blank`, which invalidates its marker and correctly leads to an uncertain-identity response. Browser checks run sequentially because the deliberate disconnect scenario affects the shared synthetic broker.

These checks use isolated unpacked/temporary extension copies with a per-run identity and a separate native-host registration. They do not establish store-distributed Chrome/Edge or permanently signed Firefox restart acceptance. No production browser profile or native-host registration was changed; test registrations and owned browser processes were removed afterward.

## Remaining workstation acceptance

Actual Narrator speech, intended touch panels/gloves, physical mixed-DPI hot-plug/docking, Windows kiosk policies/escape, reboot/power interruption, a disconnected target PC and a representative full-shift workload require the site record in [WORKSTATION_ACCEPTANCE.md](WORKSTATION_ACCEPTANCE.md). A procedure or simulated test is not recorded as a pass for those items.
