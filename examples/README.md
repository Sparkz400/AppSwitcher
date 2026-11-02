# Safe starting configurations

These examples contain no PINs, private addresses, user paths, scripts, schedules, kiosk enforcement or automatic launches/restarts. They are starting points, not qualified machine configurations.

| File | Use |
| --- | --- |
| `desktop-starter.json` | One Notepad entry, for trying launch/switch behavior. |
| `touch-workstation.json` | Larger controls, a group and an operator scope for Notepad/Paint, with a workspace membership list. Window positions are deliberately empty until captured on your screens. |
| `browser-console.json` | An Edge website pointing to `https://example.com` and a Notepad entry. The all-zero profile identity is a placeholder that cannot pair with a real profile. Use browser Setup and select your connected profile before launching it. |

1. Export your current configuration in **Settings → System → Configuration & backups**.
2. Import one example and review the comparison. Import replaces application/group/layout configuration, so use a spare installation first. Existing administrator/operator PINs are preserved by the application import flow.
3. Choose installed executables; Notepad/Paint availability depends on your Windows installation. Replace example URLs and pair the intended browser profile. Do not reuse or manually edit application IDs to work around tab ownership.
4. Review the administrator PIN, session timeout, operator access and local recovery path. The examples set no default PIN. A scope takes effect for an authenticated operator, not as a Windows security boundary.
5. Save, launch each entry manually, test switching and capture a layout on your actual displays. Enable schedules, automatic restart or kiosk only after testing their behavior and exit/recovery routes.

The example JSON uses the app's current numeric enum format (`TargetKind`: 0 native, 1 website; `Browser`: 0 Chrome, 1 Edge, 2 Firefox). Prefer editing through AppSwitcher.
