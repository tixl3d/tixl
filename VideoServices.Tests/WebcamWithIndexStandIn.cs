#nullable enable
// Compile-time stand-in for the real record at Operators/Video/Utils/WebcamScan.cs:470, which has the
// identical shape (public sealed record WebcamWithIndex(string Name, int Index)). It exists only so
// CameraControlsV4L2LiveTests can compile the operator package's camera-control backend sources
// directly into this test assembly (see the <Compile Include> entries in VideoServices.Tests.csproj) —
// WebcamScan.cs itself cannot be compiled here because it drags in DirectShowLib. Remove this file
// together with the csproj <Compile Include> entries if this project ever gains a real reference to
// the operator package.
namespace Lib.io.video;

public sealed record WebcamWithIndex(string Name, int Index);
