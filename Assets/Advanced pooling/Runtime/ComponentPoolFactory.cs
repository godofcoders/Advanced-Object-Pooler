using UnityEngine;
using UnityEngine.Pool;
using AdvancedPooling.Core;

namespace AdvancedPooling.Runtime
{
    public class ComponentPoolFactory<T> where T : MonoBehaviour, IPoolable<T>
    {
        private readonly T _prefab;
        private readonly Transform _parentContainer;
        private readonly IObjectPool<T> _pool;

        public ComponentPoolFactory(T prefab, int defaultCapacity = 20, int maxSize = 100, Transform parentContainer = null)
        {
            _prefab = prefab;
            _parentContainer = parentContainer;

            // Utilizing Unity's native memory-safe pool
            _pool = new ObjectPool<T>(
                createFunc: CreateItem,
                actionOnGet: OnGetItem,
                actionOnRelease: OnReleaseItem,
                actionOnDestroy: OnDestroyItem,
                collectionCheck: false, // Set to true in Editor to catch double-releases, false in prod
                defaultCapacity: defaultCapacity,
                maxSize: maxSize
            );
        }

        // Public method for external scripts to request an item
        public T Get() => _pool.Get();

        // --- Pool Lifecycle Callbacks ---

        private T CreateItem()
        {
            T instance = Object.Instantiate(_prefab, _parentContainer);
            instance.Initialize(_pool); // Inject the pool into the item
            return instance;
        }

        private void OnGetItem(T item)
        {
            item.gameObject.SetActive(true);
            item.OnSpawn();
        }

        private void OnReleaseItem(T item)
        {
            item.OnDespawn();
            item.gameObject.SetActive(false);
        }

        private void OnDestroyItem(T item)
        {
            Object.Destroy(item.gameObject);
        }
    }
}