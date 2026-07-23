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
    private ID3D12DescriptorHeap? _cbvSrvUavHeap;
    private ID3D12DescriptorHeap? _samplerHeap;
    private int _rtvDescriptorSize;
    private int _cbvSrvUavDescriptorSize;
    private int _samplerDescriptorSize;
    private int _nextSrvDescriptor;
    private int _nextSamplerDescriptor;

    private readonly ID3D12CommandAllocator?[] _frameAllocators = new ID3D12CommandAllocator?[MaxFramesInFlight];
    private readonly ID3D12GraphicsCommandList?[] _frameLists = new ID3D12GraphicsCommandList?[MaxFramesInFlight];

    private ID3D12CommandAllocator? _immediateAllocator;
    private ID3D12GraphicsCommandList? _immediateList;

    private IDXGISwapChain3? _swapchain;
    private readonly ID3D12Resource?[] _backBuffers = new ID3D12Resource?[MaxFramesInFlight];
    private readonly CpuDescriptorHandle[] _rtvHandles = new CpuDescriptorHandle[MaxFramesInFlight];
    private ResourceStates[] _backBufferStates = new ResourceStates[MaxFramesInFlight];
    private ID3D12Resource? _headlessRenderTarget;
    private CpuDescriptorHandle _headlessRtv;

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
    private readonly Dictionary<uint, D3D12VertexArrayResource> _vertexArrays = new();
    private readonly Dictionary<int, D3D12ShaderLayoutResource> _shaderLayouts = new();
    private readonly Dictionary<GraphicsPipelineKey, ID3D12PipelineState> _graphicsPipelines = new();

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
    internal CpuDescriptorHandle CurrentRtv => HasSwapchain ? _rtvHandles[_frameIndex] : _headlessRtv;
    internal bool HasSwapchain => _swapchain != null;
    internal int FrameIndex => _frameIndex;
    internal Dictionary<uint, D3D12BufferResource> Buffers => _buffers;
    internal Dictionary<uint, D3D12TextureResource> Textures => _textures;
    internal Dictionary<uint, D3D12VertexArrayResource> VertexArrays => _vertexArrays;

    internal uint AllocateHandle() => Interlocked.Increment(ref _nextHandle) - 1;

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
            DescriptorHeapType.RenderTargetView, MaxFramesInFlight + 1));
        _cbvSrvUavHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            CbvSrvUavHeapSize,
            DescriptorHeapFlags.ShaderVisible));
        _samplerHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.Sampler,
            SamplerHeapSize,
            DescriptorHeapFlags.ShaderVisible));
        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
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
                BufferUsage = Usage.RenderTargetOutput,
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

        foreach (var kv in _buffers)
            kv.Value.Resource?.Dispose();
        _buffers.Clear();
        foreach (var kv in _textures)
            kv.Value.Resource?.Dispose();
        _textures.Clear();
        _vertexArrays.Clear();
        foreach (var kv in _shaderLayouts)
            kv.Value.RootSignature.Dispose();
        _shaderLayouts.Clear();
        foreach (var kv in _graphicsPipelines)
            kv.Value.Dispose();
        _graphicsPipelines.Clear();

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
                _translator!.Translate(commandBuffer, CurrentFrameList);
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
            return;

        _fence.SetEventOnCompletion(fenceValue, _fenceEvent).CheckError();
        _fenceEvent.WaitOne();
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
    {
        if (_graphicsPipelines.TryGetValue(key, out ID3D12PipelineState? cached))
            return cached;

        RasterizerState raster = key.RasterState;
        if (raster.DepthTest || raster.DepthWrite || raster.StencilEnabled || raster.DoBlend)
            throw new NotSupportedException("The current D3D12 PSO slice supports no depth, stencil, or blending.");

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
            BlendState = BlendDescription.Opaque,
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
            DepthStencilState = DepthStencilDescription.None,
            InputLayout = new InputLayoutDescription(inputElements),
            PrimitiveTopologyType = D3D12Formats.ToTopologyType(key.Topology),
            RenderTargetFormats = [Format.R8G8B8A8_UNorm],
            DepthStencilFormat = Format.Unknown,
            SampleDescription = new SampleDescription(1, 0),
        };
        ID3D12PipelineState pipeline = _device!.CreateGraphicsPipelineState(description);
        _graphicsPipelines.Add(key, pipeline);
        return pipeline;
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

    // ─────────────────────── Internals ───────────────────────

    private void ExecuteImmediate(CommandBuffer commandBuffer, bool wait)
    {
        _immediateAllocator!.Reset();
        _immediateList!.Reset(_immediateAllocator);
        _translator!.Translate(commandBuffer, _immediateList);
        _immediateList.Close();
        _queue!.ExecuteCommandList(_immediateList);
        if (wait)
            WaitIdle();
        else
            Signal();
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
    public ResourceStates State = ResourceStates.Common;
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
