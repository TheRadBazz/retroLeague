using UnityEngine;

using UnityEngine.InputSystem;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(Camera))]
    public sealed class RetrowaveCameraRig : MonoBehaviour
    {
        private const float BaseFieldOfView = 72f;
        private enum FollowMode
        {
            Overview = 0,
            ControlledPlayer = 1,
            WarmupSpectator = 2,
            Podium = 3,
        }

        private const float DefaultOrbitPitch = 14f;
        private const float OrbitDistance = 9.5f;
        private const float OrbitHeight = 1.65f;
        private const float MouseOrbitSensitivity = 0.16f;
        private const float StickOrbitSensitivity = 120f;
        private const float OrbitRecentreSpeed = 1.8f;
        private const float MinOrbitPitch = -8f;
        private const float MaxOrbitPitch = 68f;
        private const float CameraCollisionProbeRadius = 0.34f;
        private const float CameraCollisionPadding = 0.22f;
        private const float MinOrbitDistance = 2.2f;
        private const float OrbitClampInTime = 0.035f;
        private const float OrbitClampOutTime = 0.12f;

        private static RetrowaveCameraRig _instance;

        private Camera _camera;
        private RetrowavePlayerController _target;
        private FollowMode _followMode;
        private Vector3 _velocity;
        private Vector3 _lastTargetPosition;
        private Vector3 _smoothedTargetPosition;
        private Vector3 _targetPositionVelocity;
        private Vector3 _estimatedVelocity;
        private Vector3 _smoothedBodyVelocity;
        private bool _hasSmoothedTargetPosition;
        private float _orbitYaw;
        private float _orbitPitch = DefaultOrbitPitch;
        private float _currentOrbitDistance = OrbitDistance;
        private float _orbitDistanceVelocity;

        public static void EnsureCamera()
        {
            if (_instance != null)
            {
                return;
            }

            var camera = Camera.main;

            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            _instance = camera.GetComponent<RetrowaveCameraRig>();

            if (_instance == null)
            {
                _instance = camera.gameObject.AddComponent<RetrowaveCameraRig>();
            }
        }

        public static void AttachTo(RetrowavePlayerController target)
        {
            EnsureCamera();
            _instance.AttachInternal(target, FollowMode.ControlledPlayer);
        }

        public static void AttachWarmupSpectatorTarget(RetrowavePlayerController target)
        {
            EnsureCamera();
            _instance.AttachInternal(target, FollowMode.WarmupSpectator);
        }

        public static bool IsFollowing(RetrowavePlayerController target)
        {
            EnsureCamera();
            return target != null
                   && _instance._target == target
                   && (_instance._followMode == FollowMode.ControlledPlayer || _instance._followMode == FollowMode.WarmupSpectator);
        }

        public static void CycleWarmupSpectatorTarget(int direction, System.Collections.Generic.IReadOnlyList<RetrowavePlayerController> candidates)
        {
            EnsureCamera();

            if (candidates == null || candidates.Count == 0)
            {
                ShowOverview();
                return;
            }

            var currentIndex = -1;

            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == _instance._target)
                {
                    currentIndex = i;
                    break;
                }
            }

            var nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + direction + candidates.Count) % candidates.Count;
            _instance.AttachInternal(candidates[nextIndex], FollowMode.WarmupSpectator);
        }

        public static string GetSpectatorCameraLabel()
        {
            EnsureCamera();

            if (_instance._followMode == FollowMode.Podium)
            {
                return "Camera: winners podium";
            }

            if (_instance._followMode == FollowMode.WarmupSpectator && _instance._target != null)
            {
                return $"Camera: warmup follow on Player {_instance._target.OwnerClientId}";
            }

            return "Camera: spectator overview";
        }

        public static void ShowPodium()
        {
            EnsureCamera();
            _instance._target = null;
            _instance._followMode = FollowMode.Podium;
            _instance._velocity = Vector3.zero;
            _instance._estimatedVelocity = Vector3.zero;
            _instance._smoothedBodyVelocity = Vector3.zero;
            _instance._targetPositionVelocity = Vector3.zero;
            _instance._hasSmoothedTargetPosition = false;
            _instance.ApplyViewSettings();
            _instance.UpdatePodiumView(instant: true);
            _instance.UpdateCursorState(false);
        }

        private void AttachInternal(RetrowavePlayerController target, FollowMode followMode)
        {
            _target = target;
            _followMode = followMode;
            _lastTargetPosition = target.transform.position;
            _smoothedTargetPosition = _lastTargetPosition;
            _estimatedVelocity = Vector3.zero;
            _smoothedBodyVelocity = Vector3.zero;
            _targetPositionVelocity = Vector3.zero;
            _hasSmoothedTargetPosition = true;
            _orbitYaw = 0f;
            _orbitPitch = DefaultOrbitPitch;
            _currentOrbitDistance = OrbitDistance;
            _orbitDistanceVelocity = 0f;
        }

        public static void ShowOverview()
        {
            EnsureCamera();
            _instance._target = null;
            _instance._followMode = FollowMode.Overview;
            _instance._velocity = Vector3.zero;
            _instance._estimatedVelocity = Vector3.zero;
            _instance._smoothedBodyVelocity = Vector3.zero;
            _instance._targetPositionVelocity = Vector3.zero;
            _instance._hasSmoothedTargetPosition = false;
            _instance.ApplyViewSettings();
            var overviewPosition = ResolveOverviewPosition();
            var overviewLookPoint = ResolveOverviewLookPoint();
            _instance.transform.position = overviewPosition;
            _instance.transform.rotation = Quaternion.LookRotation(overviewLookPoint - overviewPosition, Vector3.up);
            _instance._currentOrbitDistance = OrbitDistance;
            _instance._orbitDistanceVelocity = 0f;
            _instance.UpdateCursorState(false);
        }

        private void Awake()
        {
            _instance = this;
            _camera = GetComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.backgroundColor = new Color(0.03f, 0.01f, 0.08f);
            _camera.fieldOfView = BaseFieldOfView;
            ShowOverview();
        }

        private void LateUpdate()
        {
            ApplyViewSettings();

            if (_target == null)
            {
                if (_followMode == FollowMode.Podium)
                {
                    UpdatePodiumView(instant: false);
                    UpdateCursorState(false);
                    return;
                }

                UpdateCursorState(false);
                return;
            }

            if (_followMode == FollowMode.WarmupSpectator && !_target.IsArenaParticipant)
            {
                ShowOverview();
                return;
            }

            var targetTransform = _target.transform;
            var targetPosition = ResolveTargetPosition(targetTransform);
            var frameVelocity = (targetPosition - _lastTargetPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
            _estimatedVelocity = Vector3.Lerp(_estimatedVelocity, frameVelocity, Time.deltaTime * 10f);
            _lastTargetPosition = targetPosition;

            var currentVelocity = _target.CurrentVelocity;
            var targetBodyVelocity = currentVelocity.sqrMagnitude > 0.1f
                ? currentVelocity
                : _estimatedVelocity;
            var bodyVelocityBlendRate = _target.IsOwner && !_target.IsServer ? 5f : 9f;
            var bodyVelocityBlend = 1f - Mathf.Exp(-Time.deltaTime * bodyVelocityBlendRate);
            _smoothedBodyVelocity = Vector3.Lerp(_smoothedBodyVelocity, targetBodyVelocity, bodyVelocityBlend);
            var bodyVelocity = _smoothedBodyVelocity;
            var blendedUp = Vector3.Slerp(Vector3.up, targetTransform.up, 0.35f);
            var baseRotation = Quaternion.LookRotation(targetTransform.forward, blendedUp);
            UpdateOrbit(targetTransform, blendedUp, bodyVelocity);

            var yawRotation = Quaternion.AngleAxis(_orbitYaw, blendedUp);
            var orbitBasis = yawRotation * baseRotation;
            var pitchAxis = orbitBasis * Vector3.right;
            var orbitRotation = Quaternion.AngleAxis(_orbitPitch, pitchAxis) * orbitBasis;
            var orbitForward = orbitRotation * Vector3.forward;
            var focusPoint = targetPosition
                             + blendedUp * OrbitHeight
                             + bodyVelocity * 0.15f;
            var requestedDistance = OrbitDistance + Mathf.Clamp(bodyVelocity.magnitude * 0.04f, 0f, 1.8f);
            var clampedDistance = ResolveCameraDistance(focusPoint, orbitForward, requestedDistance);
            var springTime = clampedDistance < _currentOrbitDistance ? OrbitClampInTime : OrbitClampOutTime;
            _currentOrbitDistance = Mathf.SmoothDamp(
                _currentOrbitDistance,
                clampedDistance,
                ref _orbitDistanceVelocity,
                springTime);
            var desiredPosition = focusPoint - orbitForward * _currentOrbitDistance;

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _velocity, 0.1f);

            var lookPoint = targetPosition
                            + orbitForward * 4.5f
                            + blendedUp * 1.15f
                            + bodyVelocity * 0.08f;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(lookPoint - transform.position, blendedUp),
                Time.deltaTime * 8f);

            var targetFov = BaseFieldOfView + Mathf.Clamp(bodyVelocity.magnitude * 0.8f, 0f, 18f);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, Time.deltaTime * 5f);
        }

        private Vector3 ResolveTargetPosition(Transform targetTransform)
        {
            var rawPosition = targetTransform.position;

            if (!_hasSmoothedTargetPosition || (rawPosition - _smoothedTargetPosition).sqrMagnitude > 64f)
            {
                _smoothedTargetPosition = rawPosition;
                _targetPositionVelocity = Vector3.zero;
                _hasSmoothedTargetPosition = true;
                return rawPosition;
            }

            if (_target == null || !_target.IsOwner || _target.IsServer)
            {
                _smoothedTargetPosition = rawPosition;
                _targetPositionVelocity = Vector3.zero;
                return rawPosition;
            }

            _smoothedTargetPosition = Vector3.SmoothDamp(
                _smoothedTargetPosition,
                rawPosition,
                ref _targetPositionVelocity,
                0.055f,
                float.PositiveInfinity,
                Time.deltaTime);

            return _smoothedTargetPosition;
        }

        private void UpdatePodiumView(bool instant)
        {
            var lookPoint = RetrowavePodiumLayout.CameraLookPoint;
            var basePosition = RetrowavePodiumLayout.CameraPosition;
            var orbit = Time.unscaledTime * 0.22f;
            var desiredPosition = basePosition + new Vector3(Mathf.Sin(orbit) * 2.8f, Mathf.Sin(orbit * 0.7f) * 0.45f, Mathf.Cos(orbit) * 1.4f);
            var desiredRotation = Quaternion.LookRotation(lookPoint - desiredPosition, Vector3.up);

            if (instant)
            {
                transform.SetPositionAndRotation(desiredPosition, desiredRotation);
                _camera.fieldOfView = 56f;
                return;
            }

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _velocity, 0.28f);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, Time.unscaledDeltaTime * 3.5f);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, 56f, Time.unscaledDeltaTime * 2.8f);
        }

        private void UpdateOrbit(Transform targetTransform, Vector3 blendedUp, Vector3 bodyVelocity)
        {
            var gameplayBlocked = RetrowaveGameBootstrap.IsGameplayInputBlocked();
            UpdateCursorState(!gameplayBlocked);

            var mouse = Mouse.current;
            var gamepad = Gamepad.current;
            var orbitInput = Vector2.zero;
            var invertLookMultiplier = RetrowaveGameSettings.InvertLook ? -1f : 1f;

            if (!gameplayBlocked)
            {
                if (mouse != null)
                {
                    var mouseDelta = mouse.delta.ReadValue();
                    orbitInput += new Vector2(
                        mouseDelta.x * MouseOrbitSensitivity * RetrowaveGameSettings.MouseSensitivityXMultiplier,
                        mouseDelta.y * MouseOrbitSensitivity * RetrowaveGameSettings.MouseSensitivityYMultiplier * invertLookMultiplier);
                }

                if (gamepad != null)
                {
                    var stickInput = gamepad.rightStick.ReadValue();
                    orbitInput += new Vector2(
                        stickInput.x * (StickOrbitSensitivity * RetrowaveGameSettings.StickSensitivityXMultiplier * Time.deltaTime),
                        stickInput.y * (StickOrbitSensitivity * RetrowaveGameSettings.StickSensitivityYMultiplier * Time.deltaTime * invertLookMultiplier));
                }
            }

            if (orbitInput.sqrMagnitude > 0.0001f)
            {
                _orbitYaw += orbitInput.x;
                _orbitPitch = Mathf.Clamp(_orbitPitch - orbitInput.y, MinOrbitPitch, MaxOrbitPitch);
                return;
            }

            if (Vector3.ProjectOnPlane(bodyVelocity, blendedUp).sqrMagnitude < 9f)
            {
                return;
            }

            _orbitYaw = Mathf.Lerp(_orbitYaw, 0f, Time.deltaTime * OrbitRecentreSpeed);
            _orbitPitch = Mathf.Lerp(_orbitPitch, DefaultOrbitPitch, Time.deltaTime * OrbitRecentreSpeed);
        }

        private static float ResolveCameraDistance(Vector3 focusPoint, Vector3 orbitForward, float requestedDistance)
        {
            if (Physics.SphereCast(
                    focusPoint,
                    CameraCollisionProbeRadius,
                    -orbitForward,
                    out var hit,
                    requestedDistance + CameraCollisionPadding,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return Mathf.Clamp(hit.distance - CameraCollisionPadding, MinOrbitDistance, requestedDistance);
            }

            return requestedDistance;
        }

        private void UpdateCursorState(bool shouldLock)
        {
            Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !shouldLock;
        }

        private void ApplyViewSettings()
        {
            if (_camera == null)
            {
                return;
            }

            var layout = RetrowaveArenaConfig.CurrentLayout;
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.farClipPlane = Mathf.Max(400f, layout.OuterHalfLength * 4.2f);
        }

        private static Vector3 ResolveOverviewPosition()
        {
            var layout = RetrowaveArenaConfig.CurrentLayout;
            var overviewHeight = Mathf.Max(24f, layout.CeilingHeight * 0.7f);
            var overviewDepth = -Mathf.Max(56f, layout.OuterHalfLength * 0.82f);
            return new Vector3(0f, overviewHeight, overviewDepth);
        }

        private static Vector3 ResolveOverviewLookPoint()
        {
            var layout = RetrowaveArenaConfig.CurrentLayout;
            return new Vector3(0f, Mathf.Max(5f, layout.CeilingHeight * 0.14f), 0f);
        }
    }
}
