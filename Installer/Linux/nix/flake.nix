{
  description = "TiXL - real-time motion graphics: a preview package from the release tarball, and a development shell";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }:
    let
      system = "x86_64-linux";

      # tixl-bin is unfree only because it ships BASS (free for non-commercial use). Allowing exactly this
      # package keeps `nix run` working without NIXPKGS_ALLOW_UNFREE for everything else.
      pkgs = import nixpkgs {
        inherit system;
        config.allowUnfreePredicate = pkg: (pkg.pname or "") == "tixl-bin";
      };
      lib = pkgs.lib;

      dotnet = pkgs.dotnetCorePackages.sdk_10_0;

      # The Slang release TiXL's shaders are tested with; nixpkgs' shader-slang may be newer, and TiXL's startup
      # check warns about any other version.
      slang = pkgs.stdenv.mkDerivation {
        pname = "shader-slang-bin";
        version = "2026.18";
        src = pkgs.fetchurl {
          url = "https://github.com/shader-slang/slang/releases/download/v2026.18/slang-2026.18-linux-x86_64-glibc-2.27.tar.gz";
          hash = "sha256-5F6k8RfVG4wehPpJ9WIIHnOp8p0CvU9/rSBnhgMoKCk=";
        };
        sourceRoot = ".";
        nativeBuildInputs = [ pkgs.autoPatchelfHook ];
        buildInputs = [ pkgs.stdenv.cc.cc.lib pkgs.libx11 ];
        installPhase = ''
          runHook preInstall
          mkdir -p $out
          cp -r bin lib $out/
          install -Dm644 LICENSE $out/share/licenses/slang/LICENSE
          runHook postInstall
        '';
        meta = {
          description = "Slang shader compiler, the release TiXL is tested with";
          homepage = "https://github.com/shader-slang/slang";
          license = lib.licenses.asl20;
          sourceProvenance = [ lib.sourceTypes.binaryNativeCode ];
          platforms = [ system ];
        };
      };

      # SDL3 declares the backends it may load (video, input, audio) in its dlopen notes, which autoPatchelf
      # reads as dependencies.
      sdlBackends = with pkgs; [
        vulkan-loader
        libGL
        libdrm
        libx11
        libxext
        libxcursor
        libxi
        libxrandr
        libxfixes
        libxscrnsaver
        libxtst
        libxkbcommon
        wayland
        libdecor
        fribidi
        libthai
        alsa-lib
        libpulseaudio
        pipewire
        libjack2
        udev
        dbus
        libusb1
      ];

      # Also loaded at runtime but declared nowhere (BASS's audio, .NET's ICU and OpenSSL), so the wrapper and the
      # dev shell put everything on the library path. /run/opengl-driver holds the system's GPU drivers on NixOS.
      runtimeLibraryPath = "/run/opengl-driver/lib:" + lib.makeLibraryPath (sdlBackends ++ (with pkgs; [
        stdenv.cc.cc.lib
        icu
        openssl
        fontconfig
        freetype
        zlib
      ]));

      tixl-bin = pkgs.stdenv.mkDerivation rec {
        pname = "tixl-bin";
        version = "4.4.0.1";

        src = pkgs.fetchurl {
          url = "https://github.com/tixl3d/tixl/releases/download/v${version}/tixl-${version}-linux-x64.tar.gz";
          hash = "sha256-YXzwVC607nf5168t5Qk4jPOT8ZBi/MiRIvXgotF5kko=";
        };

        nativeBuildInputs = [ pkgs.autoPatchelfHook pkgs.makeWrapper ];
        buildInputs = [ pkgs.stdenv.cc.cc.lib pkgs.fontconfig ] ++ sdlBackends;

        # Optional SDL backends nixpkgs doesn't provide or that make no sense here: Steam integration, OpenBSD's
        # sndio, OpenXR, and the obsolete GLES 1 library name.
        autoPatchelfIgnoreMissingDeps = [ "libsteam_api.so" "libsndio.so.7" "libopenxr_loader.so.1" "libGLES_CM.so.1" ];

        dontBuild = true;
        dontStrip = true;

        # Patched by hand below, to leave the player out.
        dontAutoPatchelf = true;

        installPhase = ''
          runHook preInstall
          mkdir -p $out/lib/tixl $out/share
          cp -r . $out/lib/tixl/
          cp -r share/. $out/share/
          rm -rf $out/lib/tixl/share $out/lib/tixl/install-dependencies.sh

          # .NET's optional LTTng tracing provider: nothing loads it unless tracing is switched on, and its
          # liblttng-ust.so.0 is older than the one nixpkgs ships.
          find $out/lib/tixl -name libcoreclrtraceptprovider.so -delete
          runHook postInstall
        '';

        # Exports copy the player to other machines, so it keeps the standard interpreter and library lookup.
        # On NixOS itself an exported player then needs nix-ld or steam-run.
        postFixup = ''
          mv $out/lib/tixl/Player "$TMPDIR/Player"
          autoPatchelf $out/lib/tixl
          mv "$TMPDIR/Player" $out/lib/tixl/Player

          makeWrapper $out/lib/tixl/TiXL $out/bin/tixl \
            --prefix PATH : ${dotnet}/bin \
            --set-default TIXL_SLANGC ${slang}/bin/slangc \
            --prefix LD_LIBRARY_PATH : "${runtimeLibraryPath}" \
            --set-default DOTNET_CLI_TELEMETRY_OPTOUT 1
        '';

        meta = {
          description = "Real-time motion graphics and VJ tool (preview build repackaged from the release tarball)";
          homepage = "https://tixl.app";
          # MIT, plus the bundled BASS audio library: free for non-commercial use, commercial use needs a license
          # from un4seen.
          license = [ lib.licenses.mit lib.licenses.unfree ];
          sourceProvenance = [ lib.sourceTypes.binaryNativeCode lib.sourceTypes.binaryBytecode ];
          platforms = [ system ];
          mainProgram = "tixl";
        };
      };
    in
    {
      packages.${system} = {
        inherit tixl-bin slang;
        default = tixl-bin;
      };

      # For building TiXL from source: run `dotnet build t3.sln` and start the editor with
      # `dotnet Editor/bin/Debug/net10.0/TiXL.dll`. The built TiXL executable expects a standard dynamic
      # loader, which NixOS doesn't have without nix-ld.
      devShells.${system}.default = pkgs.mkShell {
        packages = [ dotnet slang pkgs.vulkan-tools pkgs.vulkan-validation-layers pkgs.git ];
        LD_LIBRARY_PATH = runtimeLibraryPath;
        TIXL_SLANGC = "${slang}/bin/slangc";
        VK_ADD_LAYER_PATH = "${pkgs.vulkan-validation-layers}/share/vulkan/explicit_layer.d";
        DOTNET_CLI_TELEMETRY_OPTOUT = "1";
        shellHook = ''
          echo "TiXL dev shell: dotnet build t3.sln, then dotnet Editor/bin/Debug/net10.0/TiXL.dll"
          echo "Vulkan validation: TIXL_VULKAN_VALIDATION=1"
        '';
      };
    };
}
