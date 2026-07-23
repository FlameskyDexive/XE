// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Backends.OpenGL;
using Prowl.Runtime.RHI;

using Silk.NET.OpenGL;

namespace Prowl.Runtime;

/// <summary>
/// Facade over the active <see cref="IGraphicsDevice"/> and the <see cref="CommandBuffer"/>
/// system. Hosts capability constants, resource constructors, and convenience encoders.
/// OpenGL-specific render-thread ownership lives on <see cref="OpenGLGraphicsDevice"/>.
/// </summary>
public static unsafe class Graphics
{
    // Defaulted to conservative real-world minimums so CPU-side validation (e.g. texture size
    // checks) passes before/without a GL context. Initialize() overwrites them with real device
    // limits when a graphics device is present.
    public static int MaxTextureSize { get; internal set; } = 16384;
    public static int MaxCubeMapTextureSize { get; internal set; } = 16384;
    public static int MaxArrayTextureLayers { get; internal set; } = 2048;
    public static int MaxFramebufferColorAttachments { get; internal set; } = 8;

    /// <summary>Active graphics device, or null before creation / after dispose.</summary>
    public static IGraphicsDevice? Device { get; private set; }

    /// <summary>
    /// Transitional Silk.NET GL wrapper for resource helpers that still call GL directly.
    /// Populated from <see cref="OpenGLGraphicsDevice"/> during Initialize. Prefer going
    /// through CommandBuffers / the device; do not add new direct GL call sites.
    /// </summary>
    public static GL GL;

    /// <summary>
    /// True when there is no graphics device (no window / render thread) - a dedicated server or a
    /// build launched with --headless. GPU command submission becomes a no-op so gameplay code that
    /// creates or touches GPU resources (materials, terrain, render textures, etc.) runs without
    /// crashing; read-backs return their default (zeroed) contents.
    /// </summary>
    public static bool IsHeadless =>
        Device == null
        || Device.Backend == GraphicsBackend.Null
        || !Device.IsInitialized;

    public static GraphicsProgram CurrentProgram => GraphicsProgram.currentProgram;

    /// <summary>Long-lived executor so its raster-state cache survives across CBs.</summary>
    internal static CommandExecutor Executor =>
        Device is OpenGLGraphicsDevice gl
            ? gl.Executor
            : throw new InvalidOperationException("CommandExecutor requires an OpenGL graphics device.");

    public static CommandBuffer GetCommandBuffer(string? name = null) => CommandBufferPool.Rent(name);

    /// <summary>Enqueue a CB for the render thread to execute. Fire-and-forget.</summary>
    public static void Submit(CommandBuffer cmd)
    {
        if (cmd == null) return;
        if (cmd._inPool)
            throw new InvalidOperationException("CommandBuffer has already been submitted (it's in the pool).");
        // No graphics device: drop GPU work and recycle the buffer instead of queueing it for a
        // render thread that will never drain it (which would leak the buffer).
        if (IsHeadless)
        {
            cmd._ownerReleased = true;
            CommandBufferPool.Return(cmd);
            return;
        }
        Device!.Execute(cmd, wait: false);
    }

    /// <summary>Enqueue and block until the render thread has finished the CB.
    /// Use for read-backs, shader compile error propagation, and FBO completeness
    /// checks. Render-thread exceptions rethrow on the caller's thread.</summary>
    public static void SubmitAndWait(CommandBuffer cmd)
    {
        if (cmd == null) return;
        if (cmd._inPool)
            throw new InvalidOperationException("CommandBuffer has already been submitted (it's in the pool).");
        // No graphics device: nothing executes, so don't block waiting on a render thread. Any
        // read-back this would have filled keeps its default (zeroed) contents.
        if (IsHeadless)
        {
            cmd._ownerReleased = true;
            CommandBufferPool.Return(cmd);
            return;
        }
        Device!.Execute(cmd, wait: true);
    }

    internal static void BeginFrame()
    {
        Device?.BeginFrame();
    }

    /// <summary>Time the main thread spent blocked in <see cref="EndFrameAndWait"/>
    /// last frame. High = render thread is bottleneck. Near-zero = main is.</summary>
    public static float LastFrameWaitMs { get; private set; }

