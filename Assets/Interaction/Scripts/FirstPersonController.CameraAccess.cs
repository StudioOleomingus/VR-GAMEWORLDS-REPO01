namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    /// <summary>
    /// Additive partial for the Easy Peasy controller. The vendor script declares
    /// <c>public partial class FirstPersonController</c>, so this file can expose its
    /// private camera orientation state without modifying the asset itself — meaning
    /// the asset stays safe to update from the Package Manager.
    ///
    /// Used by <see cref="InteractionSystem.PlayerInteractor"/> to ease the camera level
    /// while an object is being lifted, and to hand the pitch back cleanly afterwards so
    /// the view doesn't snap when control returns.
    /// </summary>
    public partial class FirstPersonController
    {
        /// <summary>Camera pitch in degrees. Negative looks up, positive looks down.</summary>
        public float CameraPitch
        {
            get => xRotation;
            set => xRotation = Mathf.Clamp(value, -90f, 90f);
        }

        /// <summary>Camera roll (the strafe/slide tilt) in degrees.</summary>
        public float CameraRoll
        {
            get => currentTilt;
            set
            {
                currentTilt = value;
                tiltVelocity = 0f; // reset the SmoothDamp so it resumes from rest
            }
        }

        /// <summary>
        /// Pushes the current pitch and roll onto the camera transform. Needed while the
        /// controller is disabled, because <c>HandleRotation</c> is not running to do it.
        /// </summary>
        public void ApplyCameraOrientation()
        {
            if (playerCamera != null)
                playerCamera.localRotation = Quaternion.Euler(xRotation, 0f, currentTilt);
        }
    }
}
