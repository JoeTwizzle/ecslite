// ----------------------------------------------------------------------------
// The MIT License
// Lightweight ECS framework https://github.com/Leopotam/ecslite
// Copyright (c) 2021-2022 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        void Run(EcsSystems systems, int threadId);
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

    public sealed partial class EcsSystems : IDisposable
    {
        readonly EcsWorld _defaultWorld;
        readonly Dictionary<string, EcsWorld> _worlds;
        readonly List<IEcsSystem> _allSystems;
        readonly int _numThreads;
        readonly Thread[] _threads;
        readonly Barrier _barrier1;
        readonly Barrier _barrier2;
        readonly object[] _constructorParams;
        readonly Dictionary<string, object> _injected;
        readonly Dictionary<Type, object> _injectedSingletons;
        readonly Queue<Type> _delayedAddQueue;
        SystemsBucket[] _buckets;
        int _bucketCount;
        bool _disposed;
        int _currentBucket;

        public EcsSystems(int numThreads, EcsWorld defaultWorld)
        {
            _disposed = false;
            _numThreads = numThreads;
            _bucketCount = 0;
            _buckets = new SystemsBucket[4];
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
            _delayedAddQueue = new Queue<Type>();
            _defaultWorld = defaultWorld;
            _worlds = new Dictionary<string, EcsWorld>(32);
            _allSystems = new List<IEcsSystem>(128);
            _injectedSingletons = new Dictionary<Type, object>();
            _injected = new Dictionary<string, object>();
            _constructorParams = new object[] { this };
        }

        public IReadOnlyDictionary<string, EcsWorld> AllNamedWorlds => _worlds;

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
            while (_delayedAddQueue.Count > 0)
            {
                Type type = _delayedAddQueue.Dequeue();

                IEcsSystem system = (IEcsSystem)Activator.CreateInstance(type, _constructorParams)!;
                _allSystems.Add(system);
                if (system is IEcsRunSystem runSystem)
                {
                    AddRunSystem(runSystem);
                }
            }
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

        public void Inject<T>(string identifier, T data) where T : notnull
        {
            _injected.Add(identifier, data);
        }

        public void InjectSingleton<T>(T data) where T : notnull
        {
            _injectedSingletons.Add(typeof(T), data);
        }

        public T GetSingleton<T>()
        {
            return (T)_injectedSingletons[typeof(T)];
        }

        public T GetInjected<T>(string identifier)
        {
            return (T)_injected[identifier];
        }

        public void AddWorld(EcsWorld world, string name)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (string.IsNullOrEmpty(name)) { throw new Exception("World name cant be null or empty."); }
#endif
            _worlds[name] = world;
        }

        public void Add<T>() where T : EcsSystem
        {
            _delayedAddQueue.Enqueue(typeof(T));
        }

        //public void Add(IEcsSystem system)
        //{
        //    if (system is EcsSystem)
        //    {
        //        throw new Exception("Call Add<T>() for types which derive from EcsSystem");
        //    }
        //    _allSystems.Add(system);
        //    if (system is IEcsRunSystem runSystem)
        //    {
        //        AddRunSystem(runSystem);
        //    }
        //}

        public void Run()
        {
            _currentBucket = 0;

            for (int i = 0; i < _bucketCount; i++)
            {
                //Wait for activation
                _barrier1.SignalAndWait();
                DoWork(0);
                _barrier2.SignalAndWait();
                //Set next working bucket
                Interlocked.Increment(ref _currentBucket);
            }
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
                _barrier2.SignalAndWait();
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        void DoWork(int threadId)
        {
            var systems = _buckets[_currentBucket].ParallelRunSystems;
            int count = systems?.Count ?? 0;
            int systemsPerThread = Math.DivRem(count, _threads.Length + 1, out var remainder);
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
                systems![index].Run(this, threadId);
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