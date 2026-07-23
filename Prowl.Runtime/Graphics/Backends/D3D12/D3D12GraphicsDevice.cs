// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;

using Prowl.Runtime.RHI;
using Prowl.Runtime.RHI.Shaders;

using SharpGen.Runtime;

using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;

using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace Prowl.Runtime.Backends.D3D12;

internal readonly struct D3D12DescriptorAllocation
{
    public D3D12DescriptorAllocation(CpuDescriptorHandle cpu, GpuDescriptorHandle gpu, int index)
    {
        Cpu = cpu;
        Gpu = gpu;
        Index = index;
    }

    public CpuDescriptorHandle Cpu { get; }
    public GpuDescriptorHandle Gpu { get; }
    public int Index { get; }
}

/// <summary>
/// Direct3D 12 <see cref="IGraphicsDevice"/> with swapchain presentation and
/// per-submit command translation via <see cref="D3D12CommandTranslator"/>.
/// </summary>
public sealed class D3D12GraphicsDevice : IGraphicsDevice
{
    public const int MaxFramesInFlight = 2;
    private const int CustomRtvHeapSize = 64;
    private const int CustomDsvHeapSize = 32;
    private const int CbvSrvUavHeapSize = 1024;
    private const int SamplerHeapSize = 64;

    private readonly GraphicsDeviceOptions _options;
    private readonly object _gate = new();

    private IDXGIFactory4? _factory;
    private IDXGIAdapter1? _adapter;
    private ID3D12Device? _device;
    private ID3D12CommandQueue? _queue;
    private ID3D12Fence? _fence;
    private readonly AutoResetEvent _fenceEvent = new(false);
    private ulong _fenceValue;
    private readonly ulong[] _frameFenceValues = new ulong[MaxFramesInFlight];

    private ID3D12DescriptorHeap? _rtvHeap;
    private ID3D12DescriptorHeap? _dsvHeap;
    private ID3D12DescriptorHeap? _cbvSrvUavHeap;
    private ID3D12DescriptorHeap? _samplerHeap;
    private int _rtvDescriptorSize;
    private int _dsvDescriptorSize;
    private int _cbvSrvUavDescriptorSize;
    private int _samplerDescriptorSize;
    private int _nextRtvDescriptor;
    private int _nextDsvDescriptor;
    private int _nextSrvDescriptor;
    private int _nextSamplerDescriptor;

    private readonly ID3D12CommandAllocator?[] _frameAllocators = new ID3D12CommandAllocator?[MaxFramesInFlight];
    private readonly ID3D12GraphicsCommandList?[] _frameLists = new ID3D12GraphicsCommandList?[MaxFramesInFlight];
    private readonly List<ID3D12Resource>[] _frameTransientResources =
        [new List<ID3D12Resource>(), new List<ID3D12Resource>()];
    private readonly List<(ulong FenceValue, List<ID3D12Resource> Resources)> _pendingTransientResources = new();
    private readonly Stack<List<ID3D12Resource>> _transientResourceListPool = new();

    private ID3D12CommandAllocator? _immediateAllocator;
    private ID3D12GraphicsCommandList? _immediateList;

    private IDXGISwapChain3? _swapchain;
    private readonly ID3D12Resource?[] _backBuffers = new ID3D12Resource?[MaxFramesInFlight];
    private readonly CpuDescriptorHandle[] _rtvHandles = new CpuDescriptorHandle[MaxFramesInFlight];
    private ResourceStates[] _backBufferStates = new ResourceStates[MaxFramesInFlight];
    private ID3D12Resource? _headlessRenderTarget;
    private ResourceStates _headlessRenderTargetState = ResourceStates.RenderTarget;
    private CpuDescriptorHandle _headlessRtv;
    private readonly D3D12DescriptorAllocation[] _defaultTargetSrvs = new D3D12DescriptorAllocation[MaxFramesInFlight];
    private readonly bool[] _defaultTargetSrvAllocated = new bool[MaxFramesInFlight];
    private D3D12DescriptorAllocation _headlessTargetSrv;
    private bool _headlessTargetSrvAllocated;

    private IWindowSurface? _windowSurface;
    private GraphicsDeviceCapabilities _capabilities = new() { BackendName = "Direct3D 12" };
    private D3D12CommandTranslator? _translator;
    private bool _initialized;
    private bool _shutdown;
    private int _frameIndex;
    private bool _frameBegun;
    private uint _nextHandle = 1;

    private readonly Dictionary<uint, D3D12BufferResource> _buffers = new();
    private readonly Dictionary<uint, D3D12TextureResource> _textures = new();
    private readonly Dictionary<uint, D3D12FramebufferResource> _framebuffers = new();
    private readonly Dictionary<uint, D3D12VertexArrayResource> _vertexArrays = new();
    private readonly Dictionary<int, D3D12ShaderLayoutResource> _shaderLayouts = new();
    private readonly Dictionary<D3D12GraphicsPipelineKey, ID3D12PipelineState> _graphicsPipelines = new();
    private readonly Dictionary<Format, ID3D12PipelineState> _cubemapMipPipelines = new();
    private ID3D12RootSignature? _cubemapMipRootSignature;
    private CompiledShaderBytecode? _cubemapMipBytecode;
    private readonly Dictionary<Format, ID3D12PipelineState> _framebufferBlitPipelines = new();
    private ID3D12RootSignature? _framebufferBlitRootSignature;
    private CompiledShaderBytecode? _framebufferBlitBytecode;
    private D3D12DescriptorAllocation _framebufferBlitPointSampler;
    private D3D12DescriptorAllocation _framebufferBlitLinearSampler;

    public D3D12GraphicsDevice(GraphicsDeviceOptions options)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Direct3D 12 is only available on Windows.");

        _options = options ?? throw new ArgumentNullException(nameof(options));

