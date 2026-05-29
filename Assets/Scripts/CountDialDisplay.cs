using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Serialization;
using TMPro;
using UnityEngine.Events;
using System.Collections;

/// <summary>
/// Rep counter UI: shows current/total as text and drives a radial fill Image.
/// Fires UnityEvents on count changes and when the target is reached; supports milestone tier colors.
/// </summary>
[ExecuteAlways]
public class CountDialDisplay : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("Target count (denominator). 0: fill 0, display avoids invalid fraction.")]
    [Min(0)]
    [SerializeField] int total = 12;

    [Tooltip("Current count (0 .. total; can clamp in display and fill).")]
    [Min(0)]
    [SerializeField] int current = 0;

    [Tooltip("If on: every frame, clamp current to 0..total. If off: clamp when values are set from code/inspector only.")]
    [SerializeField] bool clampInUpdate;

    [Tooltip("If on: while playing, inspector edits also fire onCountChanged / onReachedTotal. If off: visuals only for inspector edits.")]
    [SerializeField] bool emitEventsOnInspectorInPlay;

    [Header("View")]
    [Tooltip("Count label (TextMeshPro).")]
    [FormerlySerializedAs("textMeshPro")]
    [SerializeField] TextMeshProUGUI textMeshProText;
    [Tooltip("Radial Image: Filled, e.g. Radial 360. fillAmount = current / total.")]
    [SerializeField] Image dialSlider;

    [Header("Milestone tint")]
    [Tooltip("Counts per tier step. tier = clamp(current / step, 0 .. color count - 1).")]
    [Min(1)]
    [SerializeField] int milestoneStep = 10;
    [Tooltip("Color per tier (list length = tier count). Match HandBetweenModelSpawner tier prefabs for aligned indices.")]
    [SerializeField] List<Color32> milestoneTierColors = new List<Color32>();
    [Tooltip("Optional Image to tint with the tier color above.")]
    [SerializeField] Image milestoneTintImage;
    [Tooltip("TextMeshPro for numerator (CountNum). Auto-finds child named CountNum if unset.")]
    [SerializeField] TextMeshProUGUI countNumText;

    [Header("Format")]
    [SerializeField] bool zeroPad = true;
    [Min(1)]
    [SerializeField] int minDigits = 2;
    [FormerlySerializedAs("smallDenominatorForTextMeshPro")]
    [SerializeField] bool smallDenominator = true;
    [Tooltip("1 = same as numerator. 0.7 = denominator block ~70% of TMP default font size.")]
    [Range(0.25f, 1f)]
    [SerializeField] float denominatorRelativeSize = 0.7f;

    [Header("Animation")]
    [Tooltip("Animate dialSlider.fillAmount toward target instead of snapping.")]
    [SerializeField] bool animateFill = true;
    [Min(0.01f)]
    [SerializeField] float fillAnimationDuration = 0.2f;
    [SerializeField] AnimationCurve fillAnimationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("When count increases, apply a quick scale punch to the text target.")]
    [SerializeField] bool animateTextPunchOnIncrease = true;
    [SerializeField] RectTransform textScaleTarget;
    [Min(1f)]
    [SerializeField] float textPunchScale = 1.12f;
    [Min(0.01f)]
    [SerializeField] float textPunchDuration = 0.14f;
    [SerializeField] AnimationCurve textPunchCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Events")]
    [SerializeField] UnityEvent onCountChanged;
    [SerializeField] UnityEvent onReachedTotal;

    bool _firedTotal;
    Coroutine _fillRoutine;
    Coroutine _punchRoutine;
    Vector3 _baseTextScale = Vector3.one;

    void Awake() => TryBindComponentsOnSameObject();

    void TryBindComponentsOnSameObject()
    {
        if (textMeshProText == null && GetComponent<TextMeshProUGUI>() != null) textMeshProText = GetComponent<TextMeshProUGUI>();
        if (dialSlider == null && GetComponent<Image>() != null) dialSlider = GetComponent<Image>();
        if (textScaleTarget == null && textMeshProText != null) textScaleTarget = textMeshProText.rectTransform;
        if (textScaleTarget != null) _baseTextScale = textScaleTarget.localScale;
        TryBindCountNumText();
    }

    void TryBindCountNumText()
    {
        if (countNumText != null)
            return;

        var t = FindChildRecursive(transform, "CountNum");
        if (t != null)
            countNumText = t.GetComponent<TextMeshProUGUI>();
    }

    static Transform FindChildRecursive(Transform parent, string objectName)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            if (c.name == objectName)
                return c;

            var nested = FindChildRecursive(c, objectName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    void OnValidate()
    {
        if (total < 0) total = 0;
        if (current < 0) current = 0;
        if (total > 0) current = Mathf.Min(current, total);
        TryBindComponentsOnSameObject();
        if (Application.isPlaying && emitEventsOnInspectorInPlay)
        {
            ApplyAllInternal(false, false, true);
            return;
        }
        ApplyVisualsOnly();
    }

    void Start()
    {
        ApplyAllInternal(false, false, true);
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (clampInUpdate) current = total > 0 ? Mathf.Clamp(current, 0, total) : Mathf.Max(0, current);
    }

    void ApplyVisualsOnly()
    {
        ApplyText();
        ApplyDial(false);
        ApplyMilestoneVisuals();
    }

    [ContextMenu("Apply All")]
    public void ApplyAll() => ApplyAllInternal(true, false, true);

    void ApplyAllInternal(bool allowAnimation, bool triggerTextPunch, bool invokeEvents)
    {
        ApplyText();
        ApplyDial(allowAnimation);
        ApplyMilestoneVisuals();
        if (triggerTextPunch) TriggerTextPunch();

        if (!invokeEvents) return;

        onCountChanged?.Invoke();
        if (total > 0 && current >= total && !_firedTotal)
        {
            _firedTotal = true;
            onReachedTotal?.Invoke();
        }
        else if (current < total) _firedTotal = false;
    }

    void ApplyText()
    {
        if (textMeshProText == null) return;
        textMeshProText.richText = true;
        textMeshProText.text = smallDenominator
            ? BuildFractionStringForTextMeshPro()
            : BuildFractionStringPlain();
    }

    void ApplyMilestoneVisuals()
    {
        if (milestoneStep <= 0)
            return;

        TryBindCountNumText();

        var colorCount = milestoneTierColors != null ? milestoneTierColors.Count : 0;
        if (colorCount == 0)
            return;

        if (milestoneTintImage == null && countNumText == null)
            return;

        var tier = Mathf.Clamp(current / milestoneStep, 0, colorCount - 1);
        var c = milestoneTierColors[tier];

        if (milestoneTintImage != null)
            SetMilestoneImageColor(milestoneTintImage, c);

        if (countNumText != null)
            SetCountNumTextColor(countNumText, c);
    }

    void SetMilestoneImageColor(Image img, Color32 c)
    {
        if (img == null)
            return;
        img.color = c;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(img);
            if (img.canvas != null)
                EditorUtility.SetDirty(img.canvas);
        }
#endif
    }

    void SetCountNumTextColor(TextMeshProUGUI tmp, Color32 c)
    {
        if (tmp == null)
            return;

        tmp.color = c;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(tmp);
            if (tmp.canvas != null)
                EditorUtility.SetDirty(tmp.canvas);
        }
