# LeoEcsLite - Lightweight C# Entity Component System framework
Performance, zero/small memory allocations/footprint, no dependencies on any game engine - are the main goals of this project.

> **Important!** Don't forget to use `DEBUG` builds for development and `RELEASE` builds in production: all internal error checks / exception throwing work only in `DEBUG` builds and removed for performance reasons in `RELEASE`.

> **Important!** LeoEcsLite API **is not thread safe** and will never be!

# Table of content
* [Socials](#socials)
* [Installation](#installation)
    * [As unity module](#as-unity-module)
    * [As source](#as-source)
* [Main parts of ecs](#main-parts-of-ecs)
    * [Entity](#entity)
    * [Component](#component)
    * [System](#system)
* [Data sharing](#data-sharing)
* [Special classes](#special-classes)
    * [EcsPool](#ecspool)
    * [EcsFilter](#ecsfilter)
    * [EcsWorld](#ecsworld)
    * [EcsSystems](#ecssystems)
* [Engine integration](#engine-integration)
    * [Unity](#unity)
    * [Custom engine](#custom-engine)
* [Projects powered by LeoECS Lite](#projects-powered-by-leoecs-lite)
    * [With sources](#with-sources)
* [Extensions](#extensions)
* [License](#license)
* [FAQ](#faq)

# Socials
[![discord](https://img.shields.io/discord/963730852452388894.svg?label=New%20Community%20Discord%20server&style=for-the-badge&logo=discord)](https://discord.gg/ZAhCUv5YQt)

[![discord](https://img.shields.io/discord/404358247621853185.svg?label=Old%20Leo%20Discord%20server&style=for-the-badge&logo=discord)](https://discord.gg/5GZVde6)

# Installation

## As unity module
This repository can be installed as unity module directly from git url. In this way new line should be added to `Packages/manifest.json`:
```
"com.leoecscommunity.ecslite": "https://github.com/LeoECSCommunity/ecslite.git",
```
By default last released version will be used. If you need trunk / developing version then `develop` name of branch should be added after hash:
```
"com.leoecscommunity.ecslite": "https://github.com/LeoECSCommunity/ecslite.git#develop",
```

## As source
If you can't / don't want to use unity modules, code can be cloned or downloaded as archive from `releases` page.

# Main parts of ecs

## Entity
Сontainer for components. Implemented as `int`:
```csharp
// Creates new entity in world context.
int entity = _world.NewEntity ();

// Any entity can be destroyed. All components will be removed first, then entity will be destroyed. 
world.DelEntity (entity);
```

> **Important!** Entities can't live without components and will be killed automatically after last component is removed.

## Component
Container for user data without / with small logic inside:
```csharp
struct Component1 
{
    public int Id;
    public string Name;
}
```
Components can be added / requested / removed through [component pools](#ecspool).

## System
Сontainer for logic for processing filtered entities. User class should implement at least one of `IEcsInitSystem`, `IEcsDestroySystem`, `IEcsRunSystem` (or other supported) interfaces:
```csharp
class UserSystem : EcsSystem, IEcsPreInitSystem, IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, IEcsPostDestroySystem 
{    
    public UserSystem(EcsSystems systems) : base(systems)
    {

    }
    
    public void PreInit (EcsSystems systems) 
    {
        // Will be called once during EcsSystems.Init() call and before IEcsInitSystem.Init().
    }
    
    public void Init (EcsSystems systems) 
    {
        // Will be called once during EcsSystems.Init() call and after IEcsPreInitSystem.PreInit().
    }
    
    public void Run (EcsSystems systems) 
    {
        // Will be called on each EcsSystems.Run() call.
    }

    public void Destroy (EcsSystems systems) 
    {
        // Will be called once during EcsSystems.Destroy() call and before IEcsPostDestroySystem.PostDestroy().
    }
    
    public void PostDestroy (EcsSystems systems) 
    {
        // Will be called once during EcsSystems.Destroy() call and after IEcsDestroySystem.Destroy().
    }
}
```

# Data sharing
Instance of any custom type can be shared between all systems:
```csharp
EcsSystemsBuilder builder = new EcsSystemsBuilder(world);
builder.Add<UserSystem>();
builder.Inject("Test", "I like trains.");
builder.InjectSingleton(new TestSingleton());
var systems = builder.Finish(1);
systems.Init();
```

# Special classes

## EcsPool
Container for components, provides api for adding / requesting / removing components on entity:
```csharp
int entity = world.NewEntity ();
EcsPool<Component1> pool = world.GetPool<Component1> (); 

// Add() adds component to entity. If component already exists - exception will be raised in DEBUG.
ref Component1 c1 = ref pool.Add (entity);

// Get() returns exist component on entity. If component does not exists - exception will be raised in DEBUG.
ref Component1 c1 = ref pool.Get (entity);

// Del() removes component from entity. If it was last component - entity will be removed automatically too.
pool.Del (entity);
```

> **Important!** After removing component will be pooled and can be reused later. All fields will be reset to default values automatically.

## EcsFilter
Container for keeping filtered entities with specified component list:
```csharp
//For multithreading we need to specify which components we Read/Write from
[EcsWrite("Default World", typeof(Weapon))]
class WeaponSystem : EcsSystem, IEcsInitSystem, IEcsRunSystem 
{
    private readonly EcsWorld world;
    private readonly EcsPool<InitStruct> weaponPool;
    private readonly EcsFilter weaponFilter;
    public WeaponSystem(EcsSystems systems) : base(systems)
    {
         // We want to get the default world instance.
         world = GetWorld();
         // We want to get the Pool of weapon components in this world
         weaponPool = GetPool<Weapon>();
         // We want to filter entities with "Weapon" and without "Health".
         weaponFilter = world.FilterInc<Weapon>().Exc<Health>().End();
    }
    
    public void Init (EcsSystems systems) 
    {
        // Create entity
        int entity = world.NewEntity();
        // Add Weapon Component to entity
        weapons.Add (entity);
    }

    public void Run (EcsSystems systems) 
    {
        foreach (int entity in weaponFilter) 
        {
            ref Weapon weapon = ref weaponPool.Get(entity);
            weapon.Ammo = Math.Max(0, weapon.Ammo - 1);
        }
    }
}
```

Additional constraints can be added with `Inc<>()` / `Exc<>()` methods.

> Important: Filters support any amount of components, include and exclude lists should be unique. Filters can't both include and exclude the same component.

## EcsWorld
Root level container for all entities / components, works like isolated environment.

> Important: Do not forget to call `EcsWorld.Destroy()` method if instance will not be used anymore.

## EcsSystems
Group of systems to process `EcsWorld` instance:
```csharp
class Startup : MonoBehaviour 
{
    EcsWorld _world;
    EcsSystems _systems;

    void Start () 
    {
        // create ecs environment.
        _world = new EcsWorld();
        _world.AllowPool<Weapon>();
        _world.AllowPool<Health>();
        EcsSystemsBuilder builder = new EcsSystemsBuilder(_world);
        builder.Add<WeaponSystem>();
        _systems = builder.Finish(1);
        _systems.Init ();
    }
    
    void Update () 
    {
        // process all dependent systems.
        _systems?.Run ();
    }

    void OnDestroy () 
    {
        // destroy systems logical group.
        if (_systems != null) 
        {
            _systems.Destroy ();
            _systems = null;
        }
        // destroy world.
        if (_world != null) 
        {
            _world.Destroy ();
            _world = null;
        }
    }
}
```

> Important: Do not forget to call `EcsSystems.Destroy()` method if instance will not be used anymore.

# Engine integration

## Unity \[TODO]
> Unity does not yet support c# 10 preview or c# 11+. A known workaround exists, please open an issue if you want to use Unity.  
> \[Previous versions] Tested on unity 2020.3 (but not dependent on it) and contains assembly definition for compiling to separate assembly file for performance reason.

[Unity editor integration](https://github.com/LeoECSCommunity/ecslite-unityeditor) contains code templates and world debug viewer.

## Custom engine
> C#10 with preview features enabled or above required for this framework.

Code example - each part should be integrated in proper place of engine execution flow.
```csharp
using Leopotam.EcsLite;

class EcsStartup 
{
    EcsWorld _world;
    EcsSystems _systems;

    // Initialization of ecs world and systems.
    void Init ()
    {
        _world = new EcsWorld();
        _world.AllowPool<InitStruct>();
        _world.AllowPool<NormalStruct>();
        _world.AllowPool<CTORStruct>();
    
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
        _systems = builder.Finish(4); //Set number of threads to 4
        _systems.Init();
    }

    // Engine update loop.
    void UpdateLoop () 
    {
        _systems?.Run ();
    }

    // Cleanup.
    void Destroy () 
    {
        if (_systems != null) 
        {
            _systems.Destroy ();
            _systems = null;
        }
        if (_world != null) 
        {
            _world.Destroy ();
            _world = null;
        }
    }
}
```

# Projects powered by LeoECS Lite
## With sources
* ["Cornerstone"](https://github.com/JoeTwizzle/Cornerstone)
  
  [![](https://camo.githubusercontent.com/3530eb0dcc231a44683f1da0b91a7b5029f538058612e333fbfed961e64908c8/68747470733a2f2f692e696d6775722e636f6d2f446365687234322e706e67)](https://github.com/JoeTwizzle/Cornerstone)

* ["3D Platformer"](https://github.com/supremestranger/3D-Platformer-Lite)
  [![](https://camo.githubusercontent.com/dcd2f525130d73f4688c1f1cfb12f6e37d166dae23a1c6fac70e5b7873c3ab21/68747470733a2f2f692e6962622e636f2f686d374c726d342f506c6174666f726d65722e706e67)](https://github.com/supremestranger/3D-Platformer-Lite)


* ["SharpPhysics2D"](https://github.com/7Bpencil/sharpPhysics)
  [![](https://github.com/7Bpencil/sharpPhysics/raw/master/pictures/preview.png)](https://github.com/7Bpencil/sharpPhysics)


* ["Busy ECS - extremely nice (and most likely slow) ECS framework"](https://github.com/kkolyan/busyecs)

# Extensions
* [Dependency injection](https://github.com/LeoECSCommunity/ecslite-di)
* [Extended filters](https://github.com/LeoECSCommunity/ecslite-extendedfilters)
* [Extended systems](https://github.com/LeoECSCommunity/ecslite-extendedsystems)
* [Threads support](https://github.com/LeoECSCommunity/ecslite-threads)
* [Unity editor integration](https://github.com/LeoECSCommunity/ecslite-unityeditor)
* [Unity uGui bindings](https://github.com/LeoECSCommunity/ecslite-unity-ugui)
* [Unity jobs support](https://github.com/LeoECSCommunity/ecslite-threads-unity)
* [UniLeo - Unity scene data converter](https://github.com/voody2506/UniLeo-Lite)
* [Unity Physx events support](https://github.com/supremestranger/leoecs-lite-physics)
* [Multiple Shared injection](https://github.com/GoodCatGames/ecslite-multiple-shared)
* [Unity native collections support](https://github.com/odingamesdev/native-ecslite)

# License
The software is released under the terms of the [MIT license](./LICENSE.md).

No personal support or any guarantees.

# FAQ

### I copy&paste my reset components code again and again. How can I do it in other manner?

If you want to simplify your code and keep reset/init code at one place, you can setup custom handler to process cleanup / initialization for component:
```csharp
struct MyComponent : IEcsInit<MyComponent>, IEcsDestroy<MyComponent>
{
    public int Id;
    public object? LinkToAnotherComponent;
    public IDisposable? MyDisposable;

    public static void OnInit(ref MyComponent c) 
    {
        c.Id = 2;
        c.LinkToAnotherComponent = null;
        c.MyDisposable = null;
    }
    
    public static void OnDestroy(ref MyComponent c) 
    {
        c.MyDisposable?.Dispose();
    }
}
```
This method will be automatically called for brand new component instance and after component removing from entity and before recycling to component pool.
> Important: With custom `AutoReset` behaviour there are no any additional checks for reference-type fields, you should provide custom correct cleanup/init behaviour to avoid possible memory leaks.

### I want to keep references to entities in components, but entity can be killed at any system and I need protection from reusing the same ID. How can I do it?

For keeping entity somewhere you should pack it to special `EcsPackedEntity` or `EcsPackedEntityWithWorld` types:
```csharp
EcsWorld world = new EcsWorld ();
int entity = world.NewEntity ();
EcsPackedEntity packed = world.PackEntity (entity);
EcsPackedEntityWithWorld packedWithWorld = world.PackEntityWithWorld (entity);
...
if (packed.Unpack (world, out int unpacked)) 
{
    // unpacked is valid and can be used.
}
if (packedWithWorld.Unpack (out EcsWorld unpackedWorld, out int unpackedWithWorld)) 
{
    // unpackedWithWorld is valid and can be used.
}
```

### I want to add some reactive behaviour on world changes, how I can do it?

You can use `LEOECSLITE_WORLD_EVENTS` definition to enable custom event listeners support on worlds:

```csharp
class TestWorldEventListener : IEcsWorldEventListener 
{
    public void OnEntityCreated (int entity) 
    {
        // entity created - raises on world.NewEntity().
    }

    public void OnEntityChanged (int entity) 
    {
        // entity changed - raises on pool.Add() / pool.Del().
    }

    public void OnEntityDestroyed (int entity) 
    {
        // entity destroyed - raises on world.DelEntity() or last component removing.
    }

    public void OnFilterCreated (EcsFilter filter) 
    {
        // filter created - raises on world.Filter().End() for brand new filter.
    }

    public void OnWorldResized (int newSize) 
    {
        // world resized - raises on world/pools resizing when no room for entity at world.NewEntity() call.
    }

    public void OnWorldDestroyed (EcsWorld world) 
    {
        // world destroyed - raises on world.Destroy().
    }
}
...
var world = new EcsWorld ();
var listener = new TestWorldEventListener ();
world.AddEventListener (listener);
``` 

### I want to add some reactive behaviour on filter changes, how I can do it?

You can use `LEOECSLITE_FILTER_EVENTS` definition to enable custom event listeners support on filters:

```csharp
class TestFilterEventListener : IEcsFilterEventListener 
{
    public void OnEntityAdded (int entity) 
    {
        // entity added to filter.
    }

    public void OnEntityRemoved (int entity) 
    {
        // entity removed from filter.
    }
}
...
var world = new EcsWorld ();
var filter = world.Filter<C1> ().End ();
var listener = new TestFilterEventListener ();
filter.AddEventListener (listener);
``` 
