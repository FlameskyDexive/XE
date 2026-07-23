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

    private CommandPool _commandPool;
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
    private readonly Dictionary<Format, RenderPass> _pipelineRenderPasses = new();

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
    internal bool HasSwapchain => _swapchain.Handle != 0;

    internal Dictionary<uint, VkBufferResource> Buffers => _buffers;
    internal Dictionary<uint, VkImageResource> Images => _images;
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
        DestroySyncAndPool();

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
            var begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            Check(_vk.BeginCommandBuffer(vkCmd, &begin), "vkBeginCommandBuffer");

            try
            {
                _translator!.Translate(commandBuffer, vkCmd);
            }
            finally
            {
                Check(_vk.EndCommandBuffer(vkCmd), "vkEndCommandBuffer");
            }

            Fence fence = CreateFence(signaled: false);
            var submit = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &vkCmd,
            };
            Check(_vk.QueueSubmit(_graphicsQueue, 1, &submit, fence), "vkQueueSubmit");

            if (wait)
            {
                _vk.WaitForFences(_device, 1, &fence, true, ulong.MaxValue);
                _vk.DestroyFence(_device, fence, null);
                FreeTransientCommandBuffer(vkCmd);
                _fenceValue++;
            }
            else
            {
                // Retire asynchronously on next WaitIdle / EndFrame wait.
                _pendingRetire.Add((vkCmd, fence));
            }
        }

        CommandBufferPool.Return(commandBuffer);
    }

    private readonly List<(VkCommandBuffer Cmd, Fence Fence)> _pendingRetire = new();

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

    internal Pipeline GetOrCreateFullscreenPipeline(
        GraphicsPipelineKey key,
        ShaderVariant variant,
        Format colorFormat)
    {
        var cacheKey = new VkGraphicsPipelineKey(key, colorFormat);
        if (_graphicsPipelines.TryGetValue(cacheKey, out Pipeline cached))
            return cached;

        RasterizerState raster = key.RasterState;
        if (key.VertexArrayHandle != 0)
            throw new NotSupportedException("The initial Vulkan PSO slice supports shader-generated vertices only.");
        if (raster.DepthTest || raster.DepthWrite || raster.StencilEnabled || raster.DoBlend)
            throw new NotSupportedException("The initial Vulkan PSO slice supports no depth, stencil, or blending.");

        VkShaderLayoutResource layout = GetOrCreateShaderLayout(variant);
        VkShaderModuleResource modules = GetOrCreateShaderModules(variant);
        RenderPass renderPass = GetOrCreatePipelineRenderPass(colorFormat);
        byte* entryPoint = (byte*)SilkMarshal.StringToPtr("main");
        try
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
            var colorAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = false,
                ColorWriteMask = ColorComponentFlags.RBit |
                    ColorComponentFlags.GBit |
                    ColorComponentFlags.BBit |
                    ColorComponentFlags.ABit,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = 1,
                PAttachments = &colorAttachment,
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
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamicState,
                Layout = layout.PipelineLayout,
                RenderPass = renderPass,
                Subpass = 0,
                BasePipelineIndex = -1,
            };
            Check(
                _vk.CreateGraphicsPipelines(_device, default, 1, &createInfo, null, out Pipeline pipeline),
                "vkCreateGraphicsPipelines(fullscreen)");
            _graphicsPipelines.Add(cacheKey, pipeline);
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free((nint)entryPoint);
        }
    }

    private RenderPass GetOrCreatePipelineRenderPass(Format colorFormat)
    {
        if (_pipelineRenderPasses.TryGetValue(colorFormat, out RenderPass cached))
            return cached;

        var color = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal,
        };
        var colorReference = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal,
        };
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
        };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &color,
            SubpassCount = 1,
            PSubpasses = &subpass,
        };
        Check(_vk.CreateRenderPass(_device, &createInfo, null, out RenderPass renderPass), "vkCreateRenderPass(pipeline)");
        _pipelineRenderPasses.Add(colorFormat, renderPass);
        return renderPass;
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
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit,
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
        foreach (var (cmd, fence) in _pendingRetire)
        {
            _vk.DestroyFence(_device, fence, null);
            FreeTransientCommandBuffer(cmd);
        }
        _pendingRetire.Clear();
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
    public uint[] ColorHandles = Array.Empty<uint>();
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
    private readonly Format _colorFormat;

    public VkGraphicsPipelineKey(GraphicsPipelineKey pipeline, Format colorFormat)
    {
        _pipeline = pipeline;
        _colorFormat = colorFormat;
    }

    public bool Equals(VkGraphicsPipelineKey other) =>
        _pipeline.Equals(other._pipeline) && _colorFormat == other._colorFormat;

    public override bool Equals(object? obj) => obj is VkGraphicsPipelineKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_pipeline, _colorFormat);
}
