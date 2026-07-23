// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

using Prowl.Runtime.RHI;
using Prowl.Runtime.RHI.Shaders;

using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using VkCommandBuffer = Silk.NET.Vulkan.CommandBuffer;

namespace Prowl.Runtime.Backends.Vulkan;

/// <summary>
/// Vulkan <see cref="IGraphicsDevice"/> with instance/device/swapchain ownership and
/// per-submit command translation via <see cref="VulkanCommandTranslator"/>.
/// </summary>
public sealed unsafe class VulkanGraphicsDevice : IGraphicsDevice
{
    public const int MaxFramesInFlight = 2;

    private readonly GraphicsDeviceOptions _options;
    private readonly Vk _vk;
    private readonly object _gate = new();

    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private Queue _presentQueue;
    private uint _graphicsQueueFamily;
    private uint _presentQueueFamily;
    private bool _queuesShared;

    private KhrSurface? _khrSurface;
    private KhrSwapchain? _khrSwapchain;
    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;

    private SurfaceKHR _surface;
    private SwapchainKHR _swapchain;
    private Format _swapchainFormat;
    private Extent2D _swapchainExtent;
    private Image[] _swapchainImages = Array.Empty<Image>();
    private ImageView[] _swapchainImageViews = Array.Empty<ImageView>();
    private Framebuffer[] _swapchainFramebuffers = Array.Empty<Framebuffer>();
    private RenderPass _swapchainRenderPass;
    private Image _headlessImage;
    private DeviceMemory _headlessMemory;
    private ImageView _headlessView;
    private Framebuffer _headlessFramebuffer;
    private RenderPass _headlessRenderPass;

    private CommandPool _commandPool;
    private DescriptorPool _descriptorPool;
    private readonly VkCommandBuffer[] _frameCommandBuffers = new VkCommandBuffer[MaxFramesInFlight];
    private readonly Fence[] _inFlightFences = new Fence[MaxFramesInFlight];
    private readonly Semaphore[] _imageAvailable = new Semaphore[MaxFramesInFlight];
    private readonly Semaphore[] _renderFinished = new Semaphore[MaxFramesInFlight];

    private IWindowSurface? _windowSurface;
    private GraphicsDeviceCapabilities _capabilities = new() { BackendName = "Vulkan" };
    private VulkanCommandTranslator? _translator;
    private bool _initialized;
    private bool _shutdown;
    private int _currentFrame;
    private uint _currentImageIndex;
    private bool _frameBegun;
    private ulong _fenceValue;
    private uint _nextHandle = 1;

    private readonly Dictionary<uint, VkBufferResource> _buffers = new();
    private readonly Dictionary<uint, VkImageResource> _images = new();
    private readonly Dictionary<uint, VkFramebufferResource> _framebuffers = new();
    private readonly Dictionary<uint, VkVertexArrayResource> _vertexArrays = new();
    private readonly Dictionary<uint, VkShaderResource> _shaders = new();
    private readonly Dictionary<int, VkShaderLayoutResource> _shaderLayouts = new();
    private readonly Dictionary<int, VkShaderModuleResource> _shaderModules = new();
    private readonly Dictionary<VkGraphicsPipelineKey, Pipeline> _graphicsPipelines = new();
    private readonly Dictionary<VkRenderTargetFormats, RenderPass> _pipelineRenderPasses = new();

    public VulkanGraphicsDevice(GraphicsDeviceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _vk = Vk.GetApi();

        try
        {
            CreateInstance();
        }
        catch (Exception ex)
        {
            _vk.Dispose();
            throw new InvalidOperationException(
                "Failed to create Vulkan instance. Vulkan is unavailable on this machine (missing ICD/driver or loader).",
                ex);
        }
    }

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;
    public GraphicsDeviceCapabilities Capabilities => _capabilities;
    public bool IsInitialized => _initialized;

    internal Vk Vk => _vk;
    internal Device Device => _device;
    internal PhysicalDevice PhysicalDevice => _physicalDevice;
    internal Queue GraphicsQueue => _graphicsQueue;
    internal uint GraphicsQueueFamily => _graphicsQueueFamily;
    internal CommandPool CommandPool => _commandPool;
    internal Format SwapchainFormat => _swapchainFormat;
    internal Extent2D SwapchainExtent => _swapchainExtent;
    internal RenderPass SwapchainRenderPass => _swapchainRenderPass;
    internal Framebuffer CurrentSwapchainFramebuffer =>
        _swapchainFramebuffers.Length > 0 ? _swapchainFramebuffers[_currentImageIndex] : default;
    internal Image CurrentSwapchainImage =>
        _swapchainImages.Length > 0 ? _swapchainImages[_currentImageIndex] : default;
    internal Image CurrentColorImage => HasSwapchain ? CurrentSwapchainImage : _headlessImage;
    internal bool HasSwapchain => _swapchain.Handle != 0;
    internal Format CurrentColorFormat => HasSwapchain ? _swapchainFormat : Format.R8G8B8A8Unorm;
    internal RenderPass CurrentRenderPass => HasSwapchain ? _swapchainRenderPass : _headlessRenderPass;
    internal Framebuffer CurrentFramebuffer => HasSwapchain ? CurrentSwapchainFramebuffer : _headlessFramebuffer;
    internal Extent2D CurrentRenderExtent => HasSwapchain ? _swapchainExtent : new Extent2D(1, 1);

    internal Dictionary<uint, VkBufferResource> Buffers => _buffers;
    internal ulong UniformBufferOffsetAlignment { get; private set; } = 1;
    internal Dictionary<uint, VkImageResource> Images => _images;
    internal int PendingSamplerRetirementCount
    {
        get
        {
            lock (_gate)
            {
                int count = _unfencedSamplerRetire.Count;
                for (int i = 0; i < _pendingRetire.Count; i++)
                    count += _pendingRetire[i].Samplers.Count;
                return count;
            }
        }
    }
    internal Dictionary<uint, VkFramebufferResource> Framebuffers => _framebuffers;
    internal Dictionary<uint, VkVertexArrayResource> VertexArrays => _vertexArrays;
    internal Dictionary<uint, VkShaderResource> Shaders => _shaders;

    internal uint AllocateHandle() => System.Threading.Interlocked.Increment(ref _nextHandle) - 1;

    public void Initialize(IWindowSurface? surface)
    {
        if (_shutdown)
            throw new ObjectDisposedException(nameof(VulkanGraphicsDevice));
        if (_initialized)
            return;

        _windowSurface = surface;
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateCommandPoolAndSync();
        CreateDescriptorPool();

        if (surface != null)
        {
            nint surfaceHandle = surface.CreateVulkanSurface((nint)_instance.Handle);
            if (surfaceHandle == nint.Zero)
            {
                throw new InvalidOperationException(
                    "Vulkan surface creation failed. Ensure the window was created with a Vulkan-capable context (IWindow.VkSurface).");
            }

            _surface = new SurfaceKHR((ulong)surfaceHandle);
            RecreateSwapchain();
        }
        else
        {
            CreateHeadlessRenderTarget();
        }

        QueryCapabilities();
        _translator = new VulkanCommandTranslator(this);
        _initialized = true;
    }

    public void Shutdown()
    {
        if (_shutdown)
            return;
        _shutdown = true;

        if (_device.Handle != 0)
            _vk.DeviceWaitIdle(_device);

        DestroySwapchain();
        DestroyHeadlessRenderTarget();
        DestroySyncAndPool();
        DestroyDescriptorPool();

        foreach (var kv in _buffers) DestroyBuffer(kv.Value);
        _buffers.Clear();
        foreach (var kv in _images) DestroyImage(kv.Value);
        _images.Clear();
        foreach (var kv in _framebuffers) DestroyFramebuffer(kv.Value);
        _framebuffers.Clear();
        _vertexArrays.Clear();
        foreach (var kv in _shaders) DestroyShader(kv.Value);
        _shaders.Clear();
        foreach (var kv in _graphicsPipelines)
            _vk.DestroyPipeline(_device, kv.Value, null);
        _graphicsPipelines.Clear();
        foreach (var kv in _shaderLayouts) DestroyShaderLayout(kv.Value);
        _shaderLayouts.Clear();
        foreach (var kv in _shaderModules) DestroyShaderModules(kv.Value);
        _shaderModules.Clear();
        foreach (var kv in _pipelineRenderPasses)
            _vk.DestroyRenderPass(_device, kv.Value, null);
        _pipelineRenderPasses.Clear();

        if (_device.Handle != 0)
        {
            _vk.DestroyDevice(_device, null);
            _device = default;
        }

        if (_debugMessenger.Handle != 0 && _debugUtils != null)
        {
            _debugUtils.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
            _debugMessenger = default;
        }

        _khrSwapchain?.Dispose();
        _khrSwapchain = null;
        _khrSurface?.Dispose();
        _khrSurface = null;
        _debugUtils?.Dispose();
        _debugUtils = null;

        if (_surface.Handle != 0 && _khrSurface != null)
        {
            // Surface already destroyed with swapchain path; ensure cleanup if not.
        }

        if (_surface.Handle != 0)
        {
            // Need KhrSurface to destroy — recreate extension briefly if disposed.
            if (_vk.TryGetInstanceExtension(_instance, out KhrSurface khr))
            {
                khr.DestroySurface(_instance, _surface, null);
                khr.Dispose();
            }
            _surface = default;
        }

        if (_instance.Handle != 0)
        {
            _vk.DestroyInstance(_instance, null);
            _instance = default;
        }

        _vk.Dispose();
        _initialized = false;
    }

