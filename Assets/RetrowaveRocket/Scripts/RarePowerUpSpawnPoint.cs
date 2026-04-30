using System.Collections.Generic;
using UnityEngine;

namespace RetrowaveRocket
{
    public sealed class RarePowerUpSpawnPoint : MonoBehaviour
    {
        private static readonly List<RarePowerUpSpawnPoint> ActivePoints = new();

        [SerializeField] private float _gizmoRadius = 2.25f;

        public static IReadOnlyList<RarePowerUpSpawnPoint> Active => ActivePoints;

        private void OnEnable()
        {
            if (!ActivePoints.Contains(this))
            {
                ActivePoints.Add(this);
            }
        }

        private void OnDisable()
        {
            ActivePoints.Remove(this);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.12f, 1f, 0.45f, 0.55f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.25f, _gizmoRadius));
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 3f);
        }
    }
}
