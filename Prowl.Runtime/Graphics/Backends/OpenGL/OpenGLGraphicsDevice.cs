// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

using Silk.NET.Core.Native;
using Silk.NET.OpenGL;

using Prowl.Runtime.RHI;

namespace Prowl.Runtime.Backends.OpenGL;

/// <summary>
/// OpenGL <see cref="IGraphicsDevice"/> owning the render-thread FIFO, GL context
/// lifetime, and <see cref="CommandExecutor"/>. Preserves the historical
/// <c>Graphics</c> submit / frame-end semantics.
/// </summary>
public sealed unsafe class OpenGLGraphicsDevice : IGraphicsDevice
{
    private readonly GraphicsDeviceOptions _options;
    private readonly CommandExecutor _executor = new();
    private readonly BlockingCollection<CBJob> _renderQueue = new();
    private readonly ManualResetEventSlim _frameDone = new(true);

    private IWindowSurface? _surface;
    private Thread? _renderThread;
    private GL? _gl;
    private GraphicsDeviceCapabilities _capabilities = new() { BackendName = "OpenGL" };
    private bool _initialized;
    private bool _threadStarted;
    private bool _shutdown;
    private ulong _fenceValue;
    private float _lastFrameWaitMs;

    /// <summary>Set by <see cref="Graphics.Initialize"/> so debug follows the facade flag even if the device was created earlier.</summary>
    internal bool PendingDebug { get; set; }

