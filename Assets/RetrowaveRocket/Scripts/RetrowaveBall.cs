using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RetrowaveBall : NetworkBehaviour
    {
        private const float MaxBallSpeed = 44f;
        private const float BaseHitSpeed = 6f;
        private const float SpeedTransferScale = 0.72f;
        private const float ForwardInfluence = 0.38f;
        private const float UpwardBias = 0.22f;
        private const float OffsetLiftScale = 0.9f;
        private const float MaxHitVelocityChange = 24f;
        private const float MaxHitSpin = 18f;

        private Rigidbody _rigidbody;
        private MeshRenderer _renderer;
        private Light _glow;

        public static RetrowaveBall Instance { get; private set; }
        public Rigidbody Body => _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _renderer = GetComponent<MeshRenderer>();

            var glowObject = new GameObject("Ball Glow");
            glowObject.transform.SetParent(transform, false);
            _glow = glowObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.range = 10f;
            _glow.color = new Color(0.45f, 0.8f, 1f);
            _glow.intensity = 5f;
        }

        private void Update()
        {
            if (_glow == null || _rigidbody == null)
            {
                return;
            }

            _glow.intensity = 4f + _rigidbody.linearVelocity.magnitude * 0.35f;
        }

        private void FixedUpdate()
        {
            if (!IsServer)
            {
                return;
            }

            if (_rigidbody.linearVelocity.sqrMagnitude > MaxBallSpeed * MaxBallSpeed)
            {
                _rigidbody.linearVelocity = _rigidbody.linearVelocity.normalized * MaxBallSpeed;
            }

            if (transform.position.y < -14f || transform.position.magnitude > 240f)
            {
                ResetBall();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer)
            {
                return;
            }

            var player = collision.rigidbody != null
                ? collision.rigidbody.GetComponent<RetrowavePlayerController>()
                : collision.collider.GetComponentInParent<RetrowavePlayerController>();

            if (player != null)
            {
                RetrowaveMatchManager.Instance?.RegisterBallTouch(player);
                ApplyPlayerHit(player, collision);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;

            if (!IsServer)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }

        public void ResetBall()
        {
            if (!IsServer)
            {
                return;
            }

            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(RetrowaveArenaConfig.BallSpawnPoint, Quaternion.identity);
        }

        private void ApplyPlayerHit(RetrowavePlayerController player, Collision collision)
        {
            if (player.Body == null)
            {
                return;
            }

            var playerVelocity = player.Body.linearVelocity;
            var playerForward = player.transform.forward;
            var contactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            var hitOffset = transform.position - contactPoint;
            var hitDirection = transform.position - player.transform.position;

            if (hitDirection.sqrMagnitude < 0.001f)
            {
                hitDirection = transform.position - contactPoint;
            }

            if (hitDirection.sqrMagnitude < 0.001f)
            {
                hitDirection = playerForward;
            }

            hitDirection = hitDirection.normalized;
            var offsetLift = Mathf.Clamp(hitOffset.y / Mathf.Max(transform.localScale.y * 0.5f, 0.001f), -0.9f, 0.9f);
            var launchDirection = (hitDirection * (1f - ForwardInfluence) + playerForward * ForwardInfluence + Vector3.up * (UpwardBias + Mathf.Max(0f, offsetLift) * OffsetLiftScale)).normalized;

            var speedContribution = playerVelocity.magnitude * SpeedTransferScale;
            var closingContribution = Mathf.Max(0f, Vector3.Dot(playerVelocity, launchDirection));
            var desiredVelocityChange = BaseHitSpeed + speedContribution + closingContribution * 0.35f;
            desiredVelocityChange = Mathf.Clamp(desiredVelocityChange, BaseHitSpeed, MaxHitVelocityChange);

            _rigidbody.AddForce(launchDirection * desiredVelocityChange, ForceMode.VelocityChange);

            var hitSpin = Vector3.Cross(hitOffset.normalized, playerVelocity.normalized) * Mathf.Min(playerVelocity.magnitude * 0.55f, MaxHitSpin);

            if (float.IsFinite(hitSpin.x) && float.IsFinite(hitSpin.y) && float.IsFinite(hitSpin.z))
            {
                _rigidbody.AddTorque(hitSpin, ForceMode.VelocityChange);
            }
        }
    }
}
