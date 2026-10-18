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
        _uploadBuffersUsed[frameIndex] = 0;
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

    /// <summary>
    /// Records on into a new command buffer after a mid-frame submit. The collected state stays; only the render
    /// pass has to be reopened, which the next draw does.
    /// </summary>
    internal void Continue(VkCommandBuffer commandBuffer, int frameIndex)
    {
        _commandBuffer = commandBuffer;
        _frameIndex = frameIndex;
        _uploadBuffersUsed[frameIndex] = 0;
        _renderingActive = false;
    }

    internal void End() => EndRenderingIfActive();

    public void BeginRendering(ReadOnlySpan<GpuTextureView> colorTargets, GpuTextureView? depthTarget)
    {
        EndRenderingIfActive();

        _colorCount = 0;
        var targetFailed = false;

        foreach (var target in colorTargets)
        {
            if (target is not VulkanTextureView view || _colorCount >= _colorTargets.Length)
                continue;

            if (view.View.IsNull)
                targetFailed = true;
            else
                _colorTargets[_colorCount++] = view;
        }

        _depthTarget = depthTarget as VulkanTextureView;

        if (_depthTarget is { View.IsNull: true })
        {
            targetFailed = true;
            _depthTarget = null;
        }

        // Only remembered here. The pass opens at the first draw, once the bindings have been transitioned.
        // A target whose texture never got its memory makes the pass pointless: its draws are skipped instead.
        _renderingRequested = !targetFailed && (_colorCount > 0 || _depthTarget != null);
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
        _scissor = ClampScissor(rect.X, rect.Y, rect.Width, rect.Height);
        _hasScissor = true;
    }

    /// <summary>
    /// D3D11 clamps a scissor that starts left of or above the target; Vulkan rejects a negative offset, and RADV
    /// turns it into an empty rectangle that discards the whole draw, as an oversized cover-crop viewport did.
    /// </summary>
    private static VkRect2D ClampScissor(int x, int y, int width, int height)
    {
        var left = Math.Max(0, x);
        var top = Math.Max(0, y);
        return new VkRect2D(left, top, (uint)Math.Max(0, width - (left - x)), (uint)Math.Max(0, height - (top - y)));
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

        WriteUploadBuffer(buffer, data);

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
            case VulkanBuffer { Mapped: not null } buffer:
            {
                var size = buffer.Description.SizeInBytes;

                if (TryClaimSliceForThisFrame(buffer))
                    return new MappedMemory((byte*)buffer.Mapped + buffer.CurrentOffset, size, size, size);

                if (buffer.PendingWrite == null)
                    buffer.PendingWrite = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)size);

                buffer.HasPendingWrite = true;
                return new MappedMemory(buffer.PendingWrite, size, size, size);
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
    /// Moves an upload buffer to the copy this frame may write, for its first write in the frame. That is what
    /// makes D3D11's Map(WriteDiscard) free of stalls: the frames still on the GPU keep reading the copy they were
    /// given. False when the buffer was already written this frame: the slice then holds what the frame's earlier
    /// commands read when the GPU runs them, so a further write has to go through the command stream.
    /// </summary>
    private bool TryClaimSliceForThisFrame(VulkanBuffer buffer)
    {
        // Outside a frame there is no command stream to order a write in; the slice is all there is.
        if (!backend.IsFrameActive)
            return true;

        var serial = backend.RecordingFrameSerial;

        if (buffer.SliceWrittenInFrameSerial == serial)
            return false;

        buffer.CurrentSlice = _frameIndex % buffer.SliceCount;
        buffer.SliceWrittenInFrameSerial = serial;
        return true;
    }

    /// <summary>
    /// D3D11 renames a buffer on every Map(WriteDiscard), so a graph evaluated several times in one frame (test
    /// warm-up, loops, multi-pass effects) gives each draw and dispatch the constants written just before it.
    /// </summary>
    private void WriteUploadBuffer(VulkanBuffer buffer, ReadOnlySpan<byte> data)
    {
        if (TryClaimSliceForThisFrame(buffer))
        {
            data.CopyTo(new Span<byte>((byte*)buffer.Mapped + buffer.CurrentOffset, data.Length));
            return;
        }

        RecordBufferWrite(buffer, data);
    }

    public void Unmap(GpuResource resource, int subresource)
    {
        if (resource is not VulkanBuffer { HasPendingWrite: true } buffer)
            return;

        buffer.HasPendingWrite = false;
        RecordBufferWrite(buffer, new ReadOnlySpan<byte>(buffer.PendingWrite, buffer.Description.SizeInBytes));
    }

    /// <summary>Writes into the buffer's current copy in command order, after the commands recorded so far.</summary>
    private void RecordBufferWrite(VulkanBuffer buffer, ReadOnlySpan<byte> data)
    {
        EndRenderingIfActive();
        VulkanBarriers.BarrierBuffer(backend, _commandBuffer, buffer, VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

        // vkCmdUpdateBuffer is limited to 64 KB and whole words; anything else goes through a staging copy.
        if (data.Length <= 65536 && data.Length % 4 == 0)
        {
            fixed (byte* pointer = data)
            {
                backend.Api.vkCmdUpdateBuffer(_commandBuffer, buffer.Buffer, buffer.CurrentOffset, (ulong)data.Length, pointer);
            }
        }
        else
        {
            CopyThroughStaging(buffer, data);
        }
    }

    public void UpdateResource(GpuResource resource, int subresource, ReadOnlySpan<byte> data, int rowPitch, int slicePitch)
    {
        switch (resource)
        {
            // Creation already reported why there is nothing to write into.
            case VulkanBuffer { Buffer.IsNull: true }:
                break;

            case VulkanBuffer { Mapped: not null } buffer:
                WriteUploadBuffer(buffer, data);
                break;

            case VulkanBuffer buffer:
            {
                EndRenderingIfActive();
                VulkanBarriers.BarrierBuffer(backend, _commandBuffer, buffer, VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

                // vkCmdUpdateBuffer is limited to 64 KB, which covers every constant buffer TiXL writes this way.
                // Larger updates - point and vertex data filled from the CPU - go through a staging copy.
                if (data.Length <= 65536)
                {
                    fixed (byte* pointer = data)
                    {
                        backend.Api.vkCmdUpdateBuffer(_commandBuffer, buffer.Buffer, 0, (ulong)data.Length, pointer);
                    }
                }
                else
                {
                    CopyThroughStaging(buffer, data);
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

    /// <summary>
    /// A staging buffer of its own per update: the copy runs when the frame is submitted, so a shared one would
    /// be overwritten by the next update recorded in the same frame. Disposing retires it until the frame is done.
    /// </summary>
    private void CopyThroughStaging(VulkanBuffer buffer, ReadOnlySpan<byte> data)
    {
        if (backend.CreateBuffer(new GpuBufferDescription
                                     {
                                         SizeInBytes = data.Length,
                                         Usage = BufferUsage.CopySource,
                                         Memory = MemoryKind.Upload,
                                     },
                                 data, "buffer upload") is not VulkanBuffer { Mapped: not null } staging)
        {
            GraphicsLog.WarnOnce("Could not allocate a staging buffer for a buffer update; the update was dropped.");
            return;
        }

        var region = new VkBufferCopy { dstOffset = buffer.CurrentOffset, size = (ulong)data.Length };
        backend.Api.vkCmdCopyBuffer(_commandBuffer, staging.Buffer, buffer.Buffer, 1, &region);
        staging.Dispose();
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

        var staging = GetUploadBuffer(required);

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
        if (arguments is not VulkanBuffer buffer)
            return;

        BarrierIndirectArguments(buffer);

        if (!PrepareDraw())
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
        if (arguments is not VulkanBuffer buffer)
            return;

        BarrierIndirectArguments(buffer);

        if (!PrepareDispatch())
            return;

        backend.Api.vkCmdDispatchIndirect(_commandBuffer, buffer.Buffer, (ulong)offset);
    }

    /// <summary>
    /// Waits for whatever produced the indirect arguments. They are usually written by a compute shader
    /// earlier in the frame, and reading them is a stage of its own - the bound-resource barriers say nothing
    /// about it, because the argument buffer is passed to the call rather than bound to the pipeline. Without
    /// this the counts can be read while the dispatch that computes them is still writing.
    /// </summary>
    private void BarrierIndirectArguments(VulkanBuffer arguments)
    {
        if (!VulkanBarriers.NeedsBarrier(arguments, VkAccessFlags2.IndirectCommandRead))
            return;

        // Barriers are not allowed inside a render pass; attachments always load, so closing it loses nothing.
        EndRenderingIfActive();
        VulkanBarriers.BarrierBuffer(backend, _commandBuffer, arguments, VkPipelineStageFlags2.DrawIndirect,
                                     VkAccessFlags2.IndirectCommandRead);
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

        // Either side never got its memory; creation reported it.
        if (from.Image.IsNull || to.Image.IsNull)
            return;

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
        if (source is not VulkanBuffer from || destination is not VulkanBuffer to || from.Buffer.IsNull || to.Buffer.IsNull)
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
        if (source is not VulkanTexture from || destination is not VulkanTexture to)
            return;

        // A staging texture is a buffer, so the copy is a buffer transfer: the whole texture into a readback, or an
        // upload placed at an offset in the destination, as the thumbnail atlas does. Readbacks of one mip of a
        // larger texture and uploads from a mip other than the first are not laid out in the buffer.
        if (to.Image.IsNull)
        {
            if (x == 0 && y == 0 && z == 0 && sourceSubresource == 0 && destinationSubresource == 0)
                CopyTexture(source, destination);
            else
                GraphicsLog.WarnOnce("Copying a region or a mip into a staging texture is not implemented; the copy is skipped.");

            return;
        }

        if (from.Image.IsNull)
        {
            if (from.StagingBuffer.IsNull || sourceSubresource != 0)
            {
                GraphicsLog.WarnOnce("Copying from a mip of a staging texture is not implemented; the copy is skipped.");
                return;
            }

            EndRenderingIfActive();
            VulkanBarriers.TransitionImage(backend, _commandBuffer, to, VkImageLayout.TransferDstOptimal,
                                           VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

            var targetMips = Math.Max(1, to.Description.MipLevels);
            VkBufferImageCopy upload = new()
                                           {
                                               imageSubresource = new VkImageSubresourceLayers
                                                                      {
                                                                          aspectMask = to.Aspect,
                                                                          mipLevel = (uint)(destinationSubresource % targetMips),
                                                                          baseArrayLayer = (uint)(destinationSubresource / targetMips),
                                                                          layerCount = 1,
                                                                      },
                                               imageOffset = new VkOffset3D(x, y, z),
                                               imageExtent = ExtentOf(from.Description),
                                           };

            backend.Api.vkCmdCopyBufferToImage(_commandBuffer, from.StagingBuffer, to.Image, VkImageLayout.TransferDstOptimal, 1, &upload);
            return;
        }

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
        // Both directions: every level but the first is written by a blit that reads the level above it.
        VulkanBarriers.TransitionImage(backend, _commandBuffer, texture, VkImageLayout.General,
                                       VkPipelineStageFlags2.Blit, VkAccessFlags2.TransferRead | VkAccessFlags2.TransferWrite);

        var width = Math.Max(1, texture.Description.Width);
        var height = Math.Max(1, texture.Description.Height);
        var layers = texture.Description.Dimension switch
                         {
                             TextureDimension.Texture3D   => 1u,
                             TextureDimension.TextureCube => (uint)Math.Max(6, texture.Description.ArraySize),
                             _                            => (uint)Math.Max(1, texture.Description.ArraySize),
                         };

        for (var level = 1; level < levels; level++)
        {
            var nextWidth = Math.Max(1, width / 2);
            var nextHeight = Math.Max(1, height / 2);

            // Every layer: a cube map's six faces each need their own chain, or faces 1-5 keep black mips.
            VkImageBlit blit = new()
                                   {
                                       srcSubresource = new VkImageSubresourceLayers
                                                            { aspectMask = texture.Aspect, mipLevel = (uint)(level - 1), layerCount = layers },
                                       dstSubresource = new VkImageSubresourceLayers
                                                            { aspectMask = texture.Aspect, mipLevel = (uint)level, layerCount = layers },
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

    /// <summary>Vulkan timestamps need no begin, and a disjoint query has nothing to record.</summary>
    public void BeginQuery(GpuQuery query)
    {
    }

    public void EndQuery(GpuQuery query)
    {
        if (query is not VulkanQuery vulkanQuery)
            return;

        if (!vulkanQuery.Pool.IsNull)
        {
            // A query pool can't be reset inside a render pass; the next draw reopens it, loading what is there.
            EndRenderingIfActive();
            backend.Api.vkCmdResetQueryPool(_commandBuffer, vulkanQuery.Pool, 0, 1);
            backend.Api.vkCmdWriteTimestamp2(_commandBuffer, VkPipelineStageFlags2.AllCommands, vulkanQuery.Pool, 0);
        }

        vulkanQuery.WrittenInFrameSerial = backend.RecordingFrameSerial;
    }

    #region issuing work
    private bool PrepareDraw()
    {
        if (_pipeline == null)
            return false;

        // Barriers are not allowed inside a render pass. A texture uploaded or rendered into since the pass
        // opened has to be transitioned before this draw samples it, so the pass is closed and reopened around
        // the barriers; attachments always load, so nothing drawn so far is lost.
        if (_renderingActive && (BoundResourcesNeedBarriers(GraphicsStages) || InputBuffersNeedBarriers()))
            EndRenderingIfActive();

        TransitionBoundResources(GraphicsStages, VkPipelineStageFlags2.AllGraphics);
        BarrierInputBuffers();
        BeginRenderingIfNeeded();

        // Vulkan has no draw outside a pass; D3D11 silently dropped one without targets.
        if (!_renderingActive)
            return false;

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
        TransitionBoundResources(ComputeStages, VkPipelineStageFlags2.ComputeShader);
        backend.Api.vkCmdBindPipeline(_commandBuffer, VkPipelineBindPoint.Compute, _pipeline.Pipeline);
        PushDescriptors(VkPipelineBindPoint.Compute);
        return true;
    }

    /// <summary>
    /// Puts every resource bound to the given stages into the layout its use needs. This runs before the render
    /// pass opens, because a transition inside one is not allowed.
    /// </summary>
    /// <remarks>
    /// Only the stages the work runs: compute bindings stay set across draws, as they do in D3D11, and a texture
    /// a dispatch wrote as a storage image would otherwise be moved back to General after the pixel stage's
    /// transition to sampling - leaving it in the wrong layout for the draw that samples it.
    /// </remarks>
    private void TransitionBoundResources(StageRange stages, VkPipelineStageFlags2 stage)
    {
        for (var stageIndex = stages.First; stageIndex <= stages.Last; stageIndex++)
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

    /// <summary>Whether <see cref="TransitionBoundResources"/> would emit any barrier.</summary>
    private bool BoundResourcesNeedBarriers(StageRange stages)
    {
        for (var stageIndex = stages.First; stageIndex <= stages.Last; stageIndex++)
        {
            var bindings = _bindings[stageIndex];

            for (var i = 0; i < _bindingCounts[stageIndex]; i++)
            {
                var binding = bindings[i];

                var needed = binding.Kind switch
                                 {
                                     BindingKind.SampledTexture when binding.TextureView is VulkanTextureView sampled
                                         => VulkanBarriers.NeedsTransition(sampled.Texture, VkImageLayout.ShaderReadOnlyOptimal,
                                                                           VkAccessFlags2.ShaderSampledRead),
                                     BindingKind.StorageTexture when binding.TextureView is VulkanTextureView storage
                                         => VulkanBarriers.NeedsTransition(storage.Texture, VkImageLayout.General,
                                                                           VkAccessFlags2.ShaderStorageRead | VkAccessFlags2.ShaderStorageWrite),
                                     BindingKind.StructuredBuffer or BindingKind.StorageBuffer when binding.Buffer is VulkanBuffer buffer
                                         => VulkanBarriers.NeedsBarrier(buffer,
                                                                        binding.Slot % ShaderSlots.StageStride >= ShaderSlots.UnorderedAccessBase
                                                                            ? VkAccessFlags2.ShaderStorageRead | VkAccessFlags2.ShaderStorageWrite
                                                                            : VkAccessFlags2.ShaderStorageRead),
                                     BindingKind.ConstantBuffer when binding.Buffer is VulkanBuffer constants
                                         => VulkanBarriers.NeedsBarrier(constants, VkAccessFlags2.UniformRead),
                                     _ => false,
                                 };

                if (needed)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Narrows the render area and layer count to what this attachment's view covers.</summary>
    private static void Fit(VulkanTextureView view, ref int width, ref int height, ref int layers)
    {
        var mip = view.Description.FirstMip;
        width = Math.Min(width, Math.Max(1, view.Texture.Description.Width >> mip));
        height = Math.Min(height, Math.Max(1, view.Texture.Description.Height >> mip));
        layers = Math.Min(layers, view.Description.Dimension == TextureDimension.Texture3D ? 1 : Math.Max(1, view.Description.ArraySize));
    }

    private void BeginRenderingIfNeeded()
    {
        if (_renderingActive || !_renderingRequested)
            return;

        var attachments = stackalloc VkRenderingAttachmentInfo[Math.Max(1, _colorCount)];

        // The area and layers every attachment has: a view of a smaller mip (the specular prefilter renders
        // into each in turn) is smaller than its image, and a view of a cube map's faces has six layers the
        // vertex shader picks from. Vulkan rejects an area larger than an attachment, and draws to a layer
        // beyond layerCount go nowhere.
        var width = int.MaxValue;
        var height = int.MaxValue;
        var layers = int.MaxValue;

        for (var i = 0; i < _colorCount; i++)
        {
            var view = _colorTargets[i];
            // Read as well as write: the attachment loads rather than clears (below), so the pass reads what is
            // already there, and a barrier that only grants write access leaves that read unsynchronized.
            VulkanBarriers.TransitionImage(backend, _commandBuffer, view.Texture, VkImageLayout.ColorAttachmentOptimal,
                                           VkPipelineStageFlags2.ColorAttachmentOutput,
                                           VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ColorAttachmentWrite);

            attachments[i] = new VkRenderingAttachmentInfo
                                 {
                                     imageView = view.View,
                                     imageLayout = VkImageLayout.ColorAttachmentOptimal,

                                     // Load, never clear: a clear is its own call in this API, as it is in D3D11.
                                     loadOp = VkAttachmentLoadOp.Load,
                                     storeOp = VkAttachmentStoreOp.Store,
                                 };

            Fit(view, ref width, ref height, ref layers);
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

            Fit(_depthTarget, ref width, ref height, ref layers);
        }

        if (width == int.MaxValue)
            width = height = layers = 1;

        VkRenderingInfo renderingInfo = new()
                                            {
                                                renderArea = new VkRect2D(0, 0, (uint)width, (uint)height),
                                                layerCount = (uint)layers,
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

        // Sized for all sets at once and reused per set; every entry used is rewritten below.
        var maxWriteCount = _pipeline.Declared.Count;
        var writes = stackalloc VkWriteDescriptorSet[maxWriteCount];
        var bufferInfos = stackalloc VkDescriptorBufferInfo[maxWriteCount];
        var imageInfos = stackalloc VkDescriptorImageInfo[maxWriteCount];

        foreach (var setGroup in _pipeline.Declared.GroupBy(entry => entry.Key.Set))
        {
            var declared = setGroup.ToArray();
            var writeCount = 0;

            for (var i = 0; i < declared.Length; i++)
            {
                var (key, type) = (declared[i].Key, declared[i].Value);
                var binding = FindBinding(key.Slot);

                writes[writeCount] = new VkWriteDescriptorSet
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

                        bufferInfos[writeCount] = buffer == null || buffer.Buffer.IsNull
                                             ? new VkDescriptorBufferInfo { buffer = nulls.Buffer, offset = 0, range = VK_WHOLE_SIZE }
                                             : new VkDescriptorBufferInfo
                                                   {
                                                       buffer = buffer.Buffer,
                                                       offset = buffer.CurrentOffset + (ulong)(binding?.BufferOffset ?? 0),
                                                       range = size > 0 ? (ulong)size : (ulong)buffer.Description.SizeInBytes,
                                                   };

                        writes[writeCount].pBufferInfo = &bufferInfos[writeCount];
                        break;
                    }

                    case VkDescriptorType.Sampler:
                    {
                        var sampler = binding?.Sampler as VulkanSampler;
                        imageInfos[writeCount] = new VkDescriptorImageInfo { sampler = sampler?.Sampler ?? nulls.Sampler };
                        writes[writeCount].pImageInfo = &imageInfos[writeCount];
                        break;
                    }

                    case VkDescriptorType.StorageImage:
                    {
                        var view = binding?.TextureView as VulkanTextureView;
                        imageInfos[writeCount] = new VkDescriptorImageInfo
                                            {
                                                imageView = view is { View.IsNull: false } ? view.View : nulls.StorageView,
                                                imageLayout = VkImageLayout.General,
                                            };

                        writes[writeCount].pImageInfo = &imageInfos[writeCount];
                        break;
                    }

                    default:
                    {
                        var view = binding?.TextureView as VulkanTextureView;
                        imageInfos[writeCount] = new VkDescriptorImageInfo
                                            {
                                                imageView = view is { View.IsNull: false } ? view.View : nulls.SampledView,
                                                imageLayout = VkImageLayout.ShaderReadOnlyOptimal,
                                            };

                        writes[writeCount].pImageInfo = &imageInfos[writeCount];
                        break;
                    }
                }

                // Not even the placeholder exists when the device ran out of memory before the first draw;
                // the slot then stays unwritten rather than handing the driver a null handle.
                var usable = type switch
                                 {
                                     VkDescriptorType.UniformBuffer or VkDescriptorType.StorageBuffer => !bufferInfos[writeCount].buffer.IsNull,
                                     VkDescriptorType.Sampler => !imageInfos[writeCount].sampler.IsNull,
                                     _ => !imageInfos[writeCount].imageView.IsNull,
                                 };

                if (usable)
                    writeCount++;
            }

            if (backend.UsesPushDescriptors)
            {
                if (writeCount > 0)
                {
                    backend.Api.vkCmdPushDescriptorSetKHR(_commandBuffer, bindPoint, _pipeline.Layout, (uint)setGroup.Key,
                                                          (uint)writeCount, writes);
                }

                continue;
            }

            var set = backend.AllocateDescriptorSet(_pipeline.SetLayouts[setGroup.Key]);
            for (var i = 0; i < writeCount; i++)
            {
                writes[i].dstSet = set;
            }

            backend.Api.vkUpdateDescriptorSets((uint)writeCount, writes, 0, null);
            backend.Api.vkCmdBindDescriptorSets(_commandBuffer, bindPoint, _pipeline.Layout, (uint)setGroup.Key, 1, &set, 0, null);
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

    /** Vertex and index buffers are passed to the draw rather than bound to the pipeline, so they need their own barriers. */
    private bool InputBuffersNeedBarriers()
    {
        for (var i = 0; i < _vertexBufferCount; i++)
        {
            if (_vertexBuffers[i].Buffer is VulkanBuffer buffer && VulkanBarriers.NeedsBarrier(buffer, VkAccessFlags2.VertexAttributeRead))
                return true;
        }

        return _indexBuffer != null && VulkanBarriers.NeedsBarrier(_indexBuffer, VkAccessFlags2.IndexRead);
    }

    private void BarrierInputBuffers()
    {
        for (var i = 0; i < _vertexBufferCount; i++)
        {
            if (_vertexBuffers[i].Buffer is VulkanBuffer buffer)
                VulkanBarriers.BarrierBuffer(backend, _commandBuffer, buffer, VkPipelineStageFlags2.VertexAttributeInput,
                                             VkAccessFlags2.VertexAttributeRead);
        }

        if (_indexBuffer != null)
            VulkanBarriers.BarrierBuffer(backend, _commandBuffer, _indexBuffer, VkPipelineStageFlags2.IndexInput, VkAccessFlags2.IndexRead);
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
        return ClampScissor((int)_viewport.x, (int)(_viewport.y - height), (int)_viewport.width, height);
    }

    /// <summary>
    /// Staging for one texture upload. The copy runs when the frame is submitted, so every upload of a frame needs
    /// a buffer of its own, and a frame slot reuses its buffers only once its fence has been waited on.
    /// </summary>
    private VulkanBuffer? GetUploadBuffer(int size)
    {
        var pool = _uploadBuffers[_frameIndex];
        var index = _uploadBuffersUsed[_frameIndex];

        if (index < pool.Count && pool[index].Description.SizeInBytes >= size)
        {
            _uploadBuffersUsed[_frameIndex] = index + 1;
            return pool[index];
        }

        if (backend.CreateBuffer(new GpuBufferDescription
                                     {
                                         SizeInBytes = Math.Max(256, size),
                                         Usage = BufferUsage.CopySource,
                                         Memory = MemoryKind.Upload,
                                     },
                                 ReadOnlySpan<byte>.Empty, "texture upload") is not VulkanBuffer buffer)
            return null;

        if (index < pool.Count)
        {
            pool[index].Dispose();
            pool[index] = buffer;
        }
        else
        {
            pool.Add(buffer);
        }

        _uploadBuffersUsed[_frameIndex] = index + 1;
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

    private readonly record struct StageRange(int First, int Last);

    private static readonly StageRange GraphicsStages = new((int)ShaderStage.Vertex, (int)ShaderStage.Geometry);
    private static readonly StageRange ComputeStages = new((int)ShaderStage.Compute, (int)ShaderStage.Compute);
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
    private readonly List<VulkanBuffer>[] _uploadBuffers = [[], []];
    private readonly int[] _uploadBuffersUsed = new int[VulkanBackend.FramesInFlight];

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
