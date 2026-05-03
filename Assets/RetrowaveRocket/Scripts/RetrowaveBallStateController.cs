using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RetrowaveBallStateController : NetworkBehaviour
    {
        private const int MaxExplosionHits = 64;
        private static readonly Collider[] ExplosionHits = new Collider[MaxExplosionHits];

        [SerializeField] private Vector2 _inactiveDelayRange = new(18f, 32f);
        [SerializeField] private float _activeDuration = 15f;
        [SerializeField] private float _chargedHitMultiplier = 1.28f;
        [SerializeField] private float _heavyHitMultiplier = 0.82f;
        [SerializeField] private float _neonHitMultiplier = 1.06f;
        [SerializeField] private float _volatileHardHitThreshold = 0.56f;
        [SerializeField] private float _volatileExplosionRadius = 10f;
        [SerializeField] private float _volatileExplosionImpulse = 13f;
        [SerializeField] private float _volatileBallImpulse = 5f;

        private readonly NetworkVariable<int> _stateValue = new(
            (int)RetrowaveBallState.Normal,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _stateEndsAt = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private Rigidbody _body;
        private RetrowaveBall _ball;
        private MeshRenderer _renderer;
        private Light _glow;
        private TrailRenderer _trail;
        private Material _stateMaterial;
        private float _nextStateAt;

        public RetrowaveBallState State => (RetrowaveBallState)_stateValue.Value;
        public float TimeRemaining => State == RetrowaveBallState.Normal ? 0f : Mathf.Max(0f, _stateEndsAt.Value - Time.time);

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _ball = GetComponent<RetrowaveBall>();
            _renderer = GetComponent<MeshRenderer>();
            _glow = GetComponentInChildren<Light>(true);
            EnsureTrail();
            RefreshVisuals();
        }

        private bool HasSimulationAuthority => IsServer || (_ball != null && _ball.IsOfflineMode);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _stateValue.OnValueChanged += HandleStateChanged;
            RefreshVisuals();

            if (IsServer)
            {
                ScheduleNextStateServer();
            }
        }

        public override void OnNetworkDespawn()
        {
            _stateValue.OnValueChanged -= HandleStateChanged;
            base.OnNetworkDespawn();
        }

        private void FixedUpdate()
        {
            if (!HasSimulationAuthority)
            {
                return;
            }

            if (State != RetrowaveBallState.Normal)
            {
                if (Time.time >= _stateEndsAt.Value)
                {
                    SetStateServer(RetrowaveBallState.Normal);
                    ScheduleNextStateServer();
                }

                return;
            }

            var matchManager = RetrowaveMatchManager.Instance;

            if (matchManager == null || (!matchManager.IsWarmup && !matchManager.IsLiveMatch) || Time.time < _nextStateAt)
            {
                return;
            }

            SetStateServer(ChooseRandomState());
        }

        public void ModifyHitServer(
            RetrowavePlayerController player,
            bool isTeamCombo,
            ref Vector3 launchDirection,
            ref float desiredVelocityChange,
            float touchStrength)
        {
            if (!HasSimulationAuthority)
            {
                return;
            }

            var state = State;

            if (state == RetrowaveBallState.Normal)
            {
                return;
            }

            switch (state)
            {
                case RetrowaveBallState.Charged:
                    desiredVelocityChange *= _chargedHitMultiplier;
                    SetStateServer(RetrowaveBallState.Normal);
                    ScheduleNextStateServer();
                    break;

                case RetrowaveBallState.Heavy:
                    desiredVelocityChange *= _heavyHitMultiplier;
                    var flattened = Vector3.ProjectOnPlane(launchDirection, Vector3.up);

                    if (flattened.sqrMagnitude > 0.001f)
                    {
                        launchDirection = Vector3.Slerp(launchDirection, flattened.normalized, 0.42f).normalized;
                    }
                    break;

                case RetrowaveBallState.Volatile:
                    desiredVelocityChange *= isTeamCombo ? 1.16f : 1.05f;

                    if (touchStrength >= _volatileHardHitThreshold)
                    {
                        DetonateVolatileServer(player);
                        SetStateServer(RetrowaveBallState.Normal);
                        ScheduleNextStateServer();
                    }
                    break;

                case RetrowaveBallState.Neon:
                    desiredVelocityChange *= isTeamCombo ? _neonHitMultiplier + 0.08f : _neonHitMultiplier;
                    player?.AwardStyleServer(RetrowaveStyleEvent.PowerPlay);
                    break;
            }
        }

        public void ResetStateServer()
        {
            if (!HasSimulationAuthority)
            {
                return;
            }

            SetStateServer(RetrowaveBallState.Normal);
            ScheduleNextStateServer();
        }

        private void SetStateServer(RetrowaveBallState state)
        {
            _stateValue.Value = (int)state;
            _stateEndsAt.Value = state == RetrowaveBallState.Normal ? 0f : Time.time + Mathf.Max(1f, _activeDuration);
            RefreshVisuals();
        }

        private void ScheduleNextStateServer()
        {
            var min = Mathf.Min(_inactiveDelayRange.x, _inactiveDelayRange.y);
            var max = Mathf.Max(_inactiveDelayRange.x, _inactiveDelayRange.y);
            _nextStateAt = Time.time + Random.Range(Mathf.Max(1f, min), Mathf.Max(1f, max));
        }

        private static RetrowaveBallState ChooseRandomState()
        {
            return (RetrowaveBallState)Random.Range((int)RetrowaveBallState.Charged, (int)RetrowaveBallState.Neon + 1);
        }

        private void DetonateVolatileServer(RetrowavePlayerController sourcePlayer)
        {
            var origin = _body != null ? _body.worldCenterOfMass : transform.position;
            var hitCount = Physics.OverlapSphereNonAlloc(origin, _volatileExplosionRadius, ExplosionHits, ~0, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hitCount; i++)
            {
                var player = ExplosionHits[i] != null
                    ? ExplosionHits[i].GetComponentInParent<RetrowavePlayerController>()
                    : null;

                if (player == null || player.Body == null || !player.IsArenaParticipant)
                {
                    continue;
                }

                var offset = player.Body.worldCenterOfMass - origin;

                if (offset.sqrMagnitude < 0.001f)
                {
                    offset = player.transform.forward;
                }

                var falloff = Mathf.Lerp(1f, 0.28f, Mathf.Clamp01(offset.magnitude / _volatileExplosionRadius));
                player.Body.AddForce((offset.normalized + Vector3.up * 0.24f).normalized * (_volatileExplosionImpulse * falloff), ForceMode.VelocityChange);
            }

            if (_body != null && sourcePlayer != null)
            {
                var direction = (_body.worldCenterOfMass - sourcePlayer.transform.position).normalized;
                _body.AddForce((direction + Vector3.up * 0.18f).normalized * _volatileBallImpulse, ForceMode.VelocityChange);
            }
        }

        private void HandleStateChanged(int _, int __)
        {
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            var state = State;
            var color = RetrowaveBallStateCatalog.GetColor(state);
            _glow ??= GetComponentInChildren<Light>(true);

            if (_renderer != null)
            {
                _stateMaterial ??= RetrowaveStyle.CreateLitMaterial(color * 0.62f, color * 1.9f, 0.82f, 0.08f);

                if (_stateMaterial.HasProperty("_BaseColor"))
                {
                    _stateMaterial.SetColor("_BaseColor", Color.Lerp(color, Color.white, 0.16f) * 0.72f);
                }

                if (_stateMaterial.HasProperty("_Color"))
                {
                    _stateMaterial.SetColor("_Color", Color.Lerp(color, Color.white, 0.16f) * 0.72f);
                }

                if (_stateMaterial.HasProperty("_EmissionColor"))
                {
                    _stateMaterial.EnableKeyword("_EMISSION");
                    _stateMaterial.SetColor("_EmissionColor", color * (state == RetrowaveBallState.Normal ? 0.85f : 2.4f));
                }

                _renderer.sharedMaterial = _stateMaterial;
            }

            if (_glow != null)
            {
                _glow.color = color;
                _glow.intensity = state == RetrowaveBallState.Normal ? 4.5f : 8.5f;
            }

            if (_trail != null)
            {
                _trail.enabled = state == RetrowaveBallState.Neon;
                _trail.emitting = state == RetrowaveBallState.Neon;
            }
        }

        private void EnsureTrail()
        {
            _trail = GetComponent<TrailRenderer>();

            if (_trail == null)
            {
                _trail = gameObject.AddComponent<TrailRenderer>();
            }

            _trail.time = 0.75f;
            _trail.minVertexDistance = 0.18f;
            _trail.widthMultiplier = 0.42f;
            _trail.numCornerVertices = 4;
            _trail.numCapVertices = 4;
            _trail.material = RetrowaveStyle.CreateTransparentLitMaterial(
                new Color(0.05f, 1f, 0.72f, 0.36f),
                new Color(0.08f, 1f, 0.72f, 1f) * 3.6f,
                0.86f,
                0f);
            _trail.enabled = false;
            _trail.emitting = false;
        }
    }
}
