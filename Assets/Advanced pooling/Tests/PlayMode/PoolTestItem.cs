using System;
using AdvancedPooling.Core;
using UnityEngine;
using UnityEngine.Pool;

namespace AdvancedPooling.Tests
{
    public sealed class PoolTestItem : MonoBehaviour, IPoolable<PoolTestItem>
    {
        public static bool FailInitialize;
        public static bool FailSpawn;
        public static bool FailDespawn;
        public static Action<PoolTestItem> SpawnAction;
        public IObjectPool<PoolTestItem> Pool { get; private set; }
        public int SpawnCount { get; private set; }
        public int DespawnCount { get; private set; }
        public int EnableCount { get; private set; }
        public bool EnabledBeforeInitialization { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public Quaternion SpawnRotation { get; private set; }
        public Vector3 EnablePosition { get; private set; }
        public bool DespawnedWhileActive { get; private set; }

        private void OnEnable()
        {
            EnableCount++;
            EnabledBeforeInitialization |= Pool == null;
            EnablePosition = transform.position;
        }

        public void Initialize(IObjectPool<PoolTestItem> pool)
        {
            if (FailInitialize) throw new InvalidOperationException("initialize failure");
            Pool = pool;
        }

        public void OnSpawn()
        {
            SpawnCount++;
            SpawnPosition = transform.position;
            SpawnRotation = transform.rotation;
            SpawnAction?.Invoke(this);
            if (FailSpawn) throw new InvalidOperationException("spawn failure");
        }

        public void OnDespawn()
        {
            DespawnCount++;
            DespawnedWhileActive = gameObject.activeSelf;
            if (FailDespawn) throw new InvalidOperationException("despawn failure");
        }

        public static void ResetFailures()
        {
            FailInitialize = FailSpawn = FailDespawn = false;
            SpawnAction = null;
        }
    }
}
