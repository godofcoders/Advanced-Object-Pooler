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

        [Header("Performance Settings")]
        [SerializeField, Min(0)] private int _defaultCapacity = 20;
        [SerializeField, Min(1)] private int _maxCapacity = 100;
        [SerializeField, Min(1)] private int _maxActive = 100;
        [SerializeField, Min(0)] private int _prewarmCount = 20;

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
            _poolFactory = new ComponentPoolFactory<EnergyProjectile>(_prefabToSpawn,
                Mathf.Max(0, _defaultCapacity), Mathf.Max(1, _maxCapacity), _containerTransform,
                Mathf.Max(1, _maxActive));
            _poolFactory.Prewarm(Mathf.Clamp(_prewarmCount, 0, Mathf.Max(1, _maxCapacity)));
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
