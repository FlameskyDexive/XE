// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.RHI;

/// <summary>
/// Converts HLSL's separate b/t/s register namespaces into one collision-free Vulkan
/// binding namespace. D3D12 keeps the original logical registers but uses the same
/// deterministic entry ordering when building root parameters.
/// </summary>
internal sealed class ShaderDescriptorLayoutPlan
{
    public int BufferBindingBase { get; }
    public int TextureBindingBase { get; }
    public int SamplerBindingBase { get; }
    public ShaderDescriptorBinding[] Bindings { get; }

    private ShaderDescriptorLayoutPlan(
        int bufferBindingBase,
        int textureBindingBase,
        int samplerBindingBase,
        ShaderDescriptorBinding[] bindings)
    {
        BufferBindingBase = bufferBindingBase;
        TextureBindingBase = textureBindingBase;
        SamplerBindingBase = samplerBindingBase;
        Bindings = bindings;
    }

    public static ShaderDescriptorLayoutPlan Create(ShaderBindingLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        int bufferSpan = GetRegisterSpan(layout.Buffers);
        int textureSpan = GetRegisterSpan(layout.Textures);
        int bufferBase = 0;
        int textureBase = bufferSpan;
        int samplerBase = bufferSpan + textureSpan;
        int count = layout.Buffers.Length + layout.Textures.Length + layout.Samplers.Length;
        var bindings = new ShaderDescriptorBinding[count];
        int index = 0;

        AddBindings(layout.Buffers, bufferBase, bindings, ref index);
        AddBindings(layout.Textures, textureBase, bindings, ref index);
        AddBindings(layout.Samplers, samplerBase, bindings, ref index);

        return new ShaderDescriptorLayoutPlan(bufferBase, textureBase, samplerBase, bindings);
    }

    public int GetPhysicalBinding(ShaderBindingSlot slot) => slot.Kind switch
    {
        ShaderBindingKind.Buffer => BufferBindingBase + slot.Slot,
        ShaderBindingKind.Texture => TextureBindingBase + slot.Slot,
        ShaderBindingKind.Sampler => SamplerBindingBase + slot.Slot,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static int GetRegisterSpan(ShaderBindingSlot[] slots)
    {
        int maxSlot = -1;
        for (int i = 0; i < slots.Length; i++)
            maxSlot = Math.Max(maxSlot, slots[i].Slot);
        return maxSlot + 1;
    }

    private static void AddBindings(
        ShaderBindingSlot[] slots,
        int bindingBase,
        ShaderDescriptorBinding[] destination,
        ref int index)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            ShaderBindingSlot slot = slots[i];
            destination[index++] = new ShaderDescriptorBinding(slot, bindingBase + slot.Slot);
        }
    }
}

internal readonly struct ShaderDescriptorBinding
{
    public ShaderBindingSlot Slot { get; }
    public int PhysicalBinding { get; }

    public ShaderDescriptorBinding(ShaderBindingSlot slot, int physicalBinding)
    {
        Slot = slot;
        PhysicalBinding = physicalBinding;
    }
}
