using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RetrowaveBall : NetworkBehaviour
    {
        private const float MaxBallSpeed = 44f;
        private const float BaseHitSpeed = 1.8f;
        private const float MinimumTouchSpeed = 0.85f;
        private const float SpeedTransferScale = 0.46f;
        private const float ContactVelocityInfluence = 0.44f;
        private const float FrontHitInfluence = 0.22f;
        private const float UpwardBias = 0.14f;
        private const float TopTouchLift = 0.52f;
        private const float CenterTouchLift = 0.16f;
        private const float MaxHitVelocityChange = 12.5f;
        private const float MaxHitSpin = 18f;
        private const float TopTouchJuggleBoost = 0.8f;
        private const float StrongHitMomentumThreshold = 10.5f;
        private const float StrongHitApproachThreshold = 8f;
        private const float PlayerMomentumCarry = 0.24f;
        private const float PlayerMinimumCarry = 0.75f;
        private const float PlayerForwardRetention = 0.94f;

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

            var contactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            var contactVelocity = player.Body.GetPointVelocity(contactPoint);
            var relativeContactVelocity = contactVelocity - _rigidbody.GetPointVelocity(contactPoint);
            var playerVelocity = player.Body.linearVelocity;
            var playerForward = player.transform.forward;
            var hitOffset = transform.position - contactPoint;
            var contactNormal = hitOffset.sqrMagnitude > 0.0001f
                ? hitOffset.normalized
                : (transform.position - player.transform.position).normalized;
            var relativeVelocityDirection = relativeContactVelocity.sqrMagnitude > 0.0001f
                ? relativeContactVelocity.normalized
                : playerForward;
            var localBallPosition = player.transform.InverseTransformPoint(transform.position);
            var frontFactor = Mathf.Clamp01((localBallPosition.z + 0.35f) / 1.45f);
            var topFactor = Mathf.Clamp01((localBallPosition.y + 0.05f) / 1.2f);
            var centerFactor = 1f - Mathf.Clamp01(Mathf.Abs(localBallPosition.x) / 0.95f);

            if (contactNormal.sqrMagnitude < 0.001f)
            {
                contactNormal = playerForward;
            }

            var liftAmount = UpwardBias + topFactor * TopTouchLift + centerFactor * CenterTouchLift;
            var launchDirection = (
                contactNormal * 0.62f
                + relativeVelocityDirection * ContactVelocityInfluence
                + playerForward * (FrontHitInfluence + frontFactor * 0.18f)
                + Vector3.up * liftAmount).normalized;

            if (topFactor > 0.35f && centerFactor > 0.25f)
            {
                var juggleDirection = (Vector3.up * 0.82f + playerForward * 0.28f).normalized;
                launchDirection = Vector3.Slerp(launchDirection, juggleDirection, topFactor * centerFactor * 0.45f);
            }

            var playerSpeedContribution = playerVelocity.magnitude * SpeedTransferScale;
            var closingContribution = Mathf.Max(0f, Vector3.Dot(relativeContactVelocity, contactNormal));
            var forwardContribution = Mathf.Max(0f, Vector3.Dot(contactVelocity, playerForward));
            var verticalContribution = Mathf.Max(0f, contactVelocity.y);
            var rawVelocityChange = BaseHitSpeed
                                    + playerSpeedContribution
                                    + closingContribution * 0.18f
                                    + forwardContribution * 0.08f
                                    + verticalContribution * 0.16f
                                    + topFactor * centerFactor * TopTouchJuggleBoost;
            var impactStrength = Mathf.InverseLerp(
                2.25f,
                StrongHitMomentumThreshold,
                playerVelocity.magnitude + closingContribution * 0.65f + forwardContribution * 0.2f);
            var approachStrength = Mathf.InverseLerp(1.5f, StrongHitApproachThreshold, closingContribution);
            var touchStrength = Mathf.Clamp01(impactStrength * 0.7f + approachStrength * 0.3f);
            var desiredVelocityChange = Mathf.Lerp(MinimumTouchSpeed, rawVelocityChange, touchStrength);
            desiredVelocityChange = Mathf.Clamp(desiredVelocityChange, MinimumTouchSpeed, MaxHitVelocityChange);

            _rigidbody.AddForce(launchDirection * desiredVelocityChange, ForceMode.VelocityChange);
            PreservePlayerMomentum(player, playerForward, contactNormal, desiredVelocityChange, frontFactor, centerFactor);

            var spinAxis = Vector3.Cross(contactNormal, relativeVelocityDirection);
            var hitSpin = spinAxis * Mathf.Min(relativeContactVelocity.magnitude * 0.42f + player.Body.angularVelocity.magnitude * 0.28f, MaxHitSpin);

            if (float.IsFinite(hitSpin.x) && float.IsFinite(hitSpin.y) && float.IsFinite(hitSpin.z))
            {
                _rigidbody.AddTorque(hitSpin, ForceMode.VelocityChange);
            }
        }

        private static void PreservePlayerMomentum(
            RetrowavePlayerController player,
            Vector3 playerForward,
            Vector3 contactNormal,
            float desiredVelocityChange,
            float frontFactor,
            float centerFactor)
        {
            if (player.Body == null)
            {
                return;
            }

            var currentVelocity = player.Body.linearVelocity;
            var forwardSpeed = Mathf.Max(0f, Vector3.Dot(currentVelocity, playerForward));
            var retainVelocity = Mathf.Max(0f, forwardSpeed * PlayerForwardRetention);
            var sustainVelocity = Mathf.Max(
                PlayerMinimumCarry,
                desiredVelocityChange * PlayerMomentumCarry + forwardSpeed * 0.05f);
            sustainVelocity *= Mathf.Lerp(0.72f, 1.08f, Mathf.Clamp01(frontFactor * 0.75f + centerFactor * 0.25f));

            if (forwardSpeed < retainVelocity)
            {
                player.Body.AddForce(playerForward * (retainVelocity - forwardSpeed), ForceMode.VelocityChange);
            }

            player.Body.AddForce(playerForward * sustainVelocity, ForceMode.VelocityChange);

            var backwardLoss = Vector3.Dot(player.Body.linearVelocity, -contactNormal);

            if (backwardLoss > 0.1f)
            {
                player.Body.AddForce(contactNormal * Mathf.Min(backwardLoss * 0.55f, 2.2f), ForceMode.VelocityChange);
            }
        }
    }
}
