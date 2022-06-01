using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EcsLite.Systems
{
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
}
