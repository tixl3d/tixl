---
id: compile-without-sdk
title: Compiling Projects Without the .NET SDK
added: 2026-11-23
added-in-version: 4.4
scope: project-compilation
tags: [essential, install]
prerequisites:
  - A machine (or VM) where the .NET SDK is not installed, or where `dotnet` is not on `PATH`.
  - A TiXL release build. A build from source needs the SDK anyway, but its output folder works the same.
  - A scratch project directory under `Settings → Project Directories` that is safe to delete after the test.
related-help:
  - ../.help/docs/install/InstallLinux.md
  - ../.help/docs/install/InstallMacOS.md
---

TiXL compiles the C# code of operator projects with its built-in compiler. The .NET SDK is only needed for
projects that reference NuGet packages or add custom build steps. Run this set on a machine without the SDK; it
is the setup most new users have.

## Step: Startup doesn't ask for the SDK

**Action:**
Start TiXL.

**Expected:**
- No `Missing Dependencies` dialog mentions the .NET SDK.
- The editor reaches the project hub.

## Step: Creating a project

**Action:**
Open **New Project**, enter a unique name, click `Create`.

**Expected:**
- The project is created and opens without an error dialog.
- The Console shows `<name>: Build succeeded in ...ms (built-in compiler)`.

## Step: Editing an operator

**Action:**
In the new project, create a new operator (combine two ops into a new symbol), open its C# source, change a
value in its `Update` method and save the file.

**Expected:**
- The Console shows another `Build succeeded ... (built-in compiler)` line within a second or two.
- The operator's output reflects the change without restarting TiXL.

## Step: A compile error

**Action:**
Add a line that doesn't compile (for example `int x = "text";`) to the operator's `Update` method and save.

**Expected:**
- The Console lists the error with file, line and an error code like `CS0029`.
- The editor keeps running; fixing the line and saving again compiles successfully.

## Step: A project that needs the SDK

**Action:**
Close TiXL. Add `<ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup>` to the
project's `.csproj`, delete the project's `.temp/bin` folder, and start TiXL again.

**Expected:**
- The project fails to load, and the log says it needs the .NET SDK because it has `NuGet package Newtonsoft.Json`.
- Other projects load normally.

**Cleanup:**
Remove the `PackageReference` again; the project loads on the next start.

## Step: Turning the built-in compiler off

**Action:**
In `Settings → Diagnostics → Compilation`, turn off `Compile Operators Inside TiXL`, then edit and save an
operator's source again.

**Expected:**
- Without the SDK, the Console reports that the `dotnet` command was not found.
- With the setting back on, saving compiles with the built-in compiler again.
