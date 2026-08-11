using UnityEngine;
using UnityEngine.Events;

namespace InteractionSystem
{
    /// <summary>
    /// Put this on any object the player should be able to look at, and optionally pick up
    /// and inspect. A collider is required so the interaction raycast can find it.
    /// </summary>
    [DisallowMultipleComponent]
    public class Interactable : MonoBehaviour
    {
        [Header("Label")]
        [Tooltip("Shown in the world-space popup. Falls back to the GameObject name if empty.")]
        public string displayName = "";

        [Tooltip("Small hint line under the name, shown only when close enough to pick up.")]
        public string actionHint = "Examine";

        [Tooltip("Extra world-space height above the object's bounds for the popup.")]
        public float labelHeightPadding = 0.18f;

        [Tooltip("Additional world-space offset for the popup, applied after the bounds calculation.")]
        public Vector3 labelOffset = Vector3.zero;

        [Header("Pick Up")]
        [Tooltip("If false the popup still appears but E does nothing.")]
        public bool canPickUp = true;

        [Tooltip("Euler rotation the object settles into when it reaches the inspect pose, relative to the camera.")]
        public Vector3 inspectRotationOffset = Vector3.zero;

        [Tooltip("Distance from camera while inspecting. 0 = auto-fit from the object's bounds.")]
        public float inspectDistanceOverride = 0f;

        [Tooltip("Scales the auto-fit distance. >1 holds the object further away (smaller on screen).")]
        [Range(0.4f, 3f)] public float inspectFitMultiplier = 1f;

        [Header("Events")]
        public UnityEvent onFocused;
        public UnityEvent onUnfocused;
        public UnityEvent onPickedUp;
        public UnityEvent onPlacedDown;

        Renderer[] _renderers;
        Collider[] _colliders;
        Rigidbody _rigidbody;

        bool _cached;

        public Renderer[] Renderers { get { EnsureCached(); return _renderers; } }
        public Collider[] Colliders { get { EnsureCached(); return _colliders; } }
        public Rigidbody Body { get { EnsureCached(); return _rigidbody; } }
        public string Label => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        void Awake() => CacheComponents();

        void EnsureCached() { if (!_cached) CacheComponents(); }

        public void CacheComponents()
        {
            _cached = true;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);

            // Deliberately not GetComponentInParent: the interactor moves *this* transform,
            // so a Rigidbody further up the hierarchy would keep simulating independently.
            _rigidbody = GetComponent<Rigidbody>();

            if (_rigidbody == null && Application.isPlaying)
            {
                var outerBody = GetComponentInParent<Rigidbody>();
                if (outerBody != null)
                    Debug.LogWarning($"[Interactable] '{name}' sits under Rigidbody '{outerBody.name}' but has none " +
                                     "of its own. Move the Interactable onto the Rigidbody's GameObject, or the " +
                                     "physics body will fight the pickup animation.", this);
            }
        }

        /// <summary>World-space bounds of every renderer under this object.</summary>
        public bool TryGetWorldBounds(out Bounds bounds)
        {
            bounds = default;
            EnsureCached();
            if (_renderers.Length == 0 && _colliders.Length == 0) CacheComponents();

            bool found = false;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null || !r.enabled) continue;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!found && _colliders != null)
            {
                for (int i = 0; i < _colliders.Length; i++)
                {
                    var c = _colliders[i];
                    if (c == null) continue;
                    if (!found) { bounds = c.bounds; found = true; }
                    else bounds.Encapsulate(c.bounds);
                }
            }

            if (!found) bounds = new Bounds(transform.position, Vector3.one * 0.25f);
            return found;
        }

        /// <summary>Where the popup should float.</summary>
        public Vector3 GetLabelWorldPosition()
        {
            TryGetWorldBounds(out var b);
            return b.center + Vector3.up * (b.extents.y + labelHeightPadding) + labelOffset;
        }

        /// <summary>Largest half-extent, used to auto-fit the inspect distance.</summary>
        public float GetBoundingRadius()
        {
            TryGetWorldBounds(out var b);
            return Mathf.Max(0.02f, b.extents.magnitude);
        }

        public void SetCollidersEnabled(bool enabled)
        {
            EnsureCached();
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = enabled;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) CacheComponents();
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireSphere(GetLabelWorldPosition(), 0.06f);
        }
#endif
    }
}
