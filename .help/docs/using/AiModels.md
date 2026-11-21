# AI Models

*Settings → AI Models* is where the models of the AI operators live: a check that tells you what your machine can run and what is missing, a one-click setup, a browser to download models, and the advanced folder settings.

## AI Model Setup

The quickest way to get the AI operators running is **Help › AI Model Setup…** (also the **Setup wizard…** button on this page). It works like a classic installer:

1. **Welcome** — explains what will be installed, lets you pick the store folder, shows the free space on its drive and whether CUDA/TensorRT were found.
2. **Choose Components** — a **Setup type** preselects models: *Recommended* (the fastest model of every family), *Minimal* (a small depth model and core tracking, no diffusion), *Everything*, or *Custom* as soon as you tick boxes yourself. Each family can be expanded and ticked as a whole. Hovering a model shows its description, speed and quality ratings, hardware needs and the operators that use it. Installed and bundled models are shown ticked and greyed; exports you produce yourself are listed but not downloadable. **Space required** is compared with the free space on the store's drive.
3. **Installing** — downloads the selection one model after the other with overall progress, speed and remaining time. **Run in background** closes the window and keeps downloading; reopening the setup returns to the progress.
4. **Finished** — lists what was installed. Failed downloads keep their partial files, so **Retry failed** resumes them.

On a fresh installation without any downloaded model the setup opens once on its own after startup.

## Your computer

The **Your computer** card names the detected GPU(s) and the engine the AI operators use on this machine, in one plain sentence: **TensorRT** when its runtime is present (the fastest, several times faster than CUDA), **CUDA** when the CUDA stack is usable, **CoreML** on macOS, otherwise the **CPU** with a hint at what would unlock GPU acceleration (for example installing the latest NVIDIA driver or TensorRT 10 — the **Show me…** button opens the matching download page). The shipped DirectML build cannot run the current model graphs, so it is not offered as an engine.

The operators pick this engine on their own when they start: their **Execution provider** dropdowns default to **Auto**, which resolves to the fastest engine available on the machine. Pin a specific engine in the operator dropdown only when debugging.

## AI Models

The **AI models** card shows one green **All models ready** line when every family has a model, or one row per family that needs attention with a readable reason.

- **Download missing models** installs the fastest working default of every family that is missing one — a small Depth Anything model; the Stream Diffusion export is produced from the downloadable checkpoint with the bundled scripts. Downloads go into the model store, not into the TiXL installation.
- **Setup wizard…** opens the installer-style setup.
- **Run check** re-probes the runtimes and installed models.

## Model Store and Engine Folders

Behind **Advanced: folders and runtimes**: the store folder (by default `%LOCALAPPDATA%\TiXL\Models`, kept out of the TiXL installation and the git repository — **Open folder** shows it in the file browser next to its total size) and the engine runtime folder overrides. The runtimes are detected automatically (`PATH`, `CUDA_PATH`, `TENSORRT_ROOT`, the standard NVIDIA install folders, the shipped DirectML build); a folder set here wins over all of them:

- **CUDA runtime folder**
- **TensorRT runtime folder** — the folder containing `nvinfer_10.dll`
- **DirectML.dll folder**

**Re-detect runtimes** re-probes CUDA and TensorRT after you installed or moved a runtime, without restarting the editor — the *Your computer* card follows the new probe results immediately.

## Model Browser

Behind **All models**: the browser lists the models the AI operators can use, filtered by family tabs: **All** shows every entry grouped by family, and **Depth Anything** and **Stream Diffusion** list one family each. The MediaPipe tracking models ship bundled with TiXL and are not listed here. Every entry names what it is used for and which operator needs it, together with its size and whether it is installed.

- **Download** streams the model into the store with a progress bar. Each file is verified against the size the server reports and only then moved into place; a dropped connection is retried and resumed automatically, and a stalled one is detected after 45 seconds, so **Cancel** always works.
- **Remove** deletes an installed model from the store. Operators that need it afterwards report it as missing.
- **Refresh** re-checks which entries are installed, for example after you added a file by hand.

An entry can be marked as the family's default — the one that **Download missing models** installs.