    public void BeginFrame()
    {
        EnsureInitialized();
        if (!HasSwapchain)
        {
            _frameBegun = true;
            return;
        }

        Fence fence = _inFlightFences[_currentFrame];
        _vk.WaitForFences(_device, 1, &fence, true, ulong.MaxValue);

        Result acquire = _khrSwapchain!.AcquireNextImage(
            _device, _swapchain, ulong.MaxValue, _imageAvailable[_currentFrame], default, ref _currentImageIndex);

        if (acquire == Result.ErrorOutOfDateKhr || acquire == Result.SuboptimalKhr)
        {
            RecreateSwapchain();
            acquire = _khrSwapchain.AcquireNextImage(
                _device, _swapchain, ulong.MaxValue, _imageAvailable[_currentFrame], default, ref _currentImageIndex);
        }

        if (acquire != Result.Success && acquire != Result.SuboptimalKhr)
            throw new InvalidOperationException($"vkAcquireNextImageKHR failed: {acquire}");

        _vk.ResetFences(_device, 1, &fence);
        _frameBegun = true;
    }

    public void EndFrame()
    {
        EnsureInitialized();
        if (!_frameBegun)
            return;
        _frameBegun = false;

        if (!HasSwapchain)
        {
            _fenceValue++;
            return;
        }

        // Present the acquired image. Drawing CBs are submitted from Execute.
        var waitSem = _renderFinished[_currentFrame];
        // If nothing drew this frame, signal render-finished ourselves with an empty submit.
        // Present still needs a wait semaphore that has been signaled.
        SubmitEmpty(_imageAvailable[_currentFrame], waitSem, _inFlightFences[_currentFrame]);

        var swapchain = _swapchain;
        uint imageIndex = _currentImageIndex;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSem,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex,
        };

        Result present = _khrSwapchain!.QueuePresent(_presentQueue, &presentInfo);
        if (present == Result.ErrorOutOfDateKhr || present == Result.SuboptimalKhr)
            RecreateSwapchain();
        else if (present != Result.Success)
            Debug.LogWarning($"vkQueuePresentKHR failed: {present}");

