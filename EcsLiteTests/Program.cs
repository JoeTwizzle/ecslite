using EcsLite;

struct InitStruct : IEcsInit<InitStruct>, IEcsDestroy<InitStruct>
{
    public int a;

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
        //world.AllowPool<CTORStruct>();
        int ent = world.NewEntity();
        ref var init = ref world.GetPool<InitStruct>().Add(ent);
        ref var norm = ref world.GetPool<NormalStruct>().Add(ent);
        ref var ctor = ref world.GetPool<CTORStruct>().Add(ent);
        Console.WriteLine(init.a);
        Console.WriteLine(norm.a);
        Console.WriteLine(ctor.a);
        Console.ReadLine();
        return 0;
    }
}

