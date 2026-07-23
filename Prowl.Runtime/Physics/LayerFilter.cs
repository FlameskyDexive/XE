// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Jitter2.Collision;
using Jitter2.Collision.Shapes;

namespace Prowl.Runtime;

public class LayerFilter : IBroadPhaseFilter
{
    private readonly struct Pair : IEquatable<Pair>
    {
        private readonly Rigidbody3D _a, _b;

        public Pair(Rigidbody3D shapeA, Rigidbody3D shapeB)
        {
            this._a = shapeA;
            this._b = shapeB;
        }

        public bool Equals(Pair other)
        {
            return _a.Equals(other._a) && _b.Equals(other._b);
        }

        public override bool Equals(object? obj)
        {
            return obj is Pair other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_a, _b);
        }
    }

    private static readonly HashSet<Pair> _ignore = [];

    internal static void IgnoreCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB)
    {
        if (bodyA.IsNotValid() || bodyB.IsNotValid()) return;
        if (bodyA == bodyB) return;

        if (bodyB.InstanceID < bodyA.InstanceID) (bodyA, bodyB) = (bodyB, bodyA);
        _ignore.Add(new Pair(bodyA, bodyB));
    }

    internal static void EnableCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB)
    {
        if (bodyA.IsNotValid() || bodyB.IsNotValid()) return;
        if (bodyA == bodyB) return;
        if (bodyB.InstanceID < bodyA.InstanceID) (bodyA, bodyB) = (bodyB, bodyA);
        _ignore.Remove(new Pair(bodyA, bodyB));
    }

    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        if (proxyA is RigidBodyShape rbsA && proxyB is RigidBodyShape rbsB)
        {
            // Things with constraints dont collide against eachother. (TODO: This should be toggleable)
            // No LINQ (PR0001): manual scan — Filter is a per-pair physics hot path.
            var bodyA = rbsA.RigidBody;
            var bodyB = rbsB.RigidBody;
            if (HasConstraintBetween(bodyA, bodyB)) return false;
            if (HasConstraintBetween(bodyB, bodyA)) return false;

            if (rbsA.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udA ||
                rbsB.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udB)
                return true;

            bool isIgnored = false;
            Rigidbody3D rbA = udA.Rigidbody;
            Rigidbody3D rbB = udB.Rigidbody;
            if (rbA.IsValid() && rbB.IsValid())
            {
                // Order by InstanceID to match how IgnoreCollisionBetween stores the pair.
                if (rbB.InstanceID < rbA.InstanceID) (rbA, rbB) = (rbB, rbA);
                isIgnored = _ignore.Contains(new Pair(rbA, rbB));
            }
            bool canCollide = CollisionMatrix.GetLayerCollision(udA.Layer, udB.Layer);

            return canCollide && !isIgnored;
        }

        // If not both RigidBodyShapes, let other filters handle it (e.g., terrain collision)
        return true;
    }

    /// <summary>True when <paramref name="host"/> has a constraint whose other body is <paramref name="other"/>. No LINQ (PR0001).</summary>
    private static bool HasConstraintBetween(Jitter2.Dynamics.RigidBody host, Jitter2.Dynamics.RigidBody other)
    {
        foreach (var conn in host.Constraints)
        {
            if (conn.Body1 == other || conn.Body2 == other)
                return true;
        }
        return false;
    }
}