        try
        {
            bool debug = _options.Debug || _options.EnableValidation;
            if (debug)
            {
                Result debugResult = D3D12GetDebugInterface(out ID3D12Debug? debugController);
                if (debugResult.Success && debugController != null)
                {
                    debugController.EnableDebugLayer();
                    debugController.Dispose();
                }
            }

            Result factoryResult = CreateDXGIFactory2(debug, out _factory);
            if (factoryResult.Failure || _factory == null)
                throw new InvalidOperationException($"Failed to create DXGI factory: {factoryResult}.");

            _adapter = PickAdapter(_factory, _options.PreferredAdapterIndex);
            if (_adapter == null)
                throw new InvalidOperationException("No Direct3D 12-capable DXGI adapter was found.");

            Result deviceResult = D3D12CreateDevice(_adapter, FeatureLevel.Level_11_0, out _device);
            if (deviceResult.Failure || _device == null)
                throw new InvalidOperationException($"Failed to create D3D12 device: {deviceResult}.");
        }
        catch (Exception ex) when (ex is not PlatformNotSupportedException and not InvalidOperationException)
        {
            DisposeCore();
            throw new InvalidOperationException(
                "Failed to create Direct3D 12 device. D3D12 is unavailable on this machine (missing driver or runtime).",
                ex);
        }
        catch (InvalidOperationException)
        {
            DisposeCore();
            throw;
        }
    }

    public GraphicsBackend Backend => GraphicsBackend.Direct3D12;
    public GraphicsDeviceCapabilities Capabilities => _capabilities;
    public bool IsInitialized => _initialized;

    internal ID3D12Device Device => _device!;
    internal ID3D12CommandQueue Queue => _queue!;
    internal ID3D12GraphicsCommandList? CurrentFrameList =>
        _frameBegun ? _frameLists[_frameIndex] : null;
    internal ID3D12Resource? CurrentBackBuffer =>
        _swapchain != null ? _backBuffers[_frameIndex] : null;
    internal ID3D12Resource CurrentRenderTarget =>
        CurrentBackBuffer ?? _headlessRenderTarget ?? throw new InvalidOperationException("The D3D12 default render target is unavailable.");
    internal CpuDescriptorHandle CurrentRtv => HasSwapchain ? _rtvHandles[_frameIndex] : _headlessRtv;
    internal bool HasSwapchain => _swapchain != null;
    internal int FrameIndex => _frameIndex;
    internal Dictionary<uint, D3D12BufferResource> Buffers => _buffers;
    internal Dictionary<uint, D3D12TextureResource> Textures => _textures;
    internal Dictionary<uint, D3D12FramebufferResource> Framebuffers => _framebuffers;
    internal Dictionary<uint, D3D12VertexArrayResource> VertexArrays => _vertexArrays;
    internal ID3D12DescriptorHeap CbvSrvUavHeap => _cbvSrvUavHeap!;
    internal ID3D12DescriptorHeap SamplerHeap => _samplerHeap!;

    internal uint AllocateHandle() => Interlocked.Increment(ref _nextHandle) - 1;

    internal CpuDescriptorHandle AllocateRtvDescriptor()
    {
        EnsureInitialized();
        lock (_gate)
        {
            int capacity = MaxFramesInFlight + 1 + CustomRtvHeapSize;
            if (_nextRtvDescriptor >= capacity)
                throw new InvalidOperationException($"The D3D12 RTV descriptor heap is full ({capacity} descriptors).");

            int offset = _nextRtvDescriptor++ * _rtvDescriptorSize;
            return _rtvHeap!.GetCPUDescriptorHandleForHeapStart() + offset;
        }
    }

    internal CpuDescriptorHandle AllocateDsvDescriptor()
    {
        EnsureInitialized();
        lock (_gate)
        {
            if (_nextDsvDescriptor >= CustomDsvHeapSize)
                throw new InvalidOperationException($"The D3D12 DSV descriptor heap is full ({CustomDsvHeapSize} descriptors).");

            int offset = _nextDsvDescriptor++ * _dsvDescriptorSize;
            return _dsvHeap!.GetCPUDescriptorHandleForHeapStart() + offset;
        }
    }

    internal D3D12DescriptorAllocation AllocateSrvDescriptor()
    {
        EnsureInitialized();
        lock (_gate)
        {
            return AllocateDescriptor(
                _cbvSrvUavHeap!,
                ref _nextSrvDescriptor,
                CbvSrvUavHeapSize,
                _cbvSrvUavDescriptorSize,
                "CBV/SRV/UAV");
        }
    }

    internal D3D12DescriptorAllocation AllocateSamplerDescriptor()
    {
        EnsureInitialized();
        lock (_gate)
        {
            return AllocateDescriptor(
                _samplerHeap!,
                ref _nextSamplerDescriptor,
                SamplerHeapSize,
                _samplerDescriptorSize,
                "sampler");
        }
    }

    internal D3D12DescriptorAllocation GetCurrentRenderTargetSrv()
    {
        D3D12DescriptorAllocation allocation;
        if (HasSwapchain)
        {
            if (!_defaultTargetSrvAllocated[_frameIndex])
            {
                _defaultTargetSrvs[_frameIndex] = AllocateSrvDescriptor();
                _defaultTargetSrvAllocated[_frameIndex] = true;
            }
            allocation = _defaultTargetSrvs[_frameIndex];
        }
        else
        {
            if (!_headlessTargetSrvAllocated)
            {
                _headlessTargetSrv = AllocateSrvDescriptor();
                _headlessTargetSrvAllocated = true;
            }
            allocation = _headlessTargetSrv;
        }

        var srv = new ShaderResourceViewDescription
        {
            Format = Format.R8G8B8A8_UNorm,
            ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D,
            Shader4ComponentMapping = ShaderComponentMapping.Default,
            Texture2D = new Texture2DShaderResourceView { MostDetailedMip = 0, MipLevels = 1 },
        };
        Device.CreateShaderResourceView(CurrentRenderTarget, srv, allocation.Cpu);
        return allocation;
    }

    internal void TransitionCurrentRenderTarget(ID3D12GraphicsCommandList list, ResourceStates after)
    {
        if (HasSwapchain)
        {
            TransitionBackBuffer(list, after);
            return;
        }
        if (_headlessRenderTarget == null || _headlessRenderTargetState == after)
            return;
        list.ResourceBarrierTransition(_headlessRenderTarget, _headlessRenderTargetState, after);
        _headlessRenderTargetState = after;
    }

    public void Initialize(IWindowSurface? surface)
    {
        if (_shutdown)
            throw new ObjectDisposedException(nameof(D3D12GraphicsDevice));
        if (_initialized)
            return;

        _windowSurface = surface;
        _queue = _device!.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        _fence = _device.CreateFence(0);

        _rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.RenderTargetView, MaxFramesInFlight + 1 + CustomRtvHeapSize));
        _dsvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.DepthStencilView, CustomDsvHeapSize));
        _cbvSrvUavHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            CbvSrvUavHeapSize,
            DescriptorHeapFlags.ShaderVisible));
        _samplerHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.Sampler,
            SamplerHeapSize,
            DescriptorHeapFlags.ShaderVisible));
        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _dsvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
        _nextRtvDescriptor = MaxFramesInFlight + 1;
        _nextDsvDescriptor = 0;
        _cbvSrvUavDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        _samplerDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.Sampler);

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            _frameAllocators[i] = _device.CreateCommandAllocator(CommandListType.Direct);
            _frameLists[i] = _device.CreateCommandList<ID3D12GraphicsCommandList>(
                CommandListType.Direct, _frameAllocators[i]!, null);
            // Created in recording state; close so BeginFrame can Reset.
            _frameLists[i]!.Close();
        }

        _immediateAllocator = _device.CreateCommandAllocator(CommandListType.Direct);
        _immediateList = _device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Direct, _immediateAllocator, null);
        _immediateList.Close();

        if (surface != null)
        {
            nint hwnd = surface.NativeHandle;
            if (hwnd == nint.Zero)
                throw new InvalidOperationException("D3D12 swapchain requires a non-zero HWND from IWindowSurface.NativeHandle.");

            (int fbW, int fbH) = surface.FramebufferSize;
            uint width = fbW > 0 ? (uint)fbW : 1u;
            uint height = fbH > 0 ? (uint)fbH : 1u;

            var swapDesc = new SwapChainDescription1
            {
                Width = width,
                Height = height,
                Format = Format.R8G8B8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput | Usage.ShaderInput,
                BufferCount = MaxFramesInFlight,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Unspecified,
            };

            using IDXGISwapChain1 swap1 = _factory!.CreateSwapChainForHwnd(_queue, hwnd, swapDesc);
            _swapchain = swap1.QueryInterface<IDXGISwapChain3>();
            _factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);

            CreateBackBufferRtvs();
            _frameIndex = (int)_swapchain.CurrentBackBufferIndex;
        }
        else
        {
            CreateHeadlessRenderTarget();
        }

        QueryCapabilities();
        _translator = new D3D12CommandTranslator(this);
        _initialized = true;
    }

    public void Shutdown()
    {
        if (_shutdown)
            return;
        _shutdown = true;

        try { WaitIdle(); } catch { /* device may already be lost */ }

        for (int i = 0; i < MaxFramesInFlight; i++)
            DisposeTransientResources(_frameTransientResources[i]);
        for (int i = 0; i < _pendingTransientResources.Count; i++)
            DisposeTransientResources(_pendingTransientResources[i].Resources);
        _pendingTransientResources.Clear();

        foreach (var kv in _buffers)
            kv.Value.Resource?.Dispose();
        _buffers.Clear();
        foreach (var kv in _textures)
            kv.Value.Resource?.Dispose();
        _textures.Clear();
        _framebuffers.Clear();
        _vertexArrays.Clear();
        foreach (var kv in _shaderLayouts)
            kv.Value.RootSignature.Dispose();
        _shaderLayouts.Clear();
        foreach (var kv in _graphicsPipelines)
            kv.Value.Dispose();
        _graphicsPipelines.Clear();
        foreach (var kv in _cubemapMipPipelines)
            kv.Value.Dispose();
        _cubemapMipPipelines.Clear();
        _cubemapMipRootSignature?.Dispose();
        _cubemapMipRootSignature = null;
        _cubemapMipBytecode = null;
        foreach (var kv in _framebufferBlitPipelines)
            kv.Value.Dispose();
        _framebufferBlitPipelines.Clear();
        _framebufferBlitRootSignature?.Dispose();
        _framebufferBlitRootSignature = null;
        _framebufferBlitBytecode = null;

        DestroySwapchainBuffers();
        _headlessRenderTarget?.Dispose();
        _headlessRenderTarget = null;
        _swapchain?.Dispose();
        _swapchain = null;

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            _frameLists[i]?.Dispose();
            _frameLists[i] = null;
            _frameAllocators[i]?.Dispose();
            _frameAllocators[i] = null;
        }

        _immediateList?.Dispose();
        _immediateList = null;
        _immediateAllocator?.Dispose();
        _immediateAllocator = null;

        _rtvHeap?.Dispose();
        _rtvHeap = null;
        _dsvHeap?.Dispose();
        _dsvHeap = null;
        _cbvSrvUavHeap?.Dispose();
        _cbvSrvUavHeap = null;
        _samplerHeap?.Dispose();
        _samplerHeap = null;

        _fence?.Dispose();
        _fence = null;
        _queue?.Dispose();
        _queue = null;

        DisposeCore();
        _fenceEvent.Dispose();
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

        WaitFence(_frameFenceValues[_frameIndex]);
        DisposeTransientResources(_frameTransientResources[_frameIndex]);

        _frameAllocators[_frameIndex]!.Reset();
        ID3D12GraphicsCommandList list = _frameLists[_frameIndex]!;
        list.Reset(_frameAllocators[_frameIndex]!);

        TransitionBackBuffer(list, ResourceStates.RenderTarget);
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

        ID3D12GraphicsCommandList list = _frameLists[_frameIndex]!;
        TransitionBackBuffer(list, ResourceStates.Present);
        list.Close();

        _queue!.ExecuteCommandList(list);
        uint syncInterval = _options.VSync || (_windowSurface?.VSync ?? true) ? 1u : 0u;
        _swapchain!.Present(syncInterval);

        _frameFenceValues[_frameIndex] = Signal();
        _frameIndex = (int)_swapchain.CurrentBackBufferIndex;
        _fenceValue = Math.Max(_fenceValue, _frameFenceValues[(_frameIndex + MaxFramesInFlight - 1) % MaxFramesInFlight]);
    }

    public void Execute(CommandBuffer commandBuffer, bool wait)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        if (commandBuffer._inPool)
            throw new InvalidOperationException("CommandBuffer has already been submitted (it's in the pool).");
        EnsureInitialized();

        commandBuffer._submitted = true;
        commandBuffer._ownerReleased = true;

        lock (_gate)
        {
            if (_frameBegun && !wait && CurrentFrameList != null)
            {
                _translator!.Translate(commandBuffer, CurrentFrameList, _frameTransientResources[_frameIndex]);
            }
            else
            {
                ExecuteImmediate(commandBuffer, wait);
            }
        }

        CommandBufferPool.Return(commandBuffer);
    }

    public void WaitIdle()
    {
        if (!_initialized || _queue == null || _fence == null)
            return;
        ulong value = Signal();
        WaitFence(value);
    }

    public ulong GetFenceValue() => _fenceValue;

    public void WaitFence(ulong fenceValue)
    {
        if (_fence == null)
            return;
        if (_fence.CompletedValue >= fenceValue)
        {
            RetireCompletedTransientResources();
            return;
        }

        _fence.SetEventOnCompletion(fenceValue, _fenceEvent).CheckError();
        _fenceEvent.WaitOne();
        RetireCompletedTransientResources();
    }

    public void Dispose() => Shutdown();

    // ─────────────────────── Resource helpers ───────────────────────

    internal D3D12ShaderLayoutResource GetOrCreateShaderLayout(ShaderVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (_shaderLayouts.TryGetValue(variant.Id, out D3D12ShaderLayoutResource? cached))
            return cached;

        ShaderBindingLayout bindingLayout = variant.Bytecode?.BindingLayout ?? new ShaderBindingLayout();
        int count = bindingLayout.Buffers.Length + bindingLayout.Textures.Length + bindingLayout.Samplers.Length;
        var parameters = new RootParameter[count];
        int index = 0;

        for (int i = 0; i < bindingLayout.Buffers.Length; i++)
        {
            ShaderBindingSlot slot = bindingLayout.Buffers[i];
            parameters[index++] = new RootParameter(
                RootParameterType.ConstantBufferView,
                new RootDescriptor((uint)slot.Slot, 0),
                ShaderVisibility.All);
        }

        for (int i = 0; i < bindingLayout.Textures.Length; i++)
        {
            ShaderBindingSlot slot = bindingLayout.Textures[i];
            var range = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, (uint)slot.Slot, 0, 0);
            parameters[index++] = new RootParameter(new RootDescriptorTable([range]), ShaderVisibility.All);
        }

        for (int i = 0; i < bindingLayout.Samplers.Length; i++)
        {
            ShaderBindingSlot slot = bindingLayout.Samplers[i];
            var range = new DescriptorRange(DescriptorRangeType.Sampler, 1, (uint)slot.Slot, 0, 0);
            parameters[index++] = new RootParameter(new RootDescriptorTable([range]), ShaderVisibility.All);
        }

        var description = new RootSignatureDescription(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            parameters,
            []);
        ID3D12RootSignature rootSignature = _device!.CreateRootSignature(ref description, RootSignatureVersion.Version1);
        var resource = new D3D12ShaderLayoutResource
        {
            RootSignature = rootSignature,
            BindingLayout = bindingLayout,
        };
        _shaderLayouts.Add(variant.Id, resource);
        return resource;
    }

    internal ID3D12PipelineState GetOrCreateGraphicsPipeline(
        GraphicsPipelineKey key,
        ShaderVariant variant)
        => GetOrCreateGraphicsPipeline(key, variant, Format.R8G8B8A8_UNorm);

    internal ID3D12PipelineState GetOrCreateGraphicsPipeline(
        GraphicsPipelineKey key,
        ShaderVariant variant,
        Format colorFormat)
        => GetOrCreateGraphicsPipeline(key, variant, new D3D12ColorAttachmentFormats(colorFormat));

    internal ID3D12PipelineState GetOrCreateGraphicsPipeline(
        GraphicsPipelineKey key,
        ShaderVariant variant,
        D3D12ColorAttachmentFormats colorFormats)
        => GetOrCreateGraphicsPipeline(key, variant, new D3D12RenderTargetFormats(colorFormats, Format.Unknown));

    internal ID3D12PipelineState GetOrCreateGraphicsPipeline(
        GraphicsPipelineKey key,
        ShaderVariant variant,
        D3D12RenderTargetFormats targetFormats)
    {
        var cacheKey = new D3D12GraphicsPipelineKey(key, targetFormats);
        if (_graphicsPipelines.TryGetValue(cacheKey, out ID3D12PipelineState? cached))
            return cached;

        RasterizerState raster = key.RasterState;
        if ((raster.DepthTest || raster.DepthWrite) && targetFormats.DepthFormat == Format.Unknown)
            throw new InvalidOperationException("D3D12 depth testing requires a depth framebuffer attachment.");
        if (raster.StencilEnabled && !D3D12Formats.HasStencil(targetFormats.DepthFormat))
            throw new InvalidOperationException("D3D12 stencil testing requires a stencil-capable framebuffer attachment.");

        InputElementDescription[] inputElements = CreateInputElements(key.VertexArrayHandle);

        CompiledShaderBytecode bytecode = variant.Bytecode
            ?? throw new InvalidOperationException("D3D12 PSO creation requires DXIL bytecode.");
        if (bytecode.Format != ShaderBytecodeFormat.Dxil)
            throw new InvalidOperationException($"D3D12 PSO creation requires DXIL, got {bytecode.Format}.");

        D3D12ShaderLayoutResource layout = GetOrCreateShaderLayout(variant);
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = layout.RootSignature,
            VertexShader = bytecode.VertexBytecode,
            PixelShader = bytecode.FragmentBytecode,
            BlendState = CreateBlendDescription(in raster),
            SampleMask = uint.MaxValue,
            RasterizerState = new RasterizerDescription(
                D3D12Formats.ToCullMode(raster.CullFace),
                FillMode.Solid,
                raster.Winding == RasterizerState.WindingOrder.CCW,
                0,
                0f,
                0f,
                true,
                false,
                false,
                0,
                ConservativeRasterizationMode.Off),
            DepthStencilState = new DepthStencilDescription(
                raster.DepthTest,
                raster.DepthWrite,
                D3D12Formats.ToComparison(raster.Depth),
                raster.StencilEnabled,
                checked((byte)raster.StencilReadMask),
                checked((byte)raster.StencilWriteMask),
                D3D12Formats.ToStencilOperation(raster.StencilFailOp),
                D3D12Formats.ToStencilOperation(raster.StencilZFailOp),
                D3D12Formats.ToStencilOperation(raster.StencilPassOp),
                D3D12Formats.ToComparison(raster.StencilFunc),
                D3D12Formats.ToStencilOperation(raster.StencilFailOp),
                D3D12Formats.ToStencilOperation(raster.StencilZFailOp),
                D3D12Formats.ToStencilOperation(raster.StencilPassOp),
                D3D12Formats.ToComparison(raster.StencilFunc)),
            InputLayout = new InputLayoutDescription(inputElements),
            PrimitiveTopologyType = D3D12Formats.ToTopologyType(key.Topology),
            RenderTargetFormats = targetFormats.ColorFormats.ToArray(),
            DepthStencilFormat = targetFormats.DepthFormat,
            SampleDescription = new SampleDescription(1, 0),
        };
        ID3D12PipelineState pipeline = _device!.CreateGraphicsPipelineState(description);
        _graphicsPipelines.Add(cacheKey, pipeline);
        return pipeline;
    }

    private static BlendDescription CreateBlendDescription(in RasterizerState raster)
    {
        var description = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false,
        };
        description.RenderTarget[0] = new RenderTargetBlendDescription(
            raster.DoBlend,
            false,
            D3D12Formats.ToBlend(raster.BlendSrc),
            D3D12Formats.ToBlend(raster.BlendDst),
            D3D12Formats.ToBlendOp(raster.Blend),
            D3D12Formats.ToBlend(raster.BlendSrc),
            D3D12Formats.ToBlend(raster.BlendDst),
            D3D12Formats.ToBlendOp(raster.Blend),
            LogicOp.Noop,
            ColorWriteEnable.All);
        return description;
    }

    internal ID3D12PipelineState GetOrCreateCubemapMipPipeline(Format colorFormat, out ID3D12RootSignature rootSignature)
    {
        EnsureCubemapMipShader();
        rootSignature = _cubemapMipRootSignature!;
        if (_cubemapMipPipelines.TryGetValue(colorFormat, out ID3D12PipelineState? cached))
            return cached;

        CompiledShaderBytecode bytecode = _cubemapMipBytecode!;
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = rootSignature,
            VertexShader = bytecode.VertexBytecode,
            PixelShader = bytecode.FragmentBytecode,
            BlendState = BlendDescription.Opaque,
            SampleMask = uint.MaxValue,
            RasterizerState = new RasterizerDescription(
                CullMode.None,
                FillMode.Solid,
                false,
                0,
                0f,
                0f,
                true,
                false,
                false,
                0,
                ConservativeRasterizationMode.Off),
            DepthStencilState = DepthStencilDescription.None,
            InputLayout = new InputLayoutDescription([]),
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [colorFormat],
            DepthStencilFormat = Format.Unknown,
            SampleDescription = new SampleDescription(1, 0),
        };
        ID3D12PipelineState pipeline = _device!.CreateGraphicsPipelineState(description);
        _cubemapMipPipelines.Add(colorFormat, pipeline);
        return pipeline;
    }

    private void EnsureCubemapMipShader()
    {
        if (_cubemapMipRootSignature != null)
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSOutput { float4 position : SV_Position; }; VSOutput main(uint id : SV_VertexID) { VSOutput o; float2 uv = float2((id << 1) & 2, id & 2); o.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1); return o; }",
            FragmentSource = "Texture2DArray SourceTexture : register(t0); cbuffer MipParams : register(b0) { uint Face; uint SourceWidth; uint SourceHeight; }; float4 main(float4 position : SV_Position) : SV_Target { uint2 p = uint2(position.xy) * 2; uint2 m = uint2(SourceWidth - 1, SourceHeight - 1); return 0.25 * (SourceTexture.Load(int4(min(p, m), Face, 0)) + SourceTexture.Load(int4(min(p + uint2(1, 0), m), Face, 0)) + SourceTexture.Load(int4(min(p + uint2(0, 1), m), Face, 0)) + SourceTexture.Load(int4(min(p + uint2(1, 1), m), Face, 0))); }",
        });
        if (!compiled.Success)
            throw new InvalidOperationException($"Failed to compile the D3D12 cubemap mip-generation shader: {compiled.ErrorMessage}");

        _cubemapMipBytecode = new CompiledShaderBytecode(
            ShaderLanguage.Hlsl,
            ShaderBytecodeFormat.Dxil,
            compiled.VertexBytecode!,
            compiled.FragmentBytecode!,
            compiled.BindingLayout);
        var srvRange = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0, 0, 0);
        RootParameter[] parameters =
        [
            new RootParameter(new RootDescriptorTable([srvRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootConstants(0, 0, 3), ShaderVisibility.Pixel),
        ];
        var rootDescription = new RootSignatureDescription(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            parameters,
            []);
        _cubemapMipRootSignature = _device!.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);
    }

    internal ID3D12PipelineState GetOrCreateFramebufferBlitPipeline(
        Format colorFormat,
        out ID3D12RootSignature rootSignature,
        out GpuDescriptorHandle pointSampler,
        out GpuDescriptorHandle linearSampler)
    {
        EnsureFramebufferBlitShader();
        rootSignature = _framebufferBlitRootSignature!;
        pointSampler = _framebufferBlitPointSampler.Gpu;
        linearSampler = _framebufferBlitLinearSampler.Gpu;
        if (_framebufferBlitPipelines.TryGetValue(colorFormat, out ID3D12PipelineState? cached))
            return cached;

        CompiledShaderBytecode bytecode = _framebufferBlitBytecode!;
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = rootSignature,
            VertexShader = bytecode.VertexBytecode,
            PixelShader = bytecode.FragmentBytecode,
            BlendState = BlendDescription.Opaque,
            SampleMask = uint.MaxValue,
            RasterizerState = new RasterizerDescription(
                CullMode.None,
                FillMode.Solid,
                false,
                0,
                0f,
                0f,
                true,
                false,
                false,
                0,
                ConservativeRasterizationMode.Off),
            DepthStencilState = DepthStencilDescription.None,
            InputLayout = new InputLayoutDescription([]),
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [colorFormat],
            DepthStencilFormat = Format.Unknown,
            SampleDescription = new SampleDescription(1, 0),
        };
        ID3D12PipelineState pipeline = _device!.CreateGraphicsPipelineState(description);
        _framebufferBlitPipelines.Add(colorFormat, pipeline);
        return pipeline;
    }

    private void EnsureFramebufferBlitShader()
    {
        if (_framebufferBlitRootSignature != null)
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSOutput { float4 position : SV_Position; float2 uv : TEXCOORD0; }; VSOutput main(uint id : SV_VertexID) { VSOutput o; float2 uv = float2((id << 1) & 2, id & 2); o.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1); o.uv = uv; return o; }",
            FragmentSource = "Texture2D SourceTexture : register(t0); SamplerState SourceSampler : register(s0); cbuffer BlitParams : register(b0) { float4 SourceRect; }; float4 main(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target { return SourceTexture.Sample(SourceSampler, lerp(SourceRect.xy, SourceRect.zw, uv)); }",
        });
        if (!compiled.Success)
            throw new InvalidOperationException($"Failed to compile the D3D12 framebuffer-blit shader: {compiled.ErrorMessage}");

        _framebufferBlitBytecode = new CompiledShaderBytecode(
            ShaderLanguage.Hlsl,
            ShaderBytecodeFormat.Dxil,
            compiled.VertexBytecode!,
            compiled.FragmentBytecode!,
            compiled.BindingLayout);
        var srvRange = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0, 0, 0);
        var samplerRange = new DescriptorRange(DescriptorRangeType.Sampler, 1, 0, 0, 0);
        RootParameter[] parameters =
        [
            new RootParameter(new RootDescriptorTable([srvRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([samplerRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootConstants(0, 0, 4), ShaderVisibility.Pixel),
        ];
        var rootDescription = new RootSignatureDescription(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            parameters,
            []);
        _framebufferBlitRootSignature = _device!.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);

        _framebufferBlitPointSampler = AllocateSamplerDescriptor();
        _framebufferBlitLinearSampler = AllocateSamplerDescriptor();
        var pointDescription = new SamplerDescription(
            Filter.MinMagMipPoint,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            0,
            1,
            ComparisonFunction.Always,
            0,
            float.MaxValue);
        var linearDescription = new SamplerDescription(
            Filter.MinMagMipLinear,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            0,
            1,
            ComparisonFunction.Always,
            0,
            float.MaxValue);
        _device.CreateSampler(ref pointDescription, _framebufferBlitPointSampler.Cpu);
        _device.CreateSampler(ref linearDescription, _framebufferBlitLinearSampler.Cpu);
    }

    private InputElementDescription[] CreateInputElements(uint vertexArrayHandle)
    {
        if (vertexArrayHandle == 0)
            return [];
        if (!_vertexArrays.TryGetValue(vertexArrayHandle, out D3D12VertexArrayResource? vertexArray))
            throw new InvalidOperationException($"D3D12 PSO references unknown vertex array {vertexArrayHandle}.");
        VertexFormat.Element[] vertexElements = vertexArray.Format.Elements;
        VertexFormat.Element[] instanceElements = vertexArray.InstanceFormat?.Elements ?? [];
        var descriptions = new InputElementDescription[vertexElements.Length + instanceElements.Length];
        AddInputElements(descriptions, 0, vertexElements, 0, InputClassification.PerVertexData);
        AddInputElements(descriptions, vertexElements.Length, instanceElements, 1, InputClassification.PerInstanceData);
        return descriptions;
    }

    private static void AddInputElements(
        InputElementDescription[] descriptions,
        int destinationOffset,
        VertexFormat.Element[] elements,
        uint inputSlot,
        InputClassification classification)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            VertexFormat.Element element = elements[i];
            bool perInstance = classification == InputClassification.PerInstanceData;
            if (perInstance != (element.Divisor > 0))
                throw new InvalidOperationException("D3D12 vertex element divisor does not match its buffer stream.");

            D3D12Formats.GetVertexSemantic(element.Semantic, out string semanticName, out uint semanticIndex);
            descriptions[destinationOffset + i] = new InputElementDescription(
                semanticName,
                semanticIndex,
                D3D12Formats.ToVertexFormat(element.Type, element.Count, element.Normalized),
                inputSlot,
                (uint)element.Offset,
                classification,
                perInstance ? (uint)element.Divisor : 0);
        }
    }

    internal ID3D12Resource CreateCommittedBuffer(ulong size, HeapType heapType, ResourceStates initialState)
    {
        ResourceDescription desc = ResourceDescription.Buffer(size);
        return _device!.CreateCommittedResource(heapType, desc, initialState);
    }

    internal ID3D12Resource CreateCommittedTexture2D(
        uint width,
        uint height,
        Format format,
        ResourceFlags flags,
        ResourceStates initialState)
    {
        ResourceDescription desc = ResourceDescription.Texture2D(format, width, height, 1, 1, 1, 0, flags);
        return _device!.CreateCommittedResource(HeapType.Default, desc, initialState);
    }

    internal ID3D12Resource CreateCommittedTexture3D(
        uint width,
        uint height,
        uint depth,
        Format format,
        ResourceStates initialState)
    {
        ResourceDescription desc = ResourceDescription.Texture3D(
            format,
            width,
            height,
            checked((ushort)depth),
            1);
        return _device!.CreateCommittedResource(HeapType.Default, desc, initialState);
    }

    internal ID3D12Resource CreateCommittedTextureCube(
        uint size,
        uint mipLevels,
        Format format,
        ResourceStates initialState)
    {
        ResourceDescription desc = ResourceDescription.Texture2D(
            format,
            size,
            size,
            6,
            checked((ushort)mipLevels),
            1,
            0,
            ResourceFlags.AllowRenderTarget);
        return _device!.CreateCommittedResource(HeapType.Default, desc, initialState);
    }

    internal void UploadToBuffer(ID3D12Resource destination, ReadOnlySpan<byte> data, uint dstOffset)
    {
        if (data.Length == 0)
            return;

        ulong uploadSize = (ulong)data.Length;
        using ID3D12Resource upload = CreateCommittedBuffer(uploadSize, HeapType.Upload, ResourceStates.GenericRead);
        unsafe
        {
            byte* mapped = upload.Map<byte>(0);
            try
            {
                fixed (byte* src = data)
                    System.Buffer.MemoryCopy(src, mapped, (long)uploadSize, (long)uploadSize);
            }
            finally
            {
                upload.Unmap(0);
            }
        }

        _immediateAllocator!.Reset();
        _immediateList!.Reset(_immediateAllocator);
        _immediateList.CopyBufferRegion(destination, dstOffset, upload, 0, uploadSize);
        _immediateList.Close();
        _queue!.ExecuteCommandList(_immediateList);
        WaitIdle();
    }

    internal void UploadTexture2D(
        ID3D12Resource destination,
        ReadOnlySpan<byte> data,
        uint width,
        uint height,
        int bytesPerPixel,
        ResourceStates before,
        out ResourceStates after,
        uint destinationSubresource = 0)
    {
        uint rowSize = checked(width * (uint)bytesPerPixel);
        int expectedSize = checked((int)(rowSize * height));
        if (data.Length != expectedSize)
            throw new ArgumentException($"D3D12 texture upload expected {expectedSize} bytes, got {data.Length}.", nameof(data));

        uint rowPitch = (rowSize + 255u) & ~255u;
        ulong uploadSize = checked((ulong)rowPitch * height);
        var footprint = new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint(destination.Description, rowPitch),
        };

        using ID3D12Resource upload = CreateCommittedBuffer(uploadSize, HeapType.Upload, ResourceStates.GenericRead);
        unsafe
        {
            byte* mapped = upload.Map<byte>(0);
            try
            {
                fixed (byte* source = data)
                {
                    for (uint row = 0; row < height; row++)
                    {
                        byte* sourceRow = source + row * rowSize;
                        byte* destinationRow = mapped + row * rowPitch;
                        System.Buffer.MemoryCopy(sourceRow, destinationRow, rowPitch, rowSize);
                    }
                }
            }
            finally
            {
                upload.Unmap(0);
            }
        }

        using ID3D12CommandAllocator allocator = _device!.CreateCommandAllocator(CommandListType.Direct);
        using ID3D12GraphicsCommandList list = _device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Direct, allocator, null);
        list.ResourceBarrierTransition(destination, before, ResourceStates.CopyDest);
        list.CopyTextureRegion(
            new TextureCopyLocation(destination, destinationSubresource),
            0,
            0,
            0,
            new TextureCopyLocation(upload, footprint),
            null);
        list.ResourceBarrierTransition(destination, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        list.Close();
        _queue!.ExecuteCommandList(list);
        WaitIdle();
        after = ResourceStates.PixelShaderResource;
    }

    internal void UploadTexture3D(
        ID3D12Resource destination,
        ReadOnlySpan<byte> data,
        uint width,
        uint height,
        uint depth,
        int bytesPerPixel,
        ResourceStates before,
        out ResourceStates after)
    {
        uint rowSize = checked(width * (uint)bytesPerPixel);
        uint sliceSize = checked(rowSize * height);
        int expectedSize = checked((int)(sliceSize * depth));
        if (data.Length != expectedSize)
            throw new ArgumentException($"D3D12 texture upload expected {expectedSize} bytes, got {data.Length}.", nameof(data));

        uint rowPitch = (rowSize + 255u) & ~255u;
        ulong uploadSlicePitch = checked((ulong)rowPitch * height);
        ulong uploadSize = checked(uploadSlicePitch * depth);
        var footprint = new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint(destination.Description, rowPitch),
        };

        using ID3D12Resource upload = CreateCommittedBuffer(uploadSize, HeapType.Upload, ResourceStates.GenericRead);
        unsafe
        {
            byte* mapped = upload.Map<byte>(0);
            try
            {
                fixed (byte* source = data)
                {
                    for (uint slice = 0; slice < depth; slice++)
                    {
                        byte* sourceSlice = source + slice * sliceSize;
                        byte* destinationSlice = mapped + slice * uploadSlicePitch;
                        for (uint row = 0; row < height; row++)
                        {
                            byte* sourceRow = sourceSlice + row * rowSize;
                            byte* destinationRow = destinationSlice + row * rowPitch;
                            System.Buffer.MemoryCopy(sourceRow, destinationRow, rowPitch, rowSize);
                        }
                    }
                }
            }
            finally
            {
                upload.Unmap(0);
            }
        }

        using ID3D12CommandAllocator allocator = _device!.CreateCommandAllocator(CommandListType.Direct);
        using ID3D12GraphicsCommandList list = _device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, allocator, null);
        list.ResourceBarrierTransition(destination, before, ResourceStates.CopyDest);
        list.CopyTextureRegion(new TextureCopyLocation(destination, 0), 0, 0, 0, new TextureCopyLocation(upload, footprint), null);
        list.ResourceBarrierTransition(destination, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        list.Close();
        _queue!.ExecuteCommandList(list);
        WaitIdle();
        after = ResourceStates.PixelShaderResource;
    }

    internal byte[] ReadTexture2D(
        ID3D12Resource source,
        uint width,
        uint height,
        int bytesPerPixel,
        ResourceStates state,
        uint sourceSubresource = 0)
    {
        uint rowSize = checked(width * (uint)bytesPerPixel);
        uint rowPitch = (rowSize + 255u) & ~255u;
        ulong readbackSize = checked((ulong)rowPitch * height);
        var footprint = new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint(source.Description, rowPitch),
        };
        using ID3D12Resource readback = CreateCommittedBuffer(readbackSize, HeapType.Readback, ResourceStates.CopyDest);
        using ID3D12CommandAllocator allocator = _device!.CreateCommandAllocator(CommandListType.Direct);
        using ID3D12GraphicsCommandList list = _device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Direct, allocator, null);
        list.ResourceBarrierTransition(source, state, ResourceStates.CopySource);
        list.CopyTextureRegion(
            new TextureCopyLocation(readback, footprint),
            0,
            0,
            0,
            new TextureCopyLocation(source, sourceSubresource),
            null);
        list.ResourceBarrierTransition(source, ResourceStates.CopySource, state);
        list.Close();
        _queue!.ExecuteCommandList(list);
        WaitIdle();

        var result = new byte[checked((int)(rowSize * height))];
        unsafe
        {
            byte* mapped = readback.Map<byte>(0);
            try
            {
                fixed (byte* destination = result)
                {
                    for (uint row = 0; row < height; row++)
                    {
                        byte* sourceRow = mapped + row * rowPitch;
                        byte* destinationRow = destination + row * rowSize;
                        System.Buffer.MemoryCopy(sourceRow, destinationRow, rowSize, rowSize);
                    }
                }
            }
            finally
            {
                readback.Unmap(0);
            }
        }
        return result;
    }

    // ─────────────────────── Internals ───────────────────────

    private void ExecuteImmediate(CommandBuffer commandBuffer, bool wait)
    {
        List<ID3D12Resource> transientResources = RentTransientResourceList();
        _immediateAllocator!.Reset();
        _immediateList!.Reset(_immediateAllocator);
        _translator!.Translate(commandBuffer, _immediateList, transientResources);
        _immediateList.Close();
        _queue!.ExecuteCommandList(_immediateList);
        ulong fenceValue = Signal();
        if (wait)
        {
            WaitFence(fenceValue);
            DisposeTransientResources(transientResources);
            ReturnTransientResourceList(transientResources);
        }
        else
        {
            _pendingTransientResources.Add((fenceValue, transientResources));
        }
    }

    private void RetireCompletedTransientResources()
    {
        if (_fence == null)
            return;
        ulong completedValue = _fence.CompletedValue;
        for (int i = _pendingTransientResources.Count - 1; i >= 0; i--)
        {
            if (_pendingTransientResources[i].FenceValue > completedValue)
                continue;
            DisposeTransientResources(_pendingTransientResources[i].Resources);
            ReturnTransientResourceList(_pendingTransientResources[i].Resources);
            _pendingTransientResources.RemoveAt(i);
        }
    }

    private static void DisposeTransientResources(List<ID3D12Resource> resources)
    {
        for (int i = 0; i < resources.Count; i++)
            resources[i].Dispose();
        resources.Clear();
    }

    private List<ID3D12Resource> RentTransientResourceList() =>
        _transientResourceListPool.Count > 0 ? _transientResourceListPool.Pop() : new List<ID3D12Resource>();

    private void ReturnTransientResourceList(List<ID3D12Resource> resources)
    {
        resources.Clear();
        _transientResourceListPool.Push(resources);
    }

    private ulong Signal()
    {
        ulong value = ++_fenceValue;
        _queue!.Signal(_fence!, value);
        return value;
    }

    private static D3D12DescriptorAllocation AllocateDescriptor(
        ID3D12DescriptorHeap heap,
        ref int nextDescriptor,
        int capacity,
        int descriptorSize,
        string heapName)
    {
        if (nextDescriptor >= capacity)
            throw new InvalidOperationException($"The D3D12 shader-visible {heapName} descriptor heap is full ({capacity} descriptors).");

        int index = nextDescriptor++;
        int offset = index * descriptorSize;
        return new D3D12DescriptorAllocation(
            heap.GetCPUDescriptorHandleForHeapStart() + offset,
            heap.GetGPUDescriptorHandleForHeapStart() + offset,
            index);
    }

    private void TransitionBackBuffer(ID3D12GraphicsCommandList list, ResourceStates after)
    {
        ID3D12Resource? bb = _backBuffers[_frameIndex];
        if (bb == null)
            return;
        ResourceStates before = _backBufferStates[_frameIndex];
        if (before == after)
            return;
        list.ResourceBarrierTransition(bb, before, after);
        _backBufferStates[_frameIndex] = after;
    }

    private void CreateBackBufferRtvs()
    {
        DestroySwapchainBuffers();
        CpuDescriptorHandle rtvStart = _rtvHeap!.GetCPUDescriptorHandleForHeapStart();
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            _backBuffers[i] = _swapchain!.GetBuffer<ID3D12Resource>((uint)i);
            _rtvHandles[i] = rtvStart + i * _rtvDescriptorSize;
            _device!.CreateRenderTargetView(_backBuffers[i], null, _rtvHandles[i]);
            _backBufferStates[i] = ResourceStates.Common;
        }
    }

    private void CreateHeadlessRenderTarget()
    {
        _headlessRenderTarget = CreateCommittedTexture2D(
            1,
            1,
            Format.R8G8B8A8_UNorm,
            ResourceFlags.AllowRenderTarget,
            ResourceStates.RenderTarget);
        CpuDescriptorHandle start = _rtvHeap!.GetCPUDescriptorHandleForHeapStart();
        _headlessRtv = start + MaxFramesInFlight * _rtvDescriptorSize;
        _device!.CreateRenderTargetView(_headlessRenderTarget, null, _headlessRtv);
        _headlessRenderTargetState = ResourceStates.RenderTarget;
    }

    private void DestroySwapchainBuffers()
    {
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            _backBuffers[i]?.Dispose();
            _backBuffers[i] = null;
        }
    }

    private void QueryCapabilities()
    {
        _capabilities = new GraphicsDeviceCapabilities
        {
            MaxTextureSize = 16384,
            MaxCubeMapTextureSize = 16384,
            MaxArrayTextureLayers = 2048,
            MaxFramebufferColorAttachments = 8,
            MaxFramesInFlight = MaxFramesInFlight,
            SupportsCompute = true,
            SupportsGeometryShader = true,
            BackendName = "Direct3D 12",
        };
    }

    private static IDXGIAdapter1? PickAdapter(IDXGIFactory4 factory, int preferredIndex)
    {
        if (preferredIndex >= 0)
        {
            if (factory.EnumAdapters1((uint)preferredIndex, out IDXGIAdapter1 preferred).Success && preferred != null)
            {
                if (IsSupported(preferred, FeatureLevel.Level_11_0))
                    return preferred;
                preferred.Dispose();
            }
        }

        IDXGIAdapter1? chosen = null;
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1 adapter).Success; i++)
        {
            AdapterDescription1 desc = adapter.Description1;
            bool software = (desc.Flags & AdapterFlags.Software) != 0;
            if (software || !IsSupported(adapter, FeatureLevel.Level_11_0))
            {
                adapter.Dispose();
                continue;
            }

            // Prefer the first hardware adapter that supports D3D12.
            chosen = adapter;
            break;
        }

        if (chosen != null)
            return chosen;

        // Fall back to WARP if present.
        if (factory.EnumWarpAdapter(out IDXGIAdapter warp).Success && warp != null)
        {
            IDXGIAdapter1 warp1 = warp.QueryInterface<IDXGIAdapter1>();
            warp.Dispose();
            if (IsSupported(warp1, FeatureLevel.Level_11_0))
                return warp1;
            warp1.Dispose();
        }

        return null;
    }

    private void DisposeCore()
    {
        _device?.Dispose();
        _device = null;
        _adapter?.Dispose();
        _adapter = null;
        _factory?.Dispose();
        _factory = null;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("D3D12GraphicsDevice has not been initialized.");
    }
}

