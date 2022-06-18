using EcsLite;
using EcsLite.Systems;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        var normPool = world.GetPool<NormalStruct>();
        var initPool = world.GetPool<InitStruct>();
        initPool.Transfer(ent, ent2);
        Console.WriteLine(!initPool.Has(ent));
        Console.WriteLine(initPool.Has(ent2));
        normPool.Clone(ent2, ent);
        norm.a = 100;
        Console.WriteLine(normPool.Has(ent));
        Console.WriteLine(normPool.Has(ent2));
        Console.WriteLine(normPool.Get(ent).a);
        Console.WriteLine(normPool.Get(ent2).a);
        normPool.Swap(ent2, ent);
        Console.WriteLine(normPool.Get(ent).a);
        Console.WriteLine(normPool.Get(ent2).a);
        EcsSystemsBuilder builder = new EcsSystemsBuilder(world);
        builder.SetTickDelay(0); //Run as fast as possible
        builder.SetGroup("Test");
        builder.Add<TestRunSystemA>();
        builder.Add<TestRunSystemB>();
        builder.SetTickDelay(1f / 60f); //Run 60 times a second
        builder.ClearGroup();
        builder.Add<TestRunSystemC>();
        builder.Add<TestRunSystemD>();
        builder.SetTickDelay(1f / 20f);//Run 20 times a second
        builder.Add<TestRunSystemE>();
        builder.Inject("Test", "I like trains.");
        builder.InjectSingleton(new TestSingleton());
        var systems = builder.Finish(6);
        systems.Init();
        var watch = Stopwatch.StartNew();
        double delta = double.Epsilon;
        while (true)
        {
            double prev = watch.Elapsed.TotalSeconds;
            systems.Run(delta);
            double current = watch.Elapsed.TotalSeconds;
            delta = current - prev;
        }
        Console.WriteLine("Disposing!");
        systems.Dispose();
        Console.ReadLine();
        return 0;
    }
}

class TestSingleton
{
    public int Coolness = 10000;
}

[EcsWrite("Console")]
class TestRunSystemA : EcsSystem, IEcsRunSystem
{
    int runs = 0;
    public TestRunSystemA(EcsSystems systems) : base(systems)
    {
    }

    public void Run(EcsSystems systems, float dt, int id)
    {
        runs++;
        Console.WriteLine($"Running A {dt}");
    }
}
[EcsWrite("Console", typeof(int))]
class TestRunSystemB : EcsSystem, IEcsRunSystem
{
    int runs = 0;
    public TestRunSystemB(EcsSystems systems) : base(systems)
    {

    }
    public void Run(EcsSystems systems, float dt, int id)
    {
        runs++;
        //Console.WriteLine($"TestString: \"{GetInjected<string>("Test")}\"");
        //Console.WriteLine($"Running B {id}");
    }
}
[EcsRead("Test", typeof(int))]
class TestRunSystemC : EcsSystem, IEcsRunSystem
{
    int runs = 0;
    TestSingleton singleton;
    public TestRunSystemC(EcsSystems systems) : base(systems)
    {
        singleton = GetSingleton<TestSingleton>();
    }
    public void Run(EcsSystems systems, float dt, int id)
    {
        runs++;
        //Console.WriteLine($"Coolness: {singleton.Coolness}");
        //Console.WriteLine($"Running C {id}");
    }
}

[EcsWrite("Test", typeof(int))]
class TestRunSystemD : EcsSystem, IEcsRunSystem
{
    int runs = 0;
    EcsPool<InitStruct> pool;
    EcsFilter filter;
    TestSingleton singleton;
    public TestRunSystemD(EcsSystems systems) : base(systems)
    {
        pool = GetPool<InitStruct>();
        filter = FilterInc<InitStruct>().End();
        singleton = GetSingleton<TestSingleton>();
    }

    public void Run(EcsSystems systems, float dt, int id)
    {
        foreach (var item in filter)
        {

        }
        runs++;
        singleton.Coolness--;
        //Console.WriteLine($"Running D {id}");
    }
}
[EcsWrite("Test", typeof(float))]
class TestRunSystemE : EcsSystem, IEcsRunSystem
{
    float elapsed;
    int runs = 0;
    public TestRunSystemE(EcsSystems systems) : base(systems)
    {
    }
    public void Run(EcsSystems systems, float dt, int id)
    {
        runs++;
        if (elapsed < 10 && elapsed + dt > 10)
            DisableGroupNextFrame("Test");
        if (elapsed < 20 && elapsed + dt > 20)
            EnableGroupNextFrame("Test");
        elapsed += dt;
    }
}