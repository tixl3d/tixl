# libomtnet (vendored)

Managed implementation of [Open Media Transport](https://github.com/openmediatransport/libomtnet) (MIT), used by
the `Omt` operator package for `OmtInput`, `OmtOutput` and OMT streams in the output setup.

- Upstream: https://github.com/openmediatransport/libomtnet
- Pinned commit: `7d29031` (2026-10-13, "Discovery improvements and fixes on Windows"), version 1.0.0.20
- Built from source as part of the solution (`libomtnet.csproj`, net10.0) instead of shipping the release DLL, so
  the fixes below apply on every platform.

The VMX video codec it calls into (`libvmx`) is native and ships per platform under
`Operators/Omt/dependencies/runtimes/<rid>/native/`. See `Vendor/libvmx/README.md` for where each binary comes from.

## Local patches

All are marked `TiXL patch` in the source. Each one turns a condition that terminated the host process (or made OMT
unusable) into a logged, recoverable state. Worth upstreaming.

| File | Problem | Change |
| --- | --- | --- |
| `src/linux/OMTDiscoveryAvahi.cs` | Without a running `avahi-daemon`, `avahi_client_new` returns NULL and is passed to `avahi_service_browser_new` / `avahi_entry_group_new`, which `assert(client)` and **abort the process**. | Stop after a failed client; refuse to register without one. |
| `src/linux/OMTDiscoveryAvahi.cs` | `ServiceBrowserCallback` is a native-to-managed callback without exception handling; any exception there terminates the process. | Wrapped in try/catch. |
| `src/linux/OMTDiscoveryAvahi.cs` | `Thread.Abort()` throws `PlatformNotSupportedException` on .NET Core. | Removed; the join timeout is kept. |
| `src/OMTSocketAsyncPool.cs` | `OnCompleted` runs on an I/O completion thread without exception handling. | Wrapped in try/catch. |
| `src/OMTDiscovery.cs` | A missing mDNS stack (`libavahi-client.so.3` not installed) made `GetInstance` throw, which every sender and receiver constructor calls. | Falls back to the base class (no discovery); `omt://host:port` addresses keep working. |
| `src/OMTSend.cs`, `src/OMTMultiConnectSocket.cs`, `src/OMTUtils.cs` | Sockets were always IPv6 dual-stack, which fails on machines with IPv6 disabled (`ipv6.disable=1`, some containers). | `OMTUtils.CreateStreamSocket` falls back to IPv4. |
| `src/OMTLogging.cs` | An exception from the log writer or callback ended the logging thread while `Write` kept queueing, growing memory for the rest of the process; a callback alone never received lines. | Per-line try/catch, bounded queue, callback-only logging. |
| `src/linux/LinuxPlatform.cs` | `DllImport("libdl.so")` only resolves where development packages are installed. | `libdl.so.2`. |

## Updating

Replace `src/` with the new upstream `src/`, convert line endings to LF, re-apply the patches above (search for
`TiXL patch` in the old tree) and update the pinned commit here.