internal sealed class D3D12BufferResource
{
    public ID3D12Resource? Resource;
    public ulong Size;
    public BufferType Type;
    public bool Dynamic;
    public HeapType HeapType;
}

internal sealed class D3D12TextureResource
{
    public ID3D12Resource? Resource;
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
    public D3D12DescriptorAllocation[] MipGenerationSrvs = Array.Empty<D3D12DescriptorAllocation>();
    public CpuDescriptorHandle[] MipGenerationRtvs = Array.Empty<CpuDescriptorHandle>();
    public ResourceStates State = ResourceStates.Common;
    public D3D12DescriptorAllocation SrvDescriptor;
    public D3D12DescriptorAllocation SamplerDescriptor;
    public bool HasSrvDescriptor;
    public bool HasSamplerDescriptor;
    public TextureWrap WrapS = TextureWrap.Repeat;
    public TextureWrap WrapT = TextureWrap.Repeat;
    public TextureWrap WrapR = TextureWrap.Repeat;
    public TextureMin MinFilter = TextureMin.Linear;
    public TextureMag MagFilter = TextureMag.Linear;
}

internal sealed class D3D12FramebufferResource
{
    public CpuDescriptorHandle Rtv;
    public CpuDescriptorHandle[] Rtvs = Array.Empty<CpuDescriptorHandle>();
    public uint Width;
    public uint Height;
    public Format ColorFormat;
    public D3D12ColorAttachmentFormats ColorFormats;
    public uint ColorHandle;
    public uint[] ColorHandles = Array.Empty<uint>();
    public uint ColorSubresource;
    public uint[] ColorSubresources = Array.Empty<uint>();
    public bool SubresourceOnly;
    public bool[] SubresourceOnlyByAttachment = Array.Empty<bool>();
    public CpuDescriptorHandle Dsv;
    public Format DepthFormat;
    public uint DepthHandle;
}

