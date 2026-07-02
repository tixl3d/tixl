Builds your composition into a standalone executable that runs in the [ui:Player] outside the editor.

Pick the operator to export and the exporter packages it with everything it needs to run on its own. It scans ahead and pre-compiles upcoming shaders — including concatenated custom HLSL, which stays cached — so the exported build plays without the compile stutter that older preloading missed. Use it when you want to hand off a finished piece as a self-contained program.
