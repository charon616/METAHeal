using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Spawns prefabs evenly between the hands.
/// Spawn line lies in the horizontal plane (parallel to ground): same Y on both ends (configurable blend of hand heights).
/// Yaw can follow <see cref="LineGuide"/> guide lines orthogonally when <see cref="lineGuide"/> is assigned.
/// Call <see cref="DecidePosition"/> to lock spawn endpoints; until then, <see cref="updateContinuously"/> keeps positions following hands.
/// Use <see cref="deferSpawnUntilDecidePosition"/> so nothing spawns until <see cref="ReactionEventHub.DecideLayoutFromHub"/> / <see cref="DecidePosition"/> (e.g. after GameController ready flow).
/// </summary>
[DefaultExecutionOrder(100)]
public class HandBetweenModelSpawner : MonoBehaviour
{
    enum HorizontalSpawnYMode
    {
        AverageHandsY,
        LeftHandY,
        RightHandY,
        MinHandsY,
        MaxHandsY
    }

    [Header("References")]
    [SerializeField] Transform leftHand;
    [SerializeField] Transform rightHand;
    [SerializeField] Transform head;
    [Tooltip("Used for orthogonal yaw vs Line Left / Line Right segments (see Use Line Guide Orthogonal Yaw). Not used for spawn positions.")]
    [SerializeField] LineGuide lineGuide;
    [Tooltip("Single fallback prefab when tier list is unused or a tier entry is null.")]
    [SerializeField] GameObject modelPrefab;
    [Tooltip("Read tier from count dial (uses modelPrefab only when unset).")]
    [SerializeField] CountDialDisplay countDial;
    [Tooltip("World prefabs per tier. When countDial is set, GetMilestoneTierIndex selects one; null entries fall back to modelPrefab.")]
    [SerializeField] List<GameObject> modelPrefabsByMilestoneTier = new List<GameObject>();
    [Tooltip("When set, distance uses ReactionEventHub.CurrentHandDistance (runs earlier via DefaultExecutionOrder). Otherwise uses leftHand/rightHand below.")]
    [SerializeField] ReactionEventHub reactionHub;
    [Tooltip("When set, each spawned instance's ReactionPulse is RegisterPulse'd here so ReactionEventHub → PlayAllRandomized can find them.")]
    [SerializeField] ReactionScalePulseGroup scalePulseGroup;

    [Header("Spawn Settings")]
    [Min(1)]
    [SerializeField] int spawnCount = 3;
    [Tooltip("When true and spawn line is not locked, Regenerate runs every frame so instances follow hands. When false, positions stay fixed but regenerate only on dial tier change.")]
    [SerializeField] bool updateContinuously = true;
    [Tooltip("When on, do not spawn in Start/Update until ReactionEventHub.DecideLayoutFromHub → DecidePosition (explicit Regenerate() still works).")]
    [SerializeField] bool deferSpawnUntilDecidePosition;
    [Tooltip("When true, spawn segment is projected to the horizontal plane (constant Y along the line).")]
    [SerializeField] bool spawnOnHorizontalGroundLine = true;
    [SerializeField] HorizontalSpawnYMode horizontalSpawnYMode = HorizontalSpawnYMode.AverageHandsY;
    [Tooltip("When LineGuide is set: model forward is horizontal and perpendicular to guide line segments (Line Left / Line Right).")]
    [SerializeField] bool useLineGuideOrthogonalYaw = true;
    [Tooltip("When on, keep using the last valid guide-orthogonal yaw if guide direction is temporarily unavailable.")]
    [SerializeField] bool keepLastValidGuideYaw = true;
    [Tooltip("If orthogonal yaw fails or LineGuide unset, fall back to head-facing / hand-line behavior.")]
    [SerializeField] bool fallbackHeadFacingWhenNoGuide = true;
    [Tooltip("If true and head is assigned, model forward aligns with -head.forward (when guide yaw is off or unavailable).")]
    [FormerlySerializedAs("faceHead")]
    [SerializeField] bool alignOppositeHeadForward = true;
    [Tooltip("If alignOppositeHeadForward is off and head is set: face perpendicular to spawn axis toward head.")]
    [FormerlySerializedAs("alignToHandDirection")]
    [SerializeField] bool fallbackAlignAlongHands = true;
    [Tooltip("Additional yaw offset in degrees.")]
    [SerializeField] float yawOffsetDegrees = -90f;
    [Tooltip("Flip 180° when model forward points the wrong way.")]
    [SerializeField] bool reverseFacingDirection = true;
    [Header("Spawn Offset")]
    [Tooltip("Push spawn line away from body along horizontal head→hand-line direction (+ = farther from body).")]
    [SerializeField] float awayFromBodyOffsetMeters = 0f;
    [Tooltip("Additional world-space spawn offset.")]
    [SerializeField] Vector3 additionalWorldSpawnOffset = Vector3.zero;