        _currentFrame = (_currentFrame + 1) % MaxFramesInFlight;
        _fenceValue++;
    }

    public void Execute(global::Prowl.Runtime.CommandBuffer commandBuffer, bool wait)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        if (commandBuffer._inPool)
            throw new InvalidOperationException("CommandBuffer has already been submitted (it's in the pool).");
        EnsureInitialized();

        commandBuffer._submitted = true;
        commandBuffer._ownerReleased = true;

        lock (_gate)
        {
            VkCommandBuffer vkCmd = AllocateTransientCommandBuffer();
            List<DescriptorSet> descriptorSets = RentDescriptorSetList();
            List<Sampler> retiredSamplers = RentSamplerList();
            List<VkBufferResource> transientBuffers = RentTransientBufferList();
            Fence fence = default;
            bool submitted = false;
            var begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            try
            {
                Check(_vk.BeginCommandBuffer(vkCmd, &begin), "vkBeginCommandBuffer");
                try
                {
                    _translator!.Translate(commandBuffer, vkCmd, descriptorSets, retiredSamplers, transientBuffers);
                }
                finally
                {
                    Check(_vk.EndCommandBuffer(vkCmd), "vkEndCommandBuffer");
                }

                fence = CreateFence(signaled: false);
                var submit = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = &vkCmd,
                };
                Check(_vk.QueueSubmit(_graphicsQueue, 1, &submit, fence), "vkQueueSubmit");
                submitted = true;

                if (wait)
                {
                    _vk.WaitForFences(_device, 1, &fence, true, ulong.MaxValue);
                    _vk.DestroyFence(_device, fence, null);
                    FreeTransientCommandBuffer(vkCmd);
                    FreeDescriptorSets(descriptorSets);
                    ReturnDescriptorSetList(descriptorSets);
                    DestroySamplers(retiredSamplers);
                    ReturnSamplerList(retiredSamplers);
                    DestroyTransientBuffers(transientBuffers);
                    ReturnTransientBufferList(transientBuffers);
                    _fenceValue++;
                }
                else
                {
                    // Retire asynchronously on next WaitIdle / EndFrame wait.
                    _pendingRetire.Add((vkCmd, fence, descriptorSets, retiredSamplers, transientBuffers));
                }
            }
            catch
            {
                if (!submitted)
                {
                    if (fence.Handle != 0)
                        _vk.DestroyFence(_device, fence, null);
                    FreeTransientCommandBuffer(vkCmd);
                    FreeDescriptorSets(descriptorSets);
                    ReturnDescriptorSetList(descriptorSets);
                    _unfencedSamplerRetire.AddRange(retiredSamplers);
                    ReturnSamplerList(retiredSamplers);
                    DestroyTransientBuffers(transientBuffers);
                    ReturnTransientBufferList(transientBuffers);
                }
                throw;
            }
        }

        CommandBufferPool.Return(commandBuffer);
    }

    private readonly List<(VkCommandBuffer Cmd, Fence Fence, List<DescriptorSet> DescriptorSets, List<Sampler> Samplers, List<VkBufferResource> Buffers)> _pendingRetire = new();
    private readonly List<Sampler> _unfencedSamplerRetire = new();
    private readonly Stack<List<DescriptorSet>> _descriptorSetListPool = new();
    private readonly Stack<List<Sampler>> _samplerListPool = new();
    private readonly Stack<List<VkBufferResource>> _transientBufferListPool = new();

    public void WaitIdle()
    {
        if (!_initialized || _device.Handle == 0)
            return;
        _vk.DeviceWaitIdle(_device);
        RetirePending();
    }

    public ulong GetFenceValue() => _fenceValue;

    public void WaitFence(ulong fenceValue)
    {
        if (fenceValue > _fenceValue)
            WaitIdle();
    }

    public void Dispose() => Shutdown();

    // ─────────────────────── Resource helpers used by translator ───────────────────────

    internal uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out PhysicalDeviceMemoryProperties memProps);
        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                return i;
        }
        throw new InvalidOperationException("Failed to find suitable Vulkan memory type.");
    }

    internal void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags memProps,
        out Buffer buffer, out DeviceMemory memory)
    {
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, &info, null, out buffer), "vkCreateBuffer");
        _vk.GetBufferMemoryRequirements(_device, buffer, out MemoryRequirements reqs);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = reqs.Size,
            MemoryTypeIndex = FindMemoryType(reqs.MemoryTypeBits, memProps),
        };
        Check(_vk.AllocateMemory(_device, &alloc, null, out memory), "vkAllocateMemory");
        Check(_vk.BindBufferMemory(_device, buffer, memory, 0), "vkBindBufferMemory");
    }

    internal void UploadTexture2D(
        VkImageResource resource,
        ReadOnlySpan<byte> data,
        uint width,
        uint height,
        int bytesPerPixel) =>
        UploadTexture(resource, data, width, height, 1, bytesPerPixel);

    internal void UploadTexture3D(
        VkImageResource resource,
        ReadOnlySpan<byte> data,
        uint width,
        uint height,
        uint depth,
        int bytesPerPixel) =>
        UploadTexture(resource, data, width, height, depth, bytesPerPixel);

    internal void UploadTextureCubeFace(
        VkImageResource resource,
        ReadOnlySpan<byte> data,
        uint size,
        uint face,
        uint mipLevel,
        ImageLayout oldLayout,
        int bytesPerPixel) =>
        UploadTexture(resource, data, size, size, 1, bytesPerPixel, face, mipLevel, oldLayout, setResourceLayout: false);

    private void UploadTexture(
        VkImageResource resource,
        ReadOnlySpan<byte> data,
        uint width,
        uint height,
        uint depth,
        int bytesPerPixel,
        uint baseArrayLayer = 0,
        uint mipLevel = 0,
        ImageLayout oldLayout = ImageLayout.Undefined,
        bool setResourceLayout = true)
    {
        int expectedSize = checked((int)(width * height * depth * (uint)bytesPerPixel));
        if (data.Length != expectedSize)
            throw new ArgumentException($"Vulkan texture upload expected {expectedSize} bytes, got {data.Length}.", nameof(data));

        CreateBuffer(
            (ulong)data.Length,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out Buffer stagingBuffer,
            out DeviceMemory stagingMemory);
        try
        {
            void* mapped;
            Check(_vk.MapMemory(_device, stagingMemory, 0, (ulong)data.Length, 0, &mapped), "vkMapMemory");
            try
            {
                fixed (byte* source = data)
                    System.Buffer.MemoryCopy(source, mapped, data.Length, data.Length);
            }
            finally
            {
                _vk.UnmapMemory(_device, stagingMemory);
            }

            VkCommandBuffer commandBuffer = AllocateTransientCommandBuffer();
            Fence fence = default;
            try
            {
                var begin = new CommandBufferBeginInfo
                {
                    SType = StructureType.CommandBufferBeginInfo,
                    Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
                };
                Check(_vk.BeginCommandBuffer(commandBuffer, &begin), "vkBeginCommandBuffer");

                ImageMemoryBarrier toTransfer = CreateImageBarrier(
                    resource.Image,
                    oldLayout,
                    ImageLayout.TransferDstOptimal,
                    oldLayout == ImageLayout.ShaderReadOnlyOptimal ? AccessFlags.ShaderReadBit : 0,
                    AccessFlags.TransferWriteBit,
                    baseArrayLayer,
                    mipLevel);
                PipelineStageFlags sourceStage = oldLayout == ImageLayout.ShaderReadOnlyOptimal
                    ? PipelineStageFlags.FragmentShaderBit
                    : PipelineStageFlags.TopOfPipeBit;
                _vk.CmdPipelineBarrier(
                    commandBuffer,
                    sourceStage,
                    PipelineStageFlags.TransferBit,
                    0,
                    0,
                    null,
                    0,
                    null,
                    1,
                    &toTransfer);

                var copy = new BufferImageCopy
                {
                    ImageSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = mipLevel,
                        BaseArrayLayer = baseArrayLayer,
                        LayerCount = 1,
                    },
                    ImageExtent = new Extent3D(width, height, depth),
                };
                _vk.CmdCopyBufferToImage(
                    commandBuffer,
                    stagingBuffer,
                    resource.Image,
                    ImageLayout.TransferDstOptimal,
                    1,
                    &copy);

                ImageMemoryBarrier toShader = CreateImageBarrier(
                    resource.Image,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    AccessFlags.TransferWriteBit,
                    AccessFlags.ShaderReadBit,
                    baseArrayLayer,
                    mipLevel);
                _vk.CmdPipelineBarrier(
                    commandBuffer,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.FragmentShaderBit,
                    0,
                    0,
                    null,
                    0,
                    null,
                    1,
                    &toShader);
                Check(_vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

                fence = CreateFence(signaled: false);
                var submit = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = &commandBuffer,
                };
                Check(_vk.QueueSubmit(_graphicsQueue, 1, &submit, fence), "vkQueueSubmit");
                Check(_vk.WaitForFences(_device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");
                if (setResourceLayout)
                    resource.Layout = ImageLayout.ShaderReadOnlyOptimal;
            }
            finally
            {
                if (fence.Handle != 0)
                    _vk.DestroyFence(_device, fence, null);
                FreeTransientCommandBuffer(commandBuffer);
            }
        }
        finally
        {
            if (stagingBuffer.Handle != 0)
                _vk.DestroyBuffer(_device, stagingBuffer, null);
            if (stagingMemory.Handle != 0)
                _vk.FreeMemory(_device, stagingMemory, null);
        }
    }

    internal void InitializeImageForSampling(Image image, uint mipLevels, uint arrayLayers)
    {
        VkCommandBuffer commandBuffer = AllocateTransientCommandBuffer();
        Fence fence = default;
        try
        {
            var begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            Check(_vk.BeginCommandBuffer(commandBuffer, &begin), "vkBeginCommandBuffer");
            ImageMemoryBarrier barrier = CreateImageBarrier(
                image,
                ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal,
                0,
                AccessFlags.ShaderReadBit,
                0,
                0,
                mipLevels,
                arrayLayers);
            _vk.CmdPipelineBarrier(
                commandBuffer,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.FragmentShaderBit,
                0,
                0,
                null,
                0,
                null,
                1,
                &barrier);
            Check(_vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");
            fence = CreateFence(signaled: false);
            var submit = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };
            Check(_vk.QueueSubmit(_graphicsQueue, 1, &submit, fence), "vkQueueSubmit");
            Check(_vk.WaitForFences(_device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");
        }
        finally
        {
            if (fence.Handle != 0)
                _vk.DestroyFence(_device, fence, null);
            FreeTransientCommandBuffer(commandBuffer);
        }
    }

    internal byte[] ReadTexture2D(VkImageResource resource, int bytesPerPixel) =>
        ReadTextureSubresource(resource, resource.Width, resource.Height, 0, 0, bytesPerPixel);

    internal byte[] ReadTextureCubeFace(VkImageResource resource, uint face, uint mipLevel, int bytesPerPixel)
    {
        if (face >= 6)
            throw new ArgumentOutOfRangeException(nameof(face));
        if (mipLevel >= resource.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        uint width = Math.Max(1u, resource.Width >> checked((int)mipLevel));
        uint height = Math.Max(1u, resource.Height >> checked((int)mipLevel));
        return ReadTextureSubresource(resource, width, height, face, mipLevel, bytesPerPixel);
    }

    private byte[] ReadTextureSubresource(
        VkImageResource resource,
        uint width,
        uint height,
        uint arrayLayer,
        uint mipLevel,
        int bytesPerPixel)
    {
        int byteCount = checked((int)(width * height * (uint)bytesPerPixel));
        CreateBuffer(
            (ulong)byteCount,
            BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out Buffer readbackBuffer,
            out DeviceMemory readbackMemory);
        try
        {
            VkCommandBuffer commandBuffer = AllocateTransientCommandBuffer();
            Fence fence = default;
            try
            {
                var begin = new CommandBufferBeginInfo
                {
                    SType = StructureType.CommandBufferBeginInfo,
                    Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
                };
                Check(_vk.BeginCommandBuffer(commandBuffer, &begin), "vkBeginCommandBuffer");
                ImageMemoryBarrier toTransfer = CreateImageBarrier(
                    resource.Image,
                    resource.Layout,
                    ImageLayout.TransferSrcOptimal,
                    AccessFlags.ShaderReadBit,
                    AccessFlags.TransferReadBit,
                    arrayLayer,
                    mipLevel);
                _vk.CmdPipelineBarrier(
                    commandBuffer,
                    PipelineStageFlags.FragmentShaderBit,
                    PipelineStageFlags.TransferBit,
                    0,
                    0,
                    null,
                    0,
                    null,
                    1,
                    &toTransfer);

                var copy = new BufferImageCopy
                {
                    ImageSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = mipLevel,
                        BaseArrayLayer = arrayLayer,
                        LayerCount = 1,
                    },
                    ImageExtent = new Extent3D(width, height, 1),
                };
                _vk.CmdCopyImageToBuffer(
                    commandBuffer,
                    resource.Image,
                    ImageLayout.TransferSrcOptimal,
                    readbackBuffer,
                    1,
                    &copy);

                ImageMemoryBarrier toShader = CreateImageBarrier(
                    resource.Image,
                    ImageLayout.TransferSrcOptimal,
                    resource.Layout,
                    AccessFlags.TransferReadBit,
                    AccessFlags.ShaderReadBit,
                    arrayLayer,
                    mipLevel);
                _vk.CmdPipelineBarrier(
                    commandBuffer,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.FragmentShaderBit,
                    0,
                    0,
                    null,
                    0,
                    null,
                    1,
                    &toShader);
                Check(_vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

                fence = CreateFence(signaled: false);
                var submit = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = &commandBuffer,
                };
                Check(_vk.QueueSubmit(_graphicsQueue, 1, &submit, fence), "vkQueueSubmit");
                Check(_vk.WaitForFences(_device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");
            }
            finally
            {
                if (fence.Handle != 0)
                    _vk.DestroyFence(_device, fence, null);
                FreeTransientCommandBuffer(commandBuffer);
            }

            var result = new byte[byteCount];
            void* mapped;
            Check(_vk.MapMemory(_device, readbackMemory, 0, (ulong)byteCount, 0, &mapped), "vkMapMemory");
            try
            {
                fixed (byte* destination = result)
                    System.Buffer.MemoryCopy(mapped, destination, byteCount, byteCount);
            }
            finally
            {
                _vk.UnmapMemory(_device, readbackMemory);
            }
            return result;
        }
        finally
        {
            if (readbackBuffer.Handle != 0)
                _vk.DestroyBuffer(_device, readbackBuffer, null);
            if (readbackMemory.Handle != 0)
                _vk.FreeMemory(_device, readbackMemory, null);
        }
    }

    private static ImageMemoryBarrier CreateImageBarrier(
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        AccessFlags sourceAccess,
        AccessFlags destinationAccess,
        uint baseArrayLayer = 0,
        uint baseMipLevel = 0,
        uint levelCount = 1,
        uint layerCount = 1) => new()
    {
        SType = StructureType.ImageMemoryBarrier,
        OldLayout = oldLayout,
        NewLayout = newLayout,
        SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
        DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
        Image = image,
        SrcAccessMask = sourceAccess,
        DstAccessMask = destinationAccess,
        SubresourceRange = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = baseMipLevel,
            BaseArrayLayer = baseArrayLayer,
            LevelCount = levelCount,
            LayerCount = layerCount,
        },
    };

    internal void DestroyBuffer(VkBufferResource res)
    {
        if (res.Buffer.Handle != 0) _vk.DestroyBuffer(_device, res.Buffer, null);
        if (res.Memory.Handle != 0) _vk.FreeMemory(_device, res.Memory, null);
    }

    internal void DestroyImage(VkImageResource res)
    {
        if (res.View.Handle != 0) _vk.DestroyImageView(_device, res.View, null);
        if (res.Image.Handle != 0) _vk.DestroyImage(_device, res.Image, null);
        if (res.Memory.Handle != 0) _vk.FreeMemory(_device, res.Memory, null);
        if (res.Sampler.Handle != 0) _vk.DestroySampler(_device, res.Sampler, null);
    }

    internal void DestroyFramebuffer(VkFramebufferResource res)
    {
        if (res.Framebuffer.Handle != 0) _vk.DestroyFramebuffer(_device, res.Framebuffer, null);
        for (int i = 0; i < res.AttachmentViews.Length; i++)
        {
            if (res.AttachmentViews[i].Handle != 0)
                _vk.DestroyImageView(_device, res.AttachmentViews[i], null);
        }
        if (res.RenderPass.Handle != 0) _vk.DestroyRenderPass(_device, res.RenderPass, null);
    }

    internal void DestroyShader(VkShaderResource res)
    {
        if (res.Pipeline.Handle != 0) _vk.DestroyPipeline(_device, res.Pipeline, null);
        if (res.PipelineLayout.Handle != 0) _vk.DestroyPipelineLayout(_device, res.PipelineLayout, null);
        if (res.VertModule.Handle != 0) _vk.DestroyShaderModule(_device, res.VertModule, null);
        if (res.FragModule.Handle != 0) _vk.DestroyShaderModule(_device, res.FragModule, null);
    }

    internal VkShaderLayoutResource GetOrCreateShaderLayout(ShaderVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (_shaderLayouts.TryGetValue(variant.Id, out VkShaderLayoutResource? cached))
            return cached;

        ShaderBindingLayout bindingLayout = variant.Bytecode?.BindingLayout ?? new ShaderBindingLayout();
        ShaderDescriptorLayoutPlan plan = ShaderDescriptorLayoutPlan.Create(bindingLayout);
        DescriptorSetLayoutBinding[] managedBindings = new DescriptorSetLayoutBinding[plan.Bindings.Length];
        for (int i = 0; i < plan.Bindings.Length; i++)
        {
            ShaderDescriptorBinding binding = plan.Bindings[i];
            managedBindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)binding.PhysicalBinding,
                DescriptorType = ToDescriptorType(binding.Slot.Kind),
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            };
        }

        DescriptorSetLayout descriptorSetLayout;
        fixed (DescriptorSetLayoutBinding* bindings = managedBindings)
        {
            var descriptorInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)managedBindings.Length,
                PBindings = bindings,
            };
            Check(
                _vk.CreateDescriptorSetLayout(_device, &descriptorInfo, null, out descriptorSetLayout),
                "vkCreateDescriptorSetLayout");
        }

        PipelineLayout pipelineLayout;
        try
        {
            var pipelineInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &descriptorSetLayout,
            };
            Check(
                _vk.CreatePipelineLayout(_device, &pipelineInfo, null, out pipelineLayout),
                "vkCreatePipelineLayout");
        }
        catch
        {
            _vk.DestroyDescriptorSetLayout(_device, descriptorSetLayout, null);
            throw;
        }

        var resource = new VkShaderLayoutResource
        {
            DescriptorSetLayout = descriptorSetLayout,
            PipelineLayout = pipelineLayout,
            Plan = plan,
        };
        _shaderLayouts.Add(variant.Id, resource);
        return resource;
    }

    internal DescriptorSet AllocateDescriptorSet(VkShaderLayoutResource layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        DescriptorSetLayout setLayout = layout.DescriptorSetLayout;
        var allocation = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        Check(_vk.AllocateDescriptorSets(_device, &allocation, out DescriptorSet descriptorSet), "vkAllocateDescriptorSets");
        return descriptorSet;
    }

    internal void FreeDescriptorSet(DescriptorSet descriptorSet)
    {
        if (descriptorSet.Handle == 0 || _descriptorPool.Handle == 0)
            return;
        Check(_vk.FreeDescriptorSets(_device, _descriptorPool, 1, &descriptorSet), "vkFreeDescriptorSets");
    }

    private List<DescriptorSet> RentDescriptorSetList() =>
        _descriptorSetListPool.Count > 0 ? _descriptorSetListPool.Pop() : new List<DescriptorSet>(8);

    private void ReturnDescriptorSetList(List<DescriptorSet> descriptorSets)
    {
        descriptorSets.Clear();
        _descriptorSetListPool.Push(descriptorSets);
    }

    private void FreeDescriptorSets(List<DescriptorSet> descriptorSets)
    {
        for (int i = 0; i < descriptorSets.Count; i++)
            FreeDescriptorSet(descriptorSets[i]);
    }

    private List<Sampler> RentSamplerList() =>
        _samplerListPool.Count > 0 ? _samplerListPool.Pop() : new List<Sampler>(4);

    private void ReturnSamplerList(List<Sampler> samplers)
    {
        samplers.Clear();
        _samplerListPool.Push(samplers);
    }

    private void DestroySamplers(List<Sampler> samplers)
    {
        for (int i = 0; i < samplers.Count; i++)
        {
            Sampler sampler = samplers[i];
            if (sampler.Handle != 0)
                _vk.DestroySampler(_device, sampler, null);
        }
    }

    internal VkShaderModuleResource GetOrCreateShaderModules(ShaderVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (_shaderModules.TryGetValue(variant.Id, out VkShaderModuleResource? cached))
            return cached;

        CompiledShaderBytecode bytecode = variant.Bytecode
            ?? throw new InvalidOperationException("Vulkan shader modules require compiled bytecode.");
        if (bytecode.Format != ShaderBytecodeFormat.SpirV)
            throw new InvalidOperationException($"Vulkan shader modules require SPIR-V, got {bytecode.Format}.");

        ShaderModule vertexModule = CreateShaderModule(bytecode.VertexBytecode, "vertex");
        ShaderModule fragmentModule;
        try
        {
            fragmentModule = CreateShaderModule(bytecode.FragmentBytecode, "fragment");
        }
        catch
        {
            _vk.DestroyShaderModule(_device, vertexModule, null);
            throw;
        }

        var resource = new VkShaderModuleResource
        {
            VertexModule = vertexModule,
            FragmentModule = fragmentModule,
        };
        _shaderModules.Add(variant.Id, resource);
        return resource;
    }

    internal Pipeline GetOrCreateGraphicsPipeline(
        GraphicsPipelineKey key,
        ShaderVariant variant,
        Format colorFormat)
    {
        return GetOrCreateGraphicsPipeline(key, variant, new VkColorAttachmentFormats(colorFormat));
    }

    internal Pipeline GetOrCreateGraphicsPipeline(
        GraphicsPipelineKey key,
        ShaderVariant variant,
        VkColorAttachmentFormats colorFormats)
        => GetOrCreateGraphicsPipeline(key, variant, new VkRenderTargetFormats(colorFormats, Format.Undefined));

    internal Pipeline GetOrCreateGraphicsPipeline(
        GraphicsPipelineKey key,
        ShaderVariant variant,
        VkRenderTargetFormats targetFormats)
    {
        var cacheKey = new VkGraphicsPipelineKey(key, targetFormats);
        if (_graphicsPipelines.TryGetValue(cacheKey, out Pipeline cached))
            return cached;

        RasterizerState raster = key.RasterState;
        if ((raster.DepthTest || raster.DepthWrite) && targetFormats.DepthFormat == Format.Undefined)
            throw new InvalidOperationException("Vulkan depth testing requires a depth framebuffer attachment.");
        if (raster.StencilEnabled && !VulkanFormats.HasStencil(targetFormats.DepthFormat))
            throw new InvalidOperationException("Vulkan stencil testing requires a stencil-capable framebuffer attachment.");

        VkShaderLayoutResource layout = GetOrCreateShaderLayout(variant);
        VkShaderModuleResource modules = GetOrCreateShaderModules(variant);
        RenderPass renderPass = GetOrCreatePipelineRenderPass(targetFormats);
        CreateVertexInputDescriptions(
            key.VertexArrayHandle,
            out VertexInputBindingDescription[] bindings,
            out VertexInputAttributeDescription[] attributes);
        byte* entryPoint = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            fixed (VertexInputBindingDescription* bindingPointer = bindings)
            fixed (VertexInputAttributeDescription* attributePointer = attributes)
            {
                PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
                stages[0] = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.VertexBit,
                    Module = modules.VertexModule,
                    PName = entryPoint,
                };
                stages[1] = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.FragmentBit,
                    Module = modules.FragmentModule,
                    PName = entryPoint,
                };
                var vertexInput = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = (uint)bindings.Length,
                    PVertexBindingDescriptions = bindingPointer,
                    VertexAttributeDescriptionCount = (uint)attributes.Length,
                    PVertexAttributeDescriptions = attributePointer,
                };
                var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = VulkanFormats.ToTopology(key.Topology),
                    PrimitiveRestartEnable = false,
                };
                var viewportState = new PipelineViewportStateCreateInfo
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    ScissorCount = 1,
                };
                var rasterization = new PipelineRasterizationStateCreateInfo
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = false,
                    RasterizerDiscardEnable = false,
                    PolygonMode = PolygonMode.Fill,
                    CullMode = VulkanFormats.ToCullMode(raster.CullFace),
                    FrontFace = VulkanFormats.ToFrontFace(raster.Winding),
                    DepthBiasEnable = false,
                    LineWidth = 1f,
                };
                var multisample = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    RasterizationSamples = SampleCountFlags.Count1Bit,
                    SampleShadingEnable = false,
                };
                PipelineColorBlendAttachmentState* colorAttachments = stackalloc PipelineColorBlendAttachmentState[targetFormats.ColorFormats.Count];
                for (int i = 0; i < targetFormats.ColorFormats.Count; i++)
                {
                    colorAttachments[i] = new PipelineColorBlendAttachmentState
                    {
                        BlendEnable = raster.DoBlend,
                        SrcColorBlendFactor = VulkanFormats.ToBlendFactor(raster.BlendSrc),
                        DstColorBlendFactor = VulkanFormats.ToBlendFactor(raster.BlendDst),
                        ColorBlendOp = VulkanFormats.ToBlendOp(raster.Blend),
                        SrcAlphaBlendFactor = VulkanFormats.ToBlendFactor(raster.BlendSrc),
                        DstAlphaBlendFactor = VulkanFormats.ToBlendFactor(raster.BlendDst),
                        AlphaBlendOp = VulkanFormats.ToBlendOp(raster.Blend),
                        ColorWriteMask = ColorComponentFlags.RBit |
                        ColorComponentFlags.GBit |
                        ColorComponentFlags.BBit |
                        ColorComponentFlags.ABit,
                    };
                }
                var colorBlend = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    LogicOpEnable = false,
                    AttachmentCount = checked((uint)targetFormats.ColorFormats.Count),
                    PAttachments = colorAttachments,
                };
                var stencil = new StencilOpState
                {
                    FailOp = VulkanFormats.ToStencilOp(raster.StencilFailOp),
                    PassOp = VulkanFormats.ToStencilOp(raster.StencilPassOp),
                    DepthFailOp = VulkanFormats.ToStencilOp(raster.StencilZFailOp),
                    CompareOp = VulkanFormats.ToCompareOp(raster.StencilFunc),
                    CompareMask = checked((uint)raster.StencilReadMask),
                    WriteMask = checked((uint)raster.StencilWriteMask),
                    Reference = checked((uint)raster.StencilRef),
                };
                var depthStencil = new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = raster.DepthTest,
                    DepthWriteEnable = raster.DepthWrite,
                    DepthCompareOp = VulkanFormats.ToCompareOp(raster.Depth),
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = raster.StencilEnabled,
                    Front = stencil,
                    Back = stencil,
                    MinDepthBounds = 0f,
                    MaxDepthBounds = 1f,
                };
                DynamicState* dynamicStates = stackalloc DynamicState[2]
                {
                    DynamicState.Viewport,
                    DynamicState.Scissor,
                };
                var dynamicState = new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = 2,
                    PDynamicStates = dynamicStates,
                };
                var createInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = stages,
                    PVertexInputState = &vertexInput,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterization,
                    PMultisampleState = &multisample,
                    PDepthStencilState = &depthStencil,
                    PColorBlendState = &colorBlend,
                    PDynamicState = &dynamicState,
                    Layout = layout.PipelineLayout,
                    RenderPass = renderPass,
                    Subpass = 0,
                    BasePipelineIndex = -1,
                };
                Check(
                    _vk.CreateGraphicsPipelines(_device, default, 1, &createInfo, null, out Pipeline pipeline),
                    "vkCreateGraphicsPipelines");
                _graphicsPipelines.Add(cacheKey, pipeline);
                return pipeline;
            }
        }
        finally
        {
            SilkMarshal.Free((nint)entryPoint);
        }
    }

    private void CreateVertexInputDescriptions(
        uint vertexArrayHandle,
        out VertexInputBindingDescription[] bindings,
        out VertexInputAttributeDescription[] attributes)
    {
        if (vertexArrayHandle == 0)
        {
            bindings = [];
            attributes = [];
            return;
        }
        if (!_vertexArrays.TryGetValue(vertexArrayHandle, out VkVertexArrayResource? vertexArray))
            throw new InvalidOperationException($"Vulkan PSO references unknown vertex array {vertexArrayHandle}.");
        VertexFormat? instanceFormat = vertexArray.InstanceFormat;
        bindings = instanceFormat == null
            ?
            [
                new VertexInputBindingDescription
                {
                    Binding = 0,
                    Stride = (uint)vertexArray.Format.Size,
                    InputRate = VertexInputRate.Vertex,
                },
            ]
            :
            [
                new VertexInputBindingDescription
                {
                    Binding = 0,
                    Stride = (uint)vertexArray.Format.Size,
                    InputRate = VertexInputRate.Vertex,
                },
                new VertexInputBindingDescription
                {
                    Binding = 1,
                    Stride = (uint)instanceFormat.Size,
                    InputRate = VertexInputRate.Instance,
                },
            ];
        VertexFormat.Element[] vertexElements = vertexArray.Format.Elements;
        VertexFormat.Element[] instanceElements = instanceFormat?.Elements ?? [];
        attributes = new VertexInputAttributeDescription[vertexElements.Length + instanceElements.Length];
        AddVertexInputAttributes(attributes, 0, vertexElements, 0, perInstance: false);
        AddVertexInputAttributes(attributes, vertexElements.Length, instanceElements, 1, perInstance: true);
    }

    private static void AddVertexInputAttributes(
        VertexInputAttributeDescription[] attributes,
        int destinationOffset,
        VertexFormat.Element[] elements,
        uint binding,
        bool perInstance)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            VertexFormat.Element element = elements[i];
            if (perInstance ? element.Divisor != 1 : element.Divisor != 0)
            {
                throw new NotSupportedException(perInstance
                    ? "Core Vulkan instanced input currently supports divisor 1 only."
                    : "Vulkan vertex-stream element cannot have a non-zero divisor.");
            }
            attributes[destinationOffset + i] = new VertexInputAttributeDescription
            {
                Location = element.Semantic,
                Binding = binding,
                Format = VulkanFormats.ToVertexFormat(element.Type, element.Count, element.Normalized),
                Offset = (uint)element.Offset,
            };
        }
    }

    private RenderPass GetOrCreatePipelineRenderPass(VkRenderTargetFormats targetFormats)
    {
        if (_pipelineRenderPasses.TryGetValue(targetFormats, out RenderPass cached))
            return cached;

        int attachmentCount = targetFormats.ColorFormats.Count + (targetFormats.DepthFormat == Format.Undefined ? 0 : 1);
        AttachmentDescription* attachments = stackalloc AttachmentDescription[attachmentCount];
        AttachmentReference* colorReferences = stackalloc AttachmentReference[targetFormats.ColorFormats.Count];
        for (int i = 0; i < targetFormats.ColorFormats.Count; i++)
        {
            attachments[i] = new AttachmentDescription
            {
                Format = targetFormats.ColorFormats[i],
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.DontCare,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.ColorAttachmentOptimal,
            };
            colorReferences[i] = new AttachmentReference
            {
                Attachment = checked((uint)i),
                Layout = ImageLayout.ColorAttachmentOptimal,
            };
        }
        AttachmentReference depthReference = default;
        if (targetFormats.DepthFormat != Format.Undefined)
        {
            int depthIndex = targetFormats.ColorFormats.Count;
            attachments[depthIndex] = new AttachmentDescription
            {
                Format = targetFormats.DepthFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Load,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.Load,
                StencilStoreOp = AttachmentStoreOp.Store,
                InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
            };
            depthReference = new AttachmentReference
            {
                Attachment = checked((uint)depthIndex),
                Layout = ImageLayout.DepthStencilAttachmentOptimal,
            };
        }
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = checked((uint)targetFormats.ColorFormats.Count),
            PColorAttachments = colorReferences,
            PDepthStencilAttachment = targetFormats.DepthFormat == Format.Undefined ? null : &depthReference,
        };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = checked((uint)attachmentCount),
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
        };
        Check(_vk.CreateRenderPass(_device, &createInfo, null, out RenderPass renderPass), "vkCreateRenderPass(pipeline)");
        _pipelineRenderPasses.Add(targetFormats, renderPass);
        return renderPass;
    }

    private void CreateHeadlessRenderTarget()
    {
        Format format = Format.R8G8B8A8Unorm;
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(1, 1, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, &imageInfo, null, out _headlessImage), "vkCreateImage(headless)");
        _vk.GetImageMemoryRequirements(_device, _headlessImage, out MemoryRequirements requirements);
        var allocation = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        Check(_vk.AllocateMemory(_device, &allocation, null, out _headlessMemory), "vkAllocateMemory(headless)");
        Check(_vk.BindImageMemory(_device, _headlessImage, _headlessMemory, 0), "vkBindImageMemory(headless)");
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _headlessImage,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LevelCount = 1,
                LayerCount = 1,
            },
        };
        Check(_vk.CreateImageView(_device, &viewInfo, null, out _headlessView), "vkCreateImageView(headless)");
        _headlessRenderPass = GetOrCreatePipelineRenderPass(new VkRenderTargetFormats(new VkColorAttachmentFormats(format), Format.Undefined));
        ImageView attachment = _headlessView;
        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _headlessRenderPass,
            AttachmentCount = 1,
            PAttachments = &attachment,
            Width = 1,
            Height = 1,
            Layers = 1,
        };
        Check(_vk.CreateFramebuffer(_device, &framebufferInfo, null, out _headlessFramebuffer), "vkCreateFramebuffer(headless)");
    }

    private void DestroyHeadlessRenderTarget()
    {
        if (_headlessFramebuffer.Handle != 0) _vk.DestroyFramebuffer(_device, _headlessFramebuffer, null);
        if (_headlessView.Handle != 0) _vk.DestroyImageView(_device, _headlessView, null);
        if (_headlessImage.Handle != 0) _vk.DestroyImage(_device, _headlessImage, null);
        if (_headlessMemory.Handle != 0) _vk.FreeMemory(_device, _headlessMemory, null);
        _headlessFramebuffer = default;
        _headlessView = default;
        _headlessImage = default;
        _headlessMemory = default;
        _headlessRenderPass = default;
    }

    private ShaderModule CreateShaderModule(byte[] bytecode, string stage)
    {
        if (bytecode.Length == 0 || (bytecode.Length & 3) != 0)
            throw new InvalidOperationException($"Vulkan {stage} SPIR-V bytecode must be non-empty and 4-byte aligned.");

        fixed (byte* bytes = bytecode)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)bytecode.Length,
                PCode = (uint*)bytes,
            };
            Check(
                _vk.CreateShaderModule(_device, &createInfo, null, out ShaderModule module),
                $"vkCreateShaderModule({stage})");
            return module;
        }
    }

    private void DestroyShaderModules(VkShaderModuleResource resource)
    {
        if (resource.VertexModule.Handle != 0)
            _vk.DestroyShaderModule(_device, resource.VertexModule, null);
        if (resource.FragmentModule.Handle != 0)
            _vk.DestroyShaderModule(_device, resource.FragmentModule, null);
    }

    private void DestroyShaderLayout(VkShaderLayoutResource resource)
    {
        if (resource.PipelineLayout.Handle != 0)
            _vk.DestroyPipelineLayout(_device, resource.PipelineLayout, null);
        if (resource.DescriptorSetLayout.Handle != 0)
            _vk.DestroyDescriptorSetLayout(_device, resource.DescriptorSetLayout, null);
    }

    private static DescriptorType ToDescriptorType(ShaderBindingKind kind) => kind switch
    {
        ShaderBindingKind.Buffer => DescriptorType.UniformBuffer,
        ShaderBindingKind.Texture => DescriptorType.SampledImage,
        ShaderBindingKind.Sampler => DescriptorType.Sampler,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // ─────────────────────── Init helpers ───────────────────────

    private void CreateInstance()
    {
        byte* appName = (byte*)SilkMarshal.StringToPtr("Prowl");
        byte* engName = (byte*)SilkMarshal.StringToPtr("Prowl");
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = appName,
            ApplicationVersion = Vk.MakeVersion(1, 0, 0),
            PEngineName = engName,
            EngineVersion = Vk.MakeVersion(1, 0, 0),
            ApiVersion = Vk.Version11,
        };

        var extensions = new List<string> { KhrSurface.ExtensionName };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            extensions.Add("VK_KHR_win32_surface");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            extensions.Add("VK_MVK_macos_surface");
        else
        {
            extensions.Add("VK_KHR_xcb_surface");
            extensions.Add("VK_KHR_wayland_surface");
        }

        bool wantDebug = _options.Debug || _options.EnableValidation;
        if (wantDebug)
            extensions.Add(ExtDebugUtils.ExtensionName);

        string[]? layers = wantDebug ? new[] { "VK_LAYER_KHRONOS_validation" } : null;
        if (layers != null && !SupportsLayer(layers[0]))
            layers = null;

        byte** extPtr = (byte**)SilkMarshal.StringArrayToPtr(extensions);
        byte** layerPtr = layers != null ? (byte**)SilkMarshal.StringArrayToPtr(layers) : null;

        var ci = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = (uint)extensions.Count,
            PpEnabledExtensionNames = extPtr,
            EnabledLayerCount = layers != null ? (uint)layers.Length : 0,
            PpEnabledLayerNames = layerPtr,
        };

        Result r = _vk.CreateInstance(&ci, null, out _instance);
        SilkMarshal.Free((nint)extPtr);
        if (layerPtr != null) SilkMarshal.Free((nint)layerPtr);
        SilkMarshal.Free((nint)appName);
        SilkMarshal.Free((nint)engName);

        if (r != Result.Success)
            throw new InvalidOperationException($"vkCreateInstance failed: {r}");

        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
            throw new InvalidOperationException("VK_KHR_surface is not available.");

        if (wantDebug && _vk.TryGetInstanceExtension(_instance, out _debugUtils))
            CreateDebugMessenger();
    }

    private bool SupportsLayer(string name)
    {
        uint count = 0;
        _vk.EnumerateInstanceLayerProperties(ref count, null);
        if (count == 0) return false;
        var props = new LayerProperties[count];
        fixed (LayerProperties* p = props)
            _vk.EnumerateInstanceLayerProperties(ref count, p);
        for (int i = 0; i < props.Length; i++)
        {
            fixed (LayerProperties* prop = &props[i])
            {
                string layer = Marshal.PtrToStringAnsi((nint)prop->LayerName) ?? "";
                if (layer == name) return true;
            }
        }
        return false;
    }

    private void CreateDebugMessenger()
    {
        var ci = new DebugUtilsMessengerCreateInfoEXT
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                              DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                          DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                          DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback,
        };
        _debugUtils!.CreateDebugUtilsMessenger(_instance, &ci, null, out _debugMessenger);
    }

    private static uint DebugCallback(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT types,
        DebugUtilsMessengerCallbackDataEXT* data,
        void* userData)
    {
        string msg = Marshal.PtrToStringAnsi((nint)data->PMessage) ?? "";
        if ((severity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0)
            Debug.LogError($"Vulkan: {msg}");
        else
            Debug.LogWarning($"Vulkan: {msg}");
        return Vk.False;
    }

    private void PickPhysicalDevice()
    {
        uint count = 0;
        _vk.EnumeratePhysicalDevices(_instance, ref count, null);
        if (count == 0)
            throw new InvalidOperationException("No Vulkan physical devices found.");

        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices)
            _vk.EnumeratePhysicalDevices(_instance, ref count, p);

        int preferred = _options.PreferredAdapterIndex;
        if (preferred >= 0 && preferred < devices.Length)
        {
            _physicalDevice = devices[preferred];
            return;
        }

        // Prefer discrete GPU.
        PhysicalDevice chosen = devices[0];
        foreach (PhysicalDevice pd in devices)
        {
            _vk.GetPhysicalDeviceProperties(pd, out PhysicalDeviceProperties props);
            if (props.DeviceType == PhysicalDeviceType.DiscreteGpu)
            {
                chosen = pd;
                break;
            }
        }
        _physicalDevice = chosen;
    }

    private void CreateLogicalDevice()
    {
        FindQueueFamilies(out _graphicsQueueFamily, out _presentQueueFamily);
        _queuesShared = _graphicsQueueFamily == _presentQueueFamily;

        var unique = new HashSet<uint> { _graphicsQueueFamily, _presentQueueFamily };
        float priority = 1f;
        var queueInfos = new DeviceQueueCreateInfo[unique.Count];
        int qi = 0;
        foreach (uint family in unique)
        {
            queueInfos[qi++] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = family,
                QueueCount = 1,
                PQueuePriorities = &priority,
            };
        }

        string[] deviceExts = [KhrSwapchain.ExtensionName];
        byte** extPtr = (byte**)SilkMarshal.StringArrayToPtr(deviceExts);

        var features = new PhysicalDeviceFeatures();
        fixed (DeviceQueueCreateInfo* qPtr = queueInfos)
        {
            var ci = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)queueInfos.Length,
                PQueueCreateInfos = qPtr,
                EnabledExtensionCount = (uint)deviceExts.Length,
                PpEnabledExtensionNames = extPtr,
                PEnabledFeatures = &features,
            };
            Check(_vk.CreateDevice(_physicalDevice, &ci, null, out _device), "vkCreateDevice");
        }
        SilkMarshal.Free((nint)extPtr);

        _vk.GetDeviceQueue(_device, _graphicsQueueFamily, 0, out _graphicsQueue);
        _vk.GetDeviceQueue(_device, _presentQueueFamily, 0, out _presentQueue);

        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
            throw new InvalidOperationException("VK_KHR_swapchain is not available.");
    }

    private void FindQueueFamilies(out uint graphics, out uint present)
    {
        graphics = present = uint.MaxValue;
        uint count = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, ref count, null);
        var props = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* p = props)
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, ref count, p);

        for (uint i = 0; i < count; i++)
        {
            if ((props[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
                graphics = i;

            Bool32 supported = false;
            if (_surface.Handle != 0 && _khrSurface != null)
                _khrSurface.GetPhysicalDeviceSurfaceSupport(_physicalDevice, i, _surface, &supported);
            else
                supported = true; // no surface yet — accept graphics family for present too

            if (supported)
                present = i;

            if (graphics != uint.MaxValue && present != uint.MaxValue)
                return;
        }

        if (graphics == uint.MaxValue)
            throw new InvalidOperationException("No graphics queue family on Vulkan device.");
        if (present == uint.MaxValue)
            present = graphics;
    }

    private void CreateCommandPoolAndSync()
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = _graphicsQueueFamily,
        };
        Check(_vk.CreateCommandPool(_device, &poolInfo, null, out _commandPool), "vkCreateCommandPool");

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            _inFlightFences[i] = CreateFence(signaled: true);
            _imageAvailable[i] = CreateSemaphore();
            _renderFinished[i] = CreateSemaphore();

            var alloc = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            fixed (VkCommandBuffer* cb = &_frameCommandBuffers[i])
                Check(_vk.AllocateCommandBuffers(_device, &alloc, cb), "vkAllocateCommandBuffers");
        }
    }

    private void CreateDescriptorPool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3]
        {
            new() { Type = DescriptorType.UniformBuffer, DescriptorCount = 1024 },
            new() { Type = DescriptorType.SampledImage, DescriptorCount = 1024 },
            new() { Type = DescriptorType.Sampler, DescriptorCount = 1024 },
        };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = 1024,
            PoolSizeCount = 3,
            PPoolSizes = sizes,
        };
        Check(_vk.CreateDescriptorPool(_device, &poolInfo, null, out _descriptorPool), "vkCreateDescriptorPool");
    }

    private void DestroyDescriptorPool()
    {
        if (_descriptorPool.Handle == 0 || _device.Handle == 0)
            return;
        _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        _descriptorPool = default;
    }

    private void DestroySyncAndPool()
    {
        if (_device.Handle == 0) return;
        RetirePending();
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            if (_inFlightFences[i].Handle != 0) _vk.DestroyFence(_device, _inFlightFences[i], null);
            if (_imageAvailable[i].Handle != 0) _vk.DestroySemaphore(_device, _imageAvailable[i], null);
            if (_renderFinished[i].Handle != 0) _vk.DestroySemaphore(_device, _renderFinished[i], null);
            _inFlightFences[i] = default;
            _imageAvailable[i] = default;
            _renderFinished[i] = default;
        }
        if (_commandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(_device, _commandPool, null);
            _commandPool = default;
        }
    }

    internal void RecreateSwapchain()
    {
        if (_surface.Handle == 0 || _khrSurface == null || _khrSwapchain == null)
            return;

        _vk.DeviceWaitIdle(_device);
        DestroySwapchain();

        // Re-resolve present family now that surface exists.
        FindQueueFamilies(out _graphicsQueueFamily, out _presentQueueFamily);
        _queuesShared = _graphicsQueueFamily == _presentQueueFamily;

        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out SurfaceCapabilitiesKHR caps);
        uint formatCount = 0;
        _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref formatCount, null);
        var formats = new SurfaceFormatKHR[Math.Max(formatCount, 1)];
        fixed (SurfaceFormatKHR* f = formats)
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref formatCount, f);

        SurfaceFormatKHR chosenFormat = formats[0];
        for (int i = 0; i < (int)formatCount; i++)
        {
            if (formats[i].Format == Format.B8G8R8A8Unorm || formats[i].Format == Format.R8G8B8A8Unorm)
            {
                chosenFormat = formats[i];
                break;
            }
        }
        _swapchainFormat = chosenFormat.Format;

        (int fbW, int fbH) = _windowSurface?.FramebufferSize ?? (0, 0);
        uint width = fbW > 0 ? (uint)fbW : caps.CurrentExtent.Width;
        uint height = fbH > 0 ? (uint)fbH : caps.CurrentExtent.Height;
        if (caps.CurrentExtent.Width != uint.MaxValue)
        {
            width = caps.CurrentExtent.Width;
            height = caps.CurrentExtent.Height;
        }
        width = Math.Clamp(width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width);
        height = Math.Clamp(height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height);
        _swapchainExtent = new Extent2D(width, height);

        uint imageCount = caps.MinImageCount + 1;
        if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
            imageCount = caps.MaxImageCount;

        var sci = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = _swapchainFormat,
            ImageColorSpace = chosenFormat.ColorSpace,
            ImageExtent = _swapchainExtent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit,
            PreTransform = caps.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = _options.VSync ? PresentModeKHR.FifoKhr : PresentModeKHR.MailboxKhr,
            Clipped = true,
        };

        if (!_queuesShared)
        {
            uint* families = stackalloc uint[] { _graphicsQueueFamily, _presentQueueFamily };
            sci.ImageSharingMode = SharingMode.Concurrent;
            sci.QueueFamilyIndexCount = 2;
            sci.PQueueFamilyIndices = families;
        }
        else
        {
            sci.ImageSharingMode = SharingMode.Exclusive;
        }

        Check(_khrSwapchain.CreateSwapchain(_device, &sci, null, out _swapchain), "vkCreateSwapchainKHR");

        uint actualCount = 0;
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, ref actualCount, null);
        _swapchainImages = new Image[actualCount];
        fixed (Image* imgs = _swapchainImages)
            _khrSwapchain.GetSwapchainImages(_device, _swapchain, ref actualCount, imgs);

        _swapchainImageViews = new ImageView[actualCount];
        for (int i = 0; i < actualCount; i++)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _swapchainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = _swapchainFormat,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    LevelCount = 1,
                    LayerCount = 1,
                },
            };
            Check(_vk.CreateImageView(_device, &viewInfo, null, out _swapchainImageViews[i]), "vkCreateImageView");
        }

        CreateSwapchainRenderPass();
        _swapchainFramebuffers = new Framebuffer[actualCount];
        for (int i = 0; i < actualCount; i++)
        {
            ImageView attachment = _swapchainImageViews[i];
            var fbInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _swapchainRenderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = _swapchainExtent.Width,
                Height = _swapchainExtent.Height,
                Layers = 1,
            };
            Check(_vk.CreateFramebuffer(_device, &fbInfo, null, out _swapchainFramebuffers[i]), "vkCreateFramebuffer");
        }
    }

    private void CreateSwapchainRenderPass()
    {
        var color = new AttachmentDescription
        {
            Format = _swapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr,
        };
        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
        };
        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
        };
        var rp = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &color,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };
        Check(_vk.CreateRenderPass(_device, &rp, null, out _swapchainRenderPass), "vkCreateRenderPass");
    }

    private void DestroySwapchain()
    {
        if (_device.Handle == 0) return;
        for (int i = 0; i < _swapchainFramebuffers.Length; i++)
        {
            if (_swapchainFramebuffers[i].Handle != 0)
                _vk.DestroyFramebuffer(_device, _swapchainFramebuffers[i], null);
        }
        _swapchainFramebuffers = Array.Empty<Framebuffer>();

        if (_swapchainRenderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _swapchainRenderPass, null);
            _swapchainRenderPass = default;
        }

        for (int i = 0; i < _swapchainImageViews.Length; i++)
        {
            if (_swapchainImageViews[i].Handle != 0)
                _vk.DestroyImageView(_device, _swapchainImageViews[i], null);
        }
        _swapchainImageViews = Array.Empty<ImageView>();
        _swapchainImages = Array.Empty<Image>();

        if (_swapchain.Handle != 0 && _khrSwapchain != null)
        {
            _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
            _swapchain = default;
        }
    }

    private void QueryCapabilities()
    {
        _vk.GetPhysicalDeviceProperties(_physicalDevice, out PhysicalDeviceProperties props);
        UniformBufferOffsetAlignment = Math.Max(1ul, props.Limits.MinUniformBufferOffsetAlignment);
        string name = Marshal.PtrToStringAnsi((nint)props.DeviceName) ?? "Vulkan";
        _capabilities = new GraphicsDeviceCapabilities
        {
            MaxTextureSize = (int)props.Limits.MaxImageDimension2D,
            MaxCubeMapTextureSize = (int)props.Limits.MaxImageDimensionCube,
            MaxArrayTextureLayers = (int)props.Limits.MaxImageArrayLayers,
            MaxFramebufferColorAttachments = (int)props.Limits.MaxColorAttachments,
            MaxFramesInFlight = MaxFramesInFlight,
            SupportsCompute = true,
            SupportsGeometryShader = props.Limits.MaxGeometryOutputVertices > 0,
            BackendName = $"Vulkan ({name})",
        };
    }

    private VkCommandBuffer AllocateTransientCommandBuffer()
    {
        var alloc = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        VkCommandBuffer cb;
        Check(_vk.AllocateCommandBuffers(_device, &alloc, &cb), "vkAllocateCommandBuffers");
        return cb;
    }

    private void FreeTransientCommandBuffer(VkCommandBuffer cb)
    {
        _vk.FreeCommandBuffers(_device, _commandPool, 1, &cb);
    }

    private void RetirePending()
    {
        for (int i = 0; i < _pendingRetire.Count; i++)
        {
            var (cmd, fence, descriptorSets, samplers, buffers) = _pendingRetire[i];
            _vk.DestroyFence(_device, fence, null);
            FreeTransientCommandBuffer(cmd);
            FreeDescriptorSets(descriptorSets);
            ReturnDescriptorSetList(descriptorSets);
            DestroySamplers(samplers);
            ReturnSamplerList(samplers);
            DestroyTransientBuffers(buffers);
            ReturnTransientBufferList(buffers);
        }
        _pendingRetire.Clear();

        DestroySamplers(_unfencedSamplerRetire);
        _unfencedSamplerRetire.Clear();
    }

    private void DestroyTransientBuffers(List<VkBufferResource> buffers)
    {
        for (int i = 0; i < buffers.Count; i++)
            DestroyBuffer(buffers[i]);
        buffers.Clear();
    }

    private List<VkBufferResource> RentTransientBufferList() =>
        _transientBufferListPool.Count > 0 ? _transientBufferListPool.Pop() : new List<VkBufferResource>();

    private void ReturnTransientBufferList(List<VkBufferResource> buffers)
    {
        buffers.Clear();
        _transientBufferListPool.Push(buffers);
    }

    private void SubmitEmpty(Semaphore wait, Semaphore signal, Fence fence)
    {
        PipelineStageFlags waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &wait,
            PWaitDstStageMask = &waitStage,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signal,
        };
        // Only submit if fence is not already waiting on prior work — reset happened in BeginFrame.
        _vk.QueueSubmit(_graphicsQueue, 1, &submit, fence);
    }

    private Fence CreateFence(bool signaled)
    {
        var ci = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = signaled ? FenceCreateFlags.SignaledBit : 0,
        };
        Check(_vk.CreateFence(_device, &ci, null, out Fence fence), "vkCreateFence");
        return fence;
    }

    private Semaphore CreateSemaphore()
    {
        var ci = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        Check(_vk.CreateSemaphore(_device, &ci, null, out Semaphore sem), "vkCreateSemaphore");
        return sem;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("VulkanGraphicsDevice has not been initialized.");
    }

    internal static void Check(Result result, string what)
    {
        if (result != Result.Success)
            throw new InvalidOperationException($"{what} failed: {result}");
    }
}

