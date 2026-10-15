# Advanced Object Pooling for Unity

A generic component pool built on `UnityEngine.Pool.ObjectPool<T>`, with explicit ownership, lifecycle callbacks, prewarming, bounded inactive storage, optional active limits, a fixed-capacity mode, and deterministic pool cleanup.

Use it for projectiles, VFX, enemies, and other frequently reused `MonoBehaviour` components. All pool operations must run on Unity's main thread.

## Features

- **Type-safe contracts:** `ComponentPoolFactory<T>` accepts components implementing `IPoolable<T>`.
- **Self-return:** each instance receives the factory as its `IObjectPool<T>`, so self-release uses the same checks as external release.
- **Safe lifecycle:** initialize while inactive; set the world pose before activation and `OnSpawn`; run `OnDespawn` before deactivation.
- **Ownership checks in every build:** null, foreign, and duplicate returns are rejected. Hash sets provide expected O(1) membership checks without scanning inactive objects.
- **Prewarming:** create distinct inactive instances and reserve tracking capacity for a full concurrent burst before gameplay, without running spawn/despawn callbacks.
- **Capacity control:** `maxSize` bounds retained inactive instances; optional `maxActive` bounds simultaneous rentals. `TryGet` reports saturation without throwing.
- **Fixed-capacity mode:** `CreateFixed` prewarms a bounded pool whose retrieval path never instantiates replacements. Use `TryGet` to reject exhaustion without allocating an exception.
- **Usage counts:** inspect `CountAll`, `CountActive`, and `CountInactive`.
- **Cleanup:** `Clear` destroys inactive instances; `Dispose` also despawns and destroys outstanding instances and rejects further use.
- **Failure recovery:** instances whose spawn/despawn callbacks throw are discarded, and pool operation guards are reset. Disposal continues cleaning up if a despawn callback throws.

## Requirements and installation

The included project targets **Unity 6.0 (6000.0.66f2)**. Open it with that editor version to run the demo and tests. The pooling API requires `UnityEngine.Pool` (introduced in Unity 2021), but older editor versions have not been validated for this project and its packages.

To use the pool in another compatible Unity project, copy `Assets/Advanced pooling/Core` and `Assets/Advanced pooling/Runtime`, preserving their `.meta` files. Add references to `AdvancedPooling.Core` and `AdvancedPooling.Runtime` if your scripts use assembly definitions. The pool has no third-party runtime dependencies.

The optional `Unity` demo assembly also references the **Input System** package. The included project already installs it. The demo supports the new Input System and legacy input; when both are enabled it reads the new Input System.

## Implement a pooled component

```csharp
using AdvancedPooling.Core;
using UnityEngine;
using UnityEngine.Pool;

public sealed class Bullet : MonoBehaviour, IPoolable<Bullet>
{
    private IObjectPool<Bullet> _pool;
    private float _expiresAt;
    private bool _spawned;

    public void Initialize(IObjectPool<Bullet> pool)
    {
        _pool = pool;
        // Cache components here: Awake may not have run on an inactive clone.
    }

    public void OnSpawn()
    {
        _spawned = true;
        _expiresAt = Time.time + 2f;
        // transform.position/rotation already contain the requested spawn pose.
    }

    public void OnDespawn()
    {
        _spawned = false;
        // Reset physics and cancel subscriptions, timers, or coroutines you own.
    }

    private void Update()
    {
        if (_spawned && Time.time >= _expiresAt)
        {
            _spawned = false;
            _pool.Release(this);
        }
    }
}
```

`Initialize` runs once per created instance. `OnSpawn` and `OnDespawn` run once per successful rental and return, respectively. Prewarming invokes only `Initialize`. An active instance also receives `OnDespawn` during pool disposal, or as cleanup if `OnSpawn` throws.

## Create, warm, spawn, and dispose

