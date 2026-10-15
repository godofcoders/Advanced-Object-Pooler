using System;
using System.Collections.Generic;
using AdvancedPooling.Core;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace AdvancedPooling.Runtime
{
    /// <summary>A main-thread component pool with ownership checks and explicit lifetime management.</summary>
    public sealed class ComponentPoolFactory<T> : IObjectPool<T>, IDisposable
        where T : MonoBehaviour, IPoolable<T>
    {
        private readonly T _prefab;
        private readonly Transform _parentContainer;
        private readonly ObjectPool<T> _pool;
        private readonly HashSet<T> _owned;
        private readonly HashSet<T> _active;
        private readonly int _maxSize;
        private readonly int _maxActive;
        private readonly bool _allowGrowth;
        private readonly Transform _storage;
        private bool _busy;
        private bool _disposed;

        public int CountAll => _owned.Count;
        public int CountActive => _active.Count;
        public int CountInactive => CountAll - CountActive;
        public bool IsDisposed => _disposed;
        public bool AllowGrowth => _allowGrowth;

        /// <param name="defaultCapacity">Initial collection capacity; does not instantiate objects.</param>
        /// <param name="maxSize">Maximum retained inactive objects, not a limit on simultaneous spawns.</param>
        /// <param name="maxActive">Maximum simultaneous spawns; zero means unlimited.</param>
        /// <param name="allowGrowth">Whether rentals can instantiate when no inactive instance remains.</param>
        public ComponentPoolFactory(T prefab, int defaultCapacity = 20, int maxSize = 100,
            Transform parentContainer = null, int maxActive = 0, bool allowGrowth = true)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (defaultCapacity < 0) throw new ArgumentOutOfRangeException(nameof(defaultCapacity));
            if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
            if (maxActive < 0) throw new ArgumentOutOfRangeException(nameof(maxActive));

            _prefab = prefab;
            _parentContainer = parentContainer;
            _maxSize = maxSize;
            _maxActive = maxActive;
            _allowGrowth = allowGrowth;
            _owned = new HashSet<T>(defaultCapacity);
            _active = new HashSet<T>(defaultCapacity);
            // Ownership/lease checks below run in all builds, without Unity's linear collection scan.
            // Unity exposes no later reserve API; reserve retained storage now so returns cannot
            // grow its backing collection, including after prewarming while other items are rented.
            _pool = new ObjectPool<T>(CreateItem, actionOnDestroy: DestroyItem,
                collectionCheck: false, defaultCapacity: Math.Max(defaultCapacity, maxSize), maxSize: maxSize);

            var storage = new GameObject($"{prefab.name} Pool (Inactive)");
            storage.SetActive(false);
            _storage = storage.transform;
            _storage.SetParent(parentContainer, false);
        }

        /// <summary>Creates and prewarms a bounded pool. Rental never instantiates replacement objects.</summary>
        public static ComponentPoolFactory<T> CreateFixed(T prefab, int capacity, Transform parentContainer = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            var pool = new ComponentPoolFactory<T>(prefab, capacity, capacity, parentContainer,
                maxActive: capacity, allowGrowth: false);
            try
            {
                pool.Prewarm(capacity);
                return pool;
            }
            catch
            {
                pool.Dispose();
                throw;
            }
        }

        public T Get() => GetInternal(false, default, default);

        /// <summary>Sets the world pose before activation and OnSpawn.</summary>
        public T Get(Vector3 position, Quaternion rotation) => GetInternal(true, position, rotation);

        public PooledObject<T> Get(out T item)
        {
            item = Get();
            return new PooledObject<T>(item, this);
        }

        /// <summary>Returns false at the active limit or when a non-growing pool has no available instance.</summary>
        public bool TryGet(out T item) => TryGetInternal(false, default, default, out item);

        public bool TryGet(Vector3 position, Quaternion rotation, out T item) =>
            TryGetInternal(true, position, rotation, out item);

        private bool AtActiveLimit => _maxActive > 0 && CountActive >= _maxActive;

        private T GetInternal(bool setPose, Vector3 position, Quaternion rotation)
        {
            if (TryGetInternal(setPose, position, rotation, out T item)) return item;
            throw new InvalidOperationException("The pool is exhausted or its active limit has been reached. Use TryGet for allocation-free rejection.");
        }

        private bool TryGetInternal(bool setPose, Vector3 position, Quaternion rotation, out T result)
        {
            CheckAvailable();
            result = null;
            if (AtActiveLimit) return false;
            _busy = true;
            try
            {
                T item;
                // Check on every iteration: an externally destroyed instance must not trigger growth
                // in a fixed pool, including when the destroyed item was its last inactive instance.
                do
                {
                    if (!_allowGrowth && _pool.CountInactive == 0) return false;
                    item = _pool.Get();
                    if (item == null) _owned.Remove(item);
                } while (item == null);

                _active.Add(item);
                try
                {
                    item.transform.SetParent(_parentContainer, false);
                    if (setPose) item.transform.SetPositionAndRotation(position, rotation);
                    item.gameObject.SetActive(true);
                    item.OnSpawn();
                    result = item;
                    return true;
                }
                catch
                {
                    // A failing callback must not leave an unreachable active object behind.
                    _active.Remove(item);
                    try { if (item != null) item.OnDespawn(); }
                    finally { DestroyItem(item); }
                    throw;
                }
            }
            finally { _busy = false; }
        }

        public void Release(T item)
        {
            CheckAvailable();
            if (ReferenceEquals(item, null)) throw new ArgumentNullException(nameof(item));
            if (!_owned.Contains(item)) throw new ArgumentException("This object belongs to another pool.", nameof(item));
            if (!_active.Remove(item)) throw new InvalidOperationException("This object has already been returned.");
            ReleaseInternal(item);
        }

        /// <summary>Rejects null, foreign, and already returned objects without allocating an exception.</summary>
        public bool TryRelease(T item)
        {
            CheckAvailable();
            if (ReferenceEquals(item, null) || !_owned.Contains(item) || !_active.Remove(item)) return false;
            ReleaseInternal(item);
            return true;
        }

        private void ReleaseInternal(T item)
        {
            _busy = true;
            try
            {
                if (item == null)
                {
                    _owned.Remove(item);
                    return;
                }
                try { item.OnDespawn(); }
                catch
                {
                    DestroyItem(item);
                    throw;
                }
                if (item == null)
                {
                    _owned.Remove(item);
                    return;
                }
                item.gameObject.SetActive(false);
                item.transform.SetParent(_storage, false);
                _pool.Release(item);
            }
            finally { _busy = false; }
        }

        /// <summary>Ensures at least count inactive instances exist, without running spawn callbacks.</summary>
        public void Prewarm(int count)
        {
            CheckAvailable();
            if (count < 0 || count > _maxSize) throw new ArgumentOutOfRangeException(nameof(count));
            // Reserve for a complete concurrent burst, not just the inactive storage. Without this,
            // prewarming prefabs still leaves the first burst growing the active tracking set.
            _active.EnsureCapacity(checked(CountActive + count));
            _owned.EnsureCapacity(checked(CountActive + Math.Max(count, CountInactive)));
            if (CountInactive >= count) return;
            _busy = true;
            var items = new T[count];
            int acquired = 0;
            try
            {
                // Hold all instances until the target is reached; get/release in one loop warms only one.
                for (; acquired < count; acquired++) items[acquired] = _pool.Get();
            }
            finally
            {
                for (int i = 0; i < acquired; i++) _pool.Release(items[i]);
                _busy = false;
            }
        }

        /// <summary>Destroys inactive instances. Outstanding instances can still be returned.</summary>
        public void Clear()
        {
            CheckAvailable();
            _busy = true;
            try { _pool.Clear(); }
            finally { _busy = false; }
        }

        /// <summary>Despawns and destroys all owned objects. Repeated disposal is harmless.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            if (_busy) throw new InvalidOperationException("Pool lifecycle callbacks must not re-enter the same pool.");
            // Scene teardown can destroy the storage before the owner's OnDestroy runs.
            // Disposal must still release managed tracking, even when Unity objects are gone.
            _disposed = true;
            _busy = true;
            Exception failure = null;
            try
            {
                foreach (T item in _active)
                {
                    try { if (item != null) item.OnDespawn(); }
                    catch (Exception exception) { if (failure == null) failure = exception; }
                    finally { DestroyItem(item); }
                }
                _active.Clear();
                _pool.Clear();
            }
            finally
            {
                DestroyObject(_storage != null ? _storage.gameObject : null);
                _busy = false;
            }
            if (failure != null) throw new InvalidOperationException("A despawn callback failed during disposal.", failure);
        }

        private T CreateItem()
        {
            // An inactive parent suppresses OnEnable even when the source prefab is active.
            T instance = Object.Instantiate(_prefab, _storage);
            instance.gameObject.SetActive(false);
            try
            {
                instance.Initialize(this); // Self-release must pass through the same ownership checks.
                _owned.Add(instance);
                return instance;
            }
            catch
            {
                DestroyObject(instance != null ? instance.gameObject : null);
                throw;
            }
        }

        private void DestroyItem(T item)
        {
            _owned.Remove(item);
            if (item == null) return;
            item.gameObject.SetActive(false);
            DestroyObject(item.gameObject);
        }

        private static void DestroyObject(Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Object.Destroy(item);
            else Object.DestroyImmediate(item);
        }

        private void CheckAvailable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ComponentPoolFactory<T>));
            if (_busy) throw new InvalidOperationException("Pool lifecycle callbacks must not re-enter the same pool.");
            if (_storage == null) throw new InvalidOperationException("The pool container was destroyed. Dispose the pool before destroying its parent.");
        }
    }
}
