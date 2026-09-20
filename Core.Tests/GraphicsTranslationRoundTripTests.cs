using T3.Graphics;
using T3.Graphics.Compat;
using T3.Graphics.D3D11;
using Xunit;
using D3D11 = SharpDX.Direct3D11;
using Convert = T3.Graphics.D3D11.Convert;

namespace Core.Tests;

/// <summary>
/// The compatibility layer translates D3D11 into the backend's vocabulary and the D3D11 backend translates it
/// back. As long as both exist, that round trip has to be the identity — otherwise a project's saved sampler
/// or blend mode would quietly render differently after the port. Every value here is one a project can store.
/// </summary>
public class GraphicsTranslationRoundTripTests
{
    [Theory]
    [InlineData(TextureAddressMode.Wrap)]
    [InlineData(TextureAddressMode.Mirror)]
    [InlineData(TextureAddressMode.Clamp)]
    [InlineData(TextureAddressMode.Border)]
    [InlineData(TextureAddressMode.MirrorOnce)]
    public void AddressModesSurviveTheRoundTrip(TextureAddressMode mode)
    {
        Assert.Equal((D3D11.TextureAddressMode)mode, Convert.ToD3D(Translate.ToAddressMode(mode)));
    }

    [Fact]
    public void ComparisonsSurviveTheRoundTrip()
    {
        foreach (Comparison comparison in Enum.GetValues<Comparison>())
        {
            Assert.Equal((D3D11.Comparison)comparison, Convert.ToD3D(Translate.ToCompare(comparison)));
        }
    }

    [Fact]
    public void EveryFilterSurvivesTheRoundTrip()
    {
        foreach (var filter in Enum.GetValues<Filter>())
        {
            var description = new SamplerStateDescription { Filter = filter, MaximumAnisotropy = 16 };
            var roundTripped = Convert.ToD3D(Translate.ToSampler(description)).Filter;

            Assert.Equal((D3D11.Filter)filter, roundTripped);
        }
    }

    [Fact]
    public void EveryBlendOptionSurvivesTheRoundTrip()
    {
        foreach (var option in Enum.GetValues<BlendOption>())
        {
            var blend = Translate.ToBlend(new RenderTargetBlendDescription { SourceBlend = option });
            Assert.Equal((D3D11.BlendOption)option, Convert.ToD3D(blend.SourceColor));
        }
    }

    [Fact]
    public void EveryBlendOperationSurvivesTheRoundTrip()
    {
        foreach (var operation in Enum.GetValues<BlendOperation>())
        {
            var blend = Translate.ToBlend(new RenderTargetBlendDescription { BlendOperation = operation });
            Assert.Equal((D3D11.BlendOperation)operation, Convert.ToD3D(blend.ColorOp));
        }
    }

    [Fact]
    public void EveryStencilOperationSurvivesTheRoundTrip()
    {
        foreach (var operation in Enum.GetValues<StencilOperation>())
        {
            var state = Translate.ToDepthStencil(new DepthStencilStateDescription
                                                     { FrontFace = new DepthStencilOperationDescription { PassOperation = operation } });

            Assert.Equal((D3D11.StencilOperation)operation, Convert.ToD3D(state.Front.Pass));
        }
    }

    [Fact]
    public void TheWriteMaskSurvivesTheRoundTrip()
    {
        for (var mask = 0; mask <= (int)ColorWriteMaskFlags.All; mask++)
        {
            var blend = Translate.ToBlend(new RenderTargetBlendDescription { RenderTargetWriteMask = (ColorWriteMaskFlags)mask });
            Assert.Equal((D3D11.ColorWriteMaskFlags)mask, Convert.ToD3D(blend.WriteMask));
        }
    }

    [Fact]
    public void RasterizerStateSurvivesTheRoundTrip()
    {
        foreach (var cull in Enum.GetValues<CullMode>())
        {
            foreach (var fill in Enum.GetValues<FillMode>())
            {
                var description = new RasterizerStateDescription
                                      {
                                          CullMode = cull,
                                          FillMode = fill,
                                          IsFrontCounterClockwise = true,
                                          DepthBias = 7,
                                          DepthBiasClamp = 0.5f,
                                          SlopeScaledDepthBias = 1.5f,
                                          IsDepthClipEnabled = true,
                                          IsScissorEnabled = true,
                                      };

                var roundTripped = Convert.ToD3D(Translate.ToRasterizer(description));

                Assert.Equal((D3D11.CullMode)cull, roundTripped.CullMode);
                Assert.Equal((D3D11.FillMode)fill, roundTripped.FillMode);
                Assert.True(roundTripped.IsFrontCounterClockwise);
                Assert.Equal(7, roundTripped.DepthBias);
                Assert.Equal(0.5f, roundTripped.DepthBiasClamp);
                Assert.Equal(1.5f, roundTripped.SlopeScaledDepthBias);
                Assert.True(roundTripped.IsDepthClipEnabled);
                Assert.True(roundTripped.IsScissorEnabled);
            }
        }
    }

