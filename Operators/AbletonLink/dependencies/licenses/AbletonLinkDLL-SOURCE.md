# Source of AbletonLinkDLL.dll

`AbletonLinkDLL.dll` (Windows x64, 260608 bytes, SHA-256 69015b0d831350c1…) is a small C wrapper around Ableton Link,
taken unchanged from the VL.IO.AbletonLink project. It is distributed under the GNU General Public License version 2
or later (see `GPL-2.0-AbletonLink.txt`), because it includes Ableton Link.

It is built from these exact sources:

| Part | Repository and commit | Licence |
|---|---|---|
| Wrapper (`src/AbletonLinkDLL`) | [SpaceMusicZH/VL.IO.AbletonLink @ 2e2b2316](https://github.com/SpaceMusicZH/VL.IO.AbletonLink/tree/2e2b23166ad25ec6116a5d776a15f5ddb8842416/src/AbletonLinkDLL) | CC0-1.0 |
| Ableton Link (submodule `src/AbletonLinkDLL/link`) | [Ableton/link @ 2641130b](https://github.com/Ableton/link/tree/2641130bca65cdfb95794b31a6453a825333bd28) | GPL-2.0-or-later |
| asio (submodule `modules/asio-standalone` of Link) | [chriskohlhoff/asio @ 01b4e87c](https://github.com/chriskohlhoff/asio/tree/01b4e87c04abd4daec58e40463bcdc150085b269) | BSL-1.0 |

The binary in the wrapper repository at that commit (`lib/netstandard2.0/AbletonLinkDLL.dll`) is byte-identical to
the one shipped here.

To rebuild it: clone the wrapper repository, check out commit 2e2b2316 with its submodules
(`git submodule update --init --recursive`), and build `src/AbletonLinkDLL/AbletonLinkDLL.vcxproj` for x64 Release
with Visual Studio.

The TiXL operator that calls it, `[AbletonLinkSync]`, ships its C# source in `SourceCode/` of this extension.
