using EcsLite.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EcsLiteTests
{
    struct ResizeMessage
    {
        public int w, h;
    }
    internal class MessageTest
    {
        static void Main(string[] args)
        {
            MessageBus bus = new MessageBus();
            var pool = bus.GetPool<int>();
            pool.Add(0, 0);
            pool.Add(0, 0);
            pool.Add(1, 0);
            pool.Add(2, 0);
            pool.Add(1, 0);
            pool.Add(2, 0);
            pool.Add(2, 0);
            pool.Add(3, 0);
            pool.Add(3, 0);
            pool.Add(3, 0);
            var msgs = pool.Messages;
            for (int i = 0; i < msgs.Length; i++)
            {
                Console.Write($"{msgs[i].Origin}, ");
            }
            Console.WriteLine();
            pool.RemoveMessages(2);
            msgs = pool.Messages;
            for (int i = 0; i < msgs.Length; i++)
            {
                Console.Write($"{msgs[i].Origin}, ");
            }
            Console.WriteLine();
            pool.RemoveMessages(1);
            msgs = pool.Messages;
            for (int i = 0; i < msgs.Length; i++)
            {
                Console.Write($"{msgs[i].Origin}, ");
            }
            Console.WriteLine();
            pool.RemoveMessages(3);
            msgs = pool.Messages;
            for (int i = 0; i < msgs.Length; i++)
            {
                Console.Write($"{msgs[i].Origin}, ");
            }
            Console.WriteLine();
            pool.RemoveMessages(0);
            msgs = pool.Messages;
            for (int i = 0; i < msgs.Length; i++)
            {
                Console.Write($"{msgs[i].Origin}, ");
            }
            Console.WriteLine();
            Console.WriteLine("Done");
            Console.ReadLine();
        }
    }
}
