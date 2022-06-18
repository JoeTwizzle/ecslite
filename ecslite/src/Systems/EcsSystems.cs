// ----------------------------------------------------------------------------
// The MIT License
// Lightweight ECS framework https://github.com/Leopotam/ecslite
// Copyright (c) 2021-2022 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace EcsLite.Systems
{
    public abstract class EcsSystem : IEcsSystem
    {
        protected readonly EcsSystems systems;
        public EcsSystem(EcsSystems systems)
        {
            this.systems = systems;
        }

        protected EcsWorld GetWorld(string? world = null)
        {
            return systems.GetWorld(world);
        }

        protected EcsPool<T> GetPool<T>(string? world = null) where T : struct
        {
            return systems.GetWorld(world).GetPool<T>();
        }

        protected EcsWorld.Mask FilterInc<T>(string? world = null) where T : struct
        {
            return systems.GetWorld(world).FilterInc<T>();
        }

        protected EcsWorld.Mask FilterExc<T>(string? world = null) where T : struct
        {
            return systems.GetWorld(world).FilterExc<T>();
        }

        protected T GetSingleton<T>()
        {
            return systems.GetSingleton<T>();
        }

        protected T GetInjected<T>(string identifier)
        {
            return systems.GetInjected<T>(identifier);
        }

        protected void EnableGroupNextFrame(string groupName)
        {
            systems.EnableGroupNextFrame(groupName);
        }

        protected void SetGroupNextFrame(string groupName, bool state)
        {
            systems.SetGroupNextFrame(groupName, state);
        }

        protected void DisableGroupNextFrame(string groupName)
        {
            systems.DisableGroupNextFrame(groupName);
        }
    }

    internal class EcsTickedSystem
    {
        public readonly IEcsRunSystem EcsSystem;
        public readonly float TickDelay;
        public float Accumulator;
        public bool Enabled;

        public EcsTickedSystem(IEcsRunSystem ecsSystem, float tickDelay)
        {
            Enabled = true;
            EcsSystem = ecsSystem;
            if (tickDelay <= 0)
            {
                tickDelay = float.Epsilon;
            }
            TickDelay = tickDelay;
            Accumulator = 0;
        }

        public void Run(EcsSystems systems, int threadId, float dt)
        {
            Accumulator += dt;
            if (Accumulator >= TickDelay)
            {
                Accumulator %= TickDelay;
                if (Enabled)
                {
                    if (dt <= TickDelay)
                    {
                        EcsSystem.Run(systems, TickDelay, threadId);
                    }
                    else
                    {
                        EcsSystem.Run(systems, dt, threadId);
                    }
                }
            }
        }
    }

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
        void Run(EcsSystems systems, float elapsed, int threadId);
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
    public struct SystemCreateInfo
    {
        public Type Type;
        public float DelayTime;
        public string? GroupName;
        public bool GroupState;

        public SystemCreateInfo(Type type, float delayTime, string? groupName, bool groupState)
        {
            Type = type;
            DelayTime = delayTime;
            GroupName = groupName;
            GroupState = groupState;
        }
    }
    public sealed class EcsSystems : IDisposable
    {
        private readonly EcsWorld _defaultWorld;
        private readonly Dictionary<string, EcsWorld> _worlds;
        private readonly List<IEcsSystem> _allSystems;
        private readonly int _numThreads;
        private readonly Thread[] _threads;
        private readonly Barrier _barrier1;
        private readonly Barrier _barrier2;
        private readonly Dictionary<string, object> _injected;
        private readonly Dictionary<Type, object> _injectedSingletons;
        private readonly Dictionary<string, List<EcsTickedSystem>> _groups;
        private readonly ConcurrentQueue<(string name, bool state)> _groupStateChanges;
        private readonly EcsSystemsBucket[] _buckets;
        private double _totalTime;
        private double _deltaTime;
        private float _deltaTimeFloat;
        private bool _disposed;
        private int _currentBucket;
        public int ThreadCount => _numThreads;
        public EcsWorld DefaultWorld => _defaultWorld;
        public IReadOnlyDictionary<string, EcsWorld> AllNamedWorlds => _worlds;

        internal EcsSystems(int numThreads, EcsWorld defaultWorld, Dictionary<string, EcsWorld> worlds, List<IEcsSystem> allSystems, Dictionary<string, object> injected, Dictionary<Type, object> injectedSingletons, EcsSystemsBucket[] buckets, Dictionary<string, List<EcsTickedSystem>> groups)
        {
            _defaultWorld = defaultWorld;
            _worlds = worlds;
            _allSystems = allSystems;
            _numThreads = numThreads;
            _groupStateChanges = new ConcurrentQueue<(string name, bool state)>();
            _threads = new Thread[_numThreads - 1];
            _barrier1 = new Barrier(_numThreads);
            _barrier2 = new Barrier(_numThreads);
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i] = new Thread(WorkerLoopThreads)
                {
                    Name = $"RunSystem Thread {i}",
                    IsBackground = true
                };
            }
            _injected = injected;
            _injectedSingletons = injectedSingletons;
            _buckets = buckets;
            _disposed = false;
            _groups = groups;
        }

        public int GetAllSystems(ref IEcsSystem[]? list)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsWorld GetWorld(string? name = null)
        {
            if (name == null)
            {
                return _defaultWorld;
            }
            _worlds.TryGetValue(name, out var world);
            return world!;
        }

        public void Init()
        {
            foreach (var system in _allSystems)
            {
                if (system is IEcsPreInitSystem initSystem)
                {
                    initSystem.PreInit(this);
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
                    var worldName = CheckForLeakedEntities();
                    if (worldName != null) { throw new Exception($"Empty entity detected in world \"{worldName}\" after {initSystem.GetType().Name}.PreInit()."); }
#endif
                }
            }
            foreach (var system in _allSystems)
            {
                if (system is IEcsInitSystem initSystem)
                {
                    initSystem.Init(this);
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
                    var worldName = CheckForLeakedEntities();
                    if (worldName != null) { throw new Exception($"Empty entity detected in world \"{worldName}\" after {initSystem.GetType().Name}.Init()."); }
#endif
                }
            }
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i].Start(i + 1);
            }
        }

        public T GetSingleton<T>()
        {
            return (T)_injectedSingletons[typeof(T)];
        }

        public T GetInjected<T>(string identifier)
        {
#if DEBUG
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException($"Tried to GetInjected with invalid identifier: {identifier}");
            }