internal sealed class VkBufferResource
{
    public Buffer Buffer;
    public DeviceMemory Memory;
    public ulong Size;
    public BufferType Type;
    public bool Dynamic;
}

internal sealed class VkImageResource
{
    public Image Image;
    public DeviceMemory Memory;
    public ImageView View;
    public Sampler Sampler;
    public Format Format;
    public TextureImageFormat EngineFormat;
    public TextureType Type;
    public uint Width;
    public uint Height;
    public uint Depth = 1;
    public uint MipLevels = 1;
    public byte CubeInitializedFaces;
    public byte[] CubeInitializedFacesByMip = Array.Empty<byte>();
    public uint AvailableMipLevels;
    public ImageLayout Layout = ImageLayout.Undefined;
    public TextureWrap WrapS = TextureWrap.Repeat;
    public TextureWrap WrapT = TextureWrap.Repeat;
    public TextureWrap WrapR = TextureWrap.Repeat;
    public TextureMin MinFilter = TextureMin.Linear;
    public TextureMag MagFilter = TextureMag.Linear;
}

internal sealed class VkFramebufferResource
{
    public Framebuffer Framebuffer;
    public RenderPass RenderPass;
    public uint Width;
    public uint Height;
    public Format ColorFormat;
    public VkColorAttachmentFormats ColorFormats;
    public Format DepthFormat;
    public uint[] ColorHandles = Array.Empty<uint>();
    public uint[] ColorMipLevels = Array.Empty<uint>();
    public uint[] ColorArrayLayers = Array.Empty<uint>();
    public ImageView[] AttachmentViews = Array.Empty<ImageView>();
    public uint DepthHandle;
}