```csharp
using AdvancedPooling.Runtime;
using UnityEngine;

public sealed class WeaponController : MonoBehaviour
{
    [SerializeField] private Bullet _prefab;
    private ComponentPoolFactory<Bullet> _bullets;

    private void Awake()
    {
        // Setup allocates and creates all 100 bullets. Retrieval never grows this pool.
        _bullets = ComponentPoolFactory<Bullet>.CreateFixed(
            prefab: _prefab,
            capacity: 100,
            parentContainer: transform);
    }

    public void Fire()
    {
        // False means all 100 active slots are occupied; skip this shot.
        if (_bullets.TryGet(transform.position, transform.rotation, out Bullet bullet))
        {
            // Further gameplay configuration can go here. OnSpawn has already run.
        }
    }

    private void OnDestroy()
    {
        _bullets?.Dispose();
    }
}
```

Use `Get(position, rotation)` or its `TryGet` overload when spawn logic depends on pose. Parameterless `Get()` uses the instance's current transform after reparenting; it does not reset pose or scale. Custom data required by `OnSpawn` needs to be supplied by your component's own design.

## API and limits

| API / option | Behavior |
| --- | --- |
| `defaultCapacity` | Initial ownership/active tracking capacity; does **not** instantiate prefabs or cap active objects. Default: 20. Inactive storage separately reserves at least `maxSize` slots at construction. |
| `maxSize` | Maximum retained inactive objects. Returned overflow objects are despawned and destroyed. Default: 100. |
| `maxActive` | Maximum simultaneous active rentals; 0 means unlimited. Default: 0. |
| `allowGrowth` | Allow retrieval to instantiate when no inactive instance remains. Default: true. False requires explicit prewarming. |
| `CreateFixed(prefab, capacity, parentContainer)` | Construct and prewarm a pool with retention and active limits equal to capacity, and growth disabled. Capacity must be positive. |
| `Prewarm(count)` | Ensure at least this many inactive instances, up to `maxSize`, and reserve tracking for the current active count plus this target. Setup operation that may allocate. |
| `Get()` / `Get(position, rotation)` | Rent and activate an instance. Throws at the active limit or when a non-growing pool is exhausted. |
| `TryGet(out item)` / `TryGet(position, rotation, out item)` | Return false with a null item at the active limit or when a non-growing pool is exhausted. Invalid/disposed use and callback exceptions still throw. |
| `Release(item)` | Despawn, deactivate, and retain or destroy an owned outstanding instance. |
| `TryRelease(item)` | Return false for null, foreign, or duplicate returns without allocating an exception. A valid return runs the normal lifecycle; invalid/disposed pool use and callback exceptions still throw. |
| `Get(out item)` | Return a Unity `PooledObject<T>` scope. Dispose the scope once, and do not also self-release that rental. |
| `CountAll` / `CountActive` / `CountInactive` | Currently tracked owned, outstanding, and available instances. |
| `Clear()` | Destroy inactive instances; outstanding instances remain valid and can be returned. |
| `Dispose()` | Despawn active instances and destroy all owned objects and internal storage. Repeated calls are harmless. |

Inactive instances live under an inactive storage child of `parentContainer`; rentals are reparented to `parentContainer` before activation. With no parent they become scene-root objects. Keep the parent active during use, and dispose the factory before destroying its container or unloading its scene. The factory does not keep objects alive across scene changes automatically.

## Allocation behavior

Use **`CreateFixed` + `TryGet`** when gameplay must not grow the pool. Setup instantiates all objects and reserves collection capacity. Successful factory rentals and returns, exhaustion checks, and `TryRelease` rejection paths are covered by managed-allocation regression tests. The tests include full-capacity bursts, the first burst after prewarming with zero initial tracking capacity, and prewarming while other objects are outstanding.

```csharp
// During loading: allocations and instantiation are expected here.
var pool = ComponentPoolFactory<Bullet>.CreateFixed(prefab, capacity: 256);

// During gameplay: exhaustion returns false; no fallback Instantiate or exception.
if (pool.TryGet(position, rotation, out Bullet bullet))
{
    // The bullet returns itself when done, or the caller uses pool.Release(bullet).
}
```

