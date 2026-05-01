using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [DisallowMultipleComponent]
    public sealed class VehicleStyleMeter : NetworkBehaviour
    {
        private const float MaxStyle = 100f;

        [SerializeField] private float _decayPerSecond = 1.15f;
        [SerializeField] private float _notificationThreshold = 2.5f;

        private readonly NetworkVariable<float> _style = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _lastAwardEventValue = new(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _lastAwardPoints = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _lastAwardSerial = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public float Style => Mathf.Clamp(_style.Value, 0f, MaxStyle);
        public float StyleNormalized => Mathf.Clamp01(Style / MaxStyle);
        public float CaptureSpeedMultiplier => Mathf.Lerp(1f, 1.22f, StyleNormalized);
        public float BoostEfficiencyMultiplier => Mathf.Lerp(1f, 0.92f, StyleNormalized);
        public int LastAwardSerial => _lastAwardSerial.Value;
        public float LastAwardPoints => Mathf.Max(0f, _lastAwardPoints.Value);
        public RetrowaveStyleEvent LastAwardEvent => _lastAwardEventValue.Value >= 0
            ? (RetrowaveStyleEvent)_lastAwardEventValue.Value
            : RetrowaveStyleEvent.ControlledTouch;

        private void FixedUpdate()
        {
            if (!IsServer || _style.Value <= 0f)
            {
                return;
            }

            _style.Value = Mathf.Max(0f, _style.Value - _decayPerSecond * Time.fixedDeltaTime);
        }

        public void AwardServer(RetrowaveStyleEvent styleEvent, float multiplier = 1f)
        {
            if (!IsServer)
            {
                return;
            }

            var points = GetBaseAward(styleEvent) * Mathf.Max(0f, multiplier);

            if (points <= 0f)
            {
                return;
            }

            _style.Value = Mathf.Clamp(_style.Value + points, 0f, MaxStyle);

            if (points >= _notificationThreshold)
            {
                _lastAwardEventValue.Value = (int)styleEvent;
                _lastAwardPoints.Value = points;
                _lastAwardSerial.Value++;
            }
        }

        public void ClearServer()
        {
            if (IsServer)
            {
                _style.Value = 0f;
                _lastAwardEventValue.Value = -1;
                _lastAwardPoints.Value = 0f;
                _lastAwardSerial.Value = 0;
            }
        }

        private static float GetBaseAward(RetrowaveStyleEvent styleEvent)
        {
            return styleEvent switch
            {
                RetrowaveStyleEvent.AerialTouch => 9f,
                RetrowaveStyleEvent.WallRide => 2.4f,
                RetrowaveStyleEvent.Drift => 1.6f,
                RetrowaveStyleEvent.TeamCombo => 13f,
                RetrowaveStyleEvent.Pass => 8f,
                RetrowaveStyleEvent.ObjectiveCapture => 12f,
                RetrowaveStyleEvent.PowerPlay => 10f,
                RetrowaveStyleEvent.ObjectiveHold => 1.1f,
                RetrowaveStyleEvent.AerialManeuver => 4.5f,
                RetrowaveStyleEvent.FlipTrick => 7f,
                RetrowaveStyleEvent.CleanLanding => 8.5f,
                _ => 4f,
            };
        }
    }
}
