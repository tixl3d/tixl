// Mirrors the Editor project's global usings so the compile-included source files see the
// same implicit namespace set they get inside the editor build (System.Numerics matters:
// CollabProtocol's PresencePacket uses Vector2).
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Numerics;
global using T3.Core.Logging;
