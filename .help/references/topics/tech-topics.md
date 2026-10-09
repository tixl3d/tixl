# Tech Topic Registry

Hand-authored source for the `tech:` half of the reference index — subject matter that would exist
if TiXL didn't: shader languages and their compilers, graphics APIs, colour science, platform
details. The dividing line against [`ui-topics.md`](ui-topics.md) is ownership, not difficulty:

- **`ui:`** — TiXL's own vocabulary. Anything you point at or author in the editor, including
  concepts like Field, Symbol or the evaluation context.
- **`tech:`** — the outside world TiXL builds on. HLSL's swizzling rules, what `fwidth` computes,
  how Vulkan differs from DirectX. True whether or not TiXL exists.

So *writing* a shader in a custom operator is `ui:ShaderCode`; the *language* it's written in is
`tech:Hlsl`.

**Format** — identical to the UI registry: one `## ` block per topic, with `id:`, `synonyms:`,
optional `parent:`, and `classes:` where a TiXL class is a useful anchor (often none here, since
these topics describe things outside the codebase). Doc bodies live in `references/topics/tech/<Id>.md`.

Note: the editor does not resolve `tech:` links yet — it hard-codes the `ui:` prefix in
`HelpTopic.cs`. Until that lands, a `[tech:Id]` fragment renders as plain text and `tech:` mentions
sit in the index unread. Authoring them now keeps the data correct for when a consumer arrives.

## HLSL
id: Hlsl
synonyms: shader language, HLSL, slang, SPIR-V, shader compiler, swizzling, GLSL, constant buffer, cbuffer, buffer packing, register alignment
classes:

## Screen-space derivatives
id: ShaderDerivatives
parent: Hlsl
synonyms: ddx, ddy, fwidth, derivatives, pixel quad
classes:

## Vulkan
id: Vulkan
synonyms: Vulkan, DirectX 11, graphics API, SharpDX, render backend
classes:

## Linux
id: Linux
synonyms: Linux, Arch, distro, Wayland, X11, PipeWire, SDL, packaging, AppImage, Flatpak, ARM, Raspberry Pi
classes:
