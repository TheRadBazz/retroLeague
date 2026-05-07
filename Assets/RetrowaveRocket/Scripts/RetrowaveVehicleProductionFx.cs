#pragma warning disable 0649

using UnityEngine;

namespace RetrowaveRocket
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RetrowavePlayerController))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RetrowaveVehicleProductionFx : MonoBehaviour
    {
        [SerializeField] private float _boostRate = 95f;
        [SerializeField] private float _glideRate = 36f;
        [SerializeField] private float _tireSparkMinSpeed = 8f;
        [SerializeField] private float _tireSparkMinLateralSpeed = 3.2f;

        private RetrowavePlayerController _player;
        private Rigidbody _body;
        private ParticleSystem _boostLeft;
        private ParticleSystem _boostRight;
        private ParticleSystem _sparkLeft;
        private ParticleSystem _sparkRight;
        private ParticleSystem _landingBurst;
        private ParticleSystem _flipBurst;
        private ParticleSystem _overdriveBurst;
        private Light _flashLight;
        private int _lastStyleAwardSerial;
        private float _flashEndsAt;
        private float _nextCollisionSfxAt;
        private Color _flashColor = Color.white;

        private void Awake()
        {
            _player = GetComponent<RetrowavePlayerController>();
            _body = GetComponent<Rigidbody>();
            BuildEffects();
        }

        private void Update()
        {
            if (_player == null || !_player.IsRuntimeActive || !_player.IsArenaParticipant)
            {
                SetEmissionRate(_boostLeft, 0f);
                SetEmissionRate(_boostRight, 0f);
                SetEmissionRate(_sparkLeft, 0f);
                SetEmissionRate(_sparkRight, 0f);
                return;
            }

            UpdateBoostEffects();
            UpdateTireSparks();
            UpdateStyleBursts();
            UpdateFlashLight();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_player == null || !_player.IsArenaParticipant || Time.time < _nextCollisionSfxAt)
            {
                return;
            }

            var relativeSpeed = collision.relativeVelocity.magnitude;

            if (relativeSpeed < 4.5f)
            {
                return;
            }

            _nextCollisionSfxAt = Time.time + 0.12f;
            var contactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            RetrowaveArenaAudio.PlayImpact(contactPoint, Mathf.InverseLerp(4.5f, 18f, relativeSpeed));
        }

        private void BuildEffects()
        {
            _boostLeft = CreateLoopingParticles(
                "Layered Boost Left",
                new Vector3(-0.38f, -0.08f, -1.08f),
                Quaternion.Euler(0f, 180f, 0f),
                new Color(0.12f, 0.86f, 1f, 0.86f),
                0.19f,
                0.34f,
                8.4f,
                0.07f,
                14f,
                140);
            _boostRight = CreateLoopingParticles(
                "Layered Boost Right",
                new Vector3(0.38f, -0.08f, -1.08f),
                Quaternion.Euler(0f, 180f, 0f),
                new Color(1f, 0.34f, 0.84f, 0.82f),
                0.19f,
                0.34f,
                8.4f,
                0.07f,
                14f,
                140);

            _sparkLeft = CreateLoopingParticles(
                "Tire Sparks Left",
                new Vector3(-0.56f, -0.42f, -0.62f),
                Quaternion.Euler(18f, 205f, 0f),
                new Color(1f, 0.54f, 0.12f, 0.92f),
                0.16f,
                0.42f,
                5.8f,
                0.035f,
                28f,
                90);
            _sparkRight = CreateLoopingParticles(
                "Tire Sparks Right",
                new Vector3(0.56f, -0.42f, -0.62f),
                Quaternion.Euler(18f, 155f, 0f),
                new Color(1f, 0.54f, 0.12f, 0.92f),
                0.16f,
                0.42f,
                5.8f,
                0.035f,
                28f,
                90);

            _landingBurst = CreateBurstParticles("Clean Landing Flash", new Vector3(0f, -0.48f, 0f), new Color(0.5f, 1f, 0.58f, 0.78f), 0.34f, 0.72f, 2.8f, 0.22f);
            _flipBurst = CreateBurstParticles("Flip Trick Burst", new Vector3(0f, 0.05f, -0.18f), new Color(1f, 0.34f, 0.92f, 0.82f), 0.24f, 0.56f, 4.8f, 0.12f);
            _overdriveBurst = CreateBurstParticles("Overdrive Pop", new Vector3(0f, -0.1f, -0.72f), new Color(1f, 0.8f, 0.22f, 0.82f), 0.22f, 0.46f, 5.6f, 0.12f);

            var lightObject = new GameObject("Vehicle FX Flash");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.05f, -0.35f);
            _flashLight = lightObject.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.range = 6f;
            _flashLight.intensity = 0f;
        }

        private ParticleSystem CreateLoopingParticles(
            string effectName,
            Vector3 localPosition,
            Quaternion localRotation,
            Color color,
            float lifetime,
            float size,
            float speed,
            float radius,
            float coneAngle,
            int maxParticles)
        {
            var effectObject = new GameObject(effectName);
            effectObject.transform.SetParent(transform, false);
            effectObject.transform.localPosition = localPosition;
            effectObject.transform.localRotation = localRotation;

            var particles = effectObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startSpeed = speed;
            main.startColor = color;
            main.maxParticles = maxParticles;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = radius;
            shape.angle = coneAngle;

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(color, new Color(color.r, color.g, color.b, 0f));

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.material = RetrowaveStyle.CreateTransparentUnlitMaterial(color);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            particles.Play();
            return particles;
        }

        private ParticleSystem CreateBurstParticles(
            string effectName,
            Vector3 localPosition,
            Color color,
            float lifetime,
            float size,
            float speed,
            float radius)
        {
            var effectObject = new GameObject(effectName);
            effectObject.transform.SetParent(transform, false);
            effectObject.transform.localPosition = localPosition;

            var particles = effectObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startSpeed = speed;
            main.startColor = color;
            main.maxParticles = 90;

            var emission = particles.emission;
            emission.enabled = false;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(color, new Color(color.r, color.g, color.b, 0f));

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.material = RetrowaveStyle.CreateTransparentUnlitMaterial(color);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return particles;
        }

        private void UpdateBoostEffects()
        {
            var rate = 0f;
            var density = RetrowaveGameSettings.VfxDensityMultiplier;

            if (_player.BoostFxActive)
            {
                rate = _player.EngineAudioBoosting ? _boostRate : _glideRate;
                rate *= Mathf.Lerp(1f, 1.35f, _player.HeatNormalized);
            }

            SetEmissionRate(_boostLeft, rate * density);
            SetEmissionRate(_boostRight, rate * density);

            var teamColor = RetrowaveStyle.GetTeamGlow(_player.Team);
            var boostColor = _player.IsOvercharged
                ? new Color(1f, 0.82f, 0.22f, 0.92f)
                : Color.Lerp(teamColor, new Color(1f, 0.42f, 0.08f, 0.92f), _player.HeatNormalized);
            SetParticleColor(_boostLeft, Color.Lerp(boostColor, Color.white, 0.06f));
            SetParticleColor(_boostRight, boostColor);
        }

        private void UpdateTireSparks()
        {
            var rate = 0f;
            var density = RetrowaveGameSettings.VfxDensityMultiplier;

            if (_player.IsGroundedForHud && _body != null)
            {
                var velocity = _player.CurrentVelocity;
                var localVelocity = transform.InverseTransformDirection(velocity);
                var speed = velocity.magnitude;
                var lateral = Mathf.Abs(localVelocity.x);

                if (speed > _tireSparkMinSpeed && lateral > _tireSparkMinLateralSpeed)
                {
                    var slip = Mathf.InverseLerp(_tireSparkMinLateralSpeed, _tireSparkMinLateralSpeed * 2.5f, lateral);
                    rate = Mathf.Lerp(8f, 72f, slip) * Mathf.InverseLerp(_tireSparkMinSpeed, _tireSparkMinSpeed * 1.8f, speed);
                }
            }

            SetEmissionRate(_sparkLeft, rate * density);
            SetEmissionRate(_sparkRight, rate * density);
        }

        private void UpdateStyleBursts()
        {
            var serial = _player.LastStyleAwardSerial;

            if (serial == 0 || serial == _lastStyleAwardSerial)
            {
                return;
            }

            _lastStyleAwardSerial = serial;

            switch (_player.LastStyleAwardEvent)
            {
                case RetrowaveStyleEvent.CleanLanding:
                    _landingBurst.Emit(ScaleBurstCount(42));
                    TriggerFlash(new Color(0.46f, 1f, 0.52f, 1f), 0.32f);
                    RetrowaveArenaAudio.PlayImpact(transform.position, 0.52f);
                    break;
                case RetrowaveStyleEvent.FlipTrick:
                    _flipBurst.Emit(ScaleBurstCount(36));
                    TriggerFlash(new Color(1f, 0.38f, 0.9f, 1f), 0.28f);
                    RetrowaveArenaAudio.PlayImpact(transform.position, 0.42f);
                    break;
                case RetrowaveStyleEvent.ObjectiveCapture:
                    _overdriveBurst.Emit(ScaleBurstCount(48));
                    TriggerFlash(new Color(1f, 0.8f, 0.22f, 1f), 0.36f);
                    break;
            }
        }

        private static int ScaleBurstCount(int count)
        {
            return Mathf.Max(8, Mathf.RoundToInt(count * RetrowaveGameSettings.VfxDensityMultiplier));
        }

        private void TriggerFlash(Color color, float duration)
        {
            _flashColor = color;
            _flashEndsAt = Time.time + Mathf.Max(0.05f, duration);
        }

        private void UpdateFlashLight()
        {
            if (_flashLight == null)
            {
                return;
            }

            if (Time.time >= _flashEndsAt)
            {
                _flashLight.intensity = Mathf.MoveTowards(_flashLight.intensity, 0f, Time.deltaTime * 16f);
                return;
            }

            var remaining = Mathf.Clamp01((_flashEndsAt - Time.time) / 0.36f);
            _flashLight.color = _flashColor;
            _flashLight.intensity = Mathf.Lerp(0f, 8.5f, remaining);
        }

        private static void SetEmissionRate(ParticleSystem particles, float rate)
        {
            if (particles == null)
            {
                return;
            }

            var emission = particles.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
        }

        private static void SetParticleColor(ParticleSystem particles, Color color)
        {
            if (particles == null)
            {
                return;
            }

            var main = particles.main;
            main.startColor = color;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();

            if (renderer != null && renderer.sharedMaterial != null)
            {
                renderer.sharedMaterial.color = color;
            }
        }
    }
}
