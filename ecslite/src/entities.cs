// ----------------------------------------------------------------------------
// The MIT License
// Lightweight ECS framework https://github.com/Leopotam/ecslite
// Copyright (c) 2021-2022 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace EcsLite
{
    public struct EcsLocalEntity
    {
        internal int Id;
        internal int Gen;
    }

    public struct EcsEntity
    {
        internal int Id;
        internal int Gen;
        internal EcsWorld World;

#if DEBUG
        // For using in IDE debugger.
        internal object[]? DebugComponentsView
        {
            get
            {
                object[]? list = null;
                if (World != null && World.IsAlive() && World.IsEntityAlive(Id) && World.GetEntityGen(Id) == Gen)
                {
                    World.GetComponents(Id, ref list);
                }
                return list;
            }
        }
        // For using in IDE debugger.
        internal int DebugComponentsCount
        {
            get
            {
                if (World != null && World.IsAlive() && World.IsEntityAlive(Id) && World.GetEntityGen(Id) == Gen)
                {
                    return World.GetComponentsCount(Id);
                }
                return 0;
            }
        }

        // For using in IDE debugger.
        public override string ToString()
        {
            if (Id == 0 && Gen == 0) { return "Entity-Null"; }
            if (World == null || !World.IsAlive() || !World.IsEntityAlive(Id) || World.GetEntityGen(Id) != Gen) { return "Entity-NonAlive"; }
            System.Type[]? types = null;
            var count = World.GetComponentTypes(Id, ref types);
            System.Text.StringBuilder? sb = null;
            if (count > 0)
            {
                sb = new System.Text.StringBuilder(512);
                for (var i = 0; i < count; i++)
                {
                    if (sb.Length > 0) { sb.Append(','); }
                    sb.Append(types![i].Name);
                }
            }
            return $"Entity-{Id}:{Gen} [{sb}]";
        }
#endif
    }

#if ENABLE_IL2CPP
    [Il2CppSetOption (Option.NullChecks, false)]
    [Il2CppSetOption (Option.ArrayBoundsChecks, false)]
#endif
    public static class EcsEntityExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsLocalEntity PackLocalEntity(this EcsWorld world, int entity)
        {
            EcsLocalEntity packed;
            packed.Id = entity;
            packed.Gen = world.GetEntityGen(entity);
            return packed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsEntity AsGlobal(this EcsLocalEntity entity, EcsWorld world)
        {
            EcsEntity packed;
            packed.World = world;
            packed.Id = entity.Id;
            packed.Gen = entity.Gen;
            return packed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsLocalEntity AsLocal(this EcsEntity entity)
        {
            EcsLocalEntity packed;
            packed.Id = entity.Id;
            packed.Gen = entity.Gen;
            return packed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryUnpack(this in EcsLocalEntity packed, EcsWorld world, out int entity)
        {
            if (!world.IsAlive() || !world.IsEntityAlive(packed.Id) || world.GetEntityGen(packed.Id) != packed.Gen)
            {
                entity = -1;
                return false;
            }
            entity = packed.Id;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Unpack(this in EcsLocalEntity packed)
        {
            return packed.Id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsTo(this in EcsLocalEntity a, in EcsLocalEntity b)
        {
            return a.Id == b.Id && a.Gen == b.Gen;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsEntity PackEntity(this EcsWorld world, int entity)
        {
            EcsEntity packedEntity;
            packedEntity.World = world;
            packedEntity.Id = entity;
            packedEntity.Gen = world.GetEntityGen(entity);
            return packedEntity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryUnpack(this in EcsEntity packedEntity, [NotNullWhen(true)] out EcsWorld? world, out int entity)
        {
            if (packedEntity.World == null || !packedEntity.World.IsAlive() || !packedEntity.World.IsEntityAlive(packedEntity.Id) || packedEntity.World.GetEntityGen(packedEntity.Id) != packedEntity.Gen)
            {
                world = null;
                entity = -1;
                return false;
            }
            world = packedEntity.World;
            entity = packedEntity.Id;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsTo(this in EcsEntity a, in EcsEntity b)
        {
            return a.Id == b.Id && a.Gen == b.Gen && a.World == b.World;
        }
    }
}