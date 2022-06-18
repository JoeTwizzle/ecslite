using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Reflection;

namespace EcsLite.Systems
{
    public class EcsSystemsBuilder
    {
        private static readonly System.Reflection.ConstructorInfo systemsCtor = typeof(EcsSystems).GetConstructor(new Type[] { typeof(int), typeof(EcsWorld), typeof(Dictionary<string, EcsWorld>), typeof(List<IEcsSystem>), typeof(Dictionary<string, object>), typeof(Dictionary<Type, object>), typeof(EcsSystemsBucket[]), typeof(Dictionary<string, List<EcsTickedSystem>>) })!;
        private readonly Queue<SystemCreateInfo> _delayedAddQueue;
        private readonly EcsWorld _defaultWorld;
        private readonly Dictionary<string, EcsWorld> _worlds;
        private readonly List<IEcsSystem> _allSystems;
        private readonly Dictionary<string, List<EcsTickedSystem>> _groups;
        private readonly Dictionary<Type, object> _injectedSingletons;
        private readonly Dictionary<string, object> _injected;
        private readonly List<(IEcsSystem, ConstructorInfo)> _constructors;
        private EcsSystemsBucket[] _buckets;
        private string? _currentGroupName;
        private float _currentDelayTime;
        private bool _currentGroupState;
        private int _bucketCount;

        public EcsSystemsBuilder(EcsWorld world)
        {
            _injected = new Dictionary<string, object>();
            _injectedSingletons = new Dictionary<Type, object>();
            _defaultWorld = world;
            _buckets = new EcsSystemsBucket[4];
            _constructors = new List<(IEcsSystem, ConstructorInfo)>();
            _worlds = new Dictionary<string, EcsWorld>(4);
            _allSystems = new List<IEcsSystem>(128);
            _delayedAddQueue = new Queue<SystemCreateInfo>();
            _groups = new Dictionary<string, List<EcsTickedSystem>>(8);
        }

        public EcsSystemsBuilder Add<T>() where T : EcsSystem
        {
            _delayedAddQueue.Enqueue(new SystemCreateInfo(typeof(T), _currentDelayTime, _currentGroupName, _currentGroupState));
            return this;
        }

        public EcsSystemsBuilder SetTickDelay(float delay)
        {
            _currentDelayTime = delay / 1000.0f;
            return this;
        }

        public EcsSystemsBuilder SetGroup(string name, bool defaultState = true)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _currentGroupState = defaultState;
                _currentGroupName = name;
                _groups.Add(name, new List<EcsTickedSystem>());
            }
            else
            {
                throw new ArgumentException("The cannot be null or empty", nameof(name));
            }
            return this;
        }

        public EcsSystemsBuilder ClearGroup()
        {
            _currentGroupState = true;
            _currentGroupName = null;
            return this;
        }

        public EcsSystemsBuilder AddWorld(EcsWorld world, string name)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (string.IsNullOrEmpty(name)) { throw new Exception("World name can't be null or empty."); }
#endif
            _worlds[name] = world;
            return this;
        }

        public EcsSystemsBuilder Inject<T>(string identifier, T data) where T : notnull
        {
            _injected.Add(identifier, data);
            return this;
        }

        public EcsSystemsBuilder InjectSingleton<T>(T data) where T : notnull
        {
            _injectedSingletons.Add(typeof(T), data);
            return this;
        }

        public EcsSystems Finish(int numThreads)
        {
            CreateSystems();
            Array.Resize(ref _buckets, _bucketCount);
            var ecsSystems = new EcsSystems(numThreads, _defaultWorld, _worlds, _allSystems, _injected, _injectedSingletons, _buckets, _groups);
            var paramters = new object[] { ecsSystems };
            foreach (var ctor in _constructors)
            {
                ctor.Item2.Invoke(ctor.Item1, paramters);
            }
            return ecsSystems;
        }


        private void CreateSystems()
        {
            while (_delayedAddQueue.Count > 0)
            {
                SystemCreateInfo info = _delayedAddQueue.Dequeue();
                IEcsSystem system = (IEcsSystem)FormatterServices.GetUninitializedObject(info.Type);
                _constructors.Add((system, info.Type.GetConstructor(new Type[] { typeof(EcsSystems) })!));
                _allSystems.Add(system);
                if (system is IEcsRunSystem runSystem)
                {
                    int bucket = AddRunSystem(runSystem, info.DelayTime);
                    var tickedSystems = _buckets[bucket].ParallelRunSystems!;
                    var tickedSystem = tickedSystems[tickedSystems.Count - 1];
                    if (!string.IsNullOrWhiteSpace(info.GroupName))
                    {
                        tickedSystem.Enabled = info.GroupState;
                        
                        _groups[info.GroupName].Add(tickedSystem);
                    }
                }
            }
        }

        /// <summary>
        /// Inserts system into the earliest possible bucket after the last write this system reads from
        /// </summary>
        /// <param name="system"></param>
        /// <returns>The index of the bucket that was inserted into</returns>
        private int AddRunSystem(IEcsRunSystem system, float delayTime)
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
                bucket.AddUnchecked(system, delayTime);
                return bestFitIndex;
            }
            else
            {
                _bucketCount++;
                EnsureBucketsSize();
                _buckets[_bucketCount - 1].GetFitMetric(system);
                _buckets[_bucketCount - 1].AddUnchecked(system, delayTime);
                return _bucketCount - 1;
            }
        }
        void EnsureBucketsSize()
        {
            if (_buckets.Length < _bucketCount)
            {
                var buckets = new EcsSystemsBucket[_buckets.Length * 2];
                Array.Copy(_buckets, 0, buckets, 0, _buckets.Length);
                _buckets = buckets;
            }
        }
    }
}
