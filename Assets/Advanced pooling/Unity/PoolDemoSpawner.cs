using AdvancedPooling.Demo;
using AdvancedPooling.Runtime;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AdvancedPooling.Unity
{
    public class PoolDemoSpawner : MonoBehaviour
    {
        [Header("Pool Configuration")]
        [SerializeField] private EnergyProjectile _prefabToSpawn;
        [SerializeField] private Transform _containerTransform;

        [Header("Pool Capacity")]
        [Tooltip("Created before gameplay. Shots are skipped when all projectiles are active.")]
        [SerializeField, Min(1)] private int _maxCapacity = 100;

        private ComponentPoolFactory<EnergyProjectile> _poolFactory;
        private bool _ownsContainer;

        private void Awake()
        {
            if (_prefabToSpawn == null)
            {
                Debug.LogError("Assign a projectile prefab to PoolDemoSpawner.", this);
                enabled = false;
                return;
            }
            if (_containerTransform == null)
            {
                _containerTransform = new GameObject($"Pool_{_prefabToSpawn.name}").transform;
                _ownsContainer = true;
            }
            _poolFactory = ComponentPoolFactory<EnergyProjectile>.CreateFixed(
                _prefabToSpawn, Mathf.Max(1, _maxCapacity), _containerTransform);
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            bool fire = Mouse.current != null && Mouse.current.leftButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            bool fire = Input.GetMouseButton(0);
#else
            bool fire = false;
#endif
            if (fire) _poolFactory.TryGet(transform.position, transform.rotation, out _);
        }

        private void OnDestroy()
        {
            try { _poolFactory?.Dispose(); }
            finally
            {
                if (_ownsContainer && _containerTransform != null)
                    Destroy(_containerTransform.gameObject);
            }
        }
    }
}
