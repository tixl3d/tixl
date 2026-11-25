# Extensions

Some operators don't come with the default install of TiXL: Ableton Link, NDI and the MediaPipe tracking operators.
They are extensions you install when you need them, from **Settings → Extensions**. This page explains why, how to
install and remove them, and what happens to projects that use them.

## Why some operators are extensions

TiXL itself is free software under the MIT licence, and everything in the default install can be shipped under
licences compatible with it. A few integrations can't:

- **Ableton Link** is licensed under the GPL.
- **NDI** depends on NDI's own proprietary runtime and SDK licence.
- **MediaPipe** is large (about 100 MB, mostly trained models) and only works on Windows so far.

Keeping them out of the default install means TiXL can be packaged for Linux distributions and app stores, and
everybody downloads only what they use.

## Installing an extension

1. Open **Settings** and select **Extensions**.
2. Tick the extensions you want. Each row shows its licence and download size.
3. Click **Install & Restart**.

TiXL downloads the extensions, checks them, and restarts. After the restart their operators show up in the symbol
library like any other operator.

An extension that isn't available on your platform yet is shown greyed out, for example *Not available on Linux
yet*.

Extensions are built for one TiXL version. Each TiXL version keeps its own extensions, and after an update you install
them again from the same page.

## Removing an extension

Untick it and click **Install & Restart**. Projects that use its operators keep them (see below).

## Projects that use an extension

When you open a project that uses operators of an extension you haven't installed, TiXL doesn't delete them. They
show up as missing operators in the graph, and their settings and connections stay in the project files when you
save. As soon as you install the extension, they work again.

The examples that ship with TiXL use some of these operators too, for example the VJ examples use
[AbletonLinkSync]. Without the extension those parts of the examples stay empty.

## Installing from a file

Without internet access, or to try an extension that was built locally, use **Install from File...** on the same
page and pick a `.nupkg` file. The extension is installed at the next restart.

## Where extensions are stored

Installed extensions live in the `Extensions` folder of your TiXL settings folder (the folder icon below the
categories in Settings opens it). Each one has its own subfolder. You don't need to touch them; install and remove extensions from the
settings page, so TiXL can apply the changes when it starts.

## See also

- [Backups](Backups.md)
- [Exporting executables](ExportExecutables.md)
