using EcsLite;
using System.Runtime.CompilerServices;

struct InitStruct : IEcsInit<InitStruct>, IEcsDestroy<InitStruct>
{
    public int a;
    public string b;
    public static void OnDestroy(ref InitStruct c)
    {
        c.a = 0;
    }

    public static void OnInit(ref InitStruct c)
    {
        c.a++;
    }
}
struct NormalStruct
{
    public int a;
}
struct CTORStruct
{
    public int a;
    public CTORStruct()
    {
        a = 5;
    }
}
class Program
{
    static int Main(string[] args)
    {
        EcsWorld world = new EcsWorld();
        world.AllowPool<InitStruct>();
        world.AllowPool<NormalStruct>();
        world.AllowPool<CTORStruct>();
        int ent = world.NewEntity();
        int ent2 = world.NewEntity();
        ref var init = ref world.GetPool<InitStruct>().Add(ent);
        ref var norm = ref world.GetPool<NormalStruct>().Add(ent2);
        ref var ctor = ref world.GetPool<CTORStruct>().Add(ent2);

        EcsSystems systems = new EcsSystems(4, world);
        systems.Add(new TestRunSystemA());
        systems.Add(new TestRunSystemB());
        systems.Add(new TestRunSystemC());
        systems.Add(new TestRunSystemD());
        systems.Add(new TestRunSystemE());
        systems.Init();
        for (int i = 0; i < 10; i++)
        {
            systems.Run();
        }
        systems.Dispose();
        Console.ReadLine();
        return 0;
    }
}

[EcsWrite("Console")]
class TestRunSystemA : IEcsRunSystem
{
    public void Run(EcsSystems systems, int id)
    {
        Console.WriteLine($"Running A {id}");
    }
}
[EcsWrite("Console", typeof(int))]
class TestRunSystemB : IEcsRunSystem
{
    public void Run(EcsSystems systems, int id)
    {
        Console.WriteLine($"Running B {id}");
    }
}
[EcsWrite("Test", typeof(int))]
class TestRunSystemC : IEcsRunSystem
{
    public void Run(EcsSystems systems, int id)
    {
        Console.WriteLine($"Running C {id}");
    }
}

[EcsRead("Test", typeof(int))]
class TestRunSystemD : IEcsRunSystem, IEcsInitSystem
{
    EcsPool<InitStruct> pool;
    EcsFilter filter;

    public void Init(EcsSystems systems)
    {
        pool = systems.GetWorld().GetPool<InitStruct>();
    }

    public void Run(EcsSystems systems, int id)
    {
        Console.WriteLine($"Running D {id}");
    }
}
[EcsWrite("Test", typeof(float))]
class TestRunSystemE : IEcsRunSystem
{
    public void Run(EcsSystems systems, int id)
    {
        Console.WriteLine($"Running E {id}");
    }
}

