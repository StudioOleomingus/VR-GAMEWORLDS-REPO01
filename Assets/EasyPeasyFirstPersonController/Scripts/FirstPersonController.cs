namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public partial class FirstPersonController : MonoBehaviour
    {
        [Header("Settings")]
        public float walkSpeed = 3f;
        public float sprintSpeed = 5f;
        public float crouchSpeed = 1.5f;
        public float jumpSpeed = 4f;
        public float gravity = 9.81f;
        public float slideDuration = 0.7f;
        public float slideSpeed = 6f;
        public float mouseSensitivity = 2f;
        public float strafeTiltAmount = 2f;

        [Header("Movement Polish")]
        public float groundAcceleration = 50f;
        public float groundDeceleration = 60f;
        [HideInInspector] public Vector3 currentVelocity;

        [Header("Advanced Mechanics")]
        public bool enableSmoothCrouch = true;
        public float crouchTransitionSpeed = 10f;
        public bool enableSlopeSliding = true;
        public float slideUphillFriction = 3f;
        public float slideSteerControl = 4f;

        [Header("References")]
        public Transform playerCamera;
        public Transform cameraParent;
        public Transform groundCheck;
        public LayerMask groundMask;

        [HideInInspector] public CharacterController characterController;
        [HideInInspector] public IInputManager input;
        [HideInInspector] public Vector3 moveDirection;
        [HideInInspector] public bool isGrounded;

        private PlayerBaseState currentState;
        private PlayerStateFactory states;
        private float xRotation = 0f;
        private float currentTilt;
        private float tiltVelocity;

        public PlayerBaseState CurrentState { get => currentState; set => currentState = value; }

        [Header("Visual Settings")]
        public float normalFov = 60f;
        public float sprintFov = 75f;
        public float slideFovBoost = 5f;
        public float fovChangeSpeed = 8f;
        public float bobAmount = 0.03f;
        public float bobSpeed = 12f;
        public float recoilReturnSpeed = 5f;

        [HideInInspector] public Camera cam;
        [HideInInspector] public float targetFov;
        [HideInInspector] public float currentBobIntensity;
        [HideInInspector] public float currentBobSpeed;
        [HideInInspector] public float targetTilt;

        private float bobTimer;
        private float fovVelocity;
        private float originalCamY;

        [HideInInspector] public float cameraShakeTimer;
        [HideInInspector] public float cameraShakeIntensity;

        [Header("Height Settings")]
        public float standingCameraHeight = 1.75f;
        public float crouchingCameraHeight = 1f;
        public float crouchingCharacterControllerHeight = 1f;
        [HideInInspector] public float standingCharacterControllerHeight = 1.8f;
        [HideInInspector] public Vector3 standingCharacterControllerCenter = new Vector3(0, 0.9f, 0);
        [HideInInspector] public float targetCameraY;

        [Header("Ledge Settings")]
        public LayerMask ledgeLayer;
        public float ledgeDetectionDistance = 1f;
        public float climbDuration = 0.6f;
        public float climbHeightArc = 0.4f;
        public float climbTiltAmount = -7f;

        [Header("Swimming Settings")]
        public float swimSpeed = 4f;
        public float swimSprintSpeed = 6f;
        public float waterDrag = 2f;
        public LayerMask waterMask;
        [HideInInspector] public bool isInWater;
        [HideInInspector] public float currentLedgeCooldown;

        // ==================================================================
        //  PARKOUR EXTENSION
        // ==================================================================

        [Header("Parkour - Sprint Momentum")]
        [Tooltip("Master switch for the whole parkour layer: momentum, wall running and landing impact.")]
        public bool enableParkour = true;

        [Tooltip("Top speed once momentum is fully built. Sprint Speed is the starting point.")]
        public float parkourSpeed = 9f;

        [Tooltip("Seconds of sustained sprinting needed to reach full momentum.")]
        public float momentumBuildTime = 2.5f;

        [Tooltip("Seconds for momentum to bleed back to zero after you release Shift.")]
        public float momentumDecayTime = 1f;

        [Tooltip("Field of view at full momentum. Sprint Fov is the value at zero momentum.")]
        public float parkourFov = 88f;

        [Tooltip("Wall running keeps momentum topped up instead of letting it decay.")]
        public bool wallRunSustainsMomentum = true;

        [Tooltip("Current momentum, 0 to 1. Read by the speed effects and the wall run entry check.")]
        [HideInInspector] public float momentum;

        [Header("Parkour - Wall Run")]
        [Tooltip("Which layers count as runnable walls. Leave as Nothing to fall back to Ground Mask.")]
        public LayerMask wallRunMask;

        [Tooltip("How far to the side a wall can be and still be grabbed.")]
        public float wallCheckDistance = 0.9f;

        [Tooltip("Momentum needed before walls become sticky. Stops you clinging to things while walking.")]
        [Range(0f, 1f)] public float minMomentumToWallRun = 0.35f;

        [Tooltip("Gravity while on a wall. Much lower than normal gravity, so you slide down slowly.")]
        public float wallRunGravity = 2.2f;

        [Tooltip("Seconds you can stay on a single wall before it lets go.")]
        public float wallRunMaxDuration = 2f;

        [Tooltip("Speed you travel along the wall.")]
        public float wallRunSpeed = 8.5f;

        [Tooltip("Degrees the camera leans toward the wall.")]
        public float wallRunCameraTilt = 14f;

        [Tooltip("Constant push into the wall so contact is not lost on small bumps.")]
        public float wallRunStickForce = 2f;

        [Tooltip("Seconds before the same wall can be grabbed again. Forces you to alternate walls.")]
        public float wallReattachCooldown = 0.25f;

        [Tooltip("Allow the first stick to happen while still on the ground. Off means you must jump at the wall.")]
        public bool allowWallRunFromGround = true;

        [Tooltip("Small upward pop when latching on, so a ground-started wall run lifts clear of the floor.")]
        public float wallRunEntryHop = 2.5f;

        [Tooltip("Seconds after latching before the wall run can end by touching the ground again. " +
                 "Stops a ground-started run from cancelling itself on the first frame.")]
        public float wallRunGroundGrace = 0.25f;

        [Header("Parkour - Wall Jump")]
        [Tooltip("MASTER INTENSITY for the wall-to-wall jump. Scales all three forces below at once. " +
                 "Raise it for a wilder ping-pong, lower it for something tighter and more controlled.")]
        [Range(0.1f, 3f)] public float wallJumpIntensity = 1f;

        [Tooltip("Push straight out from the wall. This is what carries you to the opposite wall.")]
        public float wallJumpSideForce = 7f;

        [Tooltip("Upward kick, so each bounce gains a little height.")]
        public float wallJumpUpForce = 5f;

        [Tooltip("Push along the wall, preserving your run direction through the jump.")]
        public float wallJumpForwardForce = 3f;

        [Header("Parkour - Landing Impact")]
        [Tooltip("The camera dives toward the ground on a hard landing, then snaps back up.")]
        public bool enableLandingImpact = true;

        [Tooltip("Downward speed (m/s) below which landings are ignored.")]
        public float landingImpactThreshold = 6f;

        [Tooltip("Downward speed (m/s) that produces the full-strength effect.")]
        public float landingImpactMaxSpeed = 18f;

        [Tooltip("Degrees the view pitches down at full strength.")]
        public float landingPitchAmount = 28f;

        [Tooltip("Metres the camera drops at full strength.")]
        public float landingDipAmount = 0.45f;

        [Tooltip("Seconds for the view to dive toward the ground. Keep this short and sharp.")]
        public float landingDiveDuration = 0.12f;

        [Tooltip("Seconds for the view to correct itself back to level.")]
        public float landingRecoverDuration = 0.45f;

        [Tooltip("Fraction of the recoil during which mouse look is ignored. 0 keeps you in control throughout.")]
        [Range(0f, 1f)] public float landingLookLockFraction = 0.55f;

        [Header("Visual Preferences")]
        public bool useFovKick = true;
        public bool useHeadBob = true;
        public bool useCameraTilt = true;
        public bool useClimbTilt = true;

        [Header("Debug")]
        public bool currentStateDebug = true;

        void OnGUI()
        {
            if (currentState != null && Application.isEditor && currentStateDebug)
                GUILayout.Label("Current State: " + currentState.GetType().Name);
        }

        private void Awake()
        {
            cam = playerCamera.GetComponent<Camera>();
            cameraLocalRestPos = playerCamera.localPosition;
            targetFov = normalFov;
            targetCameraY = standingCameraHeight;
            originalCamY = standingCameraHeight;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            characterController = GetComponent<CharacterController>();
            standingCharacterControllerHeight = characterController.height;
            standingCharacterControllerCenter = characterController.center;
            input = GetComponent<IInputManager>();
            states = new PlayerStateFactory(this);

            currentState = states.Grounded();
            currentState.EnterState();

            if (enableParkour && GetComponent<ParkourSpeedEffects>() == null)
                gameObject.AddComponent<ParkourSpeedEffects>();
        }

        private void Update()
        {
            if (currentLedgeCooldown > 0)
                currentLedgeCooldown -= Time.deltaTime;

            if (wallReattachTimer > 0)
                wallReattachTimer -= Time.deltaTime;

            // Rising edge of the jump key. The base input only reports "held", which would make
            // a wall jump fire the instant you touched a wall with Space still down.
            jumpPressed = input.jump && !jumpHeldLastFrame;
            jumpHeldLastFrame = input.jump;

            isGrounded = characterController.isGrounded || Physics.CheckSphere(groundCheck.position, characterController.radius * 0.9f, groundMask, QueryTriggerInteraction.Ignore);

            UpdateMomentum();
            UpdateLandingImpact();

            currentState.UpdateState();
            HandleRotation();
            UpdateVisuals();
        }

        private void HandleRotation()
        {
            float mouseX = input.lookInput.x * mouseSensitivity;
            float mouseY = input.lookInput.y * mouseSensitivity;

            // A hard landing briefly takes the view away from the player.
            if (IsLookLocked)
            {
                mouseX = 0f;
                mouseY = 0f;
            }

            transform.Rotate(Vector3.up * mouseX);

            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);

            float strafeTilt = useCameraTilt ? (-input.moveInput.x * strafeTiltAmount) : 0;
            float combinedTargetTilt = (useCameraTilt ? targetTilt : 0) + strafeTilt;

            currentTilt = Mathf.SmoothDamp(currentTilt, combinedTargetTilt, ref tiltVelocity, 0.1f);
            ApplyCameraOrientation();
        }

        public void UpdateVisuals()
        {
            if (!useFovKick)
            {
                targetFov = normalFov;
            }
            cam.fieldOfView = Mathf.SmoothDamp(cam.fieldOfView, targetFov, ref fovVelocity, 1f / fovChangeSpeed);

            // Smoothly track the base camera height independent of headbob
            originalCamY = Mathf.Lerp(originalCamY, targetCameraY, Time.deltaTime * 8f);

            float targetBobOffset = 0f;
            if (useHeadBob && characterController.velocity.magnitude > 0.1f && isGrounded)
            {
                bobTimer += Time.deltaTime * currentBobSpeed;
                targetBobOffset = Mathf.Sin(bobTimer) * currentBobIntensity;
            }
            else
            {
                // Smoothly reset timer to prevent snapping when starting to walk again
                bobTimer = Mathf.Lerp(bobTimer, 0, Time.deltaTime * 10f);
            }

            // Smoothly transition the actual camera Y to include the bob offset
            float desiredY = originalCamY + targetBobOffset;
            
            // Apply Camera Shake (Realistic Directional Impact)
            if (cameraShakeTimer > 0)
            {
                cameraShakeTimer -= Time.deltaTime;
                
                float normalizedTime = cameraShakeTimer / 0.4f; 
                float shakeFactor = normalizedTime * normalizedTime * normalizedTime; 
                
                // 1. Sharp dip downwards based on frontal impact
                float frontalImpact = Mathf.Abs(cameraShakeDirection.z) + 0.5f;
                float dipY = -cameraShakeIntensity * shakeFactor * frontalImpact;
                
                // 2. Sharp rotational roll towards the impact side
                float sideImpact = cameraShakeDirection.x;
                float dipTilt = (cameraShakeIntensity * 15f) * sideImpact * shakeFactor;
                
                // If it's purely a frontal crash with no side impact, add a slight random tilt
                if (Mathf.Abs(sideImpact) < 0.1f) 
                    dipTilt = (cameraShakeIntensity * 5f) * shakeFactor * (Mathf.PerlinNoise(Time.time, 0) > 0.5f ? 1 : -1);
                
                // 3. Organic rattle (much lighter now)
                float rattle = (Mathf.PerlinNoise(Time.time * 30f, 0f) - 0.5f) * (cameraShakeIntensity * 0.2f) * shakeFactor;

                desiredY += dipY + rattle;
                currentTilt += dipTilt + (rattle * 5f); 
            }

            float smoothedY = Mathf.Lerp(cameraParent.localPosition.y, desiredY, Time.deltaTime * 15f);

            cameraParent.localPosition = new Vector3(cameraParent.localPosition.x, smoothedY, cameraParent.localPosition.z);

            // The landing dip rides on the camera itself rather than cameraParent, so it stays
            // sharp instead of being smoothed away by the head-bob lerp above.
            playerCamera.localPosition = cameraLocalRestPos + Vector3.up * landingDipOffset;
        }

        [HideInInspector] public Vector3 cameraShakeDirection;
        public void TriggerCameraShake(float intensity, float duration, Vector3 direction = default)
        {
            cameraShakeIntensity = intensity;
            cameraShakeTimer = duration;
            cameraShakeDirection = direction.normalized;
        }

        public bool HasCeiling()
        {
            float radius = characterController.radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (characterController.height - radius);
            float checkDistance = standingCharacterControllerHeight - characterController.height + 0.1f;

            return Physics.SphereCast(origin, radius, Vector3.up, out _, checkDistance, groundMask, QueryTriggerInteraction.Ignore);
        }
        // ==================================================================
        //  PARKOUR RUNTIME
        // ==================================================================

        [HideInInspector] public bool jumpPressed;      // rising edge of the jump key
        private bool jumpHeldLastFrame;

        [HideInInspector] public float wallReattachTimer;
        [HideInInspector] public Collider lastWall;     // wall we most recently jumped off

        private Vector3 cameraLocalRestPos;
        private Vector3 pendingLaunch;
        private bool hasPendingLaunch;

        private float landingTimer = float.MaxValue;
        private float landingTotal;
        private float landingStrength;
        private float landingPitchOffset;
        private float landingDipOffset;

        /// <summary>True while a hard landing has taken the camera away from the player.</summary>
        public bool IsLookLocked =>
            enableLandingImpact && landingTimer < landingTotal * landingLookLockFraction;

        /// <summary>Speed the player should be moving at, given how much momentum has built up.</summary>
        public float CurrentSprintSpeed =>
            enableParkour ? Mathf.Lerp(sprintSpeed, parkourSpeed, momentum) : sprintSpeed;

        /// <summary>Air control target speed, so a fast run carries its speed through a jump.</summary>
        public float CurrentAirSpeed =>
            enableParkour && input != null && input.sprint
                ? Mathf.Lerp(walkSpeed, parkourSpeed, momentum)
                : walkSpeed;

        /// <summary>Field of view matching the current momentum.</summary>
        public float CurrentSprintFov =>
            enableParkour ? Mathf.Lerp(sprintFov, parkourFov, momentum) : sprintFov;

        /// <summary>Layers treated as runnable walls, falling back to Ground Mask if unset.</summary>
        public LayerMask EffectiveWallMask => wallRunMask.value == 0 ? groundMask : wallRunMask;

        /// <summary>
        /// Momentum builds while you hold sprint and push forward, and bleeds away when you
        /// stop. Wall running can hold it steady so a chain of jumps doesn't cost you speed.
        /// </summary>
        private void UpdateMomentum()
        {
            if (!enableParkour) { momentum = 0f; return; }

            bool onWall = currentState is PlayerWallRunState;
            bool building = (input.sprint && input.moveInput.y > 0.1f && !isInWater)
                            || (onWall && wallRunSustainsMomentum);

            float rate = building
                ? 1f / Mathf.Max(0.01f, momentumBuildTime)
                : -1f / Mathf.Max(0.01f, momentumDecayTime);

            momentum = Mathf.Clamp01(momentum + rate * Time.deltaTime);
        }

        /// <summary>
        /// Looks for a runnable wall to the left and right of the player at chest height.
        /// <paramref name="side"/> is -1 for a wall on the left, +1 for one on the right.
        /// </summary>
        public bool CheckWall(out RaycastHit hit, out int side)
        {
            hit = default;
            side = 0;
            if (!enableParkour) return false;

            Vector3 origin = transform.position + Vector3.up * (characterController.height * 0.6f);
            LayerMask mask = EffectiveWallMask;

            bool hitRight = Physics.Raycast(origin, transform.right, out RaycastHit right,
                                            wallCheckDistance, mask, QueryTriggerInteraction.Ignore);
            bool hitLeft = Physics.Raycast(origin, -transform.right, out RaycastHit left,
                                           wallCheckDistance, mask, QueryTriggerInteraction.Ignore);

            // Prefer whichever is nearer if both are in range.
            if (hitRight && (!hitLeft || right.distance <= left.distance)) { hit = right; side = 1; }
            else if (hitLeft) { hit = left; side = -1; }
            else return false;

            // Reject floors, ceilings and steep ramps: we only want near-vertical surfaces.
            if (Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) > 0.3f) return false;

            return true;
        }

        /// <summary>
        /// True if the player is currently allowed to latch onto a wall. Requires real speed,
        /// sprint held, and that this isn't the wall we just pushed off.
        /// </summary>
        public bool CanStartWallRun(out RaycastHit hit, out int side)
        {
            hit = default;
            side = 0;

            if (!enableParkour) return false;
            if (momentum < minMomentumToWallRun) return false;
            if (!input.sprint) return false;
            if (isGrounded && !allowWallRunFromGround) return false;
            if (isInWater) return false;

            if (!CheckWall(out hit, out side)) return false;

            // Don't immediately re-grab the wall we just left.
            if (wallReattachTimer > 0f && hit.collider == lastWall) return false;

            // Must be travelling along the wall, not charging straight into it. Without this
            // you'd stick to any surface you happened to run face-first at.
            if (Mathf.Abs(Vector3.Dot(transform.forward, hit.normal)) > 0.75f) return false;

            return true;
        }

        /// <summary>
        /// Queues a velocity for the next Jumping state, so a wall jump can override the
        /// standard straight-up jump. Consumed once, on the next EnterState.
        /// </summary>
        public void QueueLaunch(Vector3 velocity)
        {
            pendingLaunch = velocity;
            hasPendingLaunch = true;
        }

        public bool ConsumeLaunch(out Vector3 velocity)
        {
            velocity = pendingLaunch;
            bool had = hasPendingLaunch;
            hasPendingLaunch = false;
            return had;
        }

        /// <summary>
        /// Called as the player touches down. A fast enough impact throws the camera at the
        /// ground and then hauls it back up, with look control suspended for part of it.
        /// </summary>
        public void ReportLanding(float verticalSpeed)
        {
            if (!enableLandingImpact) return;

            float impactSpeed = -verticalSpeed;          // downward is negative, flip it
            if (impactSpeed < landingImpactThreshold) return;

            landingStrength = Mathf.Clamp01(
                Mathf.InverseLerp(landingImpactThreshold, landingImpactMaxSpeed, impactSpeed));

            landingTimer = 0f;
            landingTotal = landingDiveDuration + landingRecoverDuration;
        }

        private void UpdateLandingImpact()
        {
            if (landingTimer >= landingTotal)
            {
                landingPitchOffset = 0f;
                landingDipOffset = 0f;
                return;
            }

            landingTimer += Time.deltaTime;

            float t;
            if (landingTimer <= landingDiveDuration)
            {
                // Dive: fast out-ease so the hit lands immediately.
                float d = Mathf.Clamp01(landingTimer / Mathf.Max(0.001f, landingDiveDuration));
                t = Mathf.Sin(d * Mathf.PI * 0.5f);
            }
            else
            {
                // Recover: ease back to level, overshooting slightly past zero for a bit of snap.
                float r = Mathf.Clamp01((landingTimer - landingDiveDuration) / Mathf.Max(0.001f, landingRecoverDuration));
                t = Mathf.Cos(r * Mathf.PI * 0.5f) - 0.12f * Mathf.Sin(r * Mathf.PI);
            }

            landingPitchOffset = landingPitchAmount * landingStrength * t;
            landingDipOffset = -landingDipAmount * landingStrength * t;
        }

        /// <summary>Camera pitch in degrees. Negative looks up, positive looks down.</summary>
        public float CameraPitch
        {
            get => xRotation;
            set => xRotation = Mathf.Clamp(value, -90f, 90f);
        }

        /// <summary>Camera roll (the strafe / wall-run lean) in degrees.</summary>
        public float CameraRoll
        {
            get => currentTilt;
            set
            {
                currentTilt = value;
                tiltVelocity = 0f;   // reset the SmoothDamp so it resumes from rest
            }
        }

        /// <summary>
        /// Writes the current pitch, roll and landing offset onto the camera. Public so the
        /// interaction system can drive the view while this controller is disabled.
        /// </summary>
        public void ApplyCameraOrientation()
        {
            if (playerCamera != null)
                playerCamera.localRotation = Quaternion.Euler(xRotation + landingPitchOffset, 0f, currentTilt);
        }

        public bool CheckLedge(out Vector3 climbPosition)
        {
            climbPosition = Vector3.zero;
            if (currentLedgeCooldown > 0) return false;

            RaycastHit wallHit;
            Vector3 wallOrigin = transform.position + Vector3.up * 1.5f;

            if (Physics.Raycast(wallOrigin, transform.forward, out wallHit, ledgeDetectionDistance, ledgeLayer, QueryTriggerInteraction.Ignore))
            {
                Vector3 ledgeOrigin = wallOrigin + Vector3.up * 0.6f + transform.forward * 0.2f;
                RaycastHit ledgeHit;

                if (!Physics.Raycast(ledgeOrigin, transform.forward, 0.5f, groundMask))
                {
                    if (Physics.Raycast(ledgeOrigin + transform.forward * 0.4f, Vector3.down, out ledgeHit, 1f, groundMask))
                    {
                        climbPosition = ledgeHit.point + Vector3.up * 1f;
                        return true;
                    }
                }
            }
            return false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (((1 << other.gameObject.layer) & waterMask) != 0)
            {
                isInWater = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (((1 << other.gameObject.layer) & waterMask) != 0)
            {
                isInWater = false;
            }
        }

    }
}