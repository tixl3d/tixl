# TiXL on Nix

## Run it on NixOS

```bash
nix run "github:tixl3d/tixl/feat/linux-port?dir=Installer/Linux/nix"
```

## Run it on another distribution

A Nix-built TiXL cannot reach the host's GPU drivers on its own.
[nixGL](https://github.com/nix-community/nixGL) puts them in front of it:

```bash
# Mesa (Intel, AMD)
nix run --impure github:nix-community/nixGL#nixGLIntel -- \
  nix run "github:tixl3d/tixl/feat/linux-port?dir=Installer/Linux/nix"

# NVIDIA's proprietary driver
nix run --impure github:nix-community/nixGL#nixGLNvidia -- \
  nix run "github:tixl3d/tixl/feat/linux-port?dir=Installer/Linux/nix"
```

## Add it to a system or home configuration

```nix
{
  inputs.tixl.url = "github:tixl3d/tixl/feat/linux-port?dir=Installer/Linux/nix";

  # …

  environment.systemPackages = [ inputs.tixl.packages.x86_64-linux.tixl ];
}
```

The package is unfree, because it ships the BASS audio library: free for non-commercial use, commercial use
needs a license from un4seen. No `allowUnfree` setting is needed on either side — the flake allows this one
package where it builds it, so neither `nix run` nor a configuration referencing it has to opt in.

## Dependency locking

`deps.json` pins every NuGet package the restore pulls, because the build sandbox has no network.
Regenerate it whenever a `PackageReference` or the .NET SDK version changes:

```bash
nix run .#fetch-deps -- "$PWD/deps.json"
```

## Good to know

- The editor compiles shaders with nixpkgs' `shader-slang`, not the Slang release TiXL pins, so the wrapper
  sets `TIXL_SLANGC_UNPINNED=1` and the startup check logs the version in use instead of asking for the
  pinned one.
- Exported players are bound to this machine: their .NET runtime comes from the nixpkgs packs, which point
  into the Nix store. Build the tarball (`Installer/Linux/build-tarball.sh`) for players to hand around.