internal sealed class VkVertexArrayResource
{
    public uint VertexBuffer;
    public uint IndexBuffer;
    public uint InstanceBuffer;
    public VertexFormat Format = null!;
    public VertexFormat? InstanceFormat;
}

internal sealed class VkShaderResource
{
    public ShaderModule VertModule;
    public ShaderModule FragModule;
    public Pipeline Pipeline;
    public PipelineLayout PipelineLayout;
    public bool Valid;
}

internal sealed class VkShaderLayoutResource
{
    public DescriptorSetLayout DescriptorSetLayout;
    public PipelineLayout PipelineLayout;
    public ShaderDescriptorLayoutPlan Plan = null!;
}

internal sealed class VkShaderModuleResource
{
    public ShaderModule VertexModule;
    public ShaderModule FragmentModule;
}

internal readonly struct VkGraphicsPipelineKey : IEquatable<VkGraphicsPipelineKey>
{
    private readonly GraphicsPipelineKey _pipeline;
    private readonly VkRenderTargetFormats _targetFormats;

    public VkGraphicsPipelineKey(GraphicsPipelineKey pipeline, VkRenderTargetFormats targetFormats)
    {
        _pipeline = pipeline;
        _targetFormats = targetFormats;
    }

    public bool Equals(VkGraphicsPipelineKey other) =>
        _pipeline.Equals(other._pipeline) && _targetFormats.Equals(other._targetFormats);

    public override bool Equals(object? obj) => obj is VkGraphicsPipelineKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_pipeline, _targetFormats);
}

