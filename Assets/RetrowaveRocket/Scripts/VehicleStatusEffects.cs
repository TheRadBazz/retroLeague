using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleStatusEffects : NetworkBehaviour, IStatusEffectReceiver
    {
        private struct SlowEffect
        {
            public ulong SourceId;
            public float EndsAt;
            public float MovementMultiplier;
            public float SteeringMultiplier;
        }

        private readonly NetworkVariable<bool> _isStunned = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _movementMultiplier = new(
            1f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _steeringMultiplier = new(
            1f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly List<SlowEffect> _slowEffects = new();
        private readonly Dictionary<ulong, float> _stunImmunityUntil = new();

        private Rigidbody _rigidbody;
        private float _stunnedUntil;
        private float _immunityPruneTimer;

        public bool IsStunned => _isStunned.Value;
        public float MovementMultiplier => Mathf.Clamp01(_movementMultiplier.Value);
        public float SteeringMultiplier => Mathf.Clamp01(_steeringMultiplier.Value);

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (!IsServer)
            {
                return;
            }

            UpdateStun();
            UpdateSlows();
            PruneStunImmunity();
        }

        public RetrowavePlayerInputState ModifyInput(RetrowavePlayerInputState input)
        {
            if (IsStunned)
            {
                return new RetrowavePlayerInputState
                {
                    ResetPressed = input.ResetPressed,
                };
            }

            if (MovementMultiplier >= 0.999f && SteeringMultiplier >= 0.999f)
            {
                return input;
            }

            input.Throttle *= MovementMultiplier;
            input.Steer *= SteeringMultiplier;
            input.Roll *= SteeringMultiplier;
            return input;
        }

        public float ModifyMaxSpeedMultiplier(float multiplier)
        {
            return multiplier * MovementMultiplier;
        }

        public bool ApplyStunServer(float duration, Vector3 spinTorque, ulong sourceId, float immunitySeconds)
        {
            if (!IsServer || duration <= 0f)
            {
                return false;
            }

            var now = Time.time;

            if (_stunImmunityUntil.TryGetValue(sourceId, out var immuneUntil) && immuneUntil > now)
            {
                return false;
            }

            _stunnedUntil = Mathf.Max(_stunnedUntil, now + duration);
            _isStunned.Value = true;
            _stunImmunityUntil[sourceId] = now + duration + Mathf.Max(0f, immunitySeconds);

            if (_rigidbody != null && spinTorque.sqrMagnitude > 0.001f)
            {
                _rigidbody.AddTorque(spinTorque, ForceMode.VelocityChange);
            }

            return true;
        }

        public void ApplySlowServer(float duration, float movementMultiplier, float steeringMultiplier, ulong sourceId)
        {
            if (!IsServer || duration <= 0f)
            {
                return;
            }

            var effect = new SlowEffect
            {
                SourceId = sourceId,
                EndsAt = Time.time + duration,
                MovementMultiplier = Mathf.Clamp01(movementMultiplier),
                SteeringMultiplier = Mathf.Clamp01(steeringMultiplier),
            };

            for (var i = 0; i < _slowEffects.Count; i++)
            {
                if (_slowEffects[i].SourceId != sourceId)
                {
                    continue;
                }

                _slowEffects[i] = effect;
                RecalculateSlowMultipliers();
                return;
            }

            _slowEffects.Add(effect);
            RecalculateSlowMultipliers();
        }

        public void ClearServer()
        {
            if (!IsServer)
            {
                return;
            }

            _stunnedUntil = 0f;
            _isStunned.Value = false;
            _slowEffects.Clear();
            _stunImmunityUntil.Clear();
            _movementMultiplier.Value = 1f;
            _steeringMultiplier.Value = 1f;
        }

        private void UpdateStun()
        {
            if (_isStunned.Value && Time.time >= _stunnedUntil)
            {
                _isStunned.Value = false;
                _stunnedUntil = 0f;
            }
        }

        private void UpdateSlows()
        {
            if (_slowEffects.Count <= 0)
            {
                return;
            }

            var now = Time.time;
            var changed = false;

            for (var i = _slowEffects.Count - 1; i >= 0; i--)
            {
                if (_slowEffects[i].EndsAt > now)
                {
                    continue;
                }

                _slowEffects.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                RecalculateSlowMultipliers();
            }
        }

        private void RecalculateSlowMultipliers()
        {
            var movement = 1f;
            var steering = 1f;

            for (var i = 0; i < _slowEffects.Count; i++)
            {
                movement = Mathf.Min(movement, _slowEffects[i].MovementMultiplier);
                steering = Mathf.Min(steering, _slowEffects[i].SteeringMultiplier);
            }

            _movementMultiplier.Value = movement;
            _steeringMultiplier.Value = steering;
        }

        private void PruneStunImmunity()
        {
            _immunityPruneTimer -= Time.fixedDeltaTime;

            if (_immunityPruneTimer > 0f || _stunImmunityUntil.Count <= 0)
            {
                return;
            }

            _immunityPruneTimer = 2f;
            var now = Time.time;
            var expiredSources = ListPool<ulong>.Get();

            foreach (var pair in _stunImmunityUntil)
            {
                if (pair.Value <= now)
                {
                    expiredSources.Add(pair.Key);
                }
            }

            for (var i = 0; i < expiredSources.Count; i++)
            {
                _stunImmunityUntil.Remove(expiredSources[i]);
            }

            ListPool<ulong>.Release(expiredSources);
        }

        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new();

            public static List<T> Get()
            {
                return Pool.Count > 0 ? Pool.Pop() : new List<T>();
            }

            public static void Release(List<T> list)
            {
                if (list == null)
                {
                    return;
                }

                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