    [Header("Hand distance (absolute, m) → relative scale on X")]
    [Tooltip("Absolute distance between hands (meters). Maps to Relative Scale X Min.")]
    [FormerlySerializedAs("minValue")]
    [SerializeField] float handDistanceMinMeters = 0.3f;
    [Tooltip("Absolute distance between hands (meters). Maps to Relative Scale X Max.")]
    [FormerlySerializedAs("maxValue")]
    [SerializeField] float handDistanceMaxMeters = 1f;
    [Tooltip("Relative multiplier (not world size): final localScale.x = prefab's original localScale.x × this, at handDistanceMinMeters.")]
    [FormerlySerializedAs("scaleXMin")]
    [SerializeField] float relativeScaleXAtHandMin = 0.5f;
    [Tooltip("Relative multiplier (not world size): final localScale.x = prefab's original localScale.x × this, at handDistanceMaxMeters.")]
    [FormerlySerializedAs("scaleXMax")]
    [SerializeField] float relativeScaleXAtHandMax = 1.5f;

    [Header("Scene view preview (edit mode)")]
    [SerializeField] bool showDistanceScalePreview = true;
    [SerializeField] float previewHandDistance = 0.55f;
    [SerializeField] float previewGizmoRadius = 0.12f;

    readonly List<GameObject> _spawned = new List<GameObject>();
    Vector3 _prefabLocalScale = Vector3.one;

    bool _spawnLineLocked;
    Vector3 _lockedA;
    Vector3 _lockedB;
    bool _hasLastGuideForward;
    Vector3 _lastGuideForward = Vector3.forward;

    /// <summary>Last tier used for spawning; when <see cref="updateContinuously"/> is off, tier changes trigger <see cref="Regenerate"/>.</summary>
    int _cachedDialTierForSpawn = int.MinValue;

    /// <summary>When <see cref="deferSpawnUntilDecidePosition"/> is on, set true by <see cref="DecidePosition"/> before first spawn.</summary>
    bool _decidePositionReceived;

    /// <summary>When spawn line is locked, reuse first computed yaw so tier changes do not affect orientation.</summary>
    bool _lockedSpawnRotationValid;
    Quaternion _lockedSpawnRotation = Quaternion.identity;

    void Awake()
    {
        if (modelPrefabsByMilestoneTier != null && modelPrefabsByMilestoneTier.Count > 0 && countDial == null)
            Debug.LogWarning($"{nameof(HandBetweenModelSpawner)} on \"{name}\": {nameof(modelPrefabsByMilestoneTier)} has entries but {nameof(countDial)} is unassigned. Using {nameof(modelPrefab)} only.", this);

        var p = GetActiveSpawnPrefab();
        if (p != null)
            _prefabLocalScale = p.transform.localScale;
    }

    GameObject GetActiveSpawnPrefab()
    {
        if (countDial != null && modelPrefabsByMilestoneTier != null && modelPrefabsByMilestoneTier.Count > 0)
        {
            int ti = Mathf.Clamp(countDial.GetMilestoneTierIndex(), 0, modelPrefabsByMilestoneTier.Count - 1);
            var tierPrefab = modelPrefabsByMilestoneTier[ti];
            if (tierPrefab != null)
                return tierPrefab;
        }

        return modelPrefab;
    }

