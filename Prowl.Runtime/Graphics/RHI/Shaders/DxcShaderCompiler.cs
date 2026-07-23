// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Prowl.Runtime.RHI.Shaders;

/// <summary>
/// Process-based DXC wrapper: compiles HLSL to SPIR-V (Vulkan) or DXIL (D3D12).
/// Locates <c>dxc.exe</c> on PATH, Windows Kits, or VulkanSDK. Missing DXC yields a
/// clear <see cref="ShaderCompileResult"/> error — never throws for headless / CI.
/// </summary>
public sealed class DxcShaderCompiler : IShaderCompiler
{
    private static readonly object s_locateLock = new();
    private static string? s_cachedDxcPath;
    private static bool s_locateAttempted;

    private readonly string? _dxcPathOverride;

    public DxcShaderCompiler(string? dxcPathOverride = null)
    {
        _dxcPathOverride = dxcPathOverride;
    }

    /// <summary>Resolved path to dxc.exe, or null if not found.</summary>
    public string? ResolvedDxcPath => _dxcPathOverride ?? LocateDxc();

    public ShaderCompileResult Compile(ShaderCompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Language != ShaderLanguage.Hlsl)
        {
            return ShaderCompileResult.Fail(
                $"DxcShaderCompiler only accepts HLSL source (got {request.Language}).");
        }

        if (string.IsNullOrWhiteSpace(request.VertexSource))
            return ShaderCompileResult.Fail("Vertex source is null or empty.");
        if (string.IsNullOrWhiteSpace(request.FragmentSource))
            return ShaderCompileResult.Fail("Fragment source is null or empty.");

        string? dxc = ResolvedDxcPath;
        if (string.IsNullOrEmpty(dxc))
        {
            return ShaderCompileResult.Fail(
                "DXC (dxc.exe) was not found. Install the Windows SDK, VulkanSDK, or add dxc to PATH.");
        }

        bool spirv = request.TargetBackend == GraphicsBackend.Vulkan;
        // Prefer a SPIR-V-capable DXC (Vulkan SDK) when targeting Vulkan; Windows Kits DXC often
        // lacks -DENABLE_SPIRV_CODEGEN.
        if (spirv)
        {
            string? spirvDxc = LocateDxcPreferringSpirv();
            if (!string.IsNullOrEmpty(spirvDxc))
                dxc = spirvDxc;
        }

        ShaderBytecodeFormat format = spirv ? ShaderBytecodeFormat.SpirV : ShaderBytecodeFormat.Dxil;

        string vertSource = InjectDefines(request.VertexSource, request.Keywords, request.ExtraDefines);
        string fragSource = InjectDefines(request.FragmentSource, request.Keywords, request.ExtraDefines);