internal readonly struct D3D12GraphicsPipelineKey : IEquatable<D3D12GraphicsPipelineKey>
{
    private readonly GraphicsPipelineKey _pipeline;
    private readonly D3D12RenderTargetFormats _targetFormats;

    public D3D12GraphicsPipelineKey(GraphicsPipelineKey pipeline, D3D12RenderTargetFormats targetFormats)
    {
        _pipeline = pipeline;
        _targetFormats = targetFormats;
    }

    public bool Equals(D3D12GraphicsPipelineKey other) =>
        _pipeline.Equals(other._pipeline) && _targetFormats.Equals(other._targetFormats);

    public override bool Equals(object? obj) => obj is D3D12GraphicsPipelineKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_pipeline, _targetFormats);
}

internal readonly struct D3D12RenderTargetFormats : IEquatable<D3D12RenderTargetFormats>
{
    public D3D12RenderTargetFormats(D3D12ColorAttachmentFormats colorFormats, Format depthFormat)
    {
        ColorFormats = colorFormats;
        DepthFormat = depthFormat;
    }

    public D3D12ColorAttachmentFormats ColorFormats { get; }
    public Format DepthFormat { get; }

    public bool Equals(D3D12RenderTargetFormats other) =>
        ColorFormats.Equals(other.ColorFormats) && DepthFormat == other.DepthFormat;

    public override bool Equals(object? obj) => obj is D3D12RenderTargetFormats other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ColorFormats, DepthFormat);
}

