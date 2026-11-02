# Beta software validation

Recorded on 2026-11-02. The release page identifies the final public source commit and its Windows CI run. The local evidence below covers the implemented beta features and distribution mechanism; it does not establish production workstation acceptance.

| Check | Observed result | Boundary |
| --- | --- | --- |
| Application regression suite | 253 passing .NET tests | Synthetic service/configuration scenarios |
| Browser/driver regression suite | 32 passing Node tests, including unrelated-profile rejection | Fault-injection and isolated broker tests |
| Native UI milestone | 9,948 assertions, zero binding warnings/errors | Native controls, keyboard, UI Automation and simulated contrast palettes; not actual Narrator speech or glove use |
| Startup recovery | Damaged configuration recovered only after backup PIN and confirmation; original bytes preserved; launcher stayed closed | Isolated real startup host |
| Installer | 23 checks pass: fresh install, app/bridge lock rejection, upgrade, rollback, all payload hashes, uninstall and unchanged production configuration | Unique product ID and disposable installation, actual complete payload |
| Archive integrity | All 878 payload files matched manifest hashes/lengths; no extra archive files; clean source `e1584156`; installer status `NotSigned` | Local pre-publication package; final release is built again at its published source |
| Dependency notices | All 13 exact dependencies/runtime entries match; unknown/stale inventory tests reject | License inventory and integrity, not an independent legal opinion |
| Firefox submission lint | Zero errors, warnings and notices for unsigned upload version 1.1.1 | Automated add-on lint, not store approval or Mozilla signing |

## Sustained real-host run

The isolated UI-check host ran for **1,801.41 seconds**, recorded **177 samples**, opened/closed settings throughout, and survived **five forced host-process terminations** followed by restart. Its independently launched managed application kept the same PID throughout. All health checks completed; the highest sampled request response was **11.89 ms** and peak private memory was **217.15 MiB**. The host includes test dependencies and these figures are not production-EXE performance guarantees or a long-term leak verdict.

The tested AppSwitcher assembly SHA-256 was `DE78052441377B90D1604DA449CA9CD0C3C6597598673A5601F356EDF3121BFF`. The report retains per-sample host PID, memory, handles, threads and CPU time. Only the synthetic host was terminated; no Windows reboot, physical power loss or machine policy was applied. The final public CI runs the current source separately.

## Visible browser checks

Chrome **157.0.8103.26**, Edge **156.0.4314.58** and Firefox **159.0** each passed 20 minimize/activate cycles through the real native host in isolated profiles. Chrome/Edge preserved the original document and unsaved in-page value, left unrelated same-URL tabs alone, and reconnected after deliberate host disconnection. Firefox also verified its session ownership value, recovery after marker removal and preservation of the unrelated tab during close.

Bundled Chromium **156.0.8078.4** additionally passed a full close/reopen with restored tab IDs, activation of the existing owned tab, close of only the owned pair and preservation of the unrelated tab. The dedicated session helper attaches without navigating restored tabs: the generic CLI opener can navigate one to `about:blank`, which invalidates its marker and correctly leads to an uncertain-identity response. Browser checks run sequentially because the deliberate disconnect scenario affects the shared synthetic broker.

These checks use isolated unpacked/temporary extension copies with a per-run identity and a separate native-host registration. They do not establish store-distributed Chrome/Edge or permanently signed Firefox restart acceptance. No production browser profile or native-host registration was changed; test registrations and owned browser processes were removed afterward.

## Remaining workstation acceptance

Actual Narrator speech, intended touch panels/gloves, physical mixed-DPI hot-plug/docking, Windows kiosk policies/escape, reboot/power interruption, a disconnected target PC and a representative full-shift workload require the site record in [WORKSTATION_ACCEPTANCE.md](WORKSTATION_ACCEPTANCE.md). A procedure or simulated test is not recorded as a pass for those items.
