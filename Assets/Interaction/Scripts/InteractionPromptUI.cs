using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InteractionSystem
{
    /// <summary>
    /// World-space billboard popup that floats above the object the player is looking at.
    /// Builds itself at runtime, so there is nothing to author in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionPromptUI : MonoBehaviour
    {
        [Header("Look")]
        public Color panelColor = new Color(0.04f, 0.05f, 0.07f, 0.72f);
        public Color nameColor = new Color(1f, 1f, 1f, 0.95f);
        public Color hintColor = new Color(0.62f, 0.78f, 0.9f, 0.9f);
        public float nameFontSize = 28f;
        public float hintFontSize = 19f;

        [Header("Behaviour")]
        [Tooltip("Metres of world size per metre of distance. Keeps the popup a constant size on screen.")]
        public float scalePerMetre = 0.0022f;
        public float minScale = 0.0016f;
        public float maxScale = 0.02f;
        public float fadeSpeed = 9f;
        [Tooltip("How far the popup rises as it fades in.")]
        public float riseDistance = 0.06f;

        Camera _camera;
        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _root;
        RectTransform _panel;
        TMP_Text _nameText;
        TMP_Text _hintText;

        Interactable _target;
        bool _visible;
        float _alpha;
        bool _built;

        public void Initialise(Camera cam)
        {
            _camera = cam;
            Build();
        }

        void Build()
        {
            if (_built) return;
            _built = true;

            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("[InteractionPromptUI] No default TextMeshPro font asset found. " +
                                 "Import it via Window > TextMeshPro > Import TMP Essential Resources, " +
                                 "otherwise the interaction popup will render blank.");
            }

            var canvasGo = new GameObject("InteractionPrompt_Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            _root = (RectTransform)canvasGo.transform;
            _root.sizeDelta = new Vector2(420f, 120f);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = _camera;
            _canvas.sortingOrder = 10;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 12f;
            scaler.referencePixelsPerUnit = 100f;

            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // Panel
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_root, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(420f, 120f);

            var img = panelGo.AddComponent<Image>();
            img.color = panelColor;
            img.raycastTarget = false;

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(26, 26, 16, 16);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _nameText = MakeText("Name", _panel, nameFontSize, nameColor, FontStyles.Normal);
            _hintText = MakeText("Hint", _panel, hintFontSize, hintColor, FontStyles.Normal);

            canvasGo.SetActive(true);
        }

        static TMP_Text MakeText(string label, Transform parent, float size, Color color, FontStyles style)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Show the popup over <paramref name="target"/>. <paramref name="inRange"/> reveals the key hint.</summary>
        public void Show(Interactable target, bool inRange)
        {
            if (target == null) { Hide(); return; }
            Build();

            _target = target;
            _visible = true;

            string label = target.Label;
            if (_nameText.text != label) _nameText.text = label;

            bool showHint = inRange && target.canPickUp && !string.IsNullOrWhiteSpace(target.actionHint);
            if (_hintText.gameObject.activeSelf != showHint) _hintText.gameObject.SetActive(showHint);
            if (showHint)
            {
                string hint = $"[E]  {target.actionHint}";
                if (_hintText.text != hint) _hintText.text = hint;
            }
        }

        public void Hide()
        {
            _visible = false;
        }

        void LateUpdate()
        {
            if (!_built) return;

            float target = _visible && _target != null ? 1f : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * fadeSpeed);
            _group.alpha = Mathf.SmoothStep(0f, 1f, _alpha);

            bool active = _alpha > 0.001f;
            if (_root.gameObject.activeSelf != active) _root.gameObject.SetActive(active);
            if (!active || _target == null || _camera == null) return;

            Vector3 anchor = _target.GetLabelWorldPosition();
            anchor += Vector3.up * (1f - _alpha) * -riseDistance;
            _root.position = anchor;

            // Billboard: face the camera, stay upright.
            Vector3 toCam = _root.position - _camera.transform.position;
            if (toCam.sqrMagnitude > 0.0001f)
                _root.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);

            float dist = toCam.magnitude;
            float s = Mathf.Clamp(scalePerMetre * dist, minScale, maxScale);
            s *= Mathf.Lerp(0.88f, 1f, _alpha);
            _root.localScale = Vector3.one * s;
        }
    }
}
