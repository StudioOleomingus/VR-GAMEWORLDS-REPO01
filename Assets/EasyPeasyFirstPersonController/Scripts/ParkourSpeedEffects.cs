namespace EasyPeasyFirstPersonController
{
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;

    /// <summary>
    /// Drives URP Motion Blur from the controller's momentum, so the world smears more the
    /// faster you run. Added automatically by <see cref="FirstPersonController"/> when
    /// Enable Parkour is on; add it by hand if you want to tune the values in the inspector.
    ///
    /// Builds its own Volume at runtime, so there is nothing to set up in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class ParkourSpeedEffects : MonoBehaviour
    {
        [Header("Motion Blur")]
        public bool useMotionBlur = true;

        [Tooltip("Motion blur intensity at full momentum. URP caps this at 1.")]
        [Range(0f, 1f)] public float maxMotionBlur = 0.55f;

        [Tooltip("Momentum below which no blur is applied at all, so a gentle jog stays clean.")]
        [Range(0f, 1f)] public float blurThreshold = 0.15f;

        [Tooltip("How quickly the blur follows momentum. Lower is laggier and more cinematic.")]
        public float blurResponse = 5f;

        [Tooltip("CameraOnly is the cheap, safe option. CameraAndObjects needs motion vectors enabled.")]
        public MotionBlurMode blurMode = MotionBlurMode.CameraOnly;

        public MotionBlurQuality blurQuality = MotionBlurQuality.Medium;

        [Tooltip("Optional. Leave empty and a Volume is created at runtime.")]
        public Volume speedVolume;

        FirstPersonController _ctx;
        MotionBlur _motionBlur;
        Volume _runtimeVolume;
        float _blend;

        bool OwnsVolume => _runtimeVolume != null && _runtimeVolume == speedVolume;

        void Awake()
        {
            _ctx = GetComponent<FirstPersonController>();
            if (_ctx == null)
            {
                enabled = false;
                return;
            }

            if (!useMotionBlur) return;

            var cam = _ctx.playerCamera != null ? _ctx.playerCamera.GetComponent<Camera>() : null;
            if (cam != null)
            {
                var camData = cam.GetUniversalAdditionalCameraData();
                if (camData != null) camData.renderPostProcessing = true;
            }

            if (speedVolume == null)
            {
                var go = new GameObject("~ParkourSpeedVolume");
                go.transform.SetParent(transform, false);
                _runtimeVolume = go.AddComponent<Volume>();
                _runtimeVolume.isGlobal = true;
                _runtimeVolume.priority = 90f;   // below the inspect DoF volume

                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "ParkourSpeed (runtime)";
                _runtimeVolume.sharedProfile = profile;
                speedVolume = _runtimeVolume;
            }

            var vp = speedVolume.sharedProfile != null ? speedVolume.sharedProfile : speedVolume.profile;
            if (vp == null) { useMotionBlur = false; return; }

            if (!vp.TryGet<MotionBlur>(out _motionBlur)) _motionBlur = vp.Add<MotionBlur>(true);

            _motionBlur.active = true;
            _motionBlur.mode.overrideState = true;
            _motionBlur.quality.overrideState = true;
            _motionBlur.intensity.overrideState = true;

            speedVolume.weight = 0f;
            if (OwnsVolume) speedVolume.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_runtimeVolume != null && _runtimeVolume.sharedProfile != null)
                Destroy(_runtimeVolume.sharedProfile);
        }

        void LateUpdate()
        {
            if (!useMotionBlur || _ctx == null || _motionBlur == null || speedVolume == null) return;

            // Remap momentum so the blur only starts once you are genuinely moving fast.
            float target = Mathf.InverseLerp(blurThreshold, 1f, _ctx.momentum);
            _blend = Mathf.Lerp(_blend, target, 1f - Mathf.Exp(-blurResponse * Time.deltaTime));

            bool active = _blend > 0.002f;
            if (OwnsVolume && speedVolume.gameObject.activeSelf != active)
                speedVolume.gameObject.SetActive(active);

            if (!active) { speedVolume.weight = 0f; return; }

            speedVolume.weight = 1f;
            _motionBlur.mode.value = blurMode;
            _motionBlur.quality.value = blurQuality;
            _motionBlur.intensity.value = maxMotionBlur * _blend;
        }
    }
}
