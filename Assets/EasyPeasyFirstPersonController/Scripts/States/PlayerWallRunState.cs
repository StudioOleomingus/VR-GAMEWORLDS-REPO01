namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    /// <summary>
    /// Stuck to a wall at speed. Gravity is heavily reduced so you slide down slowly while
    /// running along the surface, and Space launches you off toward the opposite wall.
    /// Chaining launches lets you ping-pong down a corridor with no floor at all.
    ///
    /// Only reachable with enough momentum built up, so ordinary walking never triggers it.
    /// </summary>
    public class PlayerWallRunState : PlayerBaseState
    {
        private Vector3 wallNormal;
        private Vector3 runDirection;
        private Collider wallCollider;
        private int wallSide;           // -1 wall on the left, +1 wall on the right
        private float timer;
        private float groundGrace;      // ignore the "landed" exit briefly after latching on

        public PlayerWallRunState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            timer = ctx.wallRunMaxDuration;

            if (ctx.CheckWall(out RaycastHit hit, out int side))
            {
                CacheWall(hit, side);
            }
            else
            {
                // Lost it between the check and the transition; bail out next frame.
                timer = 0f;
                wallNormal = ctx.transform.right;
                runDirection = ctx.transform.forward;
            }

            // Cancel any downward speed so the stick feels like a catch rather than a slam,
            // and pop up slightly so a run started on the ground actually leaves the floor.
            ctx.moveDirection.y = Mathf.Max(ctx.moveDirection.y, ctx.wallRunEntryHop);

            groundGrace = ctx.wallRunGroundGrace;
            ctx.currentBobIntensity = 0f;
        }

        public override void UpdateState()
        {
            timer -= Time.deltaTime;
            if (groundGrace > 0f) groundGrace -= Time.deltaTime;

            // Re-acquire the wall each frame so the run follows curves and corners.
            if (ctx.CheckWall(out RaycastHit hit, out int side) && side == wallSide)
                CacheWall(hit, side);

            ctx.targetFov = ctx.parkourFov;
            ctx.currentBobIntensity = 0f;
            ctx.targetCameraY = ctx.standingCameraHeight;

            // Lean toward the wall. Negative roll leans right, matching the asset's strafe tilt.
            ctx.targetTilt = -wallSide * ctx.wallRunCameraTilt;

            ApplyWallGravity();
            HandleWallMovement();
            CheckSwitchStates();
        }

        public override void ExitState()
        {
            ctx.targetTilt = 0f;

            // However we left, block this particular wall for a moment. A different wall is
            // still grabbable immediately, which is what makes the ping-pong work.
            if (wallCollider != null)
            {
                ctx.lastWall = wallCollider;
                ctx.wallReattachTimer = ctx.wallReattachCooldown;
            }
        }

        public override void CheckSwitchStates()
        {
            // Space launches you off the wall.
            if (ctx.jumpPressed)
            {
                LaunchOffWall();
                SwitchState(factory.Jumping());
                return;
            }

            // Timed out, lost the wall, or eased off the sprint key.
            bool stillOnWall = ctx.CheckWall(out _, out int side) && side == wallSide;

            if (timer <= 0f || !stillOnWall || !ctx.input.sprint)
            {
                SwitchState(factory.Fall());
                return;
            }

            // Grace period so a run started on the ground doesn't cancel itself immediately.
            if (groundGrace <= 0f && ctx.isGrounded && ctx.moveDirection.y <= 0f)
            {
                SwitchState(factory.Grounded());
                return;
            }

            if (ctx.isInWater)
            {
                SwitchState(factory.Swimming());
            }
        }

        private void CacheWall(RaycastHit hit, int side)
        {
            wallNormal = hit.normal;
            wallCollider = hit.collider;
            wallSide = side;

            // Travel along the wall, in whichever direction the player is already facing.
            Vector3 tangent = Vector3.Cross(wallNormal, Vector3.up).normalized;
            if (Vector3.Dot(tangent, ctx.transform.forward) < 0f) tangent = -tangent;
            runDirection = tangent;
        }

        private void ApplyWallGravity()
        {
            ctx.moveDirection.y -= ctx.wallRunGravity * Time.deltaTime;
        }

        private void HandleWallMovement()
        {
            // Along the wall, plus a constant push into it so small bumps don't break contact.
            Vector3 move = runDirection * ctx.wallRunSpeed
                         - wallNormal * ctx.wallRunStickForce;

            move.y = ctx.moveDirection.y;

            ctx.currentVelocity = new Vector3(move.x, 0f, move.z);
            ctx.characterController.Move(move * Time.deltaTime);
        }

        private void LaunchOffWall()
        {
            // wallJumpIntensity is the single knob that scales the whole manoeuvre.
            Vector3 launch = wallNormal * ctx.wallJumpSideForce
                           + Vector3.up * ctx.wallJumpUpForce
                           + runDirection * ctx.wallJumpForwardForce;

            ctx.QueueLaunch(launch * ctx.wallJumpIntensity);
            // ExitState records the wall so it can't be re-grabbed straight away.
        }
    }
}
