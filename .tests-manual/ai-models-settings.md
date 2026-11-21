---
id: ai-models-settings
title: AI Models Settings Page
scope: settings
tags: [ai, essential]
added: 2026-11-11
added-in-version: 4.4
prerequisites:
  - An empty project is open.
  - Network access is required only for the steps marked (network); everything else runs offline.
related-help:
  - ../.help/docs/using/AiModels.md
---

Verifies the *Settings → AI Models* page: the machine's engine recommendation, the one-click
model setup, the per-family model browser and the advanced folder overrides.
Steps that download models are marked (network) in their titles.

## Step: Opening the AI Models page

**Action:**
Open the settings and select the **AI Models** category.

**Expected:**
- The page shows the sections **Your computer**, **AI models**, **All models** and **Advanced: folders and runtimes**, in that order; the last two are collapsed cards that open on click.
- **Your computer** names the detected GPU(s) and the engine the AI operators use (TensorRT, CUDA, DirectML, CoreML or CPU) with a one-line summary.
- **AI models** shows either a green **All models ready** line or one row per family that needs attention, and offers **Download missing models**, **Setup wizard…** and **Run check**.
- No error is logged on first open, even with no models installed.

## Step: The store folder and Open folder

**Action:**
Open **Advanced: folders and runtimes**, read the **Store folder** path field, then press **Open folder**.

**Expected:**
- The field shows the model store path (`%LOCALAPPDATA%\TiXL\Models` by default on Windows).
- The file browser opens exactly at that folder, creating it if it did not exist yet.

## Step: Running the Setup Check

**Action:**
Press **Run check** under **Setup Check**.

**Expected:**
- After a short "Checking…" the result lists the model families (depth, diffusion) with an **OK**, **Missing** or **Optional** status each; MediaPipe is bundled and not listed.
- Missing models come with a readable reason, e.g. that a `[DepthAnything]` operator shows an error until a depth model is downloaded.
- The engine runtimes (CUDA, TensorRT, DirectML) are reported with readable reasons for what is not found.

## Step: Download missing models (network)

**Action:**
With at least one family row on the **AI models** card, press **Download missing models**.

**Expected:**
- Each default model downloads with a visible progress indicator, one after the other.
- When it finishes, the setup check result flips those families to **OK** without restarting the editor.
- The downloaded entries in the model browser are marked as installed by the one-click setup.

## Step: Family tabs in the model browser

**Action:**
Switch the model browser between **All**, **Depth Anything** and **Stream Diffusion**.

**Expected:**
- **All** lists every entry grouped by family.
- **Depth Anything** lists only depth models (Depth Anything 3 in several sizes), **Stream Diffusion** the V2 export entry and its checkpoint download.
- Each entry shows what it is used for, its size and whether it is installed.

## Step: Installing one model (network)

**Action:**
Pick a small model that is not installed (e.g. a quantized Depth Anything 3 Small) and press **Download**.

**Expected:**
- The download streams in with a progress bar and can be cancelled while running.
- When it completes, the entry is marked **Installed** and the store's total size grows by roughly the stated model size.

## Step: Removing one model

**Action:**
Press **Remove** on the model installed in the previous step, confirm, then **Open folder** and check the store.

**Expected:**
- The entry is no longer marked as installed.
- The model's files are gone from the store folder; other installed models are untouched.

## Step: The engine recommendation on "Your computer"

**Action:**
Read the **Your computer** card, then press **Re-detect runtimes** under **Advanced** and read the card again.

**Expected:**
- The card names the engine the operators pick on this machine: **TensorRT** when its runtime is present, **CUDA** when the CUDA stack is usable, otherwise **CPU** with a hint (the shipped DirectML build cannot run the current model graphs, so it is reported as not usable by the AI operators).
- An NVIDIA GPU is listed by name; the hint for a CUDA-only machine points at installing TensorRT 10, with a **Show me…** button that opens the NVIDIA download page.
- **Re-detect runtimes** re-probes without restarting the editor; the recommendation follows the new probe results immediately.

## Step: Runtime path override

**Action:**
Open **Advanced: folders and runtimes**, set the **TensorRT runtime folder** to an empty folder, press **Re-detect runtimes**, then clear the field and re-detect again.

**Expected:**
- With the override set, TensorRT is reported as not found and the message points at the folder that was checked.
- After clearing, the detection returns to what it was before (automatic sources like `TENSORRT_ROOT` and the standard installs).

## Step: AI Model Setup opens once on a fresh installation

**Action:**
Close TiXL, delete the model store folder and remove `"SetupWizardOffered": true` from `aiModels.json` in the settings folder. Start TiXL and wait for the startup dialogs to finish.

**Expected:**
- The **AI Model Setup** wizard opens on its own on the **Welcome** page, with a footer hint that it is shown once.
- After closing it with **Not now** and restarting TiXL, it does not open again.

## Step: Choosing components in the setup wizard

**Action:**
Open **Help › AI Model Setup…**, press **Next >**, then switch the **Setup type** between Recommended, Minimal and Everything. Hover several models and families, then untick one model.

**Expected:**
- Each setup type ticks a different set of models and shows its total download size next to the combo.
- The StreamDiffusion V2 export entry is greyed and marked *own export*; its checkpoint download is listed separately and pre-ticked in the wizard's recommended set (17 GB, untick to skip).
- The description pane shows the hovered model's text, Speed/Quality bars, hardware hint and *Used by* operators; hovering a family shows the family description.
- A family whose models are only partly ticked shows a mixed checkbox; clicking it ticks all of them.
- Unticking a model switches the setup type to **Custom**; **Space required** updates.

## Step: Installing through the wizard (network)

**Action:**
Select only **Depth Anything 3 – Small (int8)** (Custom) and press **Install**. While it downloads, press **Run in background**, then reopen **Help › AI Model Setup…**.

**Expected:**
- The Installing page shows overall progress, the current file with bytes received, speed and remaining time.
- After **Run in background** the download continues (visible in the settings model browser); reopening shows the progress page again, or the **Setup Complete** summary once it is done.
- The finished model is listed as *installed* in the wizard and in the settings model browser.

## Step: Cancelling and resuming in the wizard (network)

**Action:**
Start installing **Depth Anything 3 – Metric Large (fp16)** in the wizard and press **Cancel** after a few seconds. Then install it again and disconnect the network for about 10 seconds mid-download.

**Expected:**
- Cancel stops within a second, marks the model *Cancelled* and leaves no `.part` file in the store.
- With the network interrupted, the log shows a retry and the download continues from where it stopped once the network is back, instead of starting over.
