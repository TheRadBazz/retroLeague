using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [DisallowMultipleComponent]
    public sealed class VehicleOverdriveSystem : NetworkBehaviour
    {
        private const float MaxHeat = 100f;
        private const float OverheatClearHeat = 42f;

        [SerializeField] private float _boostHeatPerSecond = 22f;
        [SerializeField] private float _glideHeatPerSecond = 9f;
        [SerializeField] private float _jumpHeat = 7f;
        [SerializeField] private float _coolHeatPerSecond = 6f;
        [SerializeField] private float _groundCoolingBonusPerSecond = 1.2f;
        [SerializeField] private float _highHeatThreshold = 0.68f;

        private readonly NetworkVariable<float> _heat = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _overheated = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _overcharged = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private float _temporaryOverchargeUntil;

        public float Heat => Mathf.Clamp(_heat.Value, 0f, MaxHeat);
        public float HeatNormalized => Mathf.Clamp01(Heat / MaxHeat);
        public bool IsOverheated => _overheated.Value;
        public bool IsOvercharged => _overcharged.Value;
        public bool CanBoost => !IsOverheated;

        public float MaxSpeedMultiplier
        {
            get
            {
                var bonus = Mathf.Lerp(1f, 1.045f, HeatNormalized);
                return IsOverheated ? 0.92f : (IsOvercharged ? bonus + 0.045f : bonus);
            }
        }

        public float BoostForceMultiplier => IsOvercharged ? 1.08f : Mathf.Lerp(1f, 1.04f, HeatNormalized);
        public float BoostDrainMultiplier => Mathf.Lerp(1f, 1.18f, Mathf.InverseLerp(_highHeatThreshold, 1f, HeatNormalized));
        public float GroundGripMultiplier => Mathf.Lerp(1f, 0.82f, Mathf.InverseLerp(_highHeatThreshold, 1f, HeatNormalized));
        public float RechargeMultiplier => IsOverheated ? 0.55f : Mathf.Lerp(1.05f, 0.76f, HeatNormalized);

        public void TickServer(bool isBoosting, bool isGliding, bool treatedAsGrounded)
        {
            if (!IsServer)
            {
                return;
            }

            var heatDelta = 0f;

            if (isBoosting)
            {
                heatDelta += _boostHeatPerSecond * Time.fixedDeltaTime;
            }

            if (isGliding)
            {
                heatDelta += _glideHeatPerSecond * Time.fixedDeltaTime;
            }

            if (!isBoosting && !isGliding)
            {
                heatDelta -= _coolHeatPerSecond * Time.fixedDeltaTime;
            }

            if (treatedAsGrounded)
            {
                heatDelta -= _groundCoolingBonusPerSecond * Time.fixedDeltaTime;
            }

            if (_overcharged.Value && Time.time >= _temporaryOverchargeUntil)
            {
                _overcharged.Value = false;
                _temporaryOverchargeUntil = 0f;
            }

            SetHeatServer(_heat.Value + heatDelta);
        }

        public void RegisterJumpServer()
        {
            if (IsServer)
            {
                SetHeatServer(_heat.Value + _jumpHeat);
            }
        }

        public void ApplyOverchargeServer(float durationSeconds, float coolingAmount)
        {
            if (!IsServer)
            {
                return;
            }

            _temporaryOverchargeUntil = Mathf.Max(_temporaryOverchargeUntil, Time.time + Mathf.Max(0f, durationSeconds));
            _overcharged.Value = _temporaryOverchargeUntil > Time.time;
            SetHeatServer(_heat.Value - Mathf.Max(0f, coolingAmount));
        }

        public void ClearServer()
        {
            if (!IsServer)
            {
                return;
            }

            _temporaryOverchargeUntil = 0f;
            _heat.Value = 0f;
            _overheated.Value = false;
            _overcharged.Value = false;
        }

        private void SetHeatServer(float heat)
        {
            var clampedHeat = Mathf.Clamp(heat, 0f, MaxHeat);
            _heat.Value = clampedHeat;

            if (!_overheated.Value && clampedHeat >= MaxHeat - 0.01f)
            {
                _overheated.Value = true;
            }
            else if (_overheated.Value && clampedHeat <= OverheatClearHeat)
            {
                _overheated.Value = false;
            }
        }
    }
}