    void Start()
    {
        if (!deferSpawnUntilDecidePosition)
            Regenerate();
        ApplyDistanceScaleToSpawned();
    }

    void Update()
    {
        if (deferSpawnUntilDecidePosition && !_decidePositionReceived)
            return;

        bool followHandsEachFrame = updateContinuously && !_spawnLineLocked;
        if (followHandsEachFrame)
            Regenerate();
        else
            MaybeRegenerateWhenDialTierChanged();
    }

    void MaybeRegenerateWhenDialTierChanged()
    {
        if (deferSpawnUntilDecidePosition && !_decidePositionReceived)
            return;

        if (countDial == null || modelPrefabsByMilestoneTier == null || modelPrefabsByMilestoneTier.Count == 0)
            return;

        int tier = Mathf.Clamp(countDial.GetMilestoneTierIndex(), 0, modelPrefabsByMilestoneTier.Count - 1);
        if (tier == _cachedDialTierForSpawn)
            return;

        if (GetActiveSpawnPrefab() == null)
            return;

        Regenerate();
    }

    void LateUpdate()
    {
        ApplyDistanceScaleToSpawned();
    }

    void OnValidate()
    {
        if (!Application.isPlaying) return;
        ApplyDistanceScaleToSpawned();
    }

    /// <summary>Caches current horizontal spawn segment and stops following hands until unlocked.</summary>
    public void DecidePosition()
    {
        if (!TryComputeHandsSpawnSegment(out var a, out var b)) return;
        _lockedA = a;
        _lockedB = b;
        _spawnLineLocked = true;
        _lockedSpawnRotationValid = false;
        _decidePositionReceived = true;
        Regenerate();
    }

    /// <summary>Clears lock so spawn line tracks hands again.</summary>
    public void UnlockDecidedSpawnLine()
    {
        _spawnLineLocked = false;
        _lockedSpawnRotationValid = false;
        _decidePositionReceived = true;
        Regenerate();
    }

    [ContextMenu("Decide Position")]
    void ContextDecidePosition() => DecidePosition();

    [ContextMenu("Unlock Spawn Line")]
    void ContextUnlockSpawnLine() => UnlockDecidedSpawnLine();

    [ContextMenu("Regenerate")]
    public void Regenerate()
    {
        var prefab = GetActiveSpawnPrefab();
        if (prefab == null) return;

        _prefabLocalScale = prefab.transform.localScale;

        ClearSpawned();

        if (!TryGetSpawnAxis(out var a, out var b)) return;

        Quaternion rotBatch = GetSpawnRotationForBatch(a, b);

        for (int i = 1; i <= spawnCount; i++)
        {
            float t = i / (float)(spawnCount + 1);
            Vector3 p = Vector3.Lerp(a, b, t) + ComputeSpawnOffsetWorld(a, b);
            GameObject go = Instantiate(prefab, p, rotBatch, transform);
            _spawned.Add(go);

            if (scalePulseGroup != null)
            {
                var pulse = go.GetComponent<ReactionPulse>();
                if (pulse != null)
                    scalePulseGroup.RegisterPulse(pulse);
            }
        }

        ApplyDistanceScaleToSpawned();

        if (countDial != null && modelPrefabsByMilestoneTier != null && modelPrefabsByMilestoneTier.Count > 0)
            _cachedDialTierForSpawn = Mathf.Clamp(countDial.GetMilestoneTierIndex(), 0, modelPrefabsByMilestoneTier.Count - 1);
    }

    float GetHorizontalY(Vector3 leftPos, Vector3 rightPos)
    {
        switch (horizontalSpawnYMode)
        {
            case HorizontalSpawnYMode.LeftHandY:
                return leftPos.y;
            case HorizontalSpawnYMode.RightHandY:
                return rightPos.y;
            case HorizontalSpawnYMode.MinHandsY:
                return Mathf.Min(leftPos.y, rightPos.y);
            case HorizontalSpawnYMode.MaxHandsY:
                return Mathf.Max(leftPos.y, rightPos.y);
            default:
                return 0.5f * (leftPos.y + rightPos.y);
        }
    }

