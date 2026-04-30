# Swiftcam.cs — vendor C# wrapper

`Swiftcam.cs` in this folder is a verbatim copy of the C# P/Invoke wrapper shipped with the **Swift Imaging 3.0 SDK** by the camera vendor. It is the canonical interface to `swiftcam.dll`.

**Source:** `<SDK install>/dotnet/swiftcam.cs`, version `59.29807.20251021`.

**Modifications from the upstream file:**

- Added a single `namespace Lib.io.video.swiftcam;` declaration after the `using` block so the type lives under TiXL's `Lib` root namespace. No code changes.

**Do not hand-edit.** When the vendor ships a new SDK, replace this file wholesale, then re-apply the namespace declaration. Keep the diff to namespace-only so re-syncs stay trivial.

**`swiftcam.dll` is not redistributed.** The SDK ships no license file granting redistribution rights. Users install the Swift Imaging SDK separately — see [`.help/using/SwiftCamSetup.md`](../../../../../.help/using/SwiftCamSetup.md) for setup steps. Operators that depend on this wrapper handle the missing-DLL case gracefully via a runtime probe.
