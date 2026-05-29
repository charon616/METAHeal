using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loops a symmetric open/close animation on left/right UI Images (anchoredPosition X offset).
/// Runs in Update (not coroutines) so it works when the object starts inactive.
/// </summary>
[DisallowMultipleComponent]
public class HandsAnimation : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] Image leftImage;
    [SerializeField] Image rightImage;

    [Header("Motion")]
    [Tooltip("Duration of one loop (spread out → return) in seconds.")]
    [Min(0.05f)]
    [SerializeField] float cycleDuration = 1f;
    [Tooltip("Max offset (left -X, right +X on anchoredPosition).")]
    [Min(0f)]
    [SerializeField] float maxAnchorOffset = 32f;
    [Tooltip("Spread amount over normalized time 0→1. 0 = closed, 1 = max. Endpoints at 0 loop seamlessly.")]
    [SerializeField] AnimationCurve spacingCurve = CreateDefaultSpacingCurve();
    [Tooltip("Ignore time scale (keeps animating while paused).")]
    [SerializeField] bool useUnscaledTime;

    [Header("Activate / deactivate")]
    [Tooltip("Auto Play when this object is enabled (works even if inactive at scene start).")]
    [SerializeField] bool playOnEnable = true;
    [Tooltip("SetActive(false) this GameObject when Stop() is called (e.g. close a panel).")]
    [SerializeField] bool deactivateGameObjectWhenStopped;

    Vector2 _leftBase;
    Vector2 _rightBase;
    bool _basesCached;
    bool _playing;
    float _phaseTime;

    static AnimationCurve CreateDefaultSpacingCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f));
    }

    void Awake() => CacheBasesIfNeeded();

    void OnEnable()
    {
        if (!Application.isPlaying || !playOnEnable)
            return;

        Play();
    }

    void OnDisable()
    {
        _playing = false;
        _phaseTime = 0f;
        RestoreBasePositions();
    }

    void CacheBasesIfNeeded()
    {
        if (_basesCached)
            return;

        if (leftImage != null)
            _leftBase = leftImage.rectTransform.anchoredPosition;
        if (rightImage != null)
            _rightBase = rightImage.rectTransform.anchoredPosition;
        _basesCached = true;
    }

    /// <summary>Start the loop (resets if already playing). Sets flag only when inactive; Update runs after enable.</summary>
    public void Play()
    {
        if (leftImage == null && rightImage == null)
            return;

        CacheBasesIfNeeded();
        _phaseTime = 0f;
        _playing = true;
    }

    /// <summary>Stop and restore base positions. Hides this object when <see cref="deactivateGameObjectWhenStopped"/> is on.</summary>
    public void Stop()
    {
        _playing = false;
        _phaseTime = 0f;
        RestoreBasePositions();

        if (deactivateGameObjectWhenStopped && gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    void Update()
    {
        if (!_playing)
            return;

        // Do not advance while disabled or inactive in hierarchy (resume on re-enable).
        if (!isActiveAndEnabled)
            return;

        if (leftImage == null && rightImage == null)
        {
            _playing = false;
            return;
        }

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        _phaseTime += dt;

        float dur = Mathf.Max(cycleDuration, 0.05f);
        float u = Mathf.Repeat(_phaseTime, dur) / dur;

        var curve = spacingCurve != null && spacingCurve.length > 0 ? spacingCurve : CreateDefaultSpacingCurve();
        float factor = Mathf.Clamp01(curve.Evaluate(u));
        ApplySymmetricOffset(factor);
    }

    void ApplySymmetricOffset(float factor)
    {
        float dx = maxAnchorOffset * factor;
        if (leftImage != null)
            leftImage.rectTransform.anchoredPosition = _leftBase + new Vector2(-dx, 0f);
        if (rightImage != null)
            rightImage.rectTransform.anchoredPosition = _rightBase + new Vector2(dx, 0f);
    }

    void RestoreBasePositions()
    {
        if (leftImage != null)
            leftImage.rectTransform.anchoredPosition = _leftBase;
        if (rightImage != null)
            rightImage.rectTransform.anchoredPosition = _rightBase;
    }
}