    Vector3 ComputeSpawnOffsetWorld(Vector3 a, Vector3 b)
    {
        Vector3 offset = additionalWorldSpawnOffset;
        if (Mathf.Abs(awayFromBodyOffsetMeters) <= 1e-5f)
            return offset;

        Vector3 dir = Vector3.zero;
        if (head != null)
        {
            Vector3 center = 0.5f * (a + b);
            dir = Vector3.ProjectOnPlane(center - head.position, Vector3.up);

            if (dir.sqrMagnitude < 1e-8f)
                dir = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        }

        if (dir.sqrMagnitude < 1e-8f)
            dir = Vector3.forward;
        else
            dir.Normalize();

        return offset + dir * awayFromBodyOffsetMeters;
    }

    bool TryComputeHandsSpawnSegment(out Vector3 a, out Vector3 b)
    {
        a = Vector3.zero;
        b = Vector3.zero;
        if (leftHand == null || rightHand == null) return false;

        Vector3 L = leftHand.position;
        Vector3 R = rightHand.position;

        if (spawnOnHorizontalGroundLine)
        {
            float y = GetHorizontalY(L, R);
            a = new Vector3(L.x, y, L.z);
            b = new Vector3(R.x, y, R.z);
        }
        else
        {
            a = L;
            b = R;
        }

        return (b - a).sqrMagnitude > 1e-8f;
    }

    bool TryGetSpawnAxis(out Vector3 a, out Vector3 b)
    {
        if (_spawnLineLocked)
        {
            a = _lockedA;
            b = _lockedB;
            return (b - a).sqrMagnitude > 1e-8f;
        }

        return TryComputeHandsSpawnSegment(out a, out b);
    }

    /// <summary>
    /// Computes orientation once from segment midpoint. When <see cref="_spawnLineLocked"/>, caches first Quaternion for tier-change respawns.
    /// </summary>
    Quaternion GetSpawnRotationForBatch(Vector3 a, Vector3 b)
    {
        if (_spawnLineLocked && _lockedSpawnRotationValid)
            return _lockedSpawnRotation;

        Vector3 mid = Vector3.Lerp(a, b, 0.5f);
        bool hasSharedGuideForward = TryGetSharedGuideForward(a, b, out var sharedGuideForward);
        Quaternion rot = ComputeRotation(mid, a, b, hasSharedGuideForward, sharedGuideForward);

        if (_spawnLineLocked)
        {
            _lockedSpawnRotation = rot;
            _lockedSpawnRotationValid = true;
        }

        return rot;
    }

    float GetHandDistanceWorld()
    {
        if (reactionHub != null)
            return reactionHub.CurrentHandDistance;
        if (leftHand != null && rightHand != null)
            return Vector3.Distance(leftHand.position, rightHand.position);
        return 0f;
    }

    float EvaluateScaleX(float handDistance)
    {
        float d0 = Mathf.Min(handDistanceMinMeters, handDistanceMaxMeters);
        float d1 = Mathf.Max(handDistanceMinMeters, handDistanceMaxMeters);
        float t = d1 > d0 ? Mathf.InverseLerp(d0, d1, Mathf.Clamp(handDistance, d0, d1)) : 0f;
        float sx0 = Mathf.Min(relativeScaleXAtHandMin, relativeScaleXAtHandMax);
        float sx1 = Mathf.Max(relativeScaleXAtHandMin, relativeScaleXAtHandMax);
        return Mathf.Lerp(sx0, sx1, t);
    }

