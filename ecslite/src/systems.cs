// ----------------------------------------------------------------------------
// The MIT License
// Lightweight ECS framework https://github.com/Leopotam/ecslite
// Copyright (c) 2021-2022 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
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
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class EcsWriteAttribute : Attribute
    {
        public readonly string World;
        public readonly Type[] Pools;

        public EcsWriteAttribute(string world, params Type[]? pools)
        {
            World = world;
            Pools = pools?.Distinct().ToArray() ?? Array.Empty<Type>();
        }
    }
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class EcsReadAttribute : Attribute
    {
        public readonly string World;
        public readonly Type[] Pools;

        public EcsReadAttribute(string world, params Type[]? pools)
        {
            World = world;
            Pools = pools?.Distinct().ToArray() ?? Array.Empty<Type>();
        }
    }

    public sealed class EcsSystems : IDisposable
    {
        readonly EcsWorld _defaultWorld;
        readonly Dictionary<string, EcsWorld> _worlds;
        readonly List<IEcsSystem> _allSystems;
        readonly int _numThreads;
        readonly Thread[] _threads;
        Barrier _barrier;
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
            _barrier = new Barrier(_numThreads);
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i] = new Thread(WorkerLoopThreads)
                {
                    Name = $"RunSystem Thread {i}",
                    IsBackground = true
                };
            }
            _defaultWorld = defaultWorld;
            _worlds = new Dictionary<string, EcsWorld>(32);
            _allSystems = new List<IEcsSystem>(128);
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
            foreach (var system in _allSystems)
            {
                if (system is IEcsPreInitSystem initSystem)
                {
                    initSystem.PreInit(this);
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
                    var worldName = CheckForLeakedEntities();
                    if (worldName != null) { throw new System.Exception($"Empty entity detected in world \"{worldName}\" after {initSystem.GetType().Name}.PreInit()."); }
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
                    if (worldName != null) { throw new System.Exception($"Empty entity detected in world \"{worldName}\" after {initSystem.GetType().Name}.Init()."); }
#endif
                }
            }
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i].Start(i + 1);
            }
        }

        public void AddWorld(EcsWorld world, string name)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (string.IsNullOrEmpty(name)) { throw new System.Exception("World name cant be null or empty."); }
