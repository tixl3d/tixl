# Signing the Windows Installer

**Status:** Draft — 2026-10-09. Nothing implemented yet.
**Scope:** Authenticode-sign the Windows installer and TiXL's own executables, in CI for release tags, so
Microsoft Defender SmartScreen stops showing "Windows protected your PC" for TiXL downloads.
**Related:** the macOS release is already signed and notarized (`Installer/macOS/build-dmg.sh`,
`.github/workflows/macos-build.yml`, secrets in the `macos-signing` GitHub environment). This plan follows
the same shape.

## How SmartScreen decides

SmartScreen warns about a downloaded program unless it has enough *reputation*. Reputation is earned through
downloads and installs that turn out harmless, and it is attached to one of two things:

- **Unsigned:** the exact file (its hash). Every release is a new file and starts from zero, so an unsigned
  TiXL warns on every version, however popular the previous one was.
- **Signed:** the signing certificate (the publisher). Reputation carries over to every later file signed with
  it, so after a ramp-up phase new releases open without a warning.

So reputation alone can't remove the warning: it needs a signature to accumulate on. Since 2024 an EV
certificate no longer grants reputation immediately either - every option goes through the ramp-up, which
typically takes a few weeks to a few months of downloads. Signing also replaces "Unknown publisher" with
"Framefield GmbH" in the warning and in the UAC prompt, which helps even during the ramp-up.

## Options

| | Azure Artifact Signing (formerly Trusted Signing) | OV certificate from a CA (Sectigo, DigiCert, SSL.com, ...) |
|---|---|---|
| Cost | ~10 USD/month (Basic, 5,000 signatures) — verify current price | ~200–500 USD/year, plus hardware token or cloud HSM fees |
| Key storage | Microsoft's cloud HSM, nothing to protect locally | Must be a hardware token or cloud HSM (CA/B Forum rule since 2023) |
| CI | Official `azure/trusted-signing-action`, or `signtool` with the Azure dlib | Token can't sit in a GitHub runner; needs the CA's cloud signing service |
| Certificate lifetime | Short-lived certs renewed automatically; reputation is tied to the validated identity | 1–3 years; reputation can need rebuilding after renewal |
| Eligibility | Organizations with a verifiable identity and 3+ years of history — verify Framefield GmbH qualifies | Any company that passes the CA's validation |

**Recommendation:** Azure Artifact Signing. It is the cheapest, has no key to protect, and is the only option
that signs from a GitHub runner without extra infrastructure.

## What gets signed

Signed in this order, because Inno Setup packs the executables and then writes its own files:

1. `TiXL.exe` (the Editor's apphost) and `Player\Player.exe`, before packaging. The installer copies them, so a
   signed installer with unsigned executables would still show "Unknown publisher" in UAC and SmartScreen
   checks after installation.
2. The installer `Tixl-v<version>.exe` and the uninstaller Inno Setup generates — both through Inno Setup's
   `SignTool=` directive with `SignedUninstaller=yes`, so they are signed while being built.

Not signed: our managed DLLs (SmartScreen only looks at what is launched), third-party DLLs (BASS, SkiaSharp
etc. are already signed by their vendors or not checked), and the bundled `VC_redist.x64.exe` and .NET SDK
installer, which Microsoft signs.

## Steps

### 1. Account (done by the account owner, ~1 hour plus validation time)

1. Create an Azure subscription for Framefield GmbH (pay-as-you-go).
2. Create an **Artifact Signing account** (resource type "Trusted Signing Account"), region West Europe, tier
   Basic.
3. Create an **identity validation** for Framefield GmbH (organization, public trust). Microsoft checks the
   company's registration; this can take from a day to a few weeks. It may ask for documents.
4. Create a **certificate profile**, type *Public Trust*, using that identity validation.
5. Create an **app registration** (service principal) for GitHub Actions and assign it the role
   *Trusted Signing Certificate Profile Signer* on the signing account. Note tenant ID, client ID and a client
   secret (or set up OIDC federation for the repository instead of a secret).

### 2. GitHub

1. Environment `windows-signing`, deployment rule: tags `v*` only (as `macos-signing`).
2. Environment secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` (omit with OIDC), plus the
   non-secret values `ARTIFACT_SIGNING_ENDPOINT` (e.g. `https://weu.codesigning.azure.net/`),
   `ARTIFACT_SIGNING_ACCOUNT` and `ARTIFACT_SIGNING_PROFILE`.

### 3. Release workflow

There is no tag-triggered Windows workflow yet: `nightly-build.yml` builds on a schedule, and the `package`
action reads `github.event.release.tag_name`, which is empty outside a release event (the nightly installer
gets an empty version). Add `.github/workflows/windows-build.yml` mirroring `macos-build.yml`:

1. Trigger on `v*` tags and manual runs; `environment: windows-signing` only for tags.
2. Build with `Installer/Windows/build-release.ps1` (publishes the Player, builds the solution in Release).
3. **Sign the executables** with `azure/trusted-signing-action`, file list `Editor\bin\Release\...\TiXL.exe`
   and the published `Player.exe`. Timestamp server `http://timestamp.acs.microsoft.com`, digest SHA256.
4. **Package** with `iscc`, passing the version from the tag and, for signed runs, `/DSign=1`.
5. **Sign installer and uninstaller** through Inno Setup (step 4), or as a separate action step for the installer
   if wiring `SignTool` to the Azure dlib proves awkward — then set `SignedUninstaller=no` and accept an
   unsigned uninstaller, which SmartScreen doesn't check.
6. Upload the installer as an artifact and attach it to the release, like the macOS DMG.

Untagged and nightly builds stay unsigned. Signing nightlies would build reputation faster, but every nightly
is a new file nobody needs to trust permanently, and it spends signatures; revisit if the ramp-up is slow.

### 4. installer.iss

```
#ifdef Sign
SignTool=azure $f
SignedUninstaller=yes
#endif
```

`azure` is a sign tool defined on the `iscc` command line (`/Sazure=...`), pointing at `signtool.exe sign /v
/fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib <Azure.CodeSigning.Dlib.dll> /dmdf
<metadata.json>`. The metadata file holds endpoint, account and profile; the dlib authenticates through the
`AZURE_*` environment variables. Local, unsigned builds keep working because `Sign` is undefined.

### 5. Verify

- `signtool verify /pa /v Tixl-v<version>.exe` on the runner after packaging: chain to a Microsoft root,
  timestamp present.
- Download the release on a clean Windows machine (or VM) through a browser, so the file carries the
  Mark-of-the-Web, and run it. Expect the warning to name "Framefield GmbH" at first, and to disappear once
  reputation has built up.
- Optionally submit the first signed installer to Microsoft's malware analysis portal
  (https://www.microsoft.com/wdsi/filesubmission) as a software developer, which can speed up reputation.

## Exported players

Players exported from TiXL are built on the user's machine and stay unsigned; their recipients still see
SmartScreen. Signing them would need a certificate on the exporting machine, which only makes sense for users
with their own. Out of scope; worth a sentence in the export documentation.

## Open questions

1. Does Framefield GmbH meet Azure's eligibility (organization age, region)? If not, fall back to an OV
   certificate with the CA's cloud signing service and adapt step 3.
2. Client secret or OIDC federation for the GitHub app registration? OIDC avoids a long-lived secret.
3. Should the release workflow replace the manual installer build described in `Installer/Windows/README.md`,
   or should maintainers also be able to sign locally (requires Azure CLI login on their machine)?
