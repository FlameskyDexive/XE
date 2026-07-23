// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl;

/// <summary>
/// Marks a method, property, constructor, or type as a hot path (runs every frame or every event).
/// Prowl.Analyzers (PR0002–PR0007) enforces zero GC allocation patterns inside hot paths.
/// </summary>
[System.AttributeUsage(
    System.AttributeTargets.Method |
    System.AttributeTargets.Property |
    System.AttributeTargets.Constructor |
    System.AttributeTargets.Class |
    System.AttributeTargets.Struct,
    Inherited = false)]
public sealed class HotPathAttribute : System.Attribute
{
}
