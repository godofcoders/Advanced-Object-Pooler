using UnityEngine;
using AdvancedPooling.Runtime;
using AdvancedPooling.Demo; // This is where our EnergyProjectile lives

namespace AdvancedPooling.Unity
{
    public class PoolDemoSpawner : MonoBehaviour
    {
        [Header("Pool Configuration")]
        [Tooltip("The prefab must implement IPoolable<T>")]
        [SerializeField] private EnergyProjectile _prefabToSpawn;

        [Tooltip("Where in the hierarchy should spawned objects go?")]
        [SerializeField] private Transform _containerTransform;

        [Header("Performance Settings")]
        [SerializeField] private int _defaultCapacity = 20;
        [SerializeField] private int _maxCapacity = 100;

        // The Factory instance that wraps Unity's ObjectPool
        private ComponentPoolFactory<EnergyProjectile> _poolFactory;

        private void Awake()
        {
            // AAA Standard: Initialize the pool in Awake so it's ready before Gameplay starts
            // If _containerTransform is null, we create a clean folder in the hierarchy
            if (_containerTransform == null)
            {
                _containerTransform = new GameObject($"Pool_{_prefabToSpawn.name}").transform;
            }

            _poolFactory = new ComponentPoolFactory<EnergyProjectile>(
                _prefabToSpawn,
                _defaultCapacity,
                _maxCapacity,
                _containerTransform
            );
        }

        private void Update()
        {
            // Simple input to test the pool
            // Hold Mouse Button 0 (Left Click) to spam spawn objects
            if (Input.GetMouseButton(0))
            {
                SpawnItem();
            }
        }

        private void SpawnItem()
        {
            // 1. Get an item from the pool (Zero Allocation)
            EnergyProjectile item = _poolFactory.Get();

            // 2. Set its position to the spawner's position
            // (You could also add random offsets here for a fountain effect)
            item.transform.position = transform.position;
            item.transform.rotation = transform.rotation;

            // Note: The item.OnSpawn() is automatically called by the Factory!
        }
    }
}