# Precision Console

AppSwitcher uses one restrained visual system for a compact operator launcher, a larger touch presentation, and a resizable maintenance workspace.

## Visual system

- Segoe UI, 14 px body, 12 px secondary text, 24 px page title; tabular numbers for comparable readings.
- Neutral opaque surfaces, 4/8 px spacing rhythm, 4–6 px corners, subtle separators. Shadows only on floating content.
- Light and graphite palettes. State labels accompany every colored indicator. Red indicates faults or destructive actions; amber indicates attention; green indicates running software.
- Shared control templates, clear keyboard focus, meaningful automation names, accessible contrast, and visible disabled reasons.
- App identity: cream and cyan overlapping windows with exchange arrows, on transparency with minimal outer padding so the symbol fills the taskbar icon. `Assets/AppSwitcher.png` is the generated source; `scripts/build-app-icon.ps1` packages the transparent 16–256 px Windows icon used by the executable and custom captions. The generation prompt is kept beside the source asset.
- Themed custom captions use WPF WindowChrome to retain native dragging, resizing, maximize and system-menu behavior. Transparent overlays use the base window style without a caption. The compact launcher is only its configured shape and icon, without a surrounding panel, border, padding or label; it retains topmost and position-lock behavior.
- Themed scrollbars retain wheel, track, thumb, keyboard and horizontal scrolling. Save is hidden when the persisted configuration matches the editor; runtime status changes do not count as edits.
- Checkboxes, sliders, dropdowns and context menus share the theme while retaining native WPF keyboard and accessibility behavior. Settings search opens the matching section/field, and bounded undo applies only to pending configuration edits.
- Feedback is immediate and quiet; no decorative glow or animation in the default launcher.
- Use software rendering for these small 2D surfaces to avoid a dedicated hardware device's driver buffers and workers. Keep a documented hardware override for deployment-specific needs. Preserve all controls/effects, and verify rendering, input, scrolling and lifecycle behavior when changing rendering or publish settings.

## Surfaces

- Launcher: favorites and groups in a stable order, application state, direct open action, visible secondary-action menu, maintenance and monitoring entry points.
- Maintenance: Applications, Monitoring, Layouts, Access & kiosk, Audit history, System, Browser connections. Existing role checks determine availability. Browser diagnostics refresh on opening or explicitly, without a new background poller.
- Application details: General, Startup, Recovery, Window placement, Advanced. No long mixed configuration page.
- Add app offers an open-window picker and manual entry. The picker shows the selected program for review, reuses existing entries and routes browsers into website setup. Capture only on open/refresh; never make choosing a window depend on a background index or silently store changing document titles as matching rules.
- Common fields stay visible; related optional settings use named 44 px disclosures. Configured per-app sections open on selection, and search expands the exact target. Keep actionable errors and operational/security impact visible. Use short labels and contextual help instead of repeated paragraphs or ambiguous icon-only actions.
- Launcher settings use the same binding-only drawing as the actual launcher for a live preview. Preview UI exists only in settings; it adds no background polling or process.
- Dialogs and feedback: authentication and setup, confirmation, notifications, empty lists, invalid fields, import comparison, backups, and session expiry.

## Delivery checks

Preserve existing tests and add focused checks for new navigation, presentation state, permissions, localization, and configuration compatibility. Inspect actual WPF renders in both themes and languages, at compact/desktop sizes and scaled layouts. Verify touch targets, keyboard focus, long names, faults, and empty data. Record the scope and limitations of native and hardware evidence.

See [researched UX priorities](docs/UX_PRIORITIES.md) for the current scope and proposed next additions. Exercise both collapsed and expanded settings, including validation and search into hidden controls.
