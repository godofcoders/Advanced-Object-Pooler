using AdvancedPooling.Core;
using UnityEngine;
using UnityEngine.Pool;

namespace AdvancedPooling.Demo
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnergyProjectile : MonoBehaviour, IPoolable<EnergyProjectile>
    {
        [SerializeField, Min(0f)] private float _speed = 10f;
        [SerializeField, Min(0f)] private float _lifeTime = 2f;

        private IObjectPool<EnergyProjectile> _pool;
        private Rigidbody _rb;
        private bool _spawned;
        private float _expiresAt;

        public void Initialize(IObjectPool<EnergyProjectile> pool)
        {
            _pool = pool;
            // Inactive instances may not have received Awake yet.
            _rb = GetComponent<Rigidbody>();
        }

        public void OnSpawn()
        {
            _spawned = true;
            _expiresAt = Time.time + _lifeTime;
            SetVelocity(transform.right * _speed);
        }

        public void OnDespawn()
        {
            _spawned = false;
            SetVelocity(Vector3.zero);
            _rb.angularVelocity = Vector3.zero;
        }

        private void Update()
        {
            if (_spawned && Time.time >= _expiresAt) ReturnToPool();
        }

        private void SetVelocity(Vector3 velocity)
        {
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = velocity;
#else
            _rb.velocity = velocity;
#endif
        }

        private void ReturnToPool()
        {
            // Multiple contacts or timer/collision in the same frame must release only once.
            if (!_spawned) return;
            _spawned = false;
            _pool.Release(this);
        }

        private void OnTriggerEnter(Collider other) => ReturnToPool();
        private void OnCollisionEnter(Collision collision) => ReturnToPool();
    }
}