#endif
    }

    void ApplyDial(bool allowAnimation)
    {
        if (dialSlider == null) return;
        var target = total <= 0 ? 0f : Mathf.Clamp01((float)current / total);

        if (!Application.isPlaying || !allowAnimation || !animateFill)
        {
            if (_fillRoutine != null) StopCoroutine(_fillRoutine);
            _fillRoutine = null;
            dialSlider.fillAmount = target;
            return;
        }

        if (!isActiveAndEnabled)
        {
            dialSlider.fillAmount = target;
            return;
        }

        if (_fillRoutine != null) StopCoroutine(_fillRoutine);
        _fillRoutine = StartCoroutine(AnimateDialFillRoutine(target));
    }

    IEnumerator AnimateDialFillRoutine(float target)
    {
        var start = dialSlider.fillAmount;
        var elapsed = 0f;
        while (elapsed < fillAnimationDuration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / fillAnimationDuration);
            var curved = fillAnimationCurve.Evaluate(t);
            dialSlider.fillAmount = Mathf.LerpUnclamped(start, target, curved);
            yield return null;
        }
        dialSlider.fillAmount = target;
        _fillRoutine = null;
    }

    void TriggerTextPunch()
    {
        if (!Application.isPlaying || !animateTextPunchOnIncrease || textScaleTarget == null) return;
        if (!isActiveAndEnabled)
            return;

        if (_punchRoutine != null) StopCoroutine(_punchRoutine);
        _punchRoutine = StartCoroutine(TextPunchRoutine());
    }

    IEnumerator TextPunchRoutine()
    {
        var elapsed = 0f;
        textScaleTarget.localScale = _baseTextScale;
        while (elapsed < textPunchDuration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / textPunchDuration);
            var peak = textPunchCurve.Evaluate(t);
            var scale = Mathf.LerpUnclamped(1f, textPunchScale, peak);
            textScaleTarget.localScale = _baseTextScale * scale;
            yield return null;
        }
        textScaleTarget.localScale = _baseTextScale;
        _punchRoutine = null;
    }

    string BuildFractionStringPlain()
    {
        if (total <= 0) return zeroPad ? "00/00" : "0/0";
        if (!zeroPad) return $"{current}/{total}";

        var w = Mathf.Max(minDigits, total.ToString().Length);
        var a = current.ToString().PadLeft(w, '0');
        var b = total.ToString().PadLeft(w, '0');
        return $"{a}/{b}";
    }

    /// <summary>Numerator at default size; <c>"/" + denominator</c> in a smaller <c>&lt;size=…%&gt;…&lt;/size&gt;</c> block.</summary>
    string BuildFractionStringForTextMeshPro()
    {
        if (total <= 0) return BuildFractionStringPlain();
        if (!zeroPad)
        {
            if (!smallDenominator) return $"{current}/{total}";
            return WrapDenominatorForTmp($"{current}", $"{total}");
        }

        var w = Mathf.Max(minDigits, total.ToString().Length);
        var a = current.ToString().PadLeft(w, '0');
        var b = total.ToString().PadLeft(w, '0');
        if (!smallDenominator) return $"{a}/{b}";
        return WrapDenominatorForTmp(a, b);
    }

    string WrapDenominatorForTmp(string numerator, string denominator)
    {
        var pct = Mathf.Clamp(Mathf.RoundToInt(denominatorRelativeSize * 100f), 10, 100);
        // Enclose “/” and denominator in the same <size> block; numerator stays at default
        return $"{numerator}<size={pct}%>/{denominator}</size>";
    }

    public void SetTotal(int value)
    {
        total = Mathf.Max(0, value);
        if (current > total) current = total;
        ApplyAllInternal(true, false, true);
    }

    public void SetCurrent(int value)
    {
        var prev = current;
        current = total > 0 ? Mathf.Clamp(value, 0, total) : Mathf.Max(0, value);
        ApplyAllInternal(true, current > prev, true);
    }

    public void AddOne()
    {
        if (total <= 0) return;
        var prev = current;
        if (current < total) current++;
        ApplyAllInternal(true, current > prev, true);
    }

    /// <summary>Alias for UnityEvent / <see cref="ReactionEventHub"/>. Same as <see cref="AddOne"/>.</summary>
    public void OnReactionCountUp() => AddOne();

    public void ResetCount()
    {
        current = 0;
        _firedTotal = false;
        ApplyAllInternal(true, false, true);
    }

    public int Current => current;
    public int Total => total;

    /// <summary>
    /// Milestone tier index (e.g. <see cref="HandBetweenModelSpawner"/> tier prefabs).
    /// With colors: clamp(current / milestoneStep, 0 .. color count - 1); without: current / milestoneStep (caller clamps to list length).
    /// </summary>
    public int CurrentMilestoneTierIndex => GetMilestoneTierIndex();

    public int GetMilestoneTierIndex()
    {
        if (milestoneStep <= 0)
            return 0;

        var colorCount = milestoneTierColors != null ? milestoneTierColors.Count : 0;
        if (colorCount > 0)
            return Mathf.Clamp(current / milestoneStep, 0, colorCount - 1);

        return Mathf.Max(0, current / milestoneStep);
    }
}