Fixed mode never silently replaces externally destroyed objects or instances discarded after callback failure. Its available capacity shrinks until you explicitly call `Prewarm` during a suitable setup/loading phase. `Clear` empties its inactive storage; retrieval cannot repopulate it automatically.

The default constructor keeps **growth enabled** for compatibility. `Prewarm` reserves tracking for the requested burst, and inactive storage reserves at least `maxSize` slots at construction so returns cannot resize it. Unplanned growth beyond the prepared workload can still instantiate objects and grow ownership/active tracking collections.

The guarantee concerns **managed allocations in the tested factory paths**, not all Unity/native memory or arbitrary component behavior. Construction, prewarming, explicit repair, growing-pool instantiation, exceptions, and user callbacks can allocate. Inactive prewarming calls `Initialize`; it does not execute `Awake`, `OnEnable`, or `OnSpawn`, so those methods may have first-use allocations of their own. `Get` still allocates an exception on exhaustion: use `TryGet` for expected saturation. Keep scoped rentals as their `PooledObject<T>` value type rather than boxing them as `IDisposable`.

Profile the actual player build, Unity lifecycle methods, and gameplay callbacks on the target device before making whole-game allocation guarantees. No IL2CPP/device allocation results are claimed by the editor tests.

## Lifecycle rules

- Return objects through their owning pool. Never release an object twice or use it after returning it.
- `OnSpawn`, `OnDespawn`, `Initialize`, `OnEnable`, and `OnDisable` must not call back into the same pool synchronously. The operation guard rejects reentrancy. Defer such work until after the callback returns.
- Do not return from `OnDisable`; the pool deactivates objects as part of return and teardown.
- Reset per-rental state in callbacks. The factory does not reset your health, physics, materials, subscriptions, coroutines, or transform scale.
- Prefer `Release` over external `Destroy`. A destroyed inactive component is discarded on retrieval; returning a destroyed active component removes its tracking entry. Counts can include externally destroyed objects until the pool encounters them. An externally destroyed active object that is never returned still occupies its active slot until disposal.
- Cancel delayed work on despawn. A raw component reference does not identify a rental generation; an old asynchronous callback could otherwise release a later rental of the same object.
- Unity's `Destroy` is deferred in Play Mode: objects are deactivated immediately and removed from pool ownership, then destroyed at frame end.

## Demo

Open `Assets/Advanced pooling/Demo/Scenes/SpawnerDemo.unity`, enter Play Mode, and hold the left mouse button. The spawner creates a fixed pool of projectiles in Awake, places them before setting their velocity, skips shots at capacity, and disposes its pool on destruction. `EnergyProjectile` uses 3D `Rigidbody` physics and returns on either a 3D collision/trigger or its lifetime deadline. Spawned projectiles must not begin overlapping other colliders if you want them to fly before returning.

## Tests

In Unity, open **Window > General > Test Runner**, select **PlayMode**, and run `AdvancedPooling.Tests.PlayMode`. Tests cover lifecycle ordering, active/inactive source prefabs, prewarming, ownership, duplicate returns, active and retention limits, scoped rentals, clearing, disposal, callback failures, external destruction, 3D collisions and triggers, projectile expiry, spawner/container teardown, fixed-pool exhaustion, and managed allocations during single rentals, full bursts, scoped rentals, and rejected returns.

For a command-line run, replace the editor and project paths for your machine:

```sh
/path/to/Unity -batchmode -nographics \
  -projectPath /path/to/Advanced-Object-Pooler \
  -runTests -testPlatform PlayMode \
  -testResults /tmp/advanced-pooling-tests.xml \
  -logFile /tmp/advanced-pooling-unity.log
```

## Project structure

```text
Assets/Advanced pooling/
  Core/       IPoolable<T> contract (references UnityEngine.Pool)
  Runtime/    ComponentPoolFactory<T>
  Unity/      PoolDemoSpawner and EnergyProjectile examples
  Demo/       Projectile prefab and sample scene
  Tests/      Play Mode lifecycle and performance regression tests
```
