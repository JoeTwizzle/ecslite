using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace EcsLite.Messages
{
    [DebuggerDisplay("Data = {Data}", Name = "Origin = {Origin}")]
    public readonly struct Message<T>
    {
        public readonly int Origin;
        public readonly T Data;

        public Message(int origin, T data)
        {
            Origin = origin;
            Data = data;
        }

        private string? GetDebuggerDisplay()
        {
            return ToString();
        }
    }

    public interface IMessagePool
    {
        int Count { get; }
        bool HasMessages { get; }
        void AddMessageRaw(int id, object data);
        void RemoveMessages(int id);
    }

    public class MessagePool<T> : IMessagePool
    {
        public bool HasMessages => Count > 0;
        public int Count { get; private set; }
        private object _lockObj = new object();
        private Message<T>[] _messages;
        public ReadOnlySpan<Message<T>> Messages => _messages.AsSpan(0, Count);
        public MessagePool()
        {
            Count = 0;
            _messages = new Message<T>[10];
        }

        public void AddMessageRaw(int id, object data)
        {
            Add(id, (T)data);
        }

        /// <summary>
        /// Adds a message to the internal dense array
        /// </summary>
        /// <param name="id">The Origin id of the message</param>
        /// <param name="data">Data of the message</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public void Add(int id, T data)
        {
            lock (_lockObj)
            {
                if (Count >= _messages.Length)
                {
                    Array.Resize(ref _messages, (int)BitOperations.RoundUpToPowerOf2((uint)(_messages.Length + 1)));
                }
                _messages[Count++] = new Message<T>(id, data);
            }
        }

        // -------------EXAMPLE------------- 
        /*
         * { 0, 0, 1, 2, 1, 2, 2, 3, 3, 3 } -> Remove(2)
         * { 0, 0, 1, 1, 1, 3, 3 } -> Remove(1)
         * { 0, 0, 3, 3 } -> Remove(3)
         * { 0, 0 } -> Remove(0)
         * { }
        */

        /// <summary>
        /// Removes all messages with an origin matching the id
        /// </summary>
        /// <param name="id"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public void RemoveMessages(int id)
        {
            lock (_lockObj)
            {
                int removals = 0;
                int copyStart = 0;
                int copyCount = 0;
                int copyDest = 0;
                bool prevElementRemoved = false;
                bool once = true;
                for (int i = 0; i < Count; i++)
                {
                    bool toBeRemoved = _messages[i].Origin == id;
                    if (once)
                    {
                        prevElementRemoved = toBeRemoved;
                        once = false;
                    }
                    if (prevElementRemoved && !toBeRemoved)//Falling edge
                    {
                        copyStart = i;
                    }
                    else if (!prevElementRemoved && toBeRemoved)//Rising edge
                    {
                        if (copyStart != 0 && copyCount != 0)
                        {
                            Array.Copy(_messages, copyStart, _messages, copyDest, copyCount);
                        }
                        copyDest = i;
                        copyCount = 0;
                    }
                    if (toBeRemoved)
                    {
                        removals++;
                    }
                    else
                    {
                        copyCount++;
                    }
                    prevElementRemoved = toBeRemoved;
                }
                if (copyStart != 0 && copyCount != 0)
                {
                    Array.Copy(_messages, copyStart, _messages, copyDest, copyCount);
                }
                Count -= removals;
            }
        }
    }
}