namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerGroundedState : PlayerBaseState
    {
        public PlayerGroundedState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            // Read the impact speed before it gets zeroed, so a hard landing can shake the view.
            ctx.ReportLanding(ctx.moveDirection.y);

            ctx.moveDirection.y = -2f;
        }

        public override void UpdateState()
        {
            CheckSwitchStates();

            // A transition just fired; let the new state own this frame.
            if (ctx.CurrentState != this) return;

            ctx.targetCameraY = ctx.standingCameraHeight;

            bool isSprinting = ctx.input.sprint && ctx.input.moveInput.y > 0;

            // Momentum ramps sprint speed and FOV up toward the parkour values the longer
            // you hold Shift, so a sprint accelerates instead of snapping to one speed.
            float speed = isSprinting ? ctx.CurrentSprintSpeed : ctx.walkSpeed;

            ctx.targetFov = isSprinting ? ctx.CurrentSprintFov : ctx.normalFov;
            ctx.currentBobIntensity = ctx.bobAmount * (isSprinting ? 1.5f : 1f);
            ctx.currentBobSpeed = ctx.bobSpeed * (isSprinting ? 1.3f : 1f);
            ctx.targetTilt = 0;

            ctx.targetCameraY = ctx.standingCameraHeight;

            if (ctx.enableSmoothCrouch)
            {
                ctx.characterController.height = Mathf.MoveTowards(
                    ctx.characterController.height,
                    ctx.standingCharacterControllerHeight,
                    Time.deltaTime * ctx.crouchTransitionSpeed
                );

                ctx.characterController.center = Vector3.MoveTowards(
                    ctx.characterController.center,
                    ctx.standingCharacterControllerCenter,
                    Time.deltaTime * (ctx.crouchTransitionSpeed / 2f)
                );
            }
            else
            {
                ctx.characterController.height = ctx.standingCharacterControllerHeight;
                ctx.characterController.center = ctx.standingCharacterControllerCenter;
            }

            Vector2 input = ctx.input.moveInput;
            Vector3 targetMove = ctx.transform.right * input.x + ctx.transform.forward * input.y;
            
            // Normalize diagonal movement so they don't walk 40% faster diagonally
            targetMove = Vector3.ClampMagnitude(targetMove, 1f); 

            Vector3 targetVelocity = targetMove * speed;

            // Apply acceleration or deceleration based on if the player is pressing keys
            float accelRate = (input.sqrMagnitude > 0.01f) ? ctx.groundAcceleration : ctx.groundDeceleration;

            // Smoothly move current velocity towards target (gives weight and fixes the instant robotic movement)
            ctx.currentVelocity = Vector3.MoveTowards(ctx.currentVelocity, targetVelocity, accelRate * Time.deltaTime);

            Vector3 finalVelocity = ctx.currentVelocity;
            finalVelocity.y = -5f; // Keep sticking to the ground
            
            ctx.characterController.Move(finalVelocity * Time.deltaTime);
        }

        public override void ExitState() { }

        public override void CheckSwitchStates()
        {
            if (ctx.input.jump && ctx.isGrounded)
            {
                SwitchState(factory.Jumping());
            }
            else if (ctx.CanStartWallRun(out _, out _))
            {
                SwitchState(factory.WallRun());
            }
            else if (ctx.input.slide && ctx.input.sprint)
            {
                SwitchState(factory.Sliding());
            }
            else if (!ctx.isGrounded)
            {
                SwitchState(factory.Fall());
            }
            else if (ctx.input.crouch && ctx.isGrounded)
            {
                SwitchState(factory.Crouching());
            }
            else if (ctx.isInWater)
            {
                SwitchState(factory.Swimming());
            }

        }
    }
}