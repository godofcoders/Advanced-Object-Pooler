using UnityEngine;
using UnityEngine.Pool;
using AdvancedPooling.Core;

namespace AdvancedPooling.Demo
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnergyProjectile : MonoBehaviour, IPoolable<EnergyProjectile>
    {
        [SerializeField] private float _speed = 10f;
        [SerializeField] private float _lifeTime = 2f;

        private IObjectPool<EnergyProjectile> _pool;
        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        // 1. Dependency Injection from the Factory
        public void Initialize(IObjectPool<EnergyProjectile> pool)
        {
            _pool = pool;
        }

        // 2. Triggered when pulled from the pool
        public void OnSpawn()
        {
            _rb.linearVelocity = transform.right * _speed;
            Invoke(nameof(ReturnToPool), _lifeTime);
        }

        // 3. Triggered when returning to the pool
        public void OnDespawn()
        {
            _rb.linearVelocity = Vector2.zero;
            CancelInvoke(); // Clean up memory/timers!
        }

        // 4. The object handles its own lifecycle
        private void ReturnToPool()
        {
            _pool.Release(this);
        }

        // Optional: Return on collision
        private void OnTriggerEnter2D(Collider2D other)
        {
            ReturnToPool();
        }
    }
}