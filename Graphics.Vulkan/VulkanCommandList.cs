using System.Numerics;
using T3.Graphics;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace T3.Graphics.Vulkan;

/// <summary>
/// Records a frame. Everything a draw needs is collected first and issued at the draw itself, because the
/// caller sets targets before it binds textures, and Vulkan cannot transition an image inside a render pass.
/// </summary>
internal sealed unsafe class VulkanCommandList(VulkanBackend backend) : ICommandList
{
    internal void Begin(VkCommandBuffer commandBuffer, int frameIndex)
    {
        _commandBuffer = commandBuffer;
        _frameIndex = frameIndex;
        _renderingRequested = false;
        _renderingActive = false;
        _pipeline = null;
        _colorCount = 0;
        _depthTarget = null;

        for (var stage = 0; stage < MaxStages; stage++)
        {
            _bindingCounts[stage] = 0;
        }
    }

    internal void End() => EndRenderingIfActive();

    public void BeginRendering(ReadOnlySpan<GpuTextureView> colorTargets, GpuTextureView? depthTarget)
    {
        EndRenderingIfActive();

        _colorCount = 0;

        foreach (var target in colorTargets)
        {
            if (target is VulkanTextureView view && _colorCount < _colorTargets.Length)
                _colorTargets[_colorCount++] = view;
        }

        _depthTarget = depthTarget as VulkanTextureView;

        // Only remembered here. The pass opens at the first draw, once the bindings have been transitioned.
        _renderingRequested = _colorCount > 0 || _depthTarget != null;
    }

    public void EndRendering()
    {
        EndRenderingIfActive();
        _renderingRequested = false;
    }

    public void Clear(GpuTextureView target, Vector4 color)
    {
        if (target is not VulkanTextureView view || view.Texture.Image.IsNull)
            return;

        EndRenderingIfActive();
        VulkanBarriers.TransitionImage(backend, _commandBuffer, view.Texture, VkImageLayout.TransferDstOptimal,
                                       VkPipelineStageFlags2.Clear, VkAccessFlags2.TransferWrite);

        VkClearColorValue value = new(color.X, color.Y, color.Z, color.W);
        VkImageSubresourceRange range = new()
                                            {
                                                aspectMask = VkImageAspectFlags.Color,
                                                levelCount = VK_REMAINING_MIP_LEVELS,
                                                layerCount = VK_REMAINING_ARRAY_LAYERS,
                                            };

        backend.Api.vkCmdClearColorImage(_commandBuffer, view.Texture.Image, VkImageLayout.TransferDstOptimal, &value, 1, &range);
    }

    public void ClearDepthStencil(GpuTextureView target, float depth, byte stencil, bool clearDepth, bool clearStencil)
    {
        if (target is not VulkanTextureView view || view.Texture.Image.IsNull || (!clearDepth && !clearStencil))
            return;

        EndRenderingIfActive();
        VulkanBarriers.TransitionImage(backend, _commandBuffer, view.Texture, VkImageLayout.TransferDstOptimal,
                                       VkPipelineStageFlags2.Clear, VkAccessFlags2.TransferWrite);

        VkClearDepthStencilValue value = new(depth, stencil);
        var aspect = VkImageAspectFlags.None;

        if (clearDepth)
            aspect |= VkImageAspectFlags.Depth;

        if (clearStencil && VulkanConvert.HasStencil(view.Texture.Description.Format))
            aspect |= VkImageAspectFlags.Stencil;

        VkImageSubresourceRange range = new()
                                            {
                                                aspectMask = aspect,
                                                levelCount = VK_REMAINING_MIP_LEVELS,
                                                layerCount = VK_REMAINING_ARRAY_LAYERS,
                                            };

        backend.Api.vkCmdClearDepthStencilImage(_commandBuffer, view.Texture.Image, VkImageLayout.TransferDstOptimal, &value, 1, &range);
    }

    public void SetViewport(in Viewport viewport)
    {
        // D3D's clip space has +Y up. A negative viewport height reproduces it without touching a shader.
        _viewport = new VkViewport
                        {
                            x = viewport.X,
                            y = viewport.Y + viewport.Height,
                            width = viewport.Width,
                            height = -viewport.Height,
                            minDepth = viewport.MinDepth,
                            maxDepth = viewport.MaxDepth == 0 ? 1 : viewport.MaxDepth,
                        };

        _hasViewport = true;
    }

    public void SetScissor(in ScissorRect rect)
    {
        _scissor = new VkRect2D(rect.X, rect.Y, (uint)Math.Max(0, rect.Width), (uint)Math.Max(0, rect.Height));
        _hasScissor = true;
    }

    public void SetBlendConstants(Vector4 factor, uint sampleMask) => _blendConstants = factor;

    public void SetStencilReference(int reference) => _stencilReference = (uint)reference;

