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
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class EcsWriteAttribute : Attribute
    {
        // This is a positional argument
        public EcsWriteAttribute(params Type[]? types)
        {
            WrittenTypes = types?.Distinct() ?? Array.Empty<Type>();
        }

        public IEnumerable<Type> WrittenTypes { get; }
    }
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class EcsReadAttribute : Attribute
    {
        // This is a positional argument
        public EcsReadAttribute(params Type[]? types)
        {
            ReadTypes = types?.Distinct() ?? Array.Empty<Type>();
        }

        public IEnumerable<Type> ReadTypes { get; }
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
            _threads = new Thread[_numThreads];
            _barrier = new Barrier(_numThreads + 1);
            for (int i = 0; i < _numThreads; i++)
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
            }
            for (int i = 0; i < _numThreads; i++)
            {
                _threads[i].Start(i + 1);
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
                bucket.Add(system);
            }
            else
            {
                _bucketCount++;
                EnsureBucketsSize();
                _buckets[_bucketCount - 1].Add(system);
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
                DoWork(0);
                //Set next working bucket
                Console.WriteLine("Incrementing!");
                Interlocked.Increment(ref _currentBucket);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        void DoWork(int threadId)
        {
            Console.WriteLine($"{threadId} Waiting for signal");
            //Wait for activation
            _barrier.SignalAndWait();
            Console.WriteLine($"{threadId} Dispatching");
            var systems = _buckets[_currentBucket].parallelRunSystems;
            int count = systems.Count;
            int systemsPerThread = Math.DivRem(count, _numThreads + 1, out var remainder);
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
                systems[index].Run(this);
            }
            Console.WriteLine($"{threadId} Waiting for other");
            _barrier.SignalAndWait();
        }

        void WorkerLoopThreads(object id)
        {
            while (!_disposed)
            {
                int threadId = (int)id;
                DoWork(threadId);
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
#endif

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
            _disposed = true;
            for (int i = 0; i < _numThreads; i++)
            {
                _threads[i].Join();
            }
            _barrier.Dispose();
        }
        private struct Metric
        {
            public static readonly Metric Invalid = new Metric(0, false);
            public int SharedReads;
            public bool Allowed;

            public Metric(int sharedReads, bool allowed)
            {
                SharedReads = sharedReads;
                Allowed = allowed;
            }
        }
        private struct SystemsBucket
        {
            public List<IEcsRunSystem> parallelRunSystems;
            HashSet<Type> writeTypes;
            HashSet<Type> readTypes;
            public Metric GetFitMetric(IEcsRunSystem system)
            {
                if (parallelRunSystems == null)
                {
                    parallelRunSystems = new List<IEcsRunSystem>();
                }
                if (writeTypes == null)
                {
                    writeTypes = new HashSet<Type>();
                }
                if (readTypes == null)
                {
                    readTypes = new HashSet<Type>();
                }
                EcsReadAttribute? readAttribute = Attribute.GetCustomAttribute(system.GetType(), typeof(EcsReadAttribute)) as EcsReadAttribute;
                EcsWriteAttribute? writeAttribute = Attribute.GetCustomAttribute(system.GetType(), typeof(EcsWriteAttribute)) as EcsWriteAttribute;

                if (readAttribute is not null)
                {
                    foreach (var type in readAttribute.ReadTypes)
                    {
                        if (writeTypes.Contains(type))
                        {
                            return Metric.Invalid;
                        }
                    }
                }
                if (writeAttribute is not null)
                {
                    foreach (var type in writeAttribute.WrittenTypes)
                    {
                        //Cannot read and write at the same time
                        if (readTypes.Contains(type))
                        {
                            return Metric.Invalid;
                        }
                        //Cannot write and write at the same time
                        if (writeTypes.Contains(type))
                        {
                            return Metric.Invalid;
                        }
                    }
                }
                int sharedReads = 0;
                //All good can add system
                if (readAttribute is not null)
                {
                    foreach (var type in readAttribute.ReadTypes)
                    {
                        if (readTypes.Contains(type))
                        {
                            sharedReads++;
                        }
                    }
                }
                return new Metric(sharedReads, true);
            }

            public void Add(IEcsRunSystem system)
            {
                if (parallelRunSystems == null)
                {
                    parallelRunSystems = new List<IEcsRunSystem>();
                }
                if (writeTypes == null)
                {
                    writeTypes = new HashSet<Type>();
                }
                if (readTypes == null)
                {
                    readTypes = new HashSet<Type>();
                }
                EcsReadAttribute? readAttribute = Attribute.GetCustomAttribute(system.GetType(), typeof(EcsReadAttribute)) as EcsReadAttribute;
                EcsWriteAttribute? writeAttribute = Attribute.GetCustomAttribute(system.GetType(), typeof(EcsWriteAttribute)) as EcsWriteAttribute;

                if (readAttribute is not null)
                {
                    foreach (var type in readAttribute.ReadTypes)
                    {
                        readTypes.Add(type);
                    }
                }
                if (writeAttribute is not null)
                {
                    foreach (var type in writeAttribute.WrittenTypes)
                    {
                        writeTypes.Add(type);
                    }
                }
                parallelRunSystems.Add(system);
            }
        }
    }
}