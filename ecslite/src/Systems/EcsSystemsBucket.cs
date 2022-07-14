using System.Diagnostics;

namespace EcsLite.Systems
{
    internal readonly struct Metric
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

    internal struct EcsSystemsBucket
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


        public void AddUnchecked(IEcsRunSystem system)
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
            parallelRunSystems.Add(new EcsTickedSystem(system));
        }
    }
}
