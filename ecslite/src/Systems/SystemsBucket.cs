// ----------------------------------------------------------------------------
// The MIT License
// Lightweight ECS framework https://github.com/Leopotam/ecslite
// Copyright (c) 2021-2022 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System.Diagnostics;

#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace EcsLite.Systems
{
    public sealed partial class EcsSystems
    {
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
                bucket.AddUnchecked(_currentGroupState, system, _currentDelayTime);
            }
            else
            {
                _bucketCount++;
                EnsureBucketsSize();
                _buckets[_bucketCount - 1].GetFitMetric(system);
                _buckets[_bucketCount - 1].AddUnchecked(_currentGroupState, system, _currentDelayTime);
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
            public IReadOnlyList<EcsTickedSystem>? ParallelRunSystems => parallelRunSystems;
            List<EcsTickedSystem>? parallelRunSystems;
            Dictionary<string, HashSet<Type>?> writeTypes;
            Dictionary<string, HashSet<Type>?> readTypes;
            public Metric GetFitMetric(IEcsRunSystem system)
            {
                if (parallelRunSystems == null)
                {
                    parallelRunSystems = new List<EcsTickedSystem>();
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
                foreach (var writeAttribute in attributes)
                {
                    //check if world exists and contains types being read from
                    if (readTypes.TryGetValue(writeAttribute.World, out var worldTypes))
                    {
                        //This world exists but the hashset is null
                        //This implies that the entire world is being used
                        if (worldTypes is null)
                        {
                            return false;
                        }
                        //The hashset contains some amount of types, but we want to use all of the types
                        if (writeAttribute.Pools.Length == 0)
                        {
                            return false;
                        }
                        //for all types check if we are already reading from it
                        foreach (var type in writeAttribute.Pools)
                        {
                            //if we are, we can't fit in this bucket
                            if (worldTypes.Contains(type))
                            {
                                return false;
                            }
                        }
                    }

                    //check if world exists and contains types being written to
                    if (writeTypes.TryGetValue(writeAttribute.World, out worldTypes))
                    {
                        //This world exists but the hashset is null
                        //This implies that the entire world is being used
                        if (worldTypes is null)
                        {
                            return false;
                        }
                        //The hashset contains some amount of types, but we want to use all of the types
                        if (writeAttribute.Pools.Length == 0)
                        {
                            return false;
                        }
                        //for all types check if we are already writing to it
                        foreach (var type in writeAttribute.Pools)
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


            public void AddUnchecked(bool state, IEcsRunSystem system, float tickDelay)
            {
                if (parallelRunSystems == null)
                {
                    parallelRunSystems = new List<EcsTickedSystem>();
                }

                EcsReadAttribute[]? readAttributes = Attribute.GetCustomAttributes(system.GetType(), typeof(EcsReadAttribute)) as EcsReadAttribute[];
                EcsWriteAttribute[]? writeAttributes = Attribute.GetCustomAttributes(system.GetType(), typeof(EcsWriteAttribute)) as EcsWriteAttribute[];

                if (readAttributes is not null)
                {
                    foreach (var item in readAttributes)
                    {
                        if (item.Pools.Length == 0)
                        {
                            readTypes.Add(item.World, null);
                            continue;
                        }
                        //check if world exists and contains types being written to
                        if (!readTypes.TryGetValue(item.World, out var worldTypes))
                        {
                            worldTypes = new HashSet<Type>();
                            readTypes.Add(item.World, worldTypes);
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
                            writeTypes.Add(item.World, worldTypes);
                        }
                        foreach (var type in item.Pools)
                        {
                            Debug.Assert(worldTypes!.Add(type));
                        }
                    }
                }
                parallelRunSystems.Add(new EcsTickedSystem(state, system, tickDelay));
            }
        }
    }
}