    [Fact]
    public void DepthStateSurvivesTheRoundTrip()
    {
        foreach (var writeMask in Enum.GetValues<DepthWriteMask>())
        {
            var description = new DepthStencilStateDescription
                                  {
                                      IsDepthEnabled = true,
                                      DepthWriteMask = writeMask,
                                      DepthComparison = Comparison.GreaterEqual,
                                      IsStencilEnabled = true,
                                      StencilReadMask = 0x0f,
                                      StencilWriteMask = 0xf0,
                                  };

            var roundTripped = Convert.ToD3D(Translate.ToDepthStencil(description));

            Assert.True(roundTripped.IsDepthEnabled);
            Assert.Equal((D3D11.DepthWriteMask)writeMask, roundTripped.DepthWriteMask);
            Assert.Equal(D3D11.Comparison.GreaterEqual, roundTripped.DepthComparison);
            Assert.True(roundTripped.IsStencilEnabled);
            Assert.Equal(0x0f, roundTripped.StencilReadMask);
            Assert.Equal(0xf0, roundTripped.StencilWriteMask);
        }
    }

    [Fact]
    public void TopologiesSurviveTheRoundTrip()
    {
        foreach (var topology in new[]
                                     {
                                         PrimitiveTopology.PointList, PrimitiveTopology.LineList, PrimitiveTopology.LineStrip,
                                         PrimitiveTopology.TriangleList, PrimitiveTopology.TriangleStrip,
                                         PrimitiveTopology.PatchListWith1ControlPoints, PrimitiveTopology.PatchListWith4ControlPoints,
                                         PrimitiveTopology.PatchListWith32ControlPoints,
                                     })
        {
            var backendTopology = Translate.ToTopology(topology);
            var controlPoints = Translate.ToPatchControlPoints(topology);

            Assert.Equal((SharpDX.Direct3D.PrimitiveTopology)topology, Convert.ToD3D(backendTopology, controlPoints));
        }
    }

    [Fact]
    public void SamplerAddressesAndLodsSurviveTheRoundTrip()
    {
        var description = new SamplerStateDescription
                              {
                                  Filter = Filter.MinMagMipLinear,
                                  AddressU = TextureAddressMode.Border,
                                  AddressV = TextureAddressMode.MirrorOnce,
                                  AddressW = TextureAddressMode.Clamp,
                                  MipLodBias = -0.25f,
                                  MaximumAnisotropy = 1,
                                  ComparisonFunction = Comparison.Never,
                                  MinimumLod = 1f,
                                  MaximumLod = 9f,
                              };

        var roundTripped = Convert.ToD3D(Translate.ToSampler(description));

        Assert.Equal(D3D11.TextureAddressMode.Border, roundTripped.AddressU);
        Assert.Equal(D3D11.TextureAddressMode.MirrorOnce, roundTripped.AddressV);
        Assert.Equal(D3D11.TextureAddressMode.Clamp, roundTripped.AddressW);
        Assert.Equal(-0.25f, roundTripped.MipLodBias);
        Assert.Equal(1f, roundTripped.MinimumLod);
        Assert.Equal(9f, roundTripped.MaximumLod);
    }

    [Fact]
    public void BindFlagsSurviveTheRoundTripAsTextureUsage()
    {
        foreach (var flags in new[]
                                  {
                                      BindFlags.ShaderResource,
                                      BindFlags.RenderTarget,
                                      BindFlags.DepthStencil,
                                      BindFlags.UnorderedAccess,
                                      BindFlags.ShaderResource | BindFlags.RenderTarget,
                                      BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                                  })
        {
            var usage = Translate.ToTextureUsage(flags, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None);
            Assert.Equal((D3D11.BindFlags)flags, Convert.ToD3D(usage));
        }
    }

    [Fact]
    public void MemoryKindsSurviveTheRoundTrip()
    {
        foreach (var (usage, access) in new[]
                                            {
                                                (ResourceUsage.Default, CpuAccessFlags.None),
                                                (ResourceUsage.Dynamic, CpuAccessFlags.Write),
                                                (ResourceUsage.Staging, CpuAccessFlags.Read),
                                            })
        {
            var memory = Translate.ToMemoryKind(usage, access);

            Assert.Equal((D3D11.ResourceUsage)usage, Convert.ToD3D(memory));
            Assert.Equal((D3D11.CpuAccessFlags)access, Convert.ToCpuAccess(memory));
        }
    }
}