internal readonly struct D3D12ColorAttachmentFormats : IEquatable<D3D12ColorAttachmentFormats>
{
    private readonly Format _format0;
    private readonly Format _format1;
    private readonly Format _format2;
    private readonly Format _format3;
    private readonly Format _format4;
    private readonly Format _format5;
    private readonly Format _format6;
    private readonly Format _format7;

    public D3D12ColorAttachmentFormats(Format format)
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

    public D3D12ColorAttachmentFormats(ReadOnlySpan<Format> formats)
    {
        if (formats.Length is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(formats));
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

    private Format Get(int index) => index switch
    {
        0 => _format0,
        1 => _format1,
        2 => _format2,
        3 => _format3,
        4 => _format4,
        5 => _format5,
        6 => _format6,
        7 => _format7,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public Format[] ToArray()
    {
        var formats = new Format[Count];
        for (int i = 0; i < Count; i++)
            formats[i] = Get(i);
        return formats;
    }

    public bool Equals(D3D12ColorAttachmentFormats other) =>
        Count == other.Count && _format0 == other._format0 && _format1 == other._format1 &&
        _format2 == other._format2 && _format3 == other._format3 && _format4 == other._format4 &&
        _format5 == other._format5 && _format6 == other._format6 && _format7 == other._format7;

    public override bool Equals(object? obj) => obj is D3D12ColorAttachmentFormats other && Equals(other);

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

internal sealed class D3D12VertexArrayResource
{
    public uint VertexBuffer;
    public uint IndexBuffer;
    public uint InstanceBuffer;
    public VertexFormat Format = null!;
    public VertexFormat? InstanceFormat;
}

internal sealed class D3D12ShaderLayoutResource
{
    public ID3D12RootSignature RootSignature = null!;
    public ShaderBindingLayout BindingLayout = null!;
}