#endif
            _worlds[name] = world;
        }

        public void Add(IEcsSystem system)
        {
            _allSystems.Add(system);
            if (system is IEcsRunSystem runSystem)
            {
                AddRunSystem(runSystem);
            }
        }

        /// <summary>
        /// Inserts system into the earliest possible slot after the last write this system reads from
        /// </summary>
        /// <param name="system"></param>
        private void AddRunSystem(IEcsRunSystem system)
        {
            Span<Metric> metrics = stackalloc Metric[_bucketCount];
            for (int i = 0; i < _bucketCount; i++)
            {
                ref var bucket = ref _buckets[i];
                metrics[i] = bucket.GetFitMetric(system);
            }
            bool canAdd = false;
            int bestFitIndex = 0;
            int maxShared = -1;
            //determine earliest possible index
            for (int i = 0; i < metrics.Length; i++)
            {
                ref var metric = ref metrics[i];
                if (!metric.Allowed)
                {
                    bestFitIndex = i + 1;
                }
            }
            //Find optimal bucket to insert into
            for (int i = bestFitIndex; i < metrics.Length; i++)
            {
                ref var metric = ref metrics[i];
                canAdd |= metric.Allowed;
                if (metric.Allowed)
                {
                    if (metric.SharedReads > maxShared)
                    {
                        maxShared = metric.SharedReads;
                        bestFitIndex = i;
                    }
                }
            }
            if (canAdd)
            {
                ref var bucket = ref _buckets[bestFitIndex];
                bucket.GetFitMetric(system);
                bucket.AddUnchecked(system);
            }
            else
            {
                _bucketCount++;
                EnsureBucketsSize();
                _buckets[_bucketCount - 1].GetFitMetric(system);
                _buckets[_bucketCount - 1].AddUnchecked(system);
            }
        }

        void EnsureBucketsSize()
        {
            if (_buckets.Length < _bucketCount)
            {
                var buckets = new SystemsBucket[_buckets.Length * 2];
                Array.Copy(_buckets, 0, buckets, 0, _buckets.Length);
                _buckets = buckets;
            }
        }

        public void Run()
        {
            _currentBucket = 0;

            for (int i = 0; i < _bucketCount; i++)
            {
                //Wait for activation
                _barrier.SignalAndWait();
                DoWork(0);
                //Set next working bucket
                Interlocked.Increment(ref _currentBucket);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        void DoWork(int threadId)
        {
            var systems = _buckets[_currentBucket].parallelRunSystems;
            int count = systems.Count;
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
                systems[index].Run(this, threadId);
            }
        }

        void WorkerLoopThreads(object? id)
        {
            while (!_disposed)
            {
                int threadId = (int)id!;
                //Wait for activation
                _barrier.SignalAndWait();
                if (_disposed)
                {
                    return;
                }
                DoWork(threadId);
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
                    if (worldName != null) { throw new System.Exception($"Empty entity detected in world \"{worldName}\" after {destroySystem.GetType().Name}.Destroy()."); }
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
                    if (worldName != null) { throw new System.Exception($"Empty entity detected in world \"{worldName}\" after {postDestroySystem.GetType().Name}.PostDestroy()."); }
#endif
                }
            }
            _allSystems.Clear();
            _disposed = true;
            _barrier.SignalAndWait();
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i].Join();
            }
            _barrier.Dispose();
        }
        private readonly struct Metric
        {
            public static readonly Metric Invalid = new Metric(0, false);
            public readonly int SharedReads;
            public readonly bool Allowed;

            public Metric(int sharedReads, bool allowed)
            {
                SharedReads = sharedReads;
                Allowed = allowed;
            }
        }
        private struct SystemsBucket
        {
            public List<IEcsRunSystem> parallelRunSystems;
            Dictionary<string, HashSet<Type>?> writeTypes;
            Dictionary<string, HashSet<Type>?> readTypes;
            public Metric GetFitMetric(IEcsRunSystem system)
            {
                if (parallelRunSystems == null)
                {
                    parallelRunSystems = new List<IEcsRunSystem>();
                }
                if (writeTypes == null)
                {
                    writeTypes = new Dictionary<string, HashSet<Type>?>();
                }
                if (readTypes == null)
                {
                    readTypes = new Dictionary<string, HashSet<Type>?>();
                }
                EcsReadAttribute[]? readAttributes = Attribute.GetCustomAttributes(system.GetType(), typeof(EcsReadAttribute)) as EcsReadAttribute[];
                EcsWriteAttribute[]? writeAttributes = Attribute.GetCustomAttributes(system.GetType(), typeof(EcsWriteAttribute)) as EcsWriteAttribute[];
                //We want to write to items
                if (writeAttributes is not null)
                {
                    if (!CheckWriteAttribute(writeAttributes))
                    {
                        return Metric.Invalid;
                    }
                }

                //We want to read items
                if (readAttributes is not null)
                {
                    if (!CheckReadAttribute(readAttributes))
                    {
                        return Metric.Invalid;
                    }

                    //All good can add system
                    int sharedReads = 0;
                    foreach (var item in readAttributes)
                    {
                        if (readTypes.TryGetValue(item.World, out var worldTypes))
                        {
                            //count number of shared reads
                            foreach (var type in item.Pools)
                            {
                                if (worldTypes!.Contains(type))
                                {
                                    sharedReads++;
                                }
                            }
                        }
                    }
                    return new Metric(sharedReads, true);
                }
                return new Metric(0, true);
            }

            bool CheckReadAttribute(EcsReadAttribute[] attributes)
            {
                //for all (world, type[]) pairs
                foreach (var item in attributes)
                {
                    //check if world exists and contains types being written to 
                    if (writeTypes.TryGetValue(item.World, out var worldTypes))
                    {
                        //This world exists but the hashset is null
                        //This implies that the entire world is being used
                        if (worldTypes is null)
                        {
                            return false;
                        }
                        //The hashset contains some amount of types, but we want to use all of the types
                        if (item.Pools.Length == 0)
                        {
                            return false;
                        }
                        //for all types check if we are already writing to it
                        foreach (var type in item.Pools)
                        {
                            //if we are, we can't fit in this bucket
                            if (worldTypes.Contains(type))
                            {
                                return false;
                            }
                        }
                    }
                }
                return true;
            }

            bool CheckWriteAttribute(EcsWriteAttribute[] attributes)
            {
                //for all (world, type[]) pairs
                foreach (var item in attributes)
                {
                    //check if world exists and contains types being read from
                    if (readTypes.TryGetValue(item.World, out var worldTypes))
                    {
                        //This world exists but the hashset is null
                        //This implies that the entire world is being used
                        if (worldTypes is null)
                        {
                            return false;
                        }
                        //The hashset contains some amount of types, but we want to use all of the types
                        if (item.Pools.Length == 0)
                        {
                            return false;
                        }
                        //for all types check if we are already reading from it
                        foreach (var type in item.Pools)
                        {
                            //if we are, we can't fit in this bucket
                            if (worldTypes.Contains(type))
                            {
                                return false;
                            }
                        }
                    }

                    //check if world exists and contains types being written to
                    if (writeTypes.TryGetValue(item.World, out worldTypes))
                    {
                        //This world exists but the hashset is null
                        //This implies that the entire world is being used
                        if (worldTypes is null)
                        {
                            return false;
                        }
                        //The hashset contains some amount of types, but we want to use all of the types
                        if (item.Pools.Length == 0)
                        {
                            return false;
                        }
                        //for all types check if we are already writing to it
                        foreach (var type in item.Pools)
                        {
                            //if we are, we can't fit in this bucket
                            if (worldTypes.Contains(type))
                            {
                                return false;
                            }
                        }
                    }
                }
                return true;
            }


            public void AddUnchecked(IEcsRunSystem system)
            {
                if (parallelRunSystems == null)
                {
                    parallelRunSystems = new List<IEcsRunSystem>();
                }

                EcsReadAttribute[]? readAttributes = Attribute.GetCustomAttributes(system.GetType(), typeof(EcsReadAttribute)) as EcsReadAttribute[];
                EcsWriteAttribute[]? writeAttributes = Attribute.GetCustomAttributes(system.GetType(), typeof(EcsWriteAttribute)) as EcsWriteAttribute[];

                if (readAttributes is not null)
                {
                    foreach (var item in readAttributes)
                    {
                        if (item.Pools.Length == 0)
                        {
                            writeTypes.Add(item.World, null);
                            continue;
                        }
                        //check if world exists and contains types being written to
                        if (!readTypes.TryGetValue(item.World, out var worldTypes))
                        {
                            worldTypes = new HashSet<Type>();
                        }
                        foreach (var type in item.Pools)
                        {
                            worldTypes!.Add(type);
                        }
                    }
                }
                if (writeAttributes is not null)
                {
                    foreach (var item in writeAttributes)
                    {
                        if (item.Pools.Length == 0)
                        {
                            writeTypes.Add(item.World, null);
                            continue;
                        }
                        //check if world exists and contains types being written to
                        if (!writeTypes.TryGetValue(item.World, out var worldTypes))
                        {
                            worldTypes = new HashSet<Type>();
                        }
                        foreach (var type in item.Pools)
                        {
                            Debug.Assert(worldTypes!.Add(type));
                        }
                    }
                }
                parallelRunSystems.Add(system);
            }
        }
    }
}