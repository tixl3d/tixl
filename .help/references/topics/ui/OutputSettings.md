Controls how the active output is sized and framed, including the resolution the final image is rendered at.

The default "magic" 0×0 resolution means "inherit": it takes the size of an incoming image if one is connected, otherwise the current [ui:OutputWindow|output window]'s size, cropping left and right to keep the aspect ratio rather than squashing the picture. Set a real fixed resolution instead when a texture must stay a specific shape — for example a square shadow sprite — regardless of how you drag the window.
