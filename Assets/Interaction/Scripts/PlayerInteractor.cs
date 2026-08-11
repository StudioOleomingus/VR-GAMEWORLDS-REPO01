using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace InteractionSystem
{
    /// <summary>
    /// Look at an <see cref="Interactable"/> to get a world-space popup.
    /// Step close and press E to gently lift it into an inspect pose in front of the camera,
    /// with the background defocused. Move the mouse to turn it. Press E again to set it back down.
    ///
    /// Drop this on the same GameObject as your first person controller.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInteractor : MonoBehaviour
    {
        enum Phase { Idle, PickingUp, Holding, PuttingDown }

        [Header("References")]
        [Tooltip("Leave empty to use the child Camera / Camera.main.")]
        public Camera playerCamera;

        [Tooltip("Components switched off while inspecting, so the player cannot walk or look around. " +
                 "Auto-filled with the first person controller on this object if left empty.")]
        public Behaviour[] disableWhileInspecting;

        [Header("Detection")]
        [Tooltip("How far away the popup starts appearing.")]
        public float focusRange = 6f;

        [Tooltip("How close you must be before E will pick the object up.")]
        public float pickupRange = 2.5f;

        [Tooltip("Forgiveness radius on the look ray, so you don't need pixel-perfect aim.")]
        public float aimAssistRadius = 0.12f;

        public LayerMask interactableMask = ~0;

        [Header("Inspect Pose")]
        [Tooltip("Seconds for the object to float up into the inspect pose.")]
        public float pickupDuration = 0.55f;

        [Tooltip("Seconds for the object to settle back where it came from.")]
        public float placeDuration = 0.5f;

        [Tooltip("Height of the gentle arc the object travels along.")]
        public float travelArcHeight = 0.12f;

        [Tooltip("Larger values hold the object further from the camera, so it fills less of the screen.")]
        [Range(1f, 4f)] public float autoFitPadding = 1.9f;

        public float minHoldDistance = 0.4f;

        [Tooltip("Clamps the auto-fit distance. Large objects will be cropped if this is too small.")]
        public float maxHoldDistance = 2.5f;

        [Header("Inspect Rotation")]
        [Tooltip("Degrees of object rotation per pixel of mouse movement.")]
        public float rotateSensitivity = 0.35f;

        [Tooltip("Higher is snappier, lower is more floaty.")]
        public float rotateDamping = 12f;

        public bool invertRotateX = false;
        public bool invertRotateY = false;

        [Header("Background Blur")]
        public bool useDepthOfField = true;

        [Tooltip("Optional. Leave empty and a volume is created at runtime.")]
        public Volume inspectVolume;

        [Tooltip("Seconds for the blur to fade in and out.")]
        public float blurBlendDuration = 0.45f;

        [Tooltip("Lower aperture = stronger background blur.")]
        [Range(1f, 32f)] public float aperture = 1.8f;

        [Tooltip("Higher focal length = stronger background blur.")]
        [Range(1f, 300f)] public float focalLength = 95f;

        [Header("Input")]
#if ENABLE_INPUT_SYSTEM
        public Key interactKey = Key.E;
#else
        public KeyCode interactKey = KeyCode.E;
#endif

        [Header("Audio (optional)")]
        public AudioSource audioSource;
        public AudioClip pickupSound;
        public AudioClip placeSound;

        // --- runtime state ---
        Phase _phase = Phase.Idle;
        Interactable _focused;
        Interactable _held;
        InteractionPromptUI _prompt;
        Transform _holdAnchor;
        DepthOfField _dof;
        Volume _runtimeVolume;

        float _tween;               // 0..1 progress of pick up / put down
        float _blur;                // 0..1 blur weight
        float _holdDistance = 0.6f;

        Vector3 _restPosition;      // world pose to return to
        Quaternion _restRotation;
        Transform _restParent;
        Vector3 _centreOffset;      // pivot -> bounds centre, in the object's own rotation frame

        Quaternion _targetSpin = Quaternion.identity;   // user-driven rotation, camera space
        Quaternion _currentSpin = Quaternion.identity;
        Quaternion _spinStart = Quaternion.identity;

        readonly List<bool> _colliderStates = new List<bool>();
        bool _bodyWasKinematic, _bodyUsedGravity;
        RigidbodyInterpolation _bodyInterpolation;

        readonly RaycastHit[] _hits = new RaycastHit[8];

        public bool IsInspecting => _phase != Phase.Idle;
        public Interactable HeldObject => _held;
        public Interactable FocusedObject => _focused;

        // ------------------------------------------------------------------

        void Awake()
        {
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null) playerCamera = Camera.main;
            if (playerCamera == null)
            {
                Debug.LogError("[PlayerInteractor] No camera found. Assign Player Camera.", this);
                enabled = false;
                return;
            }

            if (disableWhileInspecting == null || disableWhileInspecting.Length == 0)
                disableWhileInspecting = AutoFindControllers();

            _holdAnchor = new GameObject("~InspectAnchor").transform;
            _holdAnchor.SetParent(playerCamera.transform, false);
            _holdAnchor.localPosition = Vector3.zero;
            _holdAnchor.localRotation = Quaternion.identity;

            _prompt = gameObject.AddComponent<InteractionPromptUI>();
            _prompt.Initialise(playerCamera);

            if (useDepthOfField) SetUpDepthOfField();
        }

        Behaviour[] AutoFindControllers()
        {
            var found = new List<Behaviour>();
            var fpc = GetComponent<EasyPeasyFirstPersonController.FirstPersonController>();
            if (fpc != null) found.Add(fpc);
            return found.ToArray();
        }

        void SetUpDepthOfField()
        {
            // Make sure post processing is actually on for this camera.
            var camData = playerCamera.GetUniversalAdditionalCameraData();
            if (camData != null) camData.renderPostProcessing = true;

            if (inspectVolume == null)
            {
                var go = new GameObject("~InspectDoFVolume");
                go.transform.SetParent(transform, false);
                _runtimeVolume = go.AddComponent<Volume>();
                _runtimeVolume.isGlobal = true;
                _runtimeVolume.priority = 100f;

                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "InspectDoF (runtime)";
                _runtimeVolume.sharedProfile = profile;
                inspectVolume = _runtimeVolume;
            }

            var vp = inspectVolume.sharedProfile != null ? inspectVolume.sharedProfile : inspectVolume.profile;
            if (vp == null) { useDepthOfField = false; return; }

            if (!vp.TryGet<DepthOfField>(out _dof)) _dof = vp.Add<DepthOfField>(true);

            _dof.active = true;
            _dof.mode.overrideState = true;
            _dof.mode.value = DepthOfFieldMode.Bokeh;
            _dof.focusDistance.overrideState = true;
            _dof.aperture.overrideState = true;
            _dof.focalLength.overrideState = true;
            _dof.bladeCount.overrideState = true;
            _dof.bladeCount.value = 7;
            _dof.bladeCurvature.overrideState = true;
            _dof.bladeCurvature.value = 0.9f;

            inspectVolume.weight = 0f;
            if (_ownsVolume) inspectVolume.gameObject.SetActive(false);
        }

        bool _ownsVolume => _runtimeVolume != null && _runtimeVolume == inspectVolume;

        void OnDestroy()
        {
            if (_runtimeVolume != null && _runtimeVolume.sharedProfile != null)
                Destroy(_runtimeVolume.sharedProfile);
        }

        // ------------------------------------------------------------------

        void Update()
        {
            // The held object was destroyed or unloaded out from under us: recover cleanly
            // rather than leaving the player frozen forever.
            if (_phase != Phase.Idle && _held == null)
            {
                _phase = Phase.Idle;
                SetPlayerFrozen(false);
                UpdateBlur();
                return;
            }

            switch (_phase)
            {
                case Phase.Idle:
                    ScanForInteractable();
                    if (InteractPressed() && _focused != null && _focused.canPickUp && InPickupRange(_focused))
                        BeginPickup(_focused);
                    break;

                case Phase.PickingUp:
                    AdvanceTween(pickupDuration, () =>
                    {
                        _phase = Phase.Holding;
                        _currentSpin = _targetSpin;
                    });
                    ApplyPickupPose(Ease(_tween));
                    break;

                case Phase.Holding:
                    HandleSpinInput();
                    ApplyHoldPose();
                    if (InteractPressed()) BeginPlace();
                    break;

                case Phase.PuttingDown:
                    AdvanceTween(placeDuration, FinishPlace);
                    ApplyPlacePose(Ease(_tween));
                    break;
            }

            UpdateBlur();
        }

        // --- detection ----------------------------------------------------

        void ScanForInteractable()
        {
            Interactable found = FindLookedAt();

            if (found != _focused)
            {
                if (_focused != null) _focused.onUnfocused?.Invoke();
                _focused = found;
                if (_focused != null) _focused.onFocused?.Invoke();
            }

            if (_focused != null) _prompt.Show(_focused, InPickupRange(_focused));
            else _prompt.Hide();
        }

        Interactable FindLookedAt()
        {
            var origin = playerCamera.transform.position;
            var dir = playerCamera.transform.forward;

            // Precise ray first.
            if (Physics.Raycast(origin, dir, out var hit, focusRange, interactableMask, QueryTriggerInteraction.Ignore))
            {
                var direct = hit.collider.GetComponentInParent<Interactable>();
                if (direct != null && direct != _held) return direct;
            }

            // Forgiving sweep, nearest interactable wins.
            if (aimAssistRadius <= 0f) return null;

            int count = Physics.SphereCastNonAlloc(origin, aimAssistRadius, dir, _hits, focusRange,
                                                   interactableMask, QueryTriggerInteraction.Ignore);
            Interactable best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var candidate = _hits[i].collider.GetComponentInParent<Interactable>();
                if (candidate == null || candidate == _held) continue;

                float d = Vector3.Distance(origin, candidate.transform.position);
                if (d < bestDist) { bestDist = d; best = candidate; }
            }
            return best;
        }

        bool InPickupRange(Interactable target)
        {
            if (target == null) return false;
            target.TryGetWorldBounds(out var b);
            return b.SqrDistance(playerCamera.transform.position) <= pickupRange * pickupRange;
        }

        // --- pick up ------------------------------------------------------

        void BeginPickup(Interactable target)
        {
            _held = target;
            if (_focused != null) _focused.onUnfocused?.Invoke();
            _focused = null;
            _prompt.Hide();

            _restParent = target.transform.parent;
            _restPosition = target.transform.position;
            _restRotation = target.transform.rotation;

            // Freeze physics so it doesn't fight the tween or shove the player.
            var rb = target.Body;
            if (rb != null)
            {
                _bodyWasKinematic = rb.isKinematic;
                _bodyUsedGravity = rb.useGravity;
                _bodyInterpolation = rb.interpolation;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.None;
            }

            _colliderStates.Clear();
            var cols = target.Colliders;
            for (int i = 0; i < cols.Length; i++)
            {
                _colliderStates.Add(cols[i] != null && cols[i].enabled);
                if (cols[i] != null) cols[i].enabled = false;
            }

            _holdDistance = ComputeHoldDistance(target);

            target.TryGetWorldBounds(out var b);
            _centreOffset = Quaternion.Inverse(_restRotation) * (b.center - _restPosition);

            // Start the spin from the object's current rotation expressed in camera space,
            // so the lift looks continuous rather than snapping to a canonical pose.
            _spinStart = Quaternion.Inverse(playerCamera.transform.rotation) * _restRotation;
            _targetSpin = Quaternion.Euler(target.inspectRotationOffset);
            _currentSpin = _spinStart;

            SetPlayerFrozen(true);
            target.transform.SetParent(null, true);

            _tween = 0f;
            _phase = Phase.PickingUp;

            target.onPickedUp?.Invoke();
            PlayClip(pickupSound);
        }

        float ComputeHoldDistance(Interactable target)
        {
            if (target.inspectDistanceOverride > 0f)
                return target.inspectDistanceOverride;

            float radius = target.GetBoundingRadius();
            float halfFovRad = playerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float d = radius / Mathf.Max(0.01f, Mathf.Tan(halfFovRad)) * autoFitPadding * target.inspectFitMultiplier;

            float floor = Mathf.Max(minHoldDistance, playerCamera.nearClipPlane + radius + 0.05f);
            return Mathf.Clamp(d, floor, Mathf.Max(floor, maxHoldDistance));
        }

        void ApplyPickupPose(float t)
        {
            if (_held == null) return;

            GetInspectPose(out var endPos, out var endRot, Quaternion.Slerp(_spinStart, _targetSpin, t));

            Vector3 pos = Vector3.Lerp(_restPosition, endPos, t);
            pos += Vector3.up * (Mathf.Sin(t * Mathf.PI) * travelArcHeight);

            _held.transform.SetPositionAndRotation(pos, Quaternion.Slerp(_restRotation, endRot, t));
        }

        // --- holding ------------------------------------------------------

        void HandleSpinInput()
        {
            Vector2 delta = PointerDelta();
            if (delta.sqrMagnitude > 0f)
            {
                float x = delta.x * (invertRotateX ? 1f : -1f) * rotateSensitivity;
                float y = delta.y * (invertRotateY ? -1f : 1f) * rotateSensitivity;

                // Trackball rotation in camera space: yaw about up, pitch about right.
                _targetSpin = Quaternion.AngleAxis(x, Vector3.up)
                            * Quaternion.AngleAxis(y, Vector3.right)
                            * _targetSpin;
            }

            float k = 1f - Mathf.Exp(-rotateDamping * Time.deltaTime);
            _currentSpin = Quaternion.Slerp(_currentSpin, _targetSpin, k);
        }

        void ApplyHoldPose()
        {
            if (_held == null) return;
            GetInspectPose(out var pos, out var rot, _currentSpin);
            _held.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Inspect pose for the given camera-space spin, centred on the object's bounds.</summary>
        void GetInspectPose(out Vector3 pos, out Quaternion rot, Quaternion spin)
        {
            var camT = playerCamera.transform;
            rot = camT.rotation * spin;

            Vector3 anchor = camT.position + camT.forward * _holdDistance;

            // Spin around the object's visual centre rather than its pivot, so off-centre
            // pivots don't make the object swing around wildly.
            pos = anchor - rot * _centreOffset;
        }

        // --- put down -----------------------------------------------------

        void BeginPlace()
        {
            _spinStart = _currentSpin;
            _tween = 0f;
            _phase = Phase.PuttingDown;
            PlayClip(placeSound);
        }

        void ApplyPlacePose(float t)
        {
            if (_held == null) return;

            GetInspectPose(out var startPos, out var startRot, _spinStart);

            Vector3 pos = Vector3.Lerp(startPos, _restPosition, t);
            pos += Vector3.up * (Mathf.Sin(t * Mathf.PI) * travelArcHeight);

            _held.transform.SetPositionAndRotation(pos, Quaternion.Slerp(startRot, _restRotation, t));
        }

        void FinishPlace()
        {
            var target = _held;
            if (target != null)
            {
                target.transform.SetPositionAndRotation(_restPosition, _restRotation);
                if (_restParent != null) target.transform.SetParent(_restParent, true);

                var cols = target.Colliders;
                for (int i = 0; i < cols.Length && i < _colliderStates.Count; i++)
                    if (cols[i] != null) cols[i].enabled = _colliderStates[i];

                var rb = target.Body;
                if (rb != null)
                {
                    rb.isKinematic = _bodyWasKinematic;
                    rb.useGravity = _bodyUsedGravity;
                    rb.interpolation = _bodyInterpolation;
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }

                target.onPlacedDown?.Invoke();
            }

            _held = null;
            _phase = Phase.Idle;
            SetPlayerFrozen(false);
        }

        /// <summary>Force-drops whatever is held, e.g. before loading a new scene.</summary>
        public void CancelInspect()
        {
            if (_phase == Phase.Idle) return;
            _tween = 1f;
            FinishPlace();
            _blur = 0f;
        }

        // --- shared -------------------------------------------------------

        void AdvanceTween(float duration, System.Action onDone)
        {
            _tween += Time.deltaTime / Mathf.Max(0.01f, duration);
            if (_tween >= 1f)
            {
                _tween = 1f;
                onDone?.Invoke();
            }
        }

        static float Ease(float t) => t * t * t * (t * (t * 6f - 15f) + 10f); // smootherstep

        void SetPlayerFrozen(bool frozen)
        {
            if (disableWhileInspecting == null) return;
            for (int i = 0; i < disableWhileInspecting.Length; i++)
            {
                var b = disableWhileInspecting[i];
                if (b == null) continue;
                b.enabled = !frozen;

                if (!frozen && b is EasyPeasyFirstPersonController.FirstPersonController fpc)
                    fpc.currentVelocity = Vector3.zero;
            }
        }

        void UpdateBlur()
        {
            if (!useDepthOfField || inspectVolume == null || _dof == null) return;

            float target = _phase == Phase.PickingUp || _phase == Phase.Holding ? 1f : 0f;
            _blur = Mathf.MoveTowards(_blur, target, Time.deltaTime / Mathf.Max(0.01f, blurBlendDuration));

            bool active = _blur > 0.001f;
            if (_ownsVolume && inspectVolume.gameObject.activeSelf != active)
                inspectVolume.gameObject.SetActive(active);
            if (!active) { inspectVolume.weight = 0f; return; }

            inspectVolume.weight = Mathf.SmoothStep(0f, 1f, _blur);
            _dof.focusDistance.value = _holdDistance;
            _dof.aperture.value = aperture;
            _dof.focalLength.value = focalLength;
        }

        void PlayClip(AudioClip clip)
        {
            if (clip == null) return;
            if (audioSource != null) audioSource.PlayOneShot(clip);
            else AudioSource.PlayClipAtPoint(clip, playerCamera.transform.position);
        }

        // --- input --------------------------------------------------------

        bool InteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null || interactKey == Key.None) return false;
            var control = kb[interactKey];
            return control != null && control.wasPressedThisFrame;
#else
            return Input.GetKeyDown(interactKey);
#endif
        }

        Vector2 PointerDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
#endif
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var cam = playerCamera != null ? playerCamera : GetComponentInChildren<Camera>();
            if (cam == null) return;

            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.5f);
            Gizmos.DrawRay(cam.transform.position, cam.transform.forward * focusRange);
            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.8f);
            Gizmos.DrawRay(cam.transform.position, cam.transform.forward * pickupRange);
        }
#endif
    }
}
