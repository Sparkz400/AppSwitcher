# Keyboard reference

Focus is shown around the active control. Available controls depend on your current access level. These shortcuts do not bypass a PIN or grant access to maintenance settings.

| Where | Key | Action |
| --- | --- | --- |
| Launcher and settings | **Tab / Shift+Tab** | Move to the next / previous focusable control. |
| Focused button | **Space** | Activate the button. Use this for launcher and maintenance actions. |
| Focused checkbox | **Space** | Toggle the option, when your access level permits it. |
| Launcher search | **Enter** | Launch or switch to the first matching app; a matching favourite takes precedence. |
| Launcher menu | **Escape** | Clear a nonempty search. Press again with an empty search to close the menu. |
| Settings | **Ctrl+F** | Focus **Find a setting** and select its current text. |
| Find a setting | **Enter** | Open the first matching setting. |
| Find a setting | **Escape** | Clear the search. This does not close the settings window. |
| Anywhere, if enabled and available | **Ctrl+Alt+Space** | Open the launcher menu and focus its search. |

The global shortcut is optional and off by default. Enable it in the settings search result for **shortcut**. Another application may already own the key combination; AppSwitcher reports when it cannot register it. The normal launcher button remains available. This shortcut is Windows-wide only while AppSwitcher is running.

To close settings, use its **Close** button. Settings preview live. Use **Save changes** to persist them. Closing with pending edits asks whether to discard; cancelling keeps the editor open. Session expiry discards unsaved changes and closes privileged dialogs. Pinning/position updates made during editing join the preview and cannot save unrelated edits. Do not assume **Escape** dismisses every dialog or discards changes. A button marked as a dialog's default action may also accept **Enter**; use the explicit actions above when navigating the launcher or search.

The reference follows the application's key handlers and a visible English Windows walkthrough with synthetic apps. It is not a complete Narrator or workstation accessibility qualification; that remains tracked in [issue #10](https://github.com/Sparkz400/AppSwitcher/issues/10). Report a specific focus or keyboard problem through [Getting help](../SUPPORT.md), including the screen, focused control and exact key sequence.
