{
  description = "TiXL - real-time motion graphics";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs }:
    let
      # The shipped BASS natives and the Slang build are x86-64 only.
      system = "x86_64-linux";

      # tixl is unfree only because it ships BASS (free for non-commercial use). Allowing exactly this
      # package keeps `nix run` working without NIXPKGS_ALLOW_UNFREE for everything else.
      pkgs = import nixpkgs {
        inherit system;
        config.allowUnfreePredicate = package: (package.pname or "") == "tixl";
      };

      lib = pkgs.lib;

      # Read out of Tixl.props, so a release bumps the version in one place. Matched per line, because
      # builtins.match has to match a whole string and the file is more than this one property.
      version = lib.head (
        builtins.match ".*<TixlVersion>([^<]+)</TixlVersion>.*" (
          lib.findFirst (line: lib.hasInfix "<TixlVersion>" line)
            (throw "No <TixlVersion> in Tixl.props - has the property been renamed?")
            (lib.splitString "\n" (builtins.readFile ../../../Tixl.props))
        )
      );

      # SDL3 declares the backends it may load (video, input, audio) in its dlopen notes, which autoPatchelf
      # reads as dependencies. The rest - BASS's audio, .NET's ICU and OpenSSL, fonts - is loaded without
      # being declared anywhere, so the whole set also goes on the wrapper's library path.
      runtimeLibraries = [
        pkgs.vulkan-loader
        pkgs.libGL
        pkgs.libdrm
        pkgs.libx11
        pkgs.libxext
        pkgs.libxcursor
        pkgs.libxi
        pkgs.libxrandr
        pkgs.libxfixes
        pkgs.libxscrnsaver
        pkgs.libxtst
        pkgs.libxkbcommon
        pkgs.wayland
        pkgs.libdecor
        pkgs.fribidi
        pkgs.libthai
        pkgs.alsa-lib
        pkgs.libpulseaudio
        pkgs.pipewire
        pkgs.libjack2
        pkgs.udev
        pkgs.dbus
        pkgs.libusb1
        pkgs.stdenv.cc.cc.lib
        pkgs.icu
        pkgs.openssl
        pkgs.fontconfig
        pkgs.freetype
        pkgs.zlib
      ];

      dotnet_sdk = pkgs.dotnetCorePackages.sdk_10_0;

      # deps.json pins every NuGet package the restore pulls, because the build sandbox has no network.
      # Regenerate it after changing a PackageReference or the SDK version:
      #   nix run .#fetch-deps -- "$PWD/deps.json"
      tixl = pkgs.buildDotnetModule (finalAttrs: {
        pname = "tixl";
        inherit version;

        # The whole repository: the editor's build stages operators, resources and the embedded help.
        src = ../../..;

        dotnet-sdk = dotnet_sdk;
        # The editor compiles operator projects while it runs, so it needs the SDK on PATH, not just a
        # runtime.
        dotnet-runtime = dotnet_sdk;

        nugetDeps = ./deps.json;

        # Restored as a solution, so that deps.json covers every project the build below touches.
        projectFile = "t3.sln";

        # Some NuGet packages the solution references only ship Windows-desktop assets; resolving those on
        # Linux needs the Windows targeting packs.
        dotnetFlags = [ "-p:EnableWindowsTargeting=true" ];

        nativeBuildInputs = [
          pkgs.autoPatchelfHook
          pkgs.file
        ];

        # For autoPatchelf; the same set lands on the wrapper's library path through runtimeDeps.
        buildInputs = runtimeLibraries;
        runtimeDeps = runtimeLibraries;

        # Wrapped by hand in postFixup, to end up with `tixl` rather than the assembly's `TiXL`.
        executables = [ ];

        # What Installer/Linux/build-tarball.sh does, with its restore left out: the build hook has already
        # restored, pinned to linux-x64, while the script's own restore pulls the Windows and macOS runtime
        # packs that Editor.csproj lists as RuntimeIdentifiers - packages nothing here can use.
        #
        # The hook's build phase can't take the place of the explicit solution build either: it passes
        # SelfContained as a global property, which a solution build hands to every project while project
        # references strip it again, so every project would be built twice, in parallel, into the same files.
        buildPhase = ''
          runHook preBuild

          # Exported players run on other machines, so the Player is published self-contained. The editor's
          # build picks it up from this folder.
          dotnet publish Player/Player.csproj \
            --configuration Release \
            --runtime linux-x64 \
            --self-contained \
            -p:PublishDir="$PWD/Player/bin/ReleasePublished/" \
            ${lib.escapeShellArgs finalAttrs.dotnetFlags}

          # Framework-dependent, as for a distro package: the editor needs the SDK on PATH at runtime anyway.
          dotnet build t3.sln \
            --configuration Release \
            --no-restore \
            ${lib.escapeShellArgs finalAttrs.dotnetFlags}

          # Stages the editor's build output together with the operator packages, resources and embedded help
          # its own MSBuild targets copy there - the layout every Linux package of TiXL shares.
          bash Installer/Linux/build-tarball.sh --skip-build --stage-only

          runHook postBuild
        '';

        installPhase = ''
          runHook preInstall

          mkdir -p $out/lib/tixl $out/share
          cp -a Installer/Linux/Output/tixl-linux-x64/. $out/lib/tixl/
          cp -a $out/lib/tixl/share/. $out/share/
          # The tarball's dependency notes and installer: Nix brings the SDK, Vulkan and slangc along itself.
          rm -rf $out/lib/tixl/share $out/lib/tixl/install-dependencies.sh $out/lib/tixl/DEPENDENCIES.md

          # .NET's optional LTTng tracing provider: nothing loads it unless tracing is switched on, and its
          # liblttng-ust.so.0 is older than the one nixpkgs ships.
          find $out/lib/tixl -name libcoreclrtraceptprovider.so -delete

          runHook postInstall
        '';

        # Optional SDL backends nixpkgs doesn't provide or that make no sense here: Steam integration,
        # OpenBSD's sndio, OpenXR, and the obsolete GLES 1 library name.
        autoPatchelfIgnoreMissingDeps = [
          "libsteam_api.so"
          "libsndio.so.7"
          "libopenxr_loader.so.1"
          "libGLES_CM.so.1"
        ];

        # The Player is patched along with the editor, unlike in a tarball build: its runtime comes from the
        # nixpkgs .NET packs, whose host and native libraries already point into the store, so an exported
        # player stays bound to this machine either way. Keeping it loadable here at least lets exports be
        # tested.
        postFixup = ''
          wrapDotnetProgram $out/lib/tixl/TiXL $out/bin/tixl
        '';

        makeWrapperArgs = [
          # Checked before anything on PATH, so this is the compiler TiXL uses; an explicit TIXL_SLANGC still
          # wins.
          "--set-default TIXL_SLANGC ${pkgs.shader-slang}/bin/slangc"
          # nixpkgs' shader-slang follows Slang's releases, not TiXL's pinned line, and asking for a second
          # compiler next to the packaged one would be worse. The shader cache is keyed on the version in
          # use, so blobs from the two stay apart.
          "--set-default TIXL_SLANGC_UNPINNED 1"
          # /run/opengl-driver/lib holds the system's GPU drivers on NixOS. The separator is an argument of
          # its own: makeWrapper reads --prefix as three words, and the hook splits these strings on spaces.
          "--prefix LD_LIBRARY_PATH : /run/opengl-driver/lib"
          "--set-default DOTNET_CLI_TELEMETRY_OPTOUT 1"
        ];

        meta = {
          description = "Real-time motion graphics and VJ tool";
          homepage = "https://tixl.app";
          # MIT, plus the bundled BASS audio library: free for non-commercial use, commercial use needs a
          # license from un4seen.
          license = [
            lib.licenses.mit
            lib.licenses.unfree
          ];
          sourceProvenance = [
            lib.sourceTypes.fromSource
            lib.sourceTypes.binaryNativeCode
          ];
          platforms = [ system ];
          mainProgram = "tixl";
        };
      });
    in
    {
      # `nix run .#fetch-deps -- "$PWD/deps.json"` regenerates deps.json. The script buildDotnetModule
      # exposes is a bare executable in the store, which `nix run` on the package attribute cannot
      # launch, so it is published as an app of its own.
      apps.${system}.fetch-deps = {
        type = "app";
        program = "${tixl.fetch-deps}";
      };

      packages.${system} = {
        inherit tixl;
        default = tixl;
      };
    };
}