#endif
            return (T)_injected[identifier];
        }

        public void EnableGroupNextFrame(string groupName)
        {
            SetGroupNextFrame(groupName, true);
        }

        public void SetGroupNextFrame(string groupName, bool state)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (string.IsNullOrWhiteSpace(groupName))
            {
                throw new ArgumentException($"Tried to SetGroupNextFrame with invalid name: {groupName}");
            }
            if (!_groups.ContainsKey(groupName))
            {
                throw new ArgumentException($"Tried to SetGroupNextFrame with invalid name: {groupName}");
            }
#endif
            _groupStateChanges.Enqueue((groupName, state));
        }

        public void DisableGroupNextFrame(string groupName)
        {
            SetGroupNextFrame(groupName, false);
        }

        public void Run(double elapsed)
        {
            ProcessGroupStates();
            _currentBucket = 0;
            _totalTime += elapsed;
            _deltaTime = elapsed;
            _deltaTimeFloat = (float)elapsed;
            for (int i = 0; i < _buckets.Length; i++)
            {
                //Signal activation
                _barrier1.SignalAndWait();
                DoWork(0);
                //Wait for others to finsh their work
                _barrier2.SignalAndWait();
                //Set next working bucket
                _currentBucket++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        void ProcessGroupStates()
        {
            foreach (var state in _groupStateChanges)
            {
                if (_groups.TryGetValue(state.name, out var systems))
                {
                    foreach (var system in systems)
                    {
                        system.Enabled = state.state;
                    }
                }
#if DEBUG
                else
                {
                    throw new Exception($"Tried to change state on non existant group with name: {state.name} and state: {state.state}");
                }
#endif
            }
            _groupStateChanges.Clear();
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
        void WorkerLoopThreads(object? id)
        {
            while (!_disposed)
            {
                int threadId = (int)id!;
                //Wait for activation
                _barrier1.SignalAndWait();
                if (_disposed)
                {
                    return;
                }
                DoWork(threadId);
                //Wait for others to finish their work
                _barrier2.SignalAndWait();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        void DoWork(int threadId)
        {
            var runSystems = _buckets[_currentBucket].ParallelRunSystems;
            int count = runSystems?.Count ?? 0;
            int systemsPerThread = Math.DivRem(count, _numThreads, out var remainder);
            if (remainder != 0)
            {
                systemsPerThread++;
            }
            int startIndex = threadId * systemsPerThread;
            for (int i = 0; i < systemsPerThread; i++)
            {
                int index = i + startIndex;
                if (index >= count)
                {
                    break;
                }

                runSystems![index].Run(this, threadId, _deltaTimeFloat);
            }
        }

#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
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
#endif

        public void Dispose()
        {
            for (var i = _allSystems.Count - 1; i >= 0; i--)
            {
                if (_allSystems[i] is IEcsDestroySystem destroySystem)
                {
                    destroySystem.Destroy(this);
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
                    var worldName = CheckForLeakedEntities();
                    if (worldName != null) { throw new Exception($"Empty entity detected in world \"{worldName}\" after {destroySystem.GetType().Name}.Destroy()."); }
#endif
                }
            }
            for (var i = _allSystems.Count - 1; i >= 0; i--)
            {
                if (_allSystems[i] is IEcsPostDestroySystem postDestroySystem)
                {
                    postDestroySystem.PostDestroy(this);
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
                    var worldName = CheckForLeakedEntities();
                    if (worldName != null) { throw new Exception($"Empty entity detected in world \"{worldName}\" after {postDestroySystem.GetType().Name}.PostDestroy()."); }
#endif
                }
            }
            _allSystems.Clear();
            _disposed = true;
            _barrier1.SignalAndWait();
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i].Join();
            }
            _barrier1.Dispose();
            _barrier2.Dispose();
        }
    }
}