# Legacy entry point retained so old instructions fail safely instead of overwriting
# immutable native-host installations or removing another version's registration.
[CmdletBinding()]
param()
throw 'This legacy script is retired. Open AppSwitcher > Settings > Browsers > Setup for the selected browser. Use the AppSwitcher installer to uninstall. Browser profiles and tab ownership are preserved.'
