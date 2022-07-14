// ----------------------------------------------------------------------------
// The MIT License
// Lightweight ECS framework https://github.com/Leopotam/ecslite
// Copyright (c) 2021-2022 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;

#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace EcsLite
{


    public interface IEcsPool
    {
        void Resize(int capacity);
        bool Has(int entity);
        void Del(int entity);
        void AddRaw(int entity, object dataRaw);
        object GetRaw(int entity);
        void SetRaw(int entity, object dataRaw);
        int GetId();
        Type GetComponentType();
        void Transfer(int oldEntity, int newEntity);
        void Swap(int entityA, int entityB);
        void Clone(int oldEntity, int newEntity);
    }

    public interface IEcsInit<T> where T : struct
    {
        static abstract void OnInit(ref T c);
    }

    public interface IEcsDestroy<T> where T : struct
    {
        static abstract void OnDestroy(ref T c);
    }

#if ENABLE_IL2CPP
    [Il2CppSetOption (Option.NullChecks, false)]
    [Il2CppSetOption (Option.ArrayBoundsChecks, false)]
#endif
    public sealed class EcsPool<T> : IEcsPool where T : struct
    {
        private readonly Type _type;
        private readonly EcsWorld _world;
        private readonly int _id;
        private readonly unsafe delegate* managed<ref T, void> _autoResetInit;
        private readonly unsafe delegate* managed<ref T, void> _autoResetDestroy;
        // 1-based index.
        private T[] _denseItems;
        private int[] _sparseItems;
        private int _denseItemsCount;
        private int[] _recycledItems;
        private int _recycledItemsCount;
#if ENABLE_IL2CPP && !UNITY_EDITOR
      protected  T _autoresetFakeInstance;
#endif

        internal EcsPool(EcsWorld world, int id, int denseCapacity, int sparseCapacity, int recycledCapacity)
        {
            _type = typeof(T);
            _world = world;
            _id = id;
            _denseItems = new T[denseCapacity + 1];
            _sparseItems = new int[sparseCapacity];
            _denseItemsCount = 1;
            _recycledItems = new int[recycledCapacity];
            _recycledItemsCount = 0;
            unsafe
            {
                if (typeof(IEcsInit<T>).IsAssignableFrom(_type))
                {
                    _autoResetInit = (delegate*<ref T, void>)typeof(T).GetMethod(nameof(IEcsInit<T>.OnInit))!.MethodHandle.GetFunctionPointer();
                }
                if (typeof(IEcsDestroy<T>).IsAssignableFrom(_type))
                {
                    _autoResetDestroy = (delegate*<ref T, void>)typeof(T).GetMethod(nameof(IEcsDestroy<T>.OnDestroy))!.MethodHandle.GetFunctionPointer();
                }
            }
        }

#if UNITY_2020_3_OR_NEWER
        [UnityEngine.Scripting.Preserve]
#endif
        void ReflectionSupportHack()
        {
            _world.GetPool<T>();
            _world.FilterInc<T>().Exc<T>().End();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsWorld GetWorld()
        {
            return _world;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetId()
        {
            return _id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Type GetComponentType()
        {
            return _type;
        }

        void IEcsPool.Resize(int capacity)
        {
            Array.Resize(ref _sparseItems, capacity);
        }

        object IEcsPool.GetRaw(int entity)
        {
            return Get(entity);
        }

        void IEcsPool.SetRaw(int entity, object dataRaw)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (dataRaw == null || dataRaw.GetType() != _type) { throw new ArgumentException("Invalid component data, valid \"{typeof (T).Name}\" instance required."); }
            if (_sparseItems[entity] <= 0) { throw new InvalidOperationException($"Component \"{typeof(T).Name}\" not attached to entity."); }
#endif
            _denseItems[_sparseItems[entity]] = (T)dataRaw;
        }

        void IEcsPool.AddRaw(int entity, object dataRaw)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (dataRaw == null || dataRaw.GetType() != _type) { throw new ArgumentException("Invalid component data, valid \"{typeof (T).Name}\" instance required."); }
#endif
            ref var data = ref Add(entity);
            data = (T)dataRaw;
        }

        public T[] GetRawDenseItems()
        {
            return _denseItems;
        }

        public ref int GetRawDenseItemsCount()
        {
            return ref _denseItemsCount;
        }

        public int[] GetRawSparseItems()
        {
            return _sparseItems;
        }

        public int[] GetRawRecycledItems()
        {
            return _recycledItems;
        }

        public ref int GetRawRecycledItemsCount()
        {
            return ref _recycledItemsCount;
        }

        /// <summary>
        /// Adds component to an Entity.
        /// Components are guaranteed to have default values. 
        /// Runs the Initialize method on the component if present.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns>Returns a refrence to the added component</returns>
        /// <exception cref="InvalidOperationException">Thrown when entity has component already or is not alive</exception>
        public ref T Add(int entity)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (!_world.IsEntityAlive(entity)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
            if (_sparseItems[entity] > 0) { throw new InvalidOperationException($"Component \"{typeof(T).Name}\" already attached to entity."); }
#endif
            int idx;
            if (_recycledItemsCount > 0)
            {
                idx = _recycledItems[--_recycledItemsCount];
            }
            else
            {
                idx = _denseItemsCount;
                if (_denseItemsCount == _denseItems.Length)
                {
                    Array.Resize(ref _denseItems, _denseItemsCount << 1);
                }
                _denseItemsCount++;
            }
            unsafe //Init component on creation or recycle
            {
                if (_autoResetInit != null)
                {
                    _autoResetInit(ref _denseItems[idx]);
                }
            }
            _sparseItems[entity] = idx;
            _world.OnEntityChangeInternal(entity, _id, true);
            _world.Entities[entity].ComponentsCount++;
#if DEBUG || LEOECSLITE_WORLD_EVENTS
            _world.RaiseEntityChangeEvent(entity);
#endif
            return ref _denseItems[idx];
        }

        /// <summary>
        /// Swaps ownership of two components
        /// </summary>
        /// <param name="entityA">Entity A</param>
        /// <param name="entityB">Entity B</param>
        /// <exception cref="InvalidOperationException">Throws in Debug if entity is not alive</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Swap(int entityA, int entityB)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (!_world.IsEntityAlive(entityA)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
            if (!_world.IsEntityAlive(entityB)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
            if (_sparseItems[entityA] == 0) { throw new InvalidOperationException($"Component \"{typeof(T).Name}\" not attached to entity."); }
            if (_sparseItems[entityB] == 0) { throw new InvalidOperationException($"Component \"{typeof(T).Name}\" not attached to entity."); }
#endif
            int temp = _sparseItems[entityA];
            _sparseItems[entityA] = _sparseItems[entityB];
            _sparseItems[entityB] = temp;

#if DEBUG || LEOECSLITE_WORLD_EVENTS
            _world.RaiseEntityChangeEvent(entityA);
            _world.RaiseEntityChangeEvent(entityB);
#endif
        }

        /// <summary>
        /// Changes ownership of a component from one entity to another.
        /// Does not destroy and copy the component.
        /// </summary>
        /// <param name="oldEntity">The entity the component is taken from</param>
        /// <param name="newEntity">The entity the component is given to</param>
        /// <exception cref="InvalidOperationException">Throws in Debug if entity is not alive</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Transfer(int oldEntity, int newEntity)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (!_world.IsEntityAlive(oldEntity)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
            if (!_world.IsEntityAlive(newEntity)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
            if (_sparseItems[oldEntity] == 0) { throw new InvalidOperationException($"Component \"{typeof(T).Name}\" not attached to entity."); }
            if (_sparseItems[newEntity] > 0) { throw new InvalidOperationException($"Component \"{typeof(T).Name}\" already attached to entity."); }
#endif
            _sparseItems[newEntity] = _sparseItems[oldEntity];
            _sparseItems[oldEntity] = 0;

            _world.OnEntityChangeInternal(oldEntity, _id, false);
            _world.OnEntityChangeInternal(newEntity, _id, true);
            ref var entityDataOld = ref _world.Entities[oldEntity];
            ref var entityDataNew = ref _world.Entities[newEntity];
            entityDataOld.ComponentsCount--;
            entityDataNew.ComponentsCount++;

#if DEBUG || LEOECSLITE_WORLD_EVENTS
            _world.RaiseEntityChangeEvent(oldEntity);
            _world.RaiseEntityChangeEvent(newEntity);
#endif
        }

        /// <summary>
        /// Clones component data from one entity to another entity without this component.
        /// </summary>
        /// <param name="oldEntity">Entity to clone from</param>
        /// <param name="newEntity">Entity to clone to</param>
        /// <exception cref="InvalidOperationException">Throws in Debug if entity is not alive</exception>
        public void Clone(int oldEntity, int newEntity)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (!_world.IsEntityAlive(oldEntity)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
            if (!_world.IsEntityAlive(newEntity)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
            if (_sparseItems[oldEntity] == 0) { throw new InvalidOperationException($"Component \"{typeof(T).Name}\" not attached to entity."); }
            if (_sparseItems[newEntity] > 0) { throw new InvalidOperationException($"Component \"{typeof(T).Name}\" already attached to entity."); }
#endif
            int idx;
            if (_recycledItemsCount > 0)
            {
                idx = _recycledItems[--_recycledItemsCount];
            }
            else
            {
                idx = _denseItemsCount;
                if (_denseItemsCount == _denseItems.Length)
                {
                    Array.Resize(ref _denseItems, _denseItemsCount << 1);
                }
                _denseItemsCount++;
            }
            _sparseItems[newEntity] = idx;
            _world.OnEntityChangeInternal(newEntity, _id, true);
            _world.Entities[newEntity].ComponentsCount++;
            _denseItems[idx] = _denseItems[_sparseItems[oldEntity]];

#if DEBUG || LEOECSLITE_WORLD_EVENTS
            _world.RaiseEntityChangeEvent(newEntity);
#endif
        }

        /// <summary>
        /// Gets a refrence to the component on this entity
        /// </summary>
        /// <param name="entity">The entity to get the component from</param>
        /// <returns>Returns a refrence to the component on this entity</returns>
        /// <exception cref="InvalidOperationException">Throws in Debug if the entity is not alive or doesn't have this component.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entity)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (!_world.IsEntityAlive(entity)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
            if (_sparseItems[entity] == 0) { throw new InvalidOperationException($"Cant get \"{typeof(T).Name}\" component - not attached."); }
#endif
            return ref _denseItems[_sparseItems[entity]];
        }

        /// <summary>
        /// Gets or adds refrence to the component on this entity
        /// </summary>
        /// <param name="entity">The entity to get the component from</param>
        /// <returns>Returns a refrence to the component on this entity</returns>
        /// <exception cref="InvalidOperationException">Throws in Debug if the entity is not alive or doesn't have this component.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetOrAdd(int entity)
        {
            if (Has(entity))
            {
                return ref Get(entity);
            }
            else
            {
                return ref Add(entity);
            }
        }

        /// <summary>
        /// Gets a readonly refrence to the component on this entity
        /// </summary>
        /// <param name="entity">The entity to get the component from</param>
        /// <returns>Returns a readonly refrence to the component on this entity</returns>
        /// <exception cref="InvalidOperationException">Throws in Debug if the entity is not alive or doesn't have this component.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T GetReadonly(int entity)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (!_world.IsEntityAlive(entity)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
            if (_sparseItems[entity] == 0) { throw new InvalidOperationException($"Cant get \"{typeof(T).Name}\" component - not attached."); }
#endif
            return ref _denseItems[_sparseItems[entity]];
        }

        /// <summary>
        /// Gets whether the entity has this component or not.
        /// </summary>
        /// <param name="entity">The entity to check</param>
        /// <returns>Returns whether the entity has this component or not.</returns>
        /// <exception cref="InvalidOperationException">Throws in Debug if entity is not alive</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has(int entity)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (!_world.IsEntityAlive(entity)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
#endif
            return _sparseItems[entity] > 0;
        }

        /// <summary>
        /// Removes a component from an entity.
        /// Runs the Destroy method if present.
        /// </summary>
        /// <param name="entity"></param>
        /// <exception cref="InvalidOperationException">Throws in Debug if entity is not alive</exception>
        public void Del(int entity)
        {
#if DEBUG && !ECSLITE_NO_SANITIZE_CHECKS
            if (!_world.IsEntityAlive(entity)) { throw new InvalidOperationException("Cant touch destroyed entity."); }
#endif
            ref var sparseData = ref _sparseItems[entity];
            if (sparseData > 0)
            {
                _world.OnEntityChangeInternal(entity, _id, false);
                if (_recycledItemsCount == _recycledItems.Length)
                {
                    Array.Resize(ref _recycledItems, _recycledItemsCount << 1);
                }
                _recycledItems[_recycledItemsCount++] = sparseData;
                unsafe //Destroy component on destroy
                {
                    if (_autoResetDestroy != null)
                    {
                        _autoResetDestroy(ref _denseItems[sparseData]);
                    }
                }
                // Reset to default
                _denseItems[sparseData] = default;
                sparseData = 0;
                ref var entityData = ref _world.Entities[entity];
                entityData.ComponentsCount--;
#if DEBUG || LEOECSLITE_WORLD_EVENTS
                _world.RaiseEntityChangeEvent(entity);
#endif
                if (entityData.ComponentsCount == 0)
                {
                    _world.DelEntity(entity);
                }
            }
        }
    }
}