        string tempDir = Path.Combine(Path.GetTempPath(), "ProwlDxc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string vsIn = Path.Combine(tempDir, "vs.hlsl");
            string psIn = Path.Combine(tempDir, "ps.hlsl");
            string vsOut = Path.Combine(tempDir, spirv ? "vs.spv" : "vs.dxil");
            string psOut = Path.Combine(tempDir, spirv ? "ps.spv" : "ps.dxil");

            File.WriteAllText(vsIn, vertSource, Encoding.UTF8);
            File.WriteAllText(psIn, fragSource, Encoding.UTF8);

            string? vsErr = RunDxc(dxc, vsIn, vsOut, "vs_6_0", request.VertexEntryPoint, spirv);
            if (vsErr != null)
                return ShaderCompileResult.Fail($"DXC vertex compile failed: {vsErr}");

            string? psErr = RunDxc(dxc, psIn, psOut, "ps_6_0", request.FragmentEntryPoint, spirv);
            if (psErr != null)
                return ShaderCompileResult.Fail($"DXC fragment compile failed: {psErr}");

            byte[] vsBytes = File.ReadAllBytes(vsOut);
            byte[] psBytes = File.ReadAllBytes(psOut);

            ShaderBindingLayout layout = TryParseReflection(dxc, vsIn, "vs_6_0", request.VertexEntryPoint)
                ?? new ShaderBindingLayout();

            return new ShaderCompileResult
            {
                Success = true,
                Format = format,
                VertexBytecode = vsBytes,
                FragmentBytecode = psBytes,
                BindingLayout = layout,
            };
        }
        catch (Exception ex)
        {
            return ShaderCompileResult.Fail($"DXC compile exception: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of temp compile artifacts.
            }
        }
    }

    /// <summary>
    /// Search PATH, Windows Kits, and VulkanSDK for <c>dxc.exe</c>. Result is cached.
    /// </summary>
    public static string? LocateDxc()
    {
        lock (s_locateLock)
        {
            if (s_locateAttempted)
                return s_cachedDxcPath;

            s_locateAttempted = true;
            s_cachedDxcPath = LocateDxcUncached();
            return s_cachedDxcPath;
        }
    }

    /// <summary>Clears the cached DXC path (tests).</summary>
    internal static void ResetLocateCache()
    {
        lock (s_locateLock)
        {
            s_locateAttempted = false;
            s_cachedDxcPath = null;
        }
    }

    private static string? LocateDxcUncached()
    {
        // Prefer Vulkan SDK (usually built with SPIR-V), then PATH, then Windows Kits.
        string? fromVulkan = FindInVulkanSdk();
        if (fromVulkan != null)
            return fromVulkan;

        string? fromPath = FindOnPath("dxc.exe") ?? FindOnPath("dxc");
        if (fromPath != null)
            return fromPath;

        return FindInWindowsKits();
    }

    private static string? LocateDxcPreferringSpirv()
    {
        string? fromVulkan = FindInVulkanSdk();
        if (fromVulkan != null)
            return fromVulkan;

        // Already-cached path may be Vulkan SDK from LocateDxcUncached.
        string? cached = LocateDxc();
        return cached;
    }

    private static string? FindInVulkanSdk()
    {
        string? vulkanSdk = Environment.GetEnvironmentVariable("VULKAN_SDK");
        if (string.IsNullOrEmpty(vulkanSdk))
            return null;

        string[] vulkanCandidates =
        [
            Path.Combine(vulkanSdk, "Bin", "dxc.exe"),
            Path.Combine(vulkanSdk, "bin", "dxc.exe"),
            Path.Combine(vulkanSdk, "Bin32", "dxc.exe"),
        ];
        foreach (string candidate in vulkanCandidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? FindInWindowsKits()
    {
        string[] kitRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Kits", "10", "bin"),
        ];

        foreach (string kitRoot in kitRoots)
        {
            if (!Directory.Exists(kitRoot))
                continue;

            string? best = null;
            foreach (string versionDir in Directory.EnumerateDirectories(kitRoot))
            {
                string candidate = Path.Combine(versionDir, "x64", "dxc.exe");
                if (!File.Exists(candidate))
                    candidate = Path.Combine(versionDir, "x86", "dxc.exe");
                if (!File.Exists(candidate))
                    continue;

                if (best == null ||
                    string.Compare(versionDir, Path.GetDirectoryName(Path.GetDirectoryName(best)), StringComparison.OrdinalIgnoreCase) > 0)
                {
                    best = candidate;
                }
            }

            if (best != null)
                return best;
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static string InjectDefines(
        string source,
        IReadOnlyDictionary<string, bool>? keywords,
        IReadOnlyList<string>? extraDefines)
    {
        var sb = new StringBuilder(source.Length + 128);

        if (extraDefines != null)
        {
            for (int i = 0; i < extraDefines.Count; i++)
            {
                string define = extraDefines[i];
                if (!string.IsNullOrWhiteSpace(define))
                    sb.Append("#define ").Append(define).Append('\n');
            }
        }

        if (keywords != null)
        {
            foreach (KeyValuePair<string, bool> kvp in keywords)
            {
                if (kvp.Value)
                    sb.Append("#define ").Append(kvp.Key).Append('\n');
            }
        }

        sb.Append(source);
        return sb.ToString();
    }

    private static string? RunDxc(
        string dxcPath,
        string inputPath,
        string outputPath,
        string profile,
        string entryPoint,
        bool spirv)
    {
        var args = new StringBuilder();
        args.Append("-T ").Append(profile);
        args.Append(" -E ").Append(entryPoint);
        args.Append(" -Fo \"").Append(outputPath).Append('"');
        if (spirv)
            args.Append(" -spirv -fspv-target-env=vulkan1.2");
        args.Append(" \"").Append(inputPath).Append('"');

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = dxcPath,
                Arguments = args.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return "DXC timed out after 120s.";
            }

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                string combined = (stderr + "\n" + stdout).Trim();
                return string.IsNullOrEmpty(combined)
                    ? $"exit code {process.ExitCode}"
                    : combined;
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static readonly Regex s_textureRegex = new(
        @"Texture\dD(?:Array)?\s*<[^>]+>\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex s_cbufferRegex = new(
        @"cbuffer\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Best-effort reflection from HLSL source patterns (avoids fragile -dumpbin parsing).
    /// </summary>
    private static ShaderBindingLayout? TryParseReflection(
        string dxcPath,
        string inputPath,
        string profile,
        string entryPoint)
    {
        try
        {
            string source = File.ReadAllText(inputPath);
            var textures = new List<ShaderBindingSlot>();
            var buffers = new List<ShaderBindingSlot>();

            int texSlot = 0;
            foreach (Match match in s_textureRegex.Matches(source))
            {
                textures.Add(new ShaderBindingSlot(ShaderBindingKind.Texture, texSlot++, match.Groups[1].Value));
            }

            int bufSlot = 0;
            foreach (Match match in s_cbufferRegex.Matches(source))
            {
                buffers.Add(new ShaderBindingSlot(ShaderBindingKind.Buffer, bufSlot++, match.Groups[1].Value));
            }

            return new ShaderBindingLayout
            {
                Textures = textures.ToArray(),
                Buffers = buffers.ToArray(),
            };
        }
        catch
        {
            return null;
        }
    }
}
