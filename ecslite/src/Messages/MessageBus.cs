using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace EcsLite.Messages
{
    public class MessageBus
    {
        public IReadOnlyDictionary<Type, IMessagePool> Pools => _pools;
        Dictionary<Type, IMessagePool> _pools;
        public MessageBus()
        {
            _pools = new Dictionary<Type, IMessagePool>();
        }

        /// <summary>
        /// Gets or creates a dense pool for a message of a specific type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>A dense pool with messages of the specified type</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MessagePool<T> GetPool<T>()
        {
            if (!_pools.TryGetValue(typeof(T), out var pool))
            {
                pool = new MessagePool<T>();
                _pools.Add(typeof(T), pool);
            }
            return (MessagePool<T>)pool;
        }
        /// <summary>
        /// Gets a dense pool for a message of a specific type.
        /// Does not create new pools.
        /// </summary>
        /// <param name="type">The type of the data stored</param>
        /// <returns>A dense pool with messages of the specified type</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IMessagePool GetPool(Type type)
        {
            return _pools[type];
        }
    }
}