    internal static void EndFrameAndWait()
    {
        if (Device == null)
            return;
        Device.EndFrame();
        if (Device is OpenGLGraphicsDevice gl)
            LastFrameWaitMs = gl.LastFrameWaitMs;
    }

    public static void Initialize(bool debug)
    {
        EnsureDevice(debug);
        if (Device is OpenGLGraphicsDevice glPending)
            glPending.PendingDebug = debug;

        IWindowSurface? surface = Window.InternalWindow != null
            ? new SilkWindowSurface(Window.InternalWindow)
            : null;
        Device!.Initialize(surface);

        if (Device is OpenGLGraphicsDevice glDevice)
            GL = glDevice.GLApi!;

        GraphicsDeviceCapabilities caps = Device.Capabilities;
        MaxTextureSize = caps.MaxTextureSize;
        MaxCubeMapTextureSize = caps.MaxCubeMapTextureSize;
        MaxArrayTextureLayers = caps.MaxArrayTextureLayers;
        MaxFramebufferColorAttachments = caps.MaxFramebufferColorAttachments;
    }

    /// <summary>
    /// Creates the preferred graphics device (if needed) and, for OpenGL, starts its render
    /// thread with the GL context. Must run before <see cref="Initialize"/> so Load-time
    /// SubmitAndWait has a live drain.
    /// </summary>
    public static void StartRenderThread()
    {
        EnsureDevice(debug: false);
        if (Device is OpenGLGraphicsDevice glDevice)
            glDevice.StartRenderThread(new SilkWindowSurface(Window.InternalWindow));
    }

    /// <summary>
    /// Create the device for <see cref="GraphicsBackendSelection.Preferred"/> (or CLI)
    /// without starting presentation. Used by tests and explicit host bootstraps.
    /// When the resolved backend is <see cref="GraphicsBackend.Auto"/>, OpenGL is used so
    /// the Silk window context created for Auto stays matched. Prefer an explicit
    /// <c>--graphics=</c> value for Vulkan / Direct3D12.
    /// </summary>
    public static void EnsureDevice(bool debug = false)
    {
        if (Device != null)
            return;

        GraphicsBackend backend = GraphicsBackendSelection.Preferred
            ?? GraphicsBackendSelection.Parse(Environment.GetCommandLineArgs());

        // Auto keeps the OpenGL window path stable. Explicit backends use Factory as-is
        // (with Auto's multi-candidate fallback only when Backend=Auto is forced).
        if (backend == GraphicsBackend.Auto)
            backend = GraphicsBackend.OpenGL;

        Device = GraphicsDeviceFactory.Create(new GraphicsDeviceOptions
        {
            Backend = backend,
            Debug = debug,
            EnableValidation = debug,
            VSync = Window.InternalWindow?.VSync ?? true,
        });
    }

    public static void Dispose()
    {
        Device?.Shutdown();
        Device?.Dispose();
        Device = null;
        GL = null!;
    }

    // ─────────────────────── Resource creation ───────────────────────

    public static GraphicsBuffer CreateBuffer<T>(BufferType bufferType, T[] data, bool dynamic = false) where T : unmanaged
    {
        // Convert the typed array to a byte span. The GraphicsBuffer constructor
        // copies the bytes into a CommandBuffer's transient store so the caller's
        // T[] can be freed/reused immediately after this returns.
        return new GraphicsBuffer(bufferType, System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan()), dynamic);
    }

    public static GraphicsVertexArray CreateVertexArray(
        VertexFormat format,
        GraphicsBuffer vertices,
        GraphicsBuffer? indices,
        VertexFormat? instanceFormat = null,
        GraphicsBuffer? instanceBuffer = null)
    {
        return new GraphicsVertexArray(format, vertices, indices, instanceFormat, instanceBuffer);
    }

    public static GraphicsFrameBuffer CreateFramebuffer(GraphicsFrameBuffer.Attachment[] attachments, uint width, uint height)
        => new GraphicsFrameBuffer(attachments, width, height);

    public static GraphicsTexture CreateTexture(TextureType type, TextureImageFormat format)
        => new GraphicsTexture(type, format);

    public static GraphicsProgram CompileProgram(string fragment, string vertex, string geometry)
        => new GraphicsProgram(fragment, vertex, geometry);

    // Resources replaced mid-frame (e.g. an instance buffer that grows) can't be
    // disposed immediately because earlier encoded CBs still reference the old
    // handle. DeferDispose queues them FlushDeferredDisposes runs once per frame
    // after all CBs have executed.
    private static readonly System.Collections.Generic.List<System.IDisposable> s_deferredDisposes = new();

    public static void DeferDispose(System.IDisposable resource)
    {
        if (resource == null) return;
        lock (s_deferredDisposes)
            s_deferredDisposes.Add(resource);
    }

    public static void FlushDeferredDisposes()
    {
        lock (s_deferredDisposes)
        {
            for (int i = 0; i < s_deferredDisposes.Count; i++)
                s_deferredDisposes[i].Dispose();
            s_deferredDisposes.Clear();
        }
    }

    // Convenience encoders for sticky texture state. Each rents a one-op CB and
    // submits it so the mutation runs on the render thread in submit order.

    public static void SetWrapS(GraphicsTexture texture, TextureWrap wrap) => EncodeOneOp(c => c.EncodeSetTextureWrap(texture, 0, wrap), "Texture.SetWrapS");
    public static void SetWrapT(GraphicsTexture texture, TextureWrap wrap) => EncodeOneOp(c => c.EncodeSetTextureWrap(texture, 1, wrap), "Texture.SetWrapT");
    public static void SetWrapR(GraphicsTexture texture, TextureWrap wrap) => EncodeOneOp(c => c.EncodeSetTextureWrap(texture, 2, wrap), "Texture.SetWrapR");
    public static void SetTextureFilters(GraphicsTexture texture, TextureMin min, TextureMag mag) => EncodeOneOp(c => c.EncodeSetTextureFilters(texture, min, mag), "Texture.SetFilters");
    public static void SetTextureCompareMode(GraphicsTexture texture, bool enabled) => EncodeOneOp(c => c.EncodeSetTextureCompareMode(texture, enabled), "Texture.SetCompareMode");
    public static void GenerateMipmap(GraphicsTexture texture) => EncodeOneOp(c => c.GenerateMipmap(texture), "Texture.GenerateMipmap");

    /// <summary>Synchronous texture read-back. Blocks until the destination is filled.</summary>
    public static unsafe void GetTexImage(GraphicsTexture texture, int mip, void* data)
    {
        using var cmd = GetCommandBuffer("Texture.GetTexImage");
        cmd.EncodeGetTextureDataPtr(texture, mip, (nint)data);
        SubmitAndWait(cmd);
    }

    /// <summary>Synchronous read-back of one cubemap face's mip level. Blocks until filled.</summary>
    public static void GetTexImageCubeFace(GraphicsTexture texture, int face, int mip, byte[] destination)
    {
        using var cmd = GetCommandBuffer("Texture.GetTexImageCubeFace");
        cmd.EncodeGetTextureCubeFaceData(texture, face, mip, destination);
        SubmitAndWait(cmd);
    }

    public static unsafe void TexImage2D(GraphicsTexture texture, int mip, uint width, uint height, int border, void* data)
    {
        int size = data != null ? (int)(width * height * BytesPerPixel(texture)) : 0;
        ReadOnlySpan<byte> span = data != null ? new ReadOnlySpan<byte>(data, size) : ReadOnlySpan<byte>.Empty;
        using var cmd = GetCommandBuffer("Texture.TexImage2D");
        cmd.EncodeAllocateTexture2D(texture, mip, width, height, border, span);
        Submit(cmd);
    }

    public static unsafe void TexSubImage2D(GraphicsTexture texture, int mip, int x, int y, uint width, uint height, void* data)
    {
        if (data == null) return;
        int size = (int)(width * height * BytesPerPixel(texture));
        var span = new ReadOnlySpan<byte>(data, size);
        using var cmd = GetCommandBuffer("Texture.TexSubImage2D");
        cmd.EncodeUpdateTexture2D(texture, mip, x, y, width, height, span);
        Submit(cmd);
    }

    /// <summary>Allocate (and optionally upload) one face of a cubemap at a mip level.
    /// <paramref name="face"/> is 0..5 in GL order (+X, -X, +Y, -Y, +Z, -Z).</summary>
    public static unsafe void TexImageCubeFace(GraphicsTexture texture, int face, int mip, uint size, void* data)
    {
        int byteSize = data != null ? (int)(size * size * BytesPerPixel(texture)) : 0;
        ReadOnlySpan<byte> span = data != null ? new ReadOnlySpan<byte>(data, byteSize) : ReadOnlySpan<byte>.Empty;
        using var cmd = GetCommandBuffer("Texture.TexImageCubeFace");
        cmd.EncodeAllocateTextureCubeFace(texture, face, mip, size, span);
        Submit(cmd);
    }

    public static unsafe void TexImage3D(GraphicsTexture texture, int level, uint width, uint height, uint depth, void* data)
    {
        int size = data != null ? (int)(width * height * depth * BytesPerPixel(texture)) : 0;
        ReadOnlySpan<byte> span = data != null ? new ReadOnlySpan<byte>(data, size) : ReadOnlySpan<byte>.Empty;
        using var cmd = GetCommandBuffer("Texture.TexImage3D");
        cmd.EncodeAllocateTexture3D(texture, level, width, height, depth, span);
        Submit(cmd);
    }

    public static unsafe void TexSubImage3D(GraphicsTexture texture, int level, int x, int y, int z, uint width, uint height, uint depth, void* data)
    {
        if (data == null) return;
        int size = (int)(width * height * depth * BytesPerPixel(texture));
        var span = new ReadOnlySpan<byte>(data, size);
        using var cmd = GetCommandBuffer("Texture.TexSubImage3D");
        cmd.EncodeUpdateTexture3D(texture, level, x, y, z, width, height, depth, span);
        Submit(cmd);
    }

    /// <summary>Bytes per pixel under tight packing, used to size copies into the
    /// transient store. Mirrors the GL spec's pixel cost (no row alignment).</summary>
    private static int BytesPerPixel(GraphicsTexture tex) => tex.PixelInternalFormat switch
    {
        InternalFormat.R8 or InternalFormat.R8i or InternalFormat.R8ui => 1,
        InternalFormat.RG8 or InternalFormat.RG8i or InternalFormat.RG8ui => 2,
        InternalFormat.Rgb8 or InternalFormat.Rgb8i or InternalFormat.Rgb8ui => 3,
        InternalFormat.Rgba8 or InternalFormat.Rgba8i or InternalFormat.Rgba8ui => 4,
        InternalFormat.R16 or InternalFormat.R16f or InternalFormat.R16i or InternalFormat.R16ui => 2,
        InternalFormat.RG16 or InternalFormat.RG16f or InternalFormat.RG16i or InternalFormat.RG16ui => 4,
        InternalFormat.Rgb16 or InternalFormat.Rgb16f or InternalFormat.Rgb16i or InternalFormat.Rgb16ui => 6,
        InternalFormat.Rgba16 or InternalFormat.Rgba16f or InternalFormat.Rgba16i or InternalFormat.Rgba16ui => 8,
        InternalFormat.R32f or InternalFormat.R32i or InternalFormat.R32ui => 4,
        InternalFormat.RG32f or InternalFormat.RG32i or InternalFormat.RG32ui => 8,
        InternalFormat.Rgb32f or InternalFormat.Rgb32i or InternalFormat.Rgb32ui => 12,
        InternalFormat.Rgba32f or InternalFormat.Rgba32i or InternalFormat.Rgba32ui => 16,
        InternalFormat.DepthComponent16 => 2,
        InternalFormat.DepthComponent24 => 4,
        InternalFormat.DepthComponent32f => 4,
        InternalFormat.Depth24Stencil8 => 4,
        _ => 4,
    };

    private static void EncodeOneOp(Action<CommandBuffer> encode, string name)
    {
        using var cmd = GetCommandBuffer(name);
        encode(cmd);
        Submit(cmd);
    }
}
