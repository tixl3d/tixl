# TiXL on Nix

`flake.nix` provides:

- `tixl-bin` (the default package): the release tarball repackaged for Nix, with the .NET 10 SDK (TiXL compiles
  operators at runtime), the tested Slang compiler and the runtime libraries wired into its wrapper.
- `slang`: Slang 2026.18, the release TiXL's shaders are tested with.
- a development shell for building TiXL from source.

## Try the preview

```bash
nix run "github:tixl3d/tixl/feat/linux-port?dir=Installer/Linux/nix"
```

On NixOS the GPU drivers come from the system: `hardware.graphics.enable = true;` (plus
`services.xserver.videoDrivers = [ "nvidia" ];` for NVIDIA). On other distributions a Nix-built TiXL can't use
the host's drivers without [nixGL](https://github.com/nix-community/nixGL).

The package is marked unfree because it ships the BASS audio library, which is free for non-commercial use only;
commercial use needs a BASS license from un4seen. The flake allows exactly this package, so no
`NIXPKGS_ALLOW_UNFREE` is needed.

## Build from source

```bash
nix develop "github:tixl3d/tixl/feat/linux-port?dir=Installer/Linux/nix"
dotnet build t3.sln
dotnet Editor/bin/Debug/net10.0/TiXL.dll
```

## Known limitations

- Exported players keep the standard dynamic loader, so they run on other distributions. On NixOS itself they
  need `nix-ld` or `steam-run`.
- `tixl-bin` follows the release tarball. A nixpkgs package would build from source with pinned NuGet
  dependencies instead; that waits for the stable 4.4 release and TiXL's own audio backend replacing BASS.
- Updating to a new release: change `version` and `hash` in `flake.nix`
  (`nix-prefetch-url --type sha256 <tarball url>`, then `nix hash convert --hash-algo sha256 <hash>`).
