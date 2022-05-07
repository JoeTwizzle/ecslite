// ----------------------------------------------------------------------------
// The MIT License
// Lightweight ECS framework https://github.com/Leopotam/ecslite
// Copyright (c) 2021-2022 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace EcsLite
{
    public interface IEcsSystem { }

    public interface IEcsPreInitSystem : IEcsSystem
    {
        void PreInit(EcsSystems systems);
    }

    public interface IEcsInitSystem : IEcsSystem
    {
        void Init(EcsSystems systems);
    }

    public interface IEcsRunSystem : IEcsSystem
    {
        void Run(EcsSystems systems);
    }

    public interface IEcsDestroySystem : IEcsSystem
    {
        void Destroy(EcsSystems systems);
    }

    public interface IEcsPostDestroySystem : IEcsSystem
    {
        void PostDestroy(EcsSystems systems);
    }

#if ENABLE_IL2CPP
    [Il2CppSetOption (Option.NullChecks, false)]
    [Il2CppSetOption (Option.ArrayBoundsChecks, false)]
#endif
    public sealed class EcsSystems : IDisposable
    {
        readonly EcsWorld _defaultWorld;
        readonly Dictionary<string, EcsWorld> _worlds;
        readonly List<IEcsSystem> _allSystems;
        IEcsRunSystem[] _runSystems;
        int _runSystemsCount;

        public EcsSystems(EcsWorld defaultWorld)
        {
            _runSystems = null!;
            _defaultWorld = defaultWorld;
            _worlds = new Dictionary<string, EcsWorld>(32);
            _allSystems = new List<IEcsSystem>(128);
        }

        public IReadOnlyDictionary<string, EcsWorld> AllNamedWorlds => _worlds;

        public int GetAllSystems(ref IEcsSystem[] list)
        {
            var itemsCount = _allSystems.Count;
            if (itemsCount == 0) { return 0; }
            if (list == null || list.Length < itemsCount)
            {
                list = new IEcsSystem[_allSystems.Capacity];
            }
            for (int i = 0, iMax = itemsCount; i < iMax; i++)
            {
                list[i] = _allSystems[i];
            }
            return itemsCount;
        }

        public int GetRunSystems(ref IEcsRunSystem[] list)
        {
            var itemsCount = _runSystemsCount;
            if (itemsCount == 0) { return 0; }
            if (list == null || list.Length < itemsCount)
            {
                list = new IEcsRunSystem[_runSystems.Length];
            }
            for (int i = 0, iMax = itemsCount; i < iMax; i++)
            {
                list[i] = _runSystems[i];
            }
            return itemsCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsWorld? GetWorld(string? name = null)
        {
            if (name == null)
            {
                return _defaultWorld;
            }
            _worlds.TryGetValue(name, out var world);
            return world;
        }

        public void Init()
        {
            if (_runSystemsCount > 0)
            {
                _runSystems = new IEcsRunSystem[_runSystemsCount];
            }
            foreach (var system in _allSystems)
            {
                if (system is IEcsPreInitSystem initSystem)
                {
                    initSystem.PreInit(this);
#if DEBUG && !LEOECSLITE_NO_SANITIZE_CHECKS
                    var worldName = CheckForLeakedEntities();
                    if (worldName != null) { throw new System.Exception($"Empty entity detected in world \"{worldName}\" after {initSystem.GetType().Name}.PreInit()."); }
#endif
                }
            }
            var runIdx = 0;
            foreach (var system in _allSystems)
            {
                if (system is IEcsInitSystem initSystem)
                {
                    initSystem.Init(this);
#if DEBUG && !LEOECSLITE_NO_SANITIZE_CHECKS
                    var worldName = CheckForLeakedEntities();
                    if (worldName != null) { throw new System.Exception($"Empty entity detected in world \"{worldName}\" after {initSystem.GetType().Name}.Init()."); }
#endif
                }
                if (system is IEcsRunSystem runSystem)
                {
                    _runSystems[runIdx++] = runSystem;
                }
            }
        }

        public void AddWorld(EcsWorld world, string name)
        {
#if DEBUG && !LEOECSLITE_NO_SANITIZE_CHECKS
            if (string.IsNullOrEmpty(name)) { throw new System.Exception("World name cant be null or empty."); }
#endif
            _worlds[name] = world;
        }

        public void Add(IEcsSystem system)
        {
            _allSystems.Add(system);
            if (system is IEcsRunSystem)
            {
                _runSystemsCount++;
            }
        }

        public void Run()
        {
            for (int i = 0, iMax = _runSystemsCount; i < iMax; i++)
            {
                _runSystems[i].Run(this);
#if DEBUG && !LEOECSLITE_NO_SANITIZE_CHECKS
                var worldName = CheckForLeakedEntities();
                if (worldName != null) { throw new System.Exception($"Empty entity detected in world \"{worldName}\" after {_runSystems[i].GetType().Name}.Run()."); }
#endif
            }
        }

#if DEBUG && !LEOECSLITE_NO_SANITIZE_CHECKS
        public string? CheckForLeakedEntities()
        {
            if (_defaultWorld.CheckForLeakedEntities()) { return "default"; }
            foreach (var pair in _worlds)
            {
                if (pair.Value.CheckForLeakedEntities())
                {
                    return pair.Key;
                }
            }
            return null;
        }

        public void Dispose()
        {
            for (var i = _allSystems.Count - 1; i >= 0; i--)
            {
                if (_allSystems[i] is IEcsDestroySystem destroySystem)
                {
                    destroySystem.Destroy(this);
#if DEBUG && !LEOECSLITE_NO_SANITIZE_CHECKS
                    var worldName = CheckForLeakedEntities();
                    if (worldName != null) { throw new System.Exception($"Empty entity detected in world \"{worldName}\" after {destroySystem.GetType().Name}.Destroy()."); }
#endif
                }
            }
            for (var i = _allSystems.Count - 1; i >= 0; i--)
            {
                if (_allSystems[i] is IEcsPostDestroySystem postDestroySystem)
                {
                    postDestroySystem.PostDestroy(this);
#if DEBUG && !LEOECSLITE_NO_SANITIZE_CHECKS
                    var worldName = CheckForLeakedEntities();
                    if (worldName != null) { throw new System.Exception($"Empty entity detected in world \"{worldName}\" after {postDestroySystem.GetType().Name}.PostDestroy()."); }
#endif
                }
            }
            _allSystems.Clear();
            _runSystems = null!;
        }
#endif
    }
}