    public OpenGLGraphicsDevice(GraphicsDeviceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public GraphicsDeviceCapabilities Capabilities => _capabilities;

    public bool IsInitialized => _initialized;

    /// <summary>Silk.NET GL wrapper; set during <see cref="Initialize"/>.</summary>
    internal GL? GLApi => _gl;

    /// <summary>Long-lived executor so its raster-state cache survives across CBs.</summary>
    internal CommandExecutor Executor => _executor;

    /// <summary>Time the main thread spent blocked in <see cref="EndFrame"/> last frame.</summary>
    internal float LastFrameWaitMs => _lastFrameWaitMs;

    /// <summary>
    /// Hand the GL context off the calling thread and start the continuous drain loop.
    /// Must run before <see cref="Initialize"/> so Load-time <c>SubmitAndWait</c> has a live drain.
    /// </summary>
    internal void StartRenderThread(IWindowSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (_threadStarted)
            return;
        if (_shutdown)
            throw new ObjectDisposedException(nameof(OpenGLGraphicsDevice));

        _surface = surface;
        // Hand the context off the main thread so the render thread can MakeCurrent.
        surface.ClearCurrent();
        _renderThread = new Thread(RenderThreadLoop)
        {
            IsBackground = true,
            Name = "Prowl GL Render Thread",
        };
        _threadStarted = true;
        _renderThread.Start();
    }

    public void Initialize(IWindowSurface? surface)
    {
        if (_shutdown)
            throw new ObjectDisposedException(nameof(OpenGLGraphicsDevice));

        if (surface != null)
            _surface = surface;

        if (_surface == null)
            throw new InvalidOperationException("OpenGLGraphicsDevice.Initialize requires an IWindowSurface.");

        IWindowSurface winSurface = _surface;
        Silk.NET.Windowing.IWindow silkWindow = winSurface is SilkWindowSurface silk
            ? silk.Window
            : throw new InvalidOperationException(
                "OpenGLGraphicsDevice currently requires a SilkWindowSurface (Silk.NET IWindow).");

        _gl = GL.GetApi(silkWindow);

        bool debug = PendingDebug || _options.Debug || _options.EnableValidation;
        if (debug && OperatingSystem.IsWindows())
        {
            _gl.DebugMessageCallback(DebugCallback, null);
            _gl.Enable(EnableCap.DebugOutput);
            _gl.Enable(EnableCap.DebugOutputSynchronous);
        }

        _gl.Enable(EnableCap.LineSmooth);

        // Seamless cubemap filtering removes the visible face seams when sampling a
        // cubemap with linear/trilinear filtering. Required for clean reflection-probe
        // and prefiltered-environment sampling.
        _gl.Enable(EnableCap.TextureCubeMapSeamless);

        _capabilities = new GraphicsDeviceCapabilities
        {
            MaxTextureSize = _gl.GetInteger(GLEnum.MaxTextureSize),
            MaxCubeMapTextureSize = _gl.GetInteger(GLEnum.MaxCubeMapTextureSize),
            MaxArrayTextureLayers = _gl.GetInteger(GLEnum.MaxArrayTextureLayers),
            MaxFramebufferColorAttachments = _gl.GetInteger(GLEnum.MaxColorAttachments),
            MaxFramesInFlight = 1,
            SupportsCompute = false,
            SupportsGeometryShader = true,
            BackendName = "OpenGL 4.1",
        };

        _initialized = true;
    }

    public void Shutdown()
    {
        if (_shutdown)
            return;
        _shutdown = true;

        // CompleteAdding makes the render thread's Take throw once the queue is
        // drained, so it finishes any pending work and then exits cleanly.
        try { _renderQueue.CompleteAdding(); } catch { /* already completed */ }
        _renderThread?.Join();
        _renderThread = null;
        _threadStarted = false;

        try { _surface?.MakeCurrent(); } catch { /* context may already be gone */ }

        _gl?.Dispose();
        _gl = null;
        _initialized = false;
        _frameDone.Set();
    }

    public void BeginFrame()
    {
        // Arm the frame-done gate so EndFrame blocks until THIS frame's
        // sentinel is processed. The render thread is always draining, so there's
        // nothing to wake.
        _frameDone.Reset();
    }

    public void EndFrame()
    {
        _renderQueue.Add(new CBJob { IsFrameEnd = true });
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        _frameDone.Wait();
        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
        _lastFrameWaitMs = (float)(elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        _fenceValue++;
    }

    public void Execute(CommandBuffer commandBuffer, bool wait)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        if (commandBuffer._inPool)
            throw new InvalidOperationException("CommandBuffer has already been submitted (it's in the pool).");
        if (_shutdown || !_threadStarted)
            throw new InvalidOperationException("OpenGLGraphicsDevice render thread is not running.");

        commandBuffer._submitted = true;
        commandBuffer._ownerReleased = true;

        if (!wait)
        {
            _renderQueue.Add(new CBJob { Cmd = commandBuffer });
            return;
        }

        var job = new CBJob { Cmd = commandBuffer, Done = new ManualResetEventSlim(false) };
        _renderQueue.Add(job);
        job.Done.Wait();
        job.Done.Dispose();
        job.Error?.Throw();
        _fenceValue++;
    }

    public void WaitIdle()
    {
        if (!_threadStarted || _shutdown)
            return;

        var done = new ManualResetEventSlim(false);
        _renderQueue.Add(new CBJob { Done = done });
        done.Wait();
        done.Dispose();
    }

    public ulong GetFenceValue() => _fenceValue;

    public void WaitFence(ulong fenceValue)
    {
        // OpenGL path completes work in FIFO order; drain until idle if the caller
        // asks past the last recorded fence (conservative).
        if (fenceValue > _fenceValue)
            WaitIdle();
    }

    public void Dispose() => Shutdown();

    private void RenderThreadLoop()
    {
        IWindowSurface? surface = _surface;
        if (surface == null)
        {
            _frameDone.Set();
            return;
        }

        // Take the GL context once and hold it for the entire run. The frame-end
        // sentinel does SwapBuffers; the context never bounces back to main.
        try { surface.MakeCurrent(); }
        catch (Exception ex)
        {
            Debug.LogError($"Render thread MakeCurrent failed: {ex}");
            _frameDone.Set();
            return;
        }

        try
        {
            // Single continuous drain loop. Jobs execute in submit order as they
            // arrive, so resource-creation and SubmitAndWait jobs enqueued between
            // frames or from background threads are serviced without waiting for the
            // next BeginFrame. SwapBuffers + frame-done signalling happen only on the
            // frame-end sentinel pushed by EndFrame.
            while (true)
            {
                CBJob job;
                try { job = _renderQueue.Take(); }
                catch (InvalidOperationException) { break; } // CompleteAdding + drained

                if (job.IsFrameEnd)
                {
                    try { surface.SwapBuffers(); }
                    catch (Exception ex) { Debug.LogError($"SwapBuffers failed: {ex}"); }
                    finally { _frameDone.Set(); }
                    continue;
                }

                if (job.Cmd == null)
                {
                    // Idle / WaitIdle sentinel — no CB to execute.
                    job.Done?.Set();
                    continue;
                }

                var cmd = job.Cmd;
                bool pushed = PushCBDebugGroup(cmd.Name);
                try { _executor.Execute(cmd); }
                catch (Exception ex)
                {
                    job.Error = ExceptionDispatchInfo.Capture(ex);
                    if (job.Done == null)
                        Debug.LogError($"Render thread CB '{cmd.Name ?? "<?>"}' execute failed: {ex}");
                }
                finally
                {
                    if (pushed) PopCBDebugGroup();
                    CommandBufferPool.Return(cmd);
                    job.Done?.Set();
                }
            }
        }
        finally
        {
            try { surface.ClearCurrent(); } catch { /* ignore */ }
        }
    }

#if DEBUG
    private bool PushCBDebugGroup(string? label)
    {
        if (string.IsNullOrEmpty(label) || _gl == null) return false;
        try
        {
            _gl.PushDebugGroup(Silk.NET.OpenGL.DebugSource.DebugSourceApplication, 0, (uint)label.Length, label);
            return true;
        }
        catch { return false; }
    }

    private void PopCBDebugGroup()
    {
        try { _gl?.PopDebugGroup(); } catch { /* ignore */ }
    }
#else
    private static bool PushCBDebugGroup(string? label) => false;
    private static void PopCBDebugGroup() { }
#endif

    private static void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        string? msg = SilkMarshal.PtrToString(message, NativeStringEncoding.UTF8);
        if (type == GLEnum.DebugTypeError || type == GLEnum.DebugTypeUndefinedBehavior)
            Debug.LogError($"OpenGL Error: {msg}");
        else if (type == GLEnum.DebugTypePerformance || type == GLEnum.DebugTypeMarker || type == GLEnum.DebugTypePortability)
            Debug.LogWarning($"OpenGL Warning: {msg}");
    }

    private sealed class CBJob
    {
        public CommandBuffer? Cmd;
        public ManualResetEventSlim? Done;
        public ExceptionDispatchInfo? Error;
        public bool IsFrameEnd;
    }
}