    public void SetPipeline(GpuPipeline pipeline) => _pipeline = pipeline as VulkanPipeline;

    /// <summary>
    /// Stores the stage's bindings under the numbers its shader was compiled with: one set, each stage
    /// offset inside it. Slang derives a descriptor set from the HLSL register space, which TiXL's shaders
    /// do not declare, so a set per stage is not something the compiler could produce.
    /// </summary>
    public void SetBindings(ShaderStage stage, ReadOnlySpan<Binding> bindings)
    {
        var index = (int)stage;

        if (index < 0 || index >= MaxStages)
            return;

        var target = _bindings[index];
        var count = Math.Min(bindings.Length, target.Length);
        var stageBase = ShaderSlots.BaseOf(stage);

        for (var i = 0; i < count; i++)
        {
            target[i] = bindings[i] with { Slot = stageBase + bindings[i].Slot };
        }

        _bindingCounts[index] = count;
    }

    public void SetInlineConstants(ShaderStage stage, int slot, ReadOnlySpan<byte> data)
    {
        var set = (int)stage;
        // The pipeline layouts have no push constant range, so this goes through a scratch uniform buffer —
        // the same place D3D11's map-discard constants end up.
        var buffer = ScratchBuffer(set, slot, data.Length);

        if (buffer == null)
            return;

        MapForDiscardCore(buffer, out var pointer);
        data.CopyTo(new Span<byte>(pointer, data.Length));

        var bindings = _bindings[set];
        var count = _bindingCounts[set];

        if (count < bindings.Length)
        {
            bindings[count] = new Binding { Kind = BindingKind.ConstantBuffer, Slot = ShaderSlots.BaseOf(stage) + slot, Buffer = buffer };
            _bindingCounts[set] = count + 1;
        }
    }

    public MappedMemory MapForDiscard(GpuResource resource, int subresource)
    {
        switch (resource)
        {
            case VulkanBuffer buffer:
            {
                MapForDiscardCore(buffer, out var pointer);
                return new MappedMemory(pointer, buffer.Description.SizeInBytes, buffer.Description.SizeInBytes, buffer.Description.SizeInBytes);
            }

            case VulkanTexture { Mapped: not null } texture:
            {
                var rowPitch = texture.Description.Width * FormatSizes.BytesPerPixel(texture.Description.Format);
                var size = (int)VulkanBackend.SizeOf(texture.Description);
                return new MappedMemory(texture.Mapped, rowPitch, size, size);
            }

            default:
                GraphicsLog.WarnOnce("Only a resource created with upload memory can be mapped for writing.");
                return default;
        }
    }

    /// <summary>
    /// Moves an upload buffer to the copy this frame may write. That is what makes D3D11's Map(WriteDiscard)
    /// free of stalls: the frames still on the GPU keep reading the copy they were given.
    /// </summary>
    private void MapForDiscardCore(VulkanBuffer buffer, out void* pointer)
    {
        if (buffer.SliceCount > 1 && buffer.SliceWrittenInFrame != _frameIndex)
        {
            buffer.CurrentSlice = _frameIndex % buffer.SliceCount;
            buffer.SliceWrittenInFrame = _frameIndex;
        }

        pointer = (byte*)buffer.Mapped + buffer.CurrentOffset;
    }

    public void Unmap(GpuResource resource, int subresource)
    {
    }

