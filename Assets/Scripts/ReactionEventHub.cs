using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// Centralizes reaction events. Expansion can be either hand distance &gt; threshold, or (with <see cref="LineGuide"/>)
/// right hand past the right guide line (symmetric motion: only right hand vs right line).
/// Fires transition events once per enter/exit.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class ReactionEventHub : MonoBehaviour
{
    public enum ExpansionDetectionMode
    {
        [Tooltip("expanded = distance(left, right) > Expand Threshold")]
        HandDistanceThreshold,
        [Tooltip("expanded = right hand lateral past LineGuide right line (+ optional margin). Uses LineGuide's head/hands.")]
        RightHandOutsideGuideLine
    }

    [Header("Expansion Detection")]
    [Tooltip("Optional left/right transforms for hand-distance mode and CurrentHandDistance.")]
    [SerializeField] Transform leftHand;
    [SerializeField] Transform rightHand;
    [SerializeField] ExpansionDetectionMode expansionMode = ExpansionDetectionMode.RightHandOutsideGuideLine;
    [Tooltip("Hand-distance mode: expanded when distance exceeds this (meters).")]
    [SerializeField] float expandThreshold = 0.5f;
    [Tooltip("Guide mode: extra meters beyond the guide half-width before counting as expanded.")]
    [SerializeField] float expandGuideExtraMarginM = 0f;
    [Tooltip("When on, evaluates expansion each LateUpdate.")]
    [SerializeField] bool monitorExpansion = true;

    [Header("Layout lock (optional)")]
    [Tooltip("When set, DecideLayoutFromHub locks spawn line to current hand pose.")]
    [SerializeField] HandBetweenModelSpawner modelSpawner;
    [Tooltip("Guide geometry + DecideLayoutFromHub; also used when Expansion Mode is Right Hand Outside Guide Line.")]
    [SerializeField] LineGuide lineGuide;

    [Header("Expansion Transition Events")]
    [Tooltip("Fires once when expanding (enter expanded state).")]
    [FormerlySerializedAs("onExpanded")]
    [SerializeField] UnityEvent onExpandStart;
    [Tooltip("Fires once when contracting (leave expanded state).")]
    [FormerlySerializedAs("onContracted")]
    [SerializeField] UnityEvent onExpandEnd;

    bool _isExpanded;
    bool _initialized;

    /// <summary>Last distance between hub left/right hands (m). Updated when both assigned.</summary>
    public float CurrentHandDistance { get; private set; }

    void LateUpdate()
    {
        if (leftHand != null && rightHand != null)
            CurrentHandDistance = Vector3.Distance(leftHand.position, rightHand.position);
        else
            CurrentHandDistance = 0f;

        if (!monitorExpansion) return;

        bool expandedNow;
        switch (expansionMode)
        {
            case ExpansionDetectionMode.RightHandOutsideGuideLine:
                expandedNow = lineGuide != null && lineGuide.IsRightHandOutsideRightGuide(expandGuideExtraMarginM);
                break;
            default:
                if (leftHand == null || rightHand == null) return;
                expandedNow = CurrentHandDistance > expandThreshold;
                break;
        }

        if (!_initialized)
        {
            _initialized = true;
            _isExpanded = expandedNow;
            return;
        }

        if (expandedNow == _isExpanded) return;

        _isExpanded = expandedNow;
        if (expandedNow) onExpandStart?.Invoke();
        else onExpandEnd?.Invoke();
    }

    public bool IsExpanded => _isExpanded;

    /// <summary>Debug / event entry: lock both spawner spawn segment and LineGuide geometry to current poses.</summary>
    public void DecideLayoutFromHub()
    {
        if (modelSpawner != null)
            modelSpawner.DecidePosition();
        if (lineGuide != null)
            lineGuide.DecideLayout();
    }

    /// <summary>Resume following hands for both.</summary>
    public void UnlockLayoutFromHub()
    {
        if (modelSpawner != null)
            modelSpawner.UnlockDecidedSpawnLine();
        if (lineGuide != null)
            lineGuide.UnlockGuideLayout();
    }
}
