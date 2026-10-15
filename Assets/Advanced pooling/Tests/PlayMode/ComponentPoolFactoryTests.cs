using System;
using System.Collections;
using System.Collections.Generic;
using AdvancedPooling.Demo;
using AdvancedPooling.Runtime;
using AdvancedPooling.Unity;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AdvancedPooling.Tests
{
    public sealed class ComponentPoolFactoryTests
    {
        private PoolTestItem _prefab;
        private readonly List<ComponentPoolFactory<PoolTestItem>> _pools = new List<ComponentPoolFactory<PoolTestItem>>();
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            PoolTestItem.ResetFailures();
            var go = new GameObject("Test Prefab");
            go.SetActive(false);
            _prefab = go.AddComponent<PoolTestItem>();
            _objects.Add(go);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PoolTestItem.ResetFailures();
            foreach (var pool in _pools) pool.Dispose();
            _pools.Clear();
            foreach (var go in _objects) if (go != null) Object.Destroy(go);
            _objects.Clear();
            yield return null; // Flush deferred destruction before the next test.
        }

        private ComponentPoolFactory<PoolTestItem> CreatePool(int capacity = 4, int maxSize = 4, int maxActive = 0)
        {
            var pool = new ComponentPoolFactory<PoolTestItem>(_prefab, capacity, maxSize, maxActive: maxActive);
            _pools.Add(pool);
            return pool;
        }

        [Test]
        public void ConstructorRejectsInvalidConfiguration()
        {
            Assert.Throws<ArgumentNullException>(() => new ComponentPoolFactory<PoolTestItem>(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePool(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePool(maxSize: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePool(maxActive: -1));
        }

        [Test]
        public void CapacityDoesNotPrewarm()
        {
            var pool = CreatePool();
            Assert.That(pool.CountAll, Is.Zero);
        }

        [Test]
        public void SelfReleaseUsesFactoryAndReusesObject()
        {
            var pool = CreatePool();
            var item = pool.Get();
            Assert.That(item.Pool, Is.SameAs(pool));
            Assert.That(pool.CountActive, Is.EqualTo(1));
            item.Pool.Release(item);
            Assert.That(item.DespawnedWhileActive, Is.True);
            Assert.That(item.gameObject.activeSelf, Is.False);
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.Get(), Is.SameAs(item));
            Assert.That(item.SpawnCount, Is.EqualTo(2));
            Assert.That(item.DespawnCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PoseAndInitializationPrecedeActivation(bool activePrefab)
        {
            _prefab.gameObject.SetActive(activePrefab);
            var pool = CreatePool();
            var position = new Vector3(3, 7, 11);
            var rotation = Quaternion.Euler(10, 20, 30);
            var item = pool.Get(position, rotation);
            Assert.That(item.EnabledBeforeInitialization, Is.False);
            Assert.That(item.EnableCount, Is.EqualTo(1));
            Assert.That(item.EnablePosition, Is.EqualTo(position));
            Assert.That(item.SpawnPosition, Is.EqualTo(position));
            Assert.That(Quaternion.Angle(item.SpawnRotation, rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void PrewarmCreatesDistinctInactiveInstancesWithoutSpawning()
        {
            _prefab.gameObject.SetActive(true);
            var pool = CreatePool();
            pool.Prewarm(4);
            pool.Prewarm(4);
            Assert.That(pool.CountAll, Is.EqualTo(4));
            Assert.That(pool.CountActive, Is.Zero);
            var items = new HashSet<PoolTestItem>();
            for (int i = 0; i < 4; i++)
            {
                var item = pool.Get();
                Assert.That(item.SpawnCount, Is.EqualTo(1));
                Assert.That(item.EnableCount, Is.EqualTo(1));
                Assert.That(item.DespawnCount, Is.Zero);
                Assert.That(items.Add(item), Is.True);
            }
        }

        [Test]
        public void PrewarmValidatesRetentionLimitAndRecoversFromFailure()
        {
            var pool = CreatePool();
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Prewarm(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Prewarm(5));
            PoolTestItem.FailInitialize = true;
            Assert.Throws<InvalidOperationException>(() => pool.Prewarm(2));
            PoolTestItem.FailInitialize = false;
            pool.Prewarm(2);
            Assert.That(pool.CountInactive, Is.EqualTo(2));
        }

        [Test]
        public void DoubleAndForeignReturnsAreRejectedWithoutChangingCounts()
        {
            var first = CreatePool();
            var second = CreatePool();
            var item = first.Get();
            Assert.Throws<ArgumentException>(() => second.Release(item));
            Assert.Throws<ArgumentNullException>(() => first.Release(null));
            first.Release(item);
            Assert.Throws<InvalidOperationException>(() => item.Pool.Release(item));
            Assert.That(first.CountAll, Is.EqualTo(1));
            Assert.That(first.CountActive, Is.Zero);
            Assert.That(second.CountAll, Is.Zero);
        }

        [Test]
        public void ActiveLimitSupportsBackpressureAndRecoversAfterReturn()
        {
            var pool = CreatePool(maxActive: 1);
            var item = pool.Get();
            Assert.That(pool.TryGet(out var missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(pool.TryGet(Vector3.zero, Quaternion.identity, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => pool.Get());
            pool.Release(item);
            Assert.That(pool.TryGet(new Vector3(2, 3, 4), Quaternion.identity, out var reused), Is.True);
            Assert.That(reused, Is.SameAs(item));
        }

        [Test]
        public void MaxSizeLimitsRetentionRatherThanActiveObjects()
        {
            var pool = CreatePool(maxSize: 1);
            var first = pool.Get();
            var second = pool.Get();
            Assert.That(pool.CountActive, Is.EqualTo(2));
            pool.Release(first);
            pool.Release(second);
            Assert.That(second.gameObject.activeSelf, Is.False);
            Assert.That(pool.CountAll, Is.EqualTo(1));
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.Get(), Is.SameAs(first));
        }

        [Test]
        public void ClearPreservesOutstandingObjectsAndAllowsTheirReturn()
        {
            var pool = CreatePool();
            pool.Prewarm(3);
            var item = pool.Get();
            pool.Clear();
            Assert.That(pool.CountAll, Is.EqualTo(1));
            Assert.That(pool.CountInactive, Is.Zero);
            pool.Release(item);
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.Get(), Is.SameAs(item));
        }

        [Test]
        public void ScopedLeaseReturnsThroughFactory()
        {
            var pool = CreatePool();
            using (pool.Get(out var item))
            {
                Assert.That(item.gameObject.activeSelf, Is.True);
                Assert.That(pool.CountActive, Is.EqualTo(1));
            }
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(1));
        }

        [Test]
        public void SpawnFailureDoesNotLeakAnActiveLease()
        {
            var pool = CreatePool();
            PoolTestItem.FailSpawn = true;
            Assert.Throws<InvalidOperationException>(() => pool.Get());
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.CountActive, Is.Zero);
            PoolTestItem.FailSpawn = false;
            Assert.That(pool.Get(), Is.Not.Null);
        }

        [Test]
        public void DespawnFailureDiscardsTheBrokenObject()
        {
            var pool = CreatePool();
            var item = pool.Get();
            PoolTestItem.FailDespawn = true;
            Assert.Throws<InvalidOperationException>(() => pool.Release(item));
            Assert.That(item.gameObject.activeSelf, Is.False);
            Assert.That(pool.CountAll, Is.Zero);
            PoolTestItem.FailDespawn = false;
            Assert.That(pool.Get(), Is.Not.SameAs(item));
        }

        [Test]
        public void ReentrantSpawnIsRejectedAndRolledBack()
        {
            var pool = CreatePool();
            PoolTestItem.SpawnAction = item => item.Pool.Release(item);
            Assert.Throws<InvalidOperationException>(() => pool.Get());
            Assert.That(pool.CountAll, Is.Zero);
            PoolTestItem.SpawnAction = null;
            Assert.That(pool.Get(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator DestroyedInactiveObjectIsSkipped()
        {
            var pool = CreatePool();
            var item = pool.Get();
            pool.Release(item);
            Object.Destroy(item.gameObject);
            yield return null;
            var replacement = pool.Get();
            Assert.That(replacement, Is.Not.Null);
            Assert.That(pool.CountAll, Is.EqualTo(1));
            Assert.That(pool.CountActive, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DestroyedActiveObjectCanBeReturnedWithoutCorruptingCounts()
        {
            var pool = CreatePool();
            var item = pool.Get();
            Object.Destroy(item.gameObject);
            yield return null;
            pool.Release(item);
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.Get(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator DisposeDestroysActiveAndInactiveObjectsAndRejectsFurtherUse()
        {
            var pool = CreatePool();
            pool.Prewarm(2);
            var item = pool.Get();
            pool.Dispose();
            pool.Dispose();
            Assert.That(pool.IsDisposed, Is.True);
            Assert.That(item.DespawnCount, Is.EqualTo(1));
            Assert.That(item.gameObject.activeSelf, Is.False);
            Assert.That(pool.CountAll, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => pool.Get());
            Assert.Throws<ObjectDisposedException>(() => pool.Release(item));
            Assert.Throws<ObjectDisposedException>(() => pool.Clear());
            Assert.Throws<ObjectDisposedException>(() => pool.Prewarm(0));
            Assert.Throws<ObjectDisposedException>(() => pool.TryGet(out _));
            Assert.Throws<ObjectDisposedException>(() => pool.TryRelease(item));
            yield return null;
            Assert.That(item == null, Is.True);
        }

        [UnityTest]
        public IEnumerator DisposeStillCleansUpAfterItsParentWasDestroyed()
        {
            var parent = new GameObject("Pool Parent");
            _objects.Add(parent);
            var pool = new ComponentPoolFactory<PoolTestItem>(_prefab, parentContainer: parent.transform);
            _pools.Add(pool);
            pool.Prewarm(2);
            pool.Get();
            Object.Destroy(parent);
            yield return null;
            Assert.DoesNotThrow(() => pool.Dispose());
            Assert.That(pool.IsDisposed, Is.True);
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.CountActive, Is.Zero);
        }

        [Test]
        public void DisposeFinishesCleanupWhenCallbacksThrow()
        {
            var pool = CreatePool();
            pool.Prewarm(3);
            var first = pool.Get();
            var second = pool.Get();
            PoolTestItem.FailDespawn = true;
            Assert.Throws<InvalidOperationException>(() => pool.Dispose());
            Assert.That(first.DespawnCount, Is.EqualTo(1));
            Assert.That(second.DespawnCount, Is.EqualTo(1));
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.CountActive, Is.Zero);
        }

        [Test]
        public void WarmedGetAndReleaseDoNotAllocateManagedMemory()
        {
            var pool = CreatePool();
            pool.Prewarm(4);
            // Warm JIT, activation paths, and tracking capacity before measurement.
            for (int i = 0; i < 32; i++) pool.Release(pool.Get(Vector3.one, Quaternion.identity));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) pool.Release(pool.Get(Vector3.one, Quaternion.identity));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "The warmed factory hot path must not allocate.");
        }

        private ComponentPoolFactory<PoolTestItem> CreateFixedPool(int capacity)
        {
            var pool = ComponentPoolFactory<PoolTestItem>.CreateFixed(_prefab, capacity);
            _pools.Add(pool);
            return pool;
        }

        [Test]
        public void FixedPoolIsFullyPrewarmedAndDoesNotGrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateFixedPool(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateFixedPool(-1));
            var pool = CreateFixedPool(2);
            Assert.That(pool.AllowGrowth, Is.False);
            Assert.That(pool.CountInactive, Is.EqualTo(2));
            var first = pool.Get();
            pool.Get();
            Assert.That(pool.TryGet(out var missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(pool.CountAll, Is.EqualTo(2));
            pool.Release(first);
            Assert.That(pool.TryGet(out var reused), Is.True);
            Assert.That(reused, Is.SameAs(first));
        }

        [Test]
        public void NonGrowingPoolRequiresExplicitPrewarmAfterClear()
        {
            var pool = new ComponentPoolFactory<PoolTestItem>(_prefab, allowGrowth: false);
            _pools.Add(pool);
            Assert.That(pool.TryGet(out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => pool.Get());
            pool.Prewarm(2);
            Assert.That(pool.TryGet(out var item), Is.True);
            pool.Release(item);
            pool.Clear();
            Assert.That(pool.TryGet(Vector3.one, Quaternion.identity, out _), Is.False);
            Assert.That(pool.CountAll, Is.Zero);
            pool.Prewarm(2);
            Assert.That(pool.TryGet(out _), Is.True);
        }

        [UnityTest]
        public IEnumerator FixedPoolDoesNotReplaceDestroyedInactiveInstances()
        {
            var pool = CreateFixedPool(1);
            var item = pool.Get();
            pool.Release(item);
            Object.Destroy(item.gameObject);
            yield return null;
            Assert.That(pool.TryGet(out var missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(pool.CountAll, Is.Zero);
            // Explicit setup is allowed to repair the fixed pool; retrieval never does so.
            pool.Prewarm(1);
            Assert.That(pool.TryGet(out _), Is.True);
        }

        [Test]
        public void FixedPoolDoesNotReplaceInstancesDiscardedAfterCallbackFailure()
        {
            var pool = CreateFixedPool(1);
            PoolTestItem.FailSpawn = true;
            Assert.Throws<InvalidOperationException>(() => pool.Get());
            PoolTestItem.FailSpawn = false;
            Assert.That(pool.TryGet(out _), Is.False);
            Assert.That(pool.CountAll, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PrewarmReservesTrackingForTheFirstFullBurst(bool setPose)
        {
            const int capacity = 64;
            // Warm shared code paths on a different pool, not the objects being measured.
            var warmup = CreatePool();
            warmup.Release(warmup.Get(Vector3.one, Quaternion.identity));
            warmup.Release(warmup.Get());
            var pool = CreatePool(capacity: 0, maxSize: capacity);
            pool.Prewarm(capacity);
            var items = new PoolTestItem[capacity];
            int acquired = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < capacity; i++)
            {
                bool success = setPose
                    ? pool.TryGet(Vector3.one, Quaternion.identity, out items[i])
                    : pool.TryGet(out items[i]);
                if (success) acquired++;
            }
            for (int i = capacity - 1; i >= 0; i--) pool.Release(items[i]);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(acquired, Is.EqualTo(capacity));
            Assert.That(allocated, Is.Zero, "Even the first concurrent burst must use reserved tracking capacity.");
        }

        [Test]
        public void PrewarmingWithOutstandingRentalsReservesCombinedCapacity()
        {
            const int capacity = 64;
            var pool = CreatePool(capacity: 0, maxSize: capacity + 1);
            var outstanding = pool.Get();
            pool.Prewarm(capacity);
            var items = new PoolTestItem[capacity];
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < capacity; i++) items[i] = pool.Get();
            pool.Release(outstanding);
            for (int i = 0; i < capacity; i++) pool.Release(items[i]);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(pool.CountInactive, Is.EqualTo(capacity + 1));
            Assert.That(allocated, Is.Zero, "Returning the outstanding rental must not grow inactive storage.");
        }

        [Test]
        public void FixedPoolBurstAndExhaustionDoNotAllocateManagedMemory()
        {
            const int capacity = 64;
            var pool = CreateFixedPool(capacity);
            var items = new PoolTestItem[capacity];
            int acquired = 0;
            int unexpected = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int burst = 0; burst < 16; burst++)
            {
                for (int i = 0; i < capacity; i++)
                    if (pool.TryGet(Vector3.one, Quaternion.identity, out items[i])) acquired++;
                for (int i = 0; i < 16; i++)
                {
                    if (pool.TryGet(out _)) unexpected++;
                    if (pool.TryGet(Vector3.zero, Quaternion.identity, out _)) unexpected++;
                }
                for (int i = capacity - 1; i >= 0; i--)
                {
                    if ((i & 1) == 0) pool.Release(items[i]);
                    else if (!pool.TryRelease(items[i])) unexpected++;
                }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(acquired, Is.EqualTo(capacity * 16));
            Assert.That(unexpected, Is.Zero);
            Assert.That(pool.CountAll, Is.EqualTo(capacity));
            Assert.That(allocated, Is.Zero, "Successful rentals, burst returns, and expected saturation must not allocate.");
        }

        [Test]
        public void EmptyNonGrowingPoolRejectsRentalsWithoutAllocating()
        {
            var pool = new ComponentPoolFactory<PoolTestItem>(_prefab,
                defaultCapacity: 0, maxSize: 1, allowGrowth: false);
            _pools.Add(pool);
            // This checks storage exhaustion independently of the active limit.
            int unexpected = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 512; i++)
            {
                if (pool.TryGet(out _)) unexpected++;
                if (pool.TryGet(Vector3.zero, Quaternion.identity, out _)) unexpected++;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(unexpected, Is.Zero);
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void TryReleaseRejectsInvalidReturnsWithoutAllocating()
        {
            var pool = CreateFixedPool(1);
            var item = pool.Get();
            Assert.That(pool.TryRelease(item), Is.True);
            Assert.That(pool.TryRelease(null), Is.False);
            Assert.That(pool.TryRelease(_prefab), Is.False);
            int unexpected = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                if (pool.TryRelease(item)) unexpected++;
                if (pool.TryRelease(null)) unexpected++;
                if (pool.TryRelease(_prefab)) unexpected++;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(unexpected, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ScopedFixedPoolRentalsDoNotAllocateManagedMemory()
        {
            var pool = CreateFixedPool(1);
            using (pool.Get(out _)) { }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
                using (pool.Get(out _)) { }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ProjectileReturnsOn3DCollision() => CheckProjectileContact(false);

        [UnityTest]
        public IEnumerator ProjectileReturnsOn3DTrigger() => CheckProjectileContact(true);

        private IEnumerator CheckProjectileContact(bool trigger)
        {
            var go = new GameObject("Contact Projectile Prefab");
            go.SetActive(false);
            _objects.Add(go);
            var projectile = go.AddComponent<EnergyProjectile>();
            go.GetComponent<Rigidbody>().useGravity = false;
            var collider = go.AddComponent<SphereCollider>();
            collider.radius = 0.1f;
            collider.isTrigger = trigger;
            var wall = new GameObject("Contact Target");
            _objects.Add(wall);
            wall.transform.position = new Vector3(1, 100, 0);
            wall.AddComponent<BoxCollider>();
            using (var pool = new ComponentPoolFactory<EnergyProjectile>(projectile))
            {
                pool.Get(new Vector3(0, 100, 0), Quaternion.identity);
                for (int i = 0; i < 30 && pool.CountActive > 0; i++)
                    yield return new WaitForFixedUpdate();
                Assert.That(pool.CountActive, Is.Zero, "The 3D contact must return the projectile before its timer expires.");
                Assert.That(pool.CountInactive, Is.EqualTo(1));
                yield return new WaitForFixedUpdate();
                Assert.That(pool.CountInactive, Is.EqualTo(1), "Repeated contact must not double-release.");
            }
        }

        [UnityTest]
        public IEnumerator SpawnerDisposesPoolAndDestroysItsOwnContainer() => CheckSpawnerCleanup(false);

        [UnityTest]
        public IEnumerator SpawnerPreservesCallerOwnedContainer() => CheckSpawnerCleanup(true);

        private IEnumerator CheckSpawnerCleanup(bool callerOwnedContainer)
        {
            var prefab = new GameObject("Spawner Projectile Prefab");
            prefab.SetActive(false);
            _objects.Add(prefab);
            var projectile = prefab.AddComponent<EnergyProjectile>();
            var go = new GameObject("Test Spawner");
            go.SetActive(false);
            _objects.Add(go);
            var spawner = go.AddComponent<PoolDemoSpawner>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PoolDemoSpawner).GetField("_prefabToSpawn", flags).SetValue(spawner, projectile);
            typeof(PoolDemoSpawner).GetField("_maxCapacity", flags).SetValue(spawner, 2);
            if (callerOwnedContainer)
            {
                var parent = new GameObject("Caller Container");
                _objects.Add(parent);
                typeof(PoolDemoSpawner).GetField("_containerTransform", flags).SetValue(spawner, parent.transform);
            }
            go.SetActive(true);
            var pool = (ComponentPoolFactory<EnergyProjectile>)typeof(PoolDemoSpawner).GetField("_poolFactory", flags).GetValue(spawner);
            var container = (Transform)typeof(PoolDemoSpawner).GetField("_containerTransform", flags).GetValue(spawner);
            Assert.That(pool.CountInactive, Is.EqualTo(2));
            var item = pool.Get();
            Object.Destroy(go);
            yield return null;
            Assert.That(pool.IsDisposed, Is.True);
            Assert.That(pool.CountAll, Is.Zero);
            yield return null;
            Assert.That(item == null, Is.True);
            Assert.That(container == null, Is.EqualTo(!callerOwnedContainer));
        }

        [UnityTest]
        public IEnumerator ProjectileUsesSpawnRotationAndExpiresOnce()
        {
            var go = new GameObject("Projectile Prefab");
            go.SetActive(false);
            _objects.Add(go);
            var projectile = go.AddComponent<EnergyProjectile>();
            go.GetComponent<Rigidbody>().useGravity = false;
            using (var pool = new ComponentPoolFactory<EnergyProjectile>(projectile))
            {
                var rotation = Quaternion.Euler(0, 0, 90);
                var item = pool.Get(new Vector3(10, 20, 30), rotation);
                var body = item.GetComponent<Rigidbody>();
                Assert.That(Vector3.Distance(body.linearVelocity, rotation * Vector3.right * 10f), Is.LessThan(0.01f));
                pool.Release(item);
                Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
                item = pool.Get();
                yield return new WaitForSeconds(2.1f);
                Assert.That(pool.CountActive, Is.Zero);
                Assert.That(pool.CountInactive, Is.EqualTo(1));
            }
        }
    }
}