    public void UpdateResource(GpuResource resource, int subresource, ReadOnlySpan<byte> data, int rowPitch, int slicePitch)
    {
        switch (resource)
        {
            case VulkanBuffer { Mapped: not null } buffer:
            {
                MapForDiscardCore(buffer, out var pointer);
                data.CopyTo(new Span<byte>(pointer, data.Length));
                break;
            }

            case VulkanBuffer buffer:
            {
                EndRenderingIfActive();
                VulkanBarriers.BarrierBuffer(backend, _commandBuffer, buffer, VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

                // vkCmdUpdateBuffer is limited to 64 KB, which covers every constant buffer TiXL writes this way.
                if (data.Length <= 65536)
                {
                    fixed (byte* pointer = data)
                    {
                        backend.Api.vkCmdUpdateBuffer(_commandBuffer, buffer.Buffer, 0, (ulong)data.Length, pointer);
                    }
                }
                else
                {
                    GraphicsLog.WarnOnce("An update larger than 64 KB needs a staging copy and was dropped.");
                }

                break;
            }

            case VulkanTexture { Mapped: not null } texture:
                data.CopyTo(new Span<byte>(texture.Mapped, data.Length));
                break;

            // A device-local texture has no memory the CPU can write, so the bytes go through a staging
            // buffer. The camera and video operators upload their frames this way every frame.
            case VulkanTexture texture:
            {
                EndRenderingIfActive();
                UploadTexture(texture, subresource, data, rowPitch);
                break;
            }
        }
    }

    private void UploadTexture(VulkanTexture texture, int subresource, ReadOnlySpan<byte> data, int rowPitch)
    {
        if (texture.Image.IsNull || data.IsEmpty)
            return;

        var mipLevels = Math.Max(1, texture.Description.MipLevels);
        var mip = subresource % mipLevels;
        var slice = subresource / mipLevels;
        var width = Math.Max(1, texture.Description.Width >> mip);
        var height = Math.Max(1, texture.Description.Height >> mip);
        var depth = texture.Description.Dimension == TextureDimension.Texture3D ? Math.Max(1, texture.Description.Depth >> mip) : 1;
        var bytesPerPixel = Math.Max(1, FormatSizes.BytesPerPixel(texture.Description.Format));

        if (rowPitch <= 0)
            rowPitch = width * bytesPerPixel;

        // Vulkan takes the row length in texels and requires it to cover the extent.
        if (rowPitch < width * bytesPerPixel)
        {
            GraphicsLog.WarnOnce("A texture update declared a row pitch narrower than the image; it was dropped.");
            return;
        }

        // The copy below reads rowPitch bytes for every row of the extent, so the caller has to have supplied
        // all of them. Copying only what arrived and asking for the full extent reads past the staging buffer,
        // which faults the device instead of failing.
        var rows = height * depth;
        var required = rowPitch * rows;

        if (data.Length < required)
        {
            GraphicsLog.WarnOnce("A texture update supplied fewer rows than the subresource holds; it was dropped.");
            return;
        }

        var staging = GetUploadBuffer(subresource, required);

        if (staging == null || staging.Mapped == null)
        {
            GraphicsLog.WarnOnce("Could not allocate a staging buffer for a texture update; the upload was dropped.");
            return;
        }

        data[..required].CopyTo(new Span<byte>(staging.Mapped, required));

        VulkanBarriers.TransitionImage(backend, _commandBuffer, texture, VkImageLayout.TransferDstOptimal,
                                       VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

        var region = new VkBufferImageCopy
                         {
                             bufferRowLength = (uint)(rowPitch / bytesPerPixel),
                             imageSubresource = new VkImageSubresourceLayers
                                                    {
                                                        aspectMask = texture.Aspect,
                                                        mipLevel = (uint)mip,
                                                        baseArrayLayer = (uint)slice,
                                                        layerCount = 1,
                                                    },
                             imageExtent = new VkExtent3D(width, height, depth),
                         };

        backend.Api.vkCmdCopyBufferToImage(_commandBuffer, staging.Buffer, texture.Image, VkImageLayout.TransferDstOptimal, 1, &region);
    }

    public void SetVertexBuffers(int startSlot, ReadOnlySpan<VertexBufferView> buffers)
    {
        for (var i = 0; i < buffers.Length && startSlot + i < _vertexBuffers.Length; i++)
        {
            _vertexBuffers[startSlot + i] = buffers[i];
            _vertexBufferCount = Math.Max(_vertexBufferCount, startSlot + i + 1);
        }
    }

    public void SetIndexBuffer(GpuBuffer? buffer, Format format, int offset)
    {
        _indexBuffer = buffer as VulkanBuffer;
        _indexFormat = format == Format.R16_UInt ? VkIndexType.Uint16 : VkIndexType.Uint32;
        _indexOffset = (ulong)offset;
    }

    public void Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance)
    {
        if (!PrepareDraw())
            return;

        backend.Api.vkCmdDraw(_commandBuffer, (uint)vertexCount, (uint)Math.Max(1, instanceCount), (uint)firstVertex, (uint)firstInstance);
    }

    public void DrawIndexed(int indexCount, int instanceCount, int firstIndex, int vertexOffset, int firstInstance)
    {
        if (!PrepareDraw())
            return;

        backend.Api.vkCmdDrawIndexed(_commandBuffer, (uint)indexCount, (uint)Math.Max(1, instanceCount), (uint)firstIndex, vertexOffset,
                                     (uint)firstInstance);
    }

    public void DrawIndirect(GpuBuffer arguments, int offset)
    {
        if (arguments is not VulkanBuffer buffer || !PrepareDraw())
            return;

        backend.Api.vkCmdDrawIndirect(_commandBuffer, buffer.Buffer, (ulong)offset, 1, 0);
    }

    public void Dispatch(int groupsX, int groupsY, int groupsZ)
    {
        if (!PrepareDispatch())
            return;

        backend.Api.vkCmdDispatch(_commandBuffer, (uint)groupsX, (uint)groupsY, (uint)groupsZ);
    }

    public void DispatchIndirect(GpuBuffer arguments, int offset)
    {
        if (arguments is not VulkanBuffer buffer || !PrepareDispatch())
            return;

        backend.Api.vkCmdDispatchIndirect(_commandBuffer, buffer.Buffer, (ulong)offset);
    }

    public void CopyTexture(GpuTexture source, GpuTexture destination)
    {
        if (source is not VulkanTexture from || destination is not VulkanTexture to)
            return;

        EndRenderingIfActive();

        // A staging destination is a buffer, which is how a readback actually reaches the CPU.
        if (to.Image.IsNull && !to.StagingBuffer.IsNull)
        {
            VulkanBarriers.TransitionImage(backend, _commandBuffer, from, VkImageLayout.TransferSrcOptimal,
                                           VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferRead);

            VkBufferImageCopy region = new()
                                           {
                                               imageSubresource = new VkImageSubresourceLayers
                                                                      {
                                                                          aspectMask = from.Aspect,
                                                                          layerCount = 1,
                                                                      },
                                               imageExtent = ExtentOf(from.Description),
                                           };

            backend.Api.vkCmdCopyImageToBuffer(_commandBuffer, from.Image, VkImageLayout.TransferSrcOptimal, to.StagingBuffer, 1, &region);
            return;
        }

        if (from.Image.IsNull && !from.StagingBuffer.IsNull)
        {
            VulkanBarriers.TransitionImage(backend, _commandBuffer, to, VkImageLayout.TransferDstOptimal,
                                           VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

            VkBufferImageCopy region = new()
                                           {
                                               imageSubresource = new VkImageSubresourceLayers
                                                                      {
                                                                          aspectMask = to.Aspect,
                                                                          layerCount = 1,
                                                                      },
                                               imageExtent = ExtentOf(to.Description),
                                           };

            backend.Api.vkCmdCopyBufferToImage(_commandBuffer, from.StagingBuffer, to.Image, VkImageLayout.TransferDstOptimal, 1, &region);
            return;
        }

        VulkanBarriers.TransitionImage(backend, _commandBuffer, from, VkImageLayout.TransferSrcOptimal,
                                       VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferRead);
        VulkanBarriers.TransitionImage(backend, _commandBuffer, to, VkImageLayout.TransferDstOptimal,
                                       VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

        VkImageCopy copy = new()
                               {
                                   srcSubresource = new VkImageSubresourceLayers { aspectMask = from.Aspect, layerCount = 1 },
                                   dstSubresource = new VkImageSubresourceLayers { aspectMask = to.Aspect, layerCount = 1 },
                                   extent = ExtentOf(from.Description),
                               };

        backend.Api.vkCmdCopyImage(_commandBuffer, from.Image, VkImageLayout.TransferSrcOptimal, to.Image, VkImageLayout.TransferDstOptimal,
                                   1, &copy);
    }

    public void CopyBuffer(GpuBuffer source, int sourceOffset, GpuBuffer destination, int destinationOffset, int size)
    {
        if (source is not VulkanBuffer from || destination is not VulkanBuffer to)
            return;

        EndRenderingIfActive();
        VulkanBarriers.BarrierBuffer(backend, _commandBuffer, from, VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferRead);
        VulkanBarriers.BarrierBuffer(backend, _commandBuffer, to, VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

        VkBufferCopy region = new()
                                  {
                                      srcOffset = from.CurrentOffset + (ulong)sourceOffset,
                                      dstOffset = to.CurrentOffset + (ulong)destinationOffset,
                                      size = (ulong)Math.Max(1, size),
                                  };

        backend.Api.vkCmdCopyBuffer(_commandBuffer, from.Buffer, to.Buffer, 1, &region);
    }

    public void CopyTextureRegion(GpuTexture source, int sourceSubresource, GpuTexture destination, int destinationSubresource, int x, int y, int z)
    {
        if (source is not VulkanTexture from || destination is not VulkanTexture to || from.Image.IsNull || to.Image.IsNull)
            return;

        EndRenderingIfActive();
        VulkanBarriers.TransitionImage(backend, _commandBuffer, from, VkImageLayout.TransferSrcOptimal,
                                       VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferRead);
        VulkanBarriers.TransitionImage(backend, _commandBuffer, to, VkImageLayout.TransferDstOptimal,
                                       VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

        // D3D11 numbers subresources mip-major, so the slice is what the index divides into.
        var sourceMips = Math.Max(1, from.Description.MipLevels);
        var destinationMips = Math.Max(1, to.Description.MipLevels);

        VkImageCopy copy = new()
                               {
                                   srcSubresource = new VkImageSubresourceLayers
                                                        {
                                                            aspectMask = from.Aspect,
                                                            mipLevel = (uint)(sourceSubresource % sourceMips),
                                                            baseArrayLayer = (uint)(sourceSubresource / sourceMips),
                                                            layerCount = 1,
                                                        },
                                   dstSubresource = new VkImageSubresourceLayers
                                                        {
                                                            aspectMask = to.Aspect,
                                                            mipLevel = (uint)(destinationSubresource % destinationMips),
                                                            baseArrayLayer = (uint)(destinationSubresource / destinationMips),
                                                            layerCount = 1,
                                                        },
                                   dstOffset = new VkOffset3D(x, y, z),
                                   extent = ExtentOf(from.Description),
                               };

        backend.Api.vkCmdCopyImage(_commandBuffer, from.Image, VkImageLayout.TransferSrcOptimal, to.Image, VkImageLayout.TransferDstOptimal,
                                   1, &copy);
    }

    public void ResolveTexture(GpuTexture source, GpuTexture destination, Format format)
    {
        if (source is not VulkanTexture from || destination is not VulkanTexture to || from.Image.IsNull || to.Image.IsNull)
            return;

        EndRenderingIfActive();
        VulkanBarriers.TransitionImage(backend, _commandBuffer, from, VkImageLayout.TransferSrcOptimal,
                                       VkPipelineStageFlags2.Resolve, VkAccessFlags2.TransferRead);
        VulkanBarriers.TransitionImage(backend, _commandBuffer, to, VkImageLayout.TransferDstOptimal,
                                       VkPipelineStageFlags2.Resolve, VkAccessFlags2.TransferWrite);

        VkImageResolve resolve = new()
                                     {
                                         srcSubresource = new VkImageSubresourceLayers { aspectMask = from.Aspect, layerCount = 1 },
                                         dstSubresource = new VkImageSubresourceLayers { aspectMask = to.Aspect, layerCount = 1 },
                                         extent = ExtentOf(to.Description),
                                     };

        backend.Api.vkCmdResolveImage(_commandBuffer, from.Image, VkImageLayout.TransferSrcOptimal, to.Image, VkImageLayout.TransferDstOptimal,
                                      1, &resolve);
    }

    public void GenerateMips(GpuTextureView view)
    {
        if (view is not VulkanTextureView { Texture: var texture } || texture.Image.IsNull)
            return;

        var levels = Math.Max(1, texture.Description.MipLevels);

        if (levels == 1)
            return;

        EndRenderingIfActive();
        VulkanBarriers.TransitionImage(backend, _commandBuffer, texture, VkImageLayout.General,
                                       VkPipelineStageFlags2.Blit, VkAccessFlags2.TransferWrite);

        var width = Math.Max(1, texture.Description.Width);
        var height = Math.Max(1, texture.Description.Height);

        for (var level = 1; level < levels; level++)
        {
            var nextWidth = Math.Max(1, width / 2);
            var nextHeight = Math.Max(1, height / 2);

            VkImageBlit blit = new()
                                   {
                                       srcSubresource = new VkImageSubresourceLayers
                                                            { aspectMask = texture.Aspect, mipLevel = (uint)(level - 1), layerCount = 1 },
                                       dstSubresource = new VkImageSubresourceLayers
                                                            { aspectMask = texture.Aspect, mipLevel = (uint)level, layerCount = 1 },
                                   };

            blit.srcOffsets[1] = new VkOffset3D(width, height, 1);
            blit.dstOffsets[1] = new VkOffset3D(nextWidth, nextHeight, 1);

            backend.Api.vkCmdBlitImage(_commandBuffer, texture.Image, VkImageLayout.General, texture.Image, VkImageLayout.General,
                                       1, &blit, VkFilter.Linear);

            // Each level is read to produce the next one.
            VkMemoryBarrier2 barrier = new()
                                           {
                                               srcStageMask = VkPipelineStageFlags2.Blit,
                                               srcAccessMask = VkAccessFlags2.TransferWrite,
                                               dstStageMask = VkPipelineStageFlags2.Blit,
                                               dstAccessMask = VkAccessFlags2.TransferRead,
                                           };

            VkDependencyInfo dependency = new() { memoryBarrierCount = 1, pMemoryBarriers = &barrier };
            backend.Api.vkCmdPipelineBarrier2(_commandBuffer, &dependency);

            width = nextWidth;
            height = nextHeight;
        }
    }

    /// <summary>
    /// The last thing that happens to a back buffer in a frame. Rendering left it as a colour attachment and
    /// the window system needs it in its own layout.
    /// </summary>
    internal void TransitionToPresent(VulkanTexture texture)
    {
        EndRenderingIfActive();
        VulkanBarriers.TransitionImage(backend, _commandBuffer, texture, VkImageLayout.PresentSrcKHR,
                                       VkPipelineStageFlags2.BottomOfPipe, VkAccessFlags2.None);
    }

    public void PushDebugGroup(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + '\0');

        fixed (byte* pointer = bytes)
        {
            VkDebugUtilsLabelEXT label = new() { pLabelName = pointer };
            backend.InstanceApi.vkCmdBeginDebugUtilsLabelEXT(_commandBuffer, &label);
        }
    }

    public void PopDebugGroup() => backend.InstanceApi.vkCmdEndDebugUtilsLabelEXT(_commandBuffer);

    #region issuing work
    private bool PrepareDraw()
    {
        if (_pipeline == null)
            return false;

        TransitionBoundResources(VkPipelineStageFlags2.AllGraphics);
        BeginRenderingIfNeeded();

        var api = backend.Api;
        api.vkCmdBindPipeline(_commandBuffer, VkPipelineBindPoint.Graphics, _pipeline.Pipeline);

        if (_hasViewport)
            api.vkCmdSetViewport(_commandBuffer, 0, _viewport);

        api.vkCmdSetScissor(_commandBuffer, 0, _hasScissor ? _scissor : ScissorFromViewport());

        var constants = stackalloc float[4] { _blendConstants.X, _blendConstants.Y, _blendConstants.Z, _blendConstants.W };
        api.vkCmdSetBlendConstants(_commandBuffer, constants);
        api.vkCmdSetStencilReference(_commandBuffer, VkStencilFaceFlags.FrontAndBack, _stencilReference);

        PushDescriptors(VkPipelineBindPoint.Graphics);
        BindVertexBuffers();
        return true;
    }

    private bool PrepareDispatch()
    {
        if (_pipeline == null)
            return false;

        EndRenderingIfActive();
        TransitionBoundResources(VkPipelineStageFlags2.ComputeShader);
        backend.Api.vkCmdBindPipeline(_commandBuffer, VkPipelineBindPoint.Compute, _pipeline.Pipeline);
        PushDescriptors(VkPipelineBindPoint.Compute);
        return true;
    }

    /// <summary>
    /// Puts every bound resource into the layout its use needs. This runs before the render pass opens,
    /// because a transition inside one is not allowed.
    /// </summary>
    private void TransitionBoundResources(VkPipelineStageFlags2 stage)
    {
        for (var stageIndex = 0; stageIndex < MaxStages; stageIndex++)
        {
            var bindings = _bindings[stageIndex];

            for (var i = 0; i < _bindingCounts[stageIndex]; i++)
            {
                var binding = bindings[i];

                switch (binding.Kind)
                {
                    case BindingKind.SampledTexture when binding.TextureView is VulkanTextureView sampled:
                        VulkanBarriers.TransitionImage(backend, _commandBuffer, sampled.Texture, VkImageLayout.ShaderReadOnlyOptimal,
                                                       stage, VkAccessFlags2.ShaderSampledRead);
                        break;

                    case BindingKind.StorageTexture when binding.TextureView is VulkanTextureView storage:
                        VulkanBarriers.TransitionImage(backend, _commandBuffer, storage.Texture, VkImageLayout.General,
                                                       stage, VkAccessFlags2.ShaderStorageRead | VkAccessFlags2.ShaderStorageWrite);
                        break;

                    case BindingKind.StructuredBuffer or BindingKind.StorageBuffer when binding.Buffer is VulkanBuffer buffer:
                        VulkanBarriers.BarrierBuffer(backend, _commandBuffer, buffer, stage,
                                                     binding.Slot % ShaderSlots.StageStride >= ShaderSlots.UnorderedAccessBase
                                                         ? VkAccessFlags2.ShaderStorageRead | VkAccessFlags2.ShaderStorageWrite
                                                         : VkAccessFlags2.ShaderStorageRead);
                        break;

                    case BindingKind.ConstantBuffer when binding.Buffer is VulkanBuffer constants:
                        VulkanBarriers.BarrierBuffer(backend, _commandBuffer, constants, stage, VkAccessFlags2.UniformRead);
                        break;
                }
            }
        }
    }

    private void BeginRenderingIfNeeded()
    {
        if (_renderingActive || !_renderingRequested)
            return;

        var attachments = stackalloc VkRenderingAttachmentInfo[Math.Max(1, _colorCount)];
        var width = 0;
        var height = 0;

        for (var i = 0; i < _colorCount; i++)
        {
            var view = _colorTargets[i];
            VulkanBarriers.TransitionImage(backend, _commandBuffer, view.Texture, VkImageLayout.ColorAttachmentOptimal,
                                           VkPipelineStageFlags2.ColorAttachmentOutput, VkAccessFlags2.ColorAttachmentWrite);

            attachments[i] = new VkRenderingAttachmentInfo
                                 {
                                     imageView = view.View,
                                     imageLayout = VkImageLayout.ColorAttachmentOptimal,

                                     // Load, never clear: a clear is its own call in this API, as it is in D3D11.
                                     loadOp = VkAttachmentLoadOp.Load,
                                     storeOp = VkAttachmentStoreOp.Store,
                                 };

            width = Math.Max(width, view.Texture.Description.Width);
            height = Math.Max(height, view.Texture.Description.Height);
        }

        VkRenderingAttachmentInfo depthAttachment = default;

        if (_depthTarget != null)
        {
            VulkanBarriers.TransitionImage(backend, _commandBuffer, _depthTarget.Texture, VkImageLayout.DepthStencilAttachmentOptimal,
                                           VkPipelineStageFlags2.EarlyFragmentTests | VkPipelineStageFlags2.LateFragmentTests,
                                           VkAccessFlags2.DepthStencilAttachmentRead | VkAccessFlags2.DepthStencilAttachmentWrite);

            depthAttachment = new VkRenderingAttachmentInfo
                                  {
                                      imageView = _depthTarget.View,
                                      imageLayout = VkImageLayout.DepthStencilAttachmentOptimal,
                                      loadOp = VkAttachmentLoadOp.Load,
                                      storeOp = VkAttachmentStoreOp.Store,
                                  };

            width = Math.Max(width, _depthTarget.Texture.Description.Width);
            height = Math.Max(height, _depthTarget.Texture.Description.Height);
        }

        VkRenderingInfo renderingInfo = new()
                                            {
                                                renderArea = new VkRect2D(0, 0, (uint)width, (uint)height),
                                                layerCount = 1,
                                                colorAttachmentCount = (uint)_colorCount,
                                                pColorAttachments = _colorCount > 0 ? attachments : null,
                                                pDepthAttachment = _depthTarget != null ? &depthAttachment : null,
                                            };

        backend.Api.vkCmdBeginRendering(_commandBuffer, &renderingInfo);
        _renderingActive = true;
    }

    private void EndRenderingIfActive()
    {
        if (!_renderingActive)
            return;

        backend.Api.vkCmdEndRendering(_commandBuffer);
        _renderingActive = false;
    }

    /// <summary>
    /// Pushes one descriptor per binding the pipeline's shaders declare. A declared binding the caller left
    /// unset gets a placeholder rather than nothing: D3D11 tolerated an unbound slot, and Vulkan would read
    /// an undefined descriptor.
    /// </summary>
    private void PushDescriptors(VkPipelineBindPoint bindPoint)
    {
        if (_pipeline == null || _pipeline.Declared.Count == 0)
            return;

        var nulls = backend.NullResources;

        foreach (var setGroup in _pipeline.Declared.GroupBy(entry => entry.Key.Set))
        {
            var declared = setGroup.ToArray();
            var writes = stackalloc VkWriteDescriptorSet[declared.Length];
            var bufferInfos = stackalloc VkDescriptorBufferInfo[declared.Length];
            var imageInfos = stackalloc VkDescriptorImageInfo[declared.Length];

            for (var i = 0; i < declared.Length; i++)
            {
                var (key, type) = (declared[i].Key, declared[i].Value);
                var binding = FindBinding(key.Slot);

                writes[i] = new VkWriteDescriptorSet
                                {
                                    dstBinding = (uint)key.Slot,
                                    descriptorCount = 1,
                                    descriptorType = type,
                                };

                switch (type)
                {
                    case VkDescriptorType.UniformBuffer:
                    case VkDescriptorType.StorageBuffer:
                    {
                        var buffer = binding?.Buffer as VulkanBuffer;
                        var size = binding?.BufferSize ?? 0;

                        bufferInfos[i] = buffer == null || buffer.Buffer.IsNull
                                             ? new VkDescriptorBufferInfo { buffer = nulls.Buffer, offset = 0, range = VK_WHOLE_SIZE }
                                             : new VkDescriptorBufferInfo
                                                   {
                                                       buffer = buffer.Buffer,
                                                       offset = buffer.CurrentOffset + (ulong)(binding?.BufferOffset ?? 0),
                                                       range = size > 0 ? (ulong)size : (ulong)buffer.Description.SizeInBytes,
                                                   };

                        writes[i].pBufferInfo = &bufferInfos[i];
                        break;
                    }

                    case VkDescriptorType.Sampler:
                    {
                        var sampler = binding?.Sampler as VulkanSampler;
                        imageInfos[i] = new VkDescriptorImageInfo { sampler = sampler?.Sampler ?? nulls.Sampler };
                        writes[i].pImageInfo = &imageInfos[i];
                        break;
                    }

                    case VkDescriptorType.StorageImage:
                    {
                        var view = binding?.TextureView as VulkanTextureView;
                        imageInfos[i] = new VkDescriptorImageInfo
                                            {
                                                imageView = view?.View ?? nulls.StorageView,
                                                imageLayout = VkImageLayout.General,
                                            };

                        writes[i].pImageInfo = &imageInfos[i];
                        break;
                    }

                    default:
                    {
                        var view = binding?.TextureView as VulkanTextureView;
                        imageInfos[i] = new VkDescriptorImageInfo
                                            {
                                                imageView = view?.View ?? nulls.SampledView,
                                                imageLayout = VkImageLayout.ShaderReadOnlyOptimal,
                                            };

                        writes[i].pImageInfo = &imageInfos[i];
                        break;
                    }
                }
            }

            backend.Api.vkCmdPushDescriptorSetKHR(_commandBuffer, bindPoint, _pipeline.Layout, (uint)setGroup.Key,
                                                  (uint)declared.Length, writes);
        }
    }

    /// <summary>Finds what was bound for one of the pipeline's declared bindings, across all stages.</summary>
    private Binding? FindBinding(int slot)
    {
        for (var stage = 0; stage < MaxStages; stage++)
        {
            var bindings = _bindings[stage];

            for (var i = 0; i < _bindingCounts[stage]; i++)
            {
                if (bindings[i].Slot == slot)
                    return bindings[i];
            }
        }

        return null;
    }

    private void BindVertexBuffers()
    {
        for (var i = 0; i < _vertexBufferCount; i++)
        {
            if (_vertexBuffers[i].Buffer is not VulkanBuffer buffer || buffer.Buffer.IsNull)
                continue;

            var handle = buffer.Buffer;
            var offset = buffer.CurrentOffset + (ulong)_vertexBuffers[i].Offset;
            backend.Api.vkCmdBindVertexBuffers(_commandBuffer, (uint)i, 1, &handle, &offset);
        }

        if (_indexBuffer is { Buffer.IsNull: false })
            backend.Api.vkCmdBindIndexBuffer(_commandBuffer, _indexBuffer.Buffer, _indexBuffer.CurrentOffset + _indexOffset, _indexFormat);
    }

    private VkRect2D ScissorFromViewport()
    {
        // The viewport is stored flipped, so its origin is the bottom edge.
        var height = (int)-_viewport.height;
        return new VkRect2D((int)_viewport.x, (int)(_viewport.y - height), (uint)_viewport.width, (uint)height);
    }

    /// <summary>Staging for one subresource's pixels, grown and reused across frames like the scratch constants.</summary>
    private VulkanBuffer? GetUploadBuffer(int subresource, int size)
    {
        if (_uploadBuffers.TryGetValue(subresource, out var buffer) && buffer.Description.SizeInBytes >= size)
            return buffer;

        buffer?.Dispose();

        buffer = backend.CreateBuffer(new GpuBufferDescription
                                          {
                                              SizeInBytes = Math.Max(256, size),
                                              Usage = BufferUsage.CopySource,
                                              Memory = MemoryKind.Upload,
                                          },
                                      ReadOnlySpan<byte>.Empty, "texture upload") as VulkanBuffer;

        if (buffer != null)
            _uploadBuffers[subresource] = buffer;

        return buffer;
    }

    private VulkanBuffer? ScratchBuffer(int set, int slot, int size)
    {
        var key = (set, slot);

        if (_scratchBuffers.TryGetValue(key, out var buffer) && buffer.Description.SizeInBytes >= size)
            return buffer;

        buffer?.Dispose();

        buffer = backend.CreateBuffer(new GpuBufferDescription
                                          {
                                              SizeInBytes = Math.Max(256, size),
                                              Usage = BufferUsage.Constant,
                                              Memory = MemoryKind.Upload,
                                          },
                                      ReadOnlySpan<byte>.Empty, "inline constants") as VulkanBuffer;

        if (buffer != null)
            _scratchBuffers[key] = buffer;

        return buffer;
    }

    private static VkExtent3D ExtentOf(in TextureDescription description)
        => new(Math.Max(1, description.Width), Math.Max(1, description.Height),
               description.Dimension == TextureDimension.Texture3D ? Math.Max(1, description.Depth) : 1);
    #endregion

    private const int MaxStages = 4;
    private const int MaxBindingsPerStage = 166;

    private readonly Binding[][] _bindings =
        [
            new Binding[MaxBindingsPerStage],
            new Binding[MaxBindingsPerStage],
            new Binding[MaxBindingsPerStage],
            new Binding[MaxBindingsPerStage],
        ];

    private readonly int[] _bindingCounts = new int[MaxStages];
    private readonly VulkanTextureView[] _colorTargets = new VulkanTextureView[BlendTargetStates.MaxRenderTargets];
    private readonly VertexBufferView[] _vertexBuffers = new VertexBufferView[8];
    private readonly Dictionary<(int, int), VulkanBuffer> _scratchBuffers = [];
    private readonly Dictionary<int, VulkanBuffer> _uploadBuffers = [];

    private VkCommandBuffer _commandBuffer;
    private VulkanPipeline? _pipeline;
    private VulkanTextureView? _depthTarget;
    private VulkanBuffer? _indexBuffer;
    private VkIndexType _indexFormat = VkIndexType.Uint32;
    private ulong _indexOffset;
    private VkViewport _viewport;
    private VkRect2D _scissor;
    private Vector4 _blendConstants = Vector4.One;
    private uint _stencilReference;
    private int _frameIndex;
    private int _colorCount;
    private int _vertexBufferCount;
    private bool _renderingRequested;
    private bool _renderingActive;
    private bool _hasViewport;
    private bool _hasScissor;
}
