using System.Collections;
using UnityEngine;

/// <summary>
/// Attach one instance per prefab/object. Pulses this transform's localScale.y up and back, and optionally
/// localPosition.y (bob). Call <see cref="SchedulePulse"/> / <see cref="ScheduleBob"/> from a coordinator (e.g. <see cref="ReactionScalePulseGroup"/>).
/// Works with <see cref="HandBetweenModelSpawner"/> because that script only overwrites localScale.x each frame.
/// </summary>
public class ReactionPulse : MonoBehaviour
{
    [Header("Scale pulse")]
    [SerializeField] float riseSeconds = 0.12f;
    [SerializeField] float fallSeconds = 0.28f;
    [SerializeField] AnimationCurve riseEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] AnimationCurve fallEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Local Y bob (optional)")]
    [Tooltip("0 = bob disabled. Nonzero = local Y bob when ReactionScalePulseGroup calls ScheduleBob.")]
    [SerializeField] float bobDeltaLocalY;
    [SerializeField] float bobRiseSeconds = 0.14f;
    [SerializeField] float bobFallSeconds = 0.22f;
    [SerializeField] AnimationCurve bobRiseEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] AnimationCurve bobFallEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    Coroutine _pulseRoutine;
    Coroutine _bobRoutine;
    float _restScaleY;
    float _restLocalY;

    void Awake()
    {
        _restScaleY = transform.localScale.y;
        _restLocalY = transform.localPosition.y;
    }

    /// <summary>
    /// Wait <paramref name="delaySeconds"/>, then pulse scale Y to <c>restY × peakYMultiplier</c> and return to rest.
    /// </summary>
    public void SchedulePulse(float delaySeconds, float peakYMultiplier)
    {
        if (_pulseRoutine != null)
        {
            StopCoroutine(_pulseRoutine);
            RestoreRestScaleY();
        }

        _pulseRoutine = StartCoroutine(PulseRoutine(delaySeconds, peakYMultiplier));
    }

    /// <summary>Same as <see cref="SchedulePulse"/> with zero delay.</summary>
    public void PlayPulse(float peakYMultiplier) => SchedulePulse(0f, peakYMultiplier);

    /// <summary>
    /// Wait <paramref name="delaySeconds"/>, then bob localPosition.y by <see cref="bobDeltaLocalY"/> and return.
    /// Independent from the scale pulse coroutine.
    /// </summary>
    public void ScheduleBob(float delaySeconds)
    {
        if (Mathf.Abs(bobDeltaLocalY) < 1e-8f) return;

        if (_bobRoutine != null)
        {
            StopCoroutine(_bobRoutine);
            RestoreRestLocalY();
        }

        _bobRoutine = StartCoroutine(BobRoutine(delaySeconds));
    }

    void RestoreRestScaleY()
    {
        Vector3 ls = transform.localScale;
        ls.y = _restScaleY;
        transform.localScale = ls;
    }

    void RestoreRestLocalY()
    {
        Vector3 lp = transform.localPosition;
        lp.y = _restLocalY;
        transform.localPosition = lp;
    }

    IEnumerator PulseRoutine(float delaySeconds, float peakYMultiplier)
    {
        yield return new WaitForSeconds(delaySeconds);

        float restY = transform.localScale.y;
        _restScaleY = restY;
        float peakY = restY * peakYMultiplier;

        float rise = Mathf.Max(0f, riseSeconds);
        float t = 0f;
        while (t < rise)
        {
            t += Time.deltaTime;
            float u = rise > 0f ? Mathf.Clamp01(t / rise) : 1f;
            float e = riseEase.Evaluate(u);
            Vector3 ls = transform.localScale;
            ls.y = Mathf.LerpUnclamped(restY, peakY, e);
            transform.localScale = ls;
            yield return null;
        }

        float fall = Mathf.Max(0f, fallSeconds);
        t = 0f;
        while (t < fall)
        {
            t += Time.deltaTime;
            float u = fall > 0f ? Mathf.Clamp01(t / fall) : 1f;
            float e = fallEase.Evaluate(u);
            Vector3 ls = transform.localScale;
            ls.y = Mathf.LerpUnclamped(peakY, restY, e);
            transform.localScale = ls;
            yield return null;
        }

        {
            Vector3 ls = transform.localScale;
            ls.y = restY;
            transform.localScale = ls;
        }

        _restScaleY = restY;
        _pulseRoutine = null;
    }

    IEnumerator BobRoutine(float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);

        float restY = transform.localPosition.y;
        _restLocalY = restY;
        float peakY = restY + bobDeltaLocalY;

        float rise = Mathf.Max(0f, bobRiseSeconds);
        float t = 0f;
        while (t < rise)
        {
            t += Time.deltaTime;
            float u = rise > 0f ? Mathf.Clamp01(t / rise) : 1f;
            float e = bobRiseEase.Evaluate(u);
            Vector3 lp = transform.localPosition;
            lp.y = Mathf.LerpUnclamped(restY, peakY, e);
            transform.localPosition = lp;
            yield return null;
        }

        float fall = Mathf.Max(0f, bobFallSeconds);
        t = 0f;
        while (t < fall)
        {
            t += Time.deltaTime;
            float u = fall > 0f ? Mathf.Clamp01(t / fall) : 1f;
            float e = bobFallEase.Evaluate(u);
            Vector3 lp = transform.localPosition;
            lp.y = Mathf.LerpUnclamped(peakY, restY, e);
            transform.localPosition = lp;
            yield return null;
        }

        {
            Vector3 lp = transform.localPosition;
            lp.y = restY;
            transform.localPosition = lp;
        }

        _restLocalY = restY;
        _bobRoutine = null;
    }
}
