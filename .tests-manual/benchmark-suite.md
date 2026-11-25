---
id: benchmark-suite
title: Benchmark Window
added: 2026-11-24
added-in-version: 4.4
scope: editor-shell
tags: [rendering, performance]
prerequisites:
  - Any project is open.
  - For the last step, a result file from another machine, OS or build (any file from the Benchmarks folder works).
related-help:
  - ../.help/docs/using/OptimizingRenderingPerformance.md
---

The Benchmark window sweeps three tests (CPU, Compute, Draw) from small to large load,
back down and up again, and graphs the duration with the authored Reference marked. These steps check the run, the graphs and the
comparison with other runs.

## Step: Opening the window

**Action:**
Open the Performance window (click the frame graph in the app bar) and click **Benchmark...**.

**Expected:**
- The Benchmark window opens. Without saved results it shows a short explanation and **Run benchmark**.
- It is also listed under **Help → Development Tools**, and not in the Windows menu.

## Step: Running the suite

**Action:**
Note whether VSync is on in the Performance window. Click **Run benchmark** and don't touch the editor.

**Expected:**
- A **Cancel** button and a progress line ("CPU: 500 loop iterations …, pass 1 of 3") replace the toolbar.
- The graphs fill in step by step while the run goes on.
- After about a minute and a half the toolbar returns and shows "Saved <date>-<os>-<backend>.json".
- VSync is back to the state it had before the run.

## Step: Reading the graphs

**Action:**
Look at the three graphs and hover one of them.

**Expected:**
- Each graph has a large title (CPU, Compute, Draw) with a grade and the duration at the Reference, e.g. "B (3.1ms for 20)", a line saying what the test does, duration lines at 0.1, 1, 8, 16 and 50 ms, and amount labels (1, 10, 100, 1K …).
- The grade is green for A+ and A, blue for B and C, red for D and F. In a Debug build the CPU grade is about the same as in Release, and hovering the CPU title shows its stretched range.
- The curves rise with the amount, inside a blue band showing the spread of the three passes. CPU and Draw stop after the first step above 50 ms; Compute may reach 10M.
- Hovering the title explains the Reference and the grade range. Hovering the plot shows the nearest amount with the fastest and slowest pass (Min and Max) and their CPU and GPU times.
- The small print below the graphs names the GPU, driver, OS, TiXL version and power source.

## Step: History

**Action:**
Run the benchmark twice without changing anything.

**Expected:**
- **History** lists both runs, newest first, with date and commit.
- The newer row shows a change in brackets for each test, mostly within a few percent and not coloured.
- A change above 10 % is green (faster) or red (slower).

## Step: Debug build notice

**Action:**
Run the benchmark in a Debug build (or start TiXL with `TIXL_VULKAN_VALIDATION=1`).

**Expected:**
- A red line says that CPU times are only comparable with runs of the same configuration.
- The result file name ends in `-debug` for Debug builds.

## Step: Cancelling

**Action:**
Start a run and click **Cancel** after a few seconds.

**Expected:**
- The run stops at once, the toolbar shows "Cancelled", and no new result file appears.
- VSync is back to its previous state.

## Step: Comparing runs

**Action:**
Click **Open folder**, copy another result file into the folder, close and reopen the Benchmark window, expand
**Compare** and tick the other run. Then click **Copy summary** and paste it into a text editor.

**Expected:**
- The other run appears in the Compare list with its GPU; runs of another build configuration or with changed tests say so in brackets.
- Its curves appear in the graphs in the colour of its checkbox.
- The summary is a Markdown table with one row per test and one column per run, followed by one line per run with GPU, driver, OS and version.
