# Startup and recovery

Settings for show machines that must come up on their own. Find them in **Settings → Startup and Recovery**.

Startup automation is **disabled by default**. Switch on **Enable** at the top of the panel to activate it — until then TiXL starts normally with the Hub, nothing autostarts, and a crashed editor stays closed. Turning the switch off again also removes the login entry.

- **Startup project**: a project folder that opens automatically after launch, instead of the Hub. When TiXL was started by the OS login entry, the welcome window is skipped.
- **Start with the system**: installs a per-user login entry. Windows uses the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key, Linux `~/.config/autostart/app.tixl.TiXL.desktop`, macOS `~/Library/LaunchAgents/app.tixl.TiXL.autostart.plist`. None of them need administrator rights. The entry is refreshed on every launch so it follows the current build.
- **Restart after a crash**: runs the editor under a small supervisor process (`--watchdog`) that relaunches it when it exits abnormally.