internal readonly struct VkRenderTargetFormats : IEquatable<VkRenderTargetFormats>
{
    public VkRenderTargetFormats(VkColorAttachmentFormats colorFormats, Format depthFormat)
    {
        ColorFormats = colorFormats;
        DepthFormat = depthFormat;
    }

    public VkColorAttachmentFormats ColorFormats { get; }
    public Format DepthFormat { get; }

    public bool Equals(VkRenderTargetFormats other) =>
        ColorFormats.Equals(other.ColorFormats) && DepthFormat == other.DepthFormat;

    public override bool Equals(object? obj) => obj is VkRenderTargetFormats other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ColorFormats, DepthFormat);
}

internal readonly struct VkColorAttachmentFormats : IEquatable<VkColorAttachmentFormats>
{
    private readonly Format _format0;
    private readonly Format _format1;
    private readonly Format _format2;
    private readonly Format _format3;
    private readonly Format _format4;
    private readonly Format _format5;
    private readonly Format _format6;
    private readonly Format _format7;

    public VkColorAttachmentFormats(Format format)
    {
        Count = 1;
        _format0 = format;
        _format1 = default;
        _format2 = default;
        _format3 = default;
        _format4 = default;
        _format5 = default;
        _format6 = default;
        _format7 = default;
    }

    public VkColorAttachmentFormats(ReadOnlySpan<Format> formats)
    {
        if (formats.Length is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(formats), "Vulkan supports one through eight color attachments in this RHI slice.");

        Count = formats.Length;
        _format0 = formats[0];
        _format1 = formats.Length > 1 ? formats[1] : default;
        _format2 = formats.Length > 2 ? formats[2] : default;
        _format3 = formats.Length > 3 ? formats[3] : default;
        _format4 = formats.Length > 4 ? formats[4] : default;
        _format5 = formats.Length > 5 ? formats[5] : default;
        _format6 = formats.Length > 6 ? formats[6] : default;
        _format7 = formats.Length > 7 ? formats[7] : default;
    }

    public int Count { get; }

    public Format this[int index] => index switch
    {
        0 when Count > 0 => _format0,
        1 when Count > 1 => _format1,
        2 when Count > 2 => _format2,
        3 when Count > 3 => _format3,
        4 when Count > 4 => _format4,
        5 when Count > 5 => _format5,
        6 when Count > 6 => _format6,
        7 when Count > 7 => _format7,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public bool Equals(VkColorAttachmentFormats other) =>
        Count == other.Count &&
        _format0 == other._format0 &&
        _format1 == other._format1 &&
        _format2 == other._format2 &&
        _format3 == other._format3 &&
        _format4 == other._format4 &&
        _format5 == other._format5 &&
        _format6 == other._format6 &&
        _format7 == other._format7;

    public override bool Equals(object? obj) => obj is VkColorAttachmentFormats other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Count);
        hash.Add(_format0);
        hash.Add(_format1);
        hash.Add(_format2);
        hash.Add(_format3);
        hash.Add(_format4);
        hash.Add(_format5);
        hash.Add(_format6);
        hash.Add(_format7);
        return hash.ToHashCode();
    }
}