    void ApplyDistanceScaleToSpawned()
    {
        if (_spawned.Count == 0) return;
        float d = GetHandDistanceWorld();
        float mulX = EvaluateScaleX(d);
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] == null) continue;
            Transform t = _spawned[i].transform;
            Vector3 ls = t.localScale;
            ls.x = _prefabLocalScale.x * mulX;
            t.localScale = ls;
        }
    }

    bool TryGetSharedGuideForward(Vector3 axisFrom, Vector3 axisTo, out Vector3 forward)
    {
        forward = Vector3.forward;
        if (!useLineGuideOrthogonalYaw || lineGuide == null) return false;

        Vector3 axisMid = 0.5f * (axisFrom + axisTo);
        if (lineGuide.TryGetOrthogonalForwardToGuideLines(axisMid, head, out forward))
        {
            _lastGuideForward = forward;
            _hasLastGuideForward = true;
            return true;
        }

        if (keepLastValidGuideYaw && _hasLastGuideForward)
        {
            forward = _lastGuideForward;
            return true;
        }

        return false;
    }

    Quaternion ComputeRotation(Vector3 spawnPos, Vector3 axisFrom, Vector3 axisTo, bool hasSharedGuideForward, Vector3 sharedGuideForward)
    {
        float finalYawOffset = yawOffsetDegrees + (reverseFacingDirection ? 180f : 0f);

        if (hasSharedGuideForward)
        {
            return Quaternion.LookRotation(sharedGuideForward.normalized, Vector3.up) * Quaternion.Euler(0f, finalYawOffset, 0f);
        }

        if (!fallbackHeadFacingWhenNoGuide)
            return Quaternion.identity * Quaternion.Euler(0f, finalYawOffset, 0f);

        Quaternion baseRot = Quaternion.identity;

        if (alignOppositeHeadForward && head != null)
        {
            Vector3 oppositeHeadForward = -head.forward;
            if (oppositeHeadForward.sqrMagnitude > 1e-8f)
                baseRot = Quaternion.LookRotation(oppositeHeadForward.normalized, Vector3.up);
        }
        else if (head != null)
        {
            Vector3 span = axisTo - axisFrom;
            float len2 = span.sqrMagnitude;
            if (len2 > 1e-8f)
            {
                Vector3 u = span * (1f / Mathf.Sqrt(len2));
                float t = Vector3.Dot(head.position - axisFrom, u);
                Vector3 closestOnLine = axisFrom + t * u;
                Vector3 towardHead = head.position - closestOnLine;
                if (towardHead.sqrMagnitude > 1e-8f)
                    baseRot = Quaternion.LookRotation(towardHead.normalized, Vector3.up);
                else if (fallbackAlignAlongHands)
                    baseRot = Quaternion.LookRotation(u, Vector3.up);
            }
        }
        else if (fallbackAlignAlongHands)
        {
            Vector3 handDir = axisTo - axisFrom;
            if (handDir.sqrMagnitude > 1e-8f)
                baseRot = Quaternion.LookRotation(handDir.normalized, Vector3.up);
        }

        return baseRot * Quaternion.Euler(0f, finalYawOffset, 0f);
    }

    void OnDrawGizmosSelected()
    {
        if (!showDistanceScalePreview) return;
        if (!TryGetSpawnAxis(out var a, out var b)) return;
        bool hasSharedGuideForward = TryGetSharedGuideForward(a, b, out var sharedGuideForward);

        float d = Application.isPlaying ? GetHandDistanceWorld() : Mathf.Max(0f, previewHandDistance);
        float mulX = EvaluateScaleX(d);
        float r = Mathf.Max(0.01f, previewGizmoRadius);
        Color fill = new Color(0.25f, 0.85f, 1f, 0.35f);
        Color wire = new Color(0.2f, 0.75f, 1f, 0.95f);

        for (int i = 1; i <= spawnCount; i++)
        {
            float t = i / (float)(spawnCount + 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            Quaternion rot = ComputeRotation(p, a, b, hasSharedGuideForward, sharedGuideForward);
            Vector3 size = new Vector3(mulX * r * 2f, r * 2f, r * 2f);

            Gizmos.matrix = Matrix4x4.TRS(p, rot, Vector3.one);
            Gizmos.color = fill;
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = wire;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    [ContextMenu("Clear Spawned")]
    public void ClearSpawned()
    {
        if (scalePulseGroup != null)
            scalePulseGroup.ClearRegisteredPulses();

        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null) Destroy(_spawned[i]);
        }
        _spawned.Clear();
    }
}
