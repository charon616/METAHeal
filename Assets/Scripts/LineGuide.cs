using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Two parallel horizontal LineRenderers: symmetric about the axis from body center (head XZ, avg hand Y) to hands midpoint.
/// Lock with <see cref="DecideLayout"/> to freeze endpoints.
/// </summary>
public class LineGuide : MonoBehaviour
{
    [Header("References")]
    [Tooltip("HMD / Center Eye anchor.")]
    [SerializeField] Transform head;
    [SerializeField] Transform leftHand;
    [SerializeField] Transform rightHand;
    [FormerlySerializedAs("line")]
    [SerializeField] LineRenderer lineRight;
    [SerializeField] LineRenderer lineLeft;

    [Header("Segments")]
    [Tooltip("Total span (m) along the body→hands direction. Each line is centered on the body column (half extends toward hands, half the opposite way).")]
    [SerializeField] float length = 0.4f;
    [Tooltip("Project segment onto horizontal plane (same Y at both ends).")]
    [SerializeField] bool lineSegmentWorldHorizontal = true;
    [Tooltip("While unlocked, refresh line geometry each LateUpdate.")]
    [FormerlySerializedAs("driveGuideEveryFrameWhileUnlocked")]
    [SerializeField] bool updateGuideWhenUnlocked = true;

    [Header("Offset from body–hands axis")]
    [FormerlySerializedAs("lateralElbowDistance")]
    [SerializeField] float lineOffsetFromAxisM = 0.22f;
    [FormerlySerializedAs("scaleLateralWithHandSpread")]
    [SerializeField] bool scaleLineOffsetWithHandSpread = true;
    [SerializeField] float handSpreadMinMeters = 0.25f;
    [SerializeField] float handSpreadMaxMeters = 1f;
    [FormerlySerializedAs("lateralElbowMinM")]
    [SerializeField] float lineOffsetMinM = 0.15f;
    [FormerlySerializedAs("lateralElbowMaxM")]
    [SerializeField] float lineOffsetMaxM = 0.35f;

    [Header("Colors")]
    [SerializeField] Color normalColor = Color.white;
    [SerializeField] Color expandedColor = Color.green;

    [Header("Debug")]
    [FormerlySerializedAs("showShoulderSpheres")]
    [SerializeField] bool showAnchorSpheres;
    [FormerlySerializedAs("shoulderSphereRadius")]
    [SerializeField] float anchorSphereRadius = 0.04f;
    [FormerlySerializedAs("leftShoulderDebugColor")]
    [SerializeField] Color leftAnchorColor = new Color(0.2f, 0.6f, 1f, 0.85f);
    [FormerlySerializedAs("rightShoulderDebugColor")]
    [SerializeField] Color rightAnchorColor = new Color(1f, 0.55f, 0.2f, 0.85f);

    bool _guideInitialized;
    bool _layoutLocked;
    Vector3 _lockRight0;
    Vector3 _lockRight1;
    Vector3 _lockLeft0;
    Vector3 _lockLeft1;

    void Start() => SetColor(normalColor);

    void LateUpdate()
    {
        if (_layoutLocked)
        {
            ApplyLockedLineSegments();
            return;
        }

        if (head == null || leftHand == null || rightHand == null) return;

        if (!_guideInitialized)
        {
            UpdateGuide();
            _guideInitialized = true;
            return;
        }

        if (updateGuideWhenUnlocked)
            UpdateGuide();
    }

    /// <summary>Snapshot endpoints; stop following until <see cref="UnlockGuideLayout"/>.</summary>
    public void DecideLayout()
    {
        if (head == null || leftHand == null || rightHand == null) return;
        UpdateGuide();
        bool any = false;
        if (lineRight != null && lineRight.positionCount >= 2)
        {
            _lockRight0 = lineRight.GetPosition(0);
            _lockRight1 = lineRight.GetPosition(1);
            any = true;
        }
        if (lineLeft != null && lineLeft.positionCount >= 2)
        {
            _lockLeft0 = lineLeft.GetPosition(0);
            _lockLeft1 = lineLeft.GetPosition(1);
            any = true;
        }
        _layoutLocked = any;
    }

    public void UnlockGuideLayout()
    {
        _layoutLocked = false;
        if (head != null && leftHand != null && rightHand != null)
            UpdateGuide();
    }

    void ApplyLockedLineSegments()
    {
        if (lineRight != null)
        {
            lineRight.positionCount = 2;
            lineRight.SetPosition(0, _lockRight0);
            lineRight.SetPosition(1, _lockRight1);
        }
        if (lineLeft != null)
        {
            lineLeft.positionCount = 2;
            lineLeft.SetPosition(0, _lockLeft0);
            lineLeft.SetPosition(1, _lockLeft1);
        }
    }

    void SetLineWorldEndpoints(LineRenderer lr, Vector3 from, Vector3 to)
    {
        if (lr == null) return;
        if (lineSegmentWorldHorizontal)
        {
            var dH = Vector3.ProjectOnPlane(to - from, Vector3.up);
            if (dH.sqrMagnitude < 1e-8f)
            {
                if (head != null && TryGetHorizontalForward(head, out var fH))
                    dH = fH * length;
                else
                    dH = new Vector3(length, 0f, 0f);
            }
            to = from + dH;
        }
        lr.positionCount = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
    }

    static bool TryGetHorizontalForward(Transform h, out Vector3 fH)
    {
        fH = Vector3.ProjectOnPlane(h.forward, Vector3.up);
        if (fH.sqrMagnitude < 1e-6f)
        {
            fH = Vector3.forward;
            return false;
        }
        fH.Normalize();
        return true;
    }

    float EffectiveLineOffsetFromAxis()
    {
        if (!scaleLineOffsetWithHandSpread)
            return lineOffsetFromAxisM;

        float d = Vector3.Distance(leftHand.position, rightHand.position);
        float d0 = Mathf.Min(handSpreadMinMeters, handSpreadMaxMeters);
        float d1 = Mathf.Max(handSpreadMinMeters, handSpreadMaxMeters);
        float t = d1 > d0 ? Mathf.InverseLerp(d0, d1, Mathf.Clamp(d, d0, d1)) : 0f;
        float l0 = Mathf.Min(lineOffsetMinM, lineOffsetMaxM);
        float l1 = Mathf.Max(lineOffsetMinM, lineOffsetMaxM);
        return Mathf.Lerp(l0, l1, t);
    }

    bool TryGetBodyHandsParallelGuide(out Vector3 bodyCenterFlat, out Vector3 dirUnit, out Vector3 perpUnit)
    {
        bodyCenterFlat = Vector3.zero;
        dirUnit = Vector3.forward;
        perpUnit = Vector3.right;

        float avgY = 0.5f * (leftHand.position.y + rightHand.position.y);
        bodyCenterFlat = new Vector3(head.position.x, avgY, head.position.z);

        Vector3 mid = 0.5f * (leftHand.position + rightHand.position);
        Vector3 handsMidFlat = new Vector3(mid.x, avgY, mid.z);

        Vector3 along = Vector3.ProjectOnPlane(handsMidFlat - bodyCenterFlat, Vector3.up);
        if (along.sqrMagnitude < 1e-8f)
        {
            if (!TryGetHorizontalForward(head, out var fh))
                return false;
            along = Vector3.ProjectOnPlane(fh, Vector3.up);
        }
        if (along.sqrMagnitude < 1e-8f)
            return false;

        dirUnit = along.normalized;
        perpUnit = Vector3.Cross(Vector3.up, dirUnit).normalized;
        return true;
    }

    void UpdateGuide()
    {
        if (head == null || leftHand == null || rightHand == null) return;
        if (!TryGetBodyHandsParallelGuide(out var bodyFlat, out var dirUnit, out var perpUnit))
            return;

        float half = EffectiveLineOffsetFromAxis();
        Vector3 halfAlong = dirUnit * (length * 0.5f);
        Vector3 anchorR = bodyFlat + perpUnit * half;
        Vector3 anchorL = bodyFlat - perpUnit * half;
        if (lineRight != null)
            SetLineWorldEndpoints(lineRight, anchorR - halfAlong, anchorR + halfAlong);
        if (lineLeft != null)
            SetLineWorldEndpoints(lineLeft, anchorL - halfAlong, anchorL + halfAlong);
    }

    /// <summary>
    /// Horizontal signed distance from the body–hands axis toward the <see cref="lineRight"/> side (along <c>perpUnit</c>).
    /// The right guide sits at approximately <paramref name="halfWidthFromAxis"/> along that direction from <c>bodyFlat</c>.
    /// Outside past the right line when lateral &gt; halfWidth (+ margin).
    /// </summary>
    public bool TryGetRightHandLateralVsGuide(out float lateralTowardRightLineSigned, out float halfWidthFromAxis)
    {
        lateralTowardRightLineSigned = 0f;
        halfWidthFromAxis = 0f;
        if (head == null || leftHand == null || rightHand == null) return false;
        if (!TryGetBodyHandsParallelGuide(out var bodyFlat, out _, out var perpUnit))
            return false;

        halfWidthFromAxis = EffectiveLineOffsetFromAxis();
        float avgY = 0.5f * (leftHand.position.y + rightHand.position.y);
        Vector3 rf = new Vector3(rightHand.position.x, avgY, rightHand.position.z);
        lateralTowardRightLineSigned = Vector3.Dot(rf - bodyFlat, perpUnit);
        return true;
    }

    /// <summary>
    /// True when the right hand is past <see cref="lineRight"/> (outside the corridor on that side).
    /// Uses the same geometry as live guide lines; symmetric motion assumed—only right hand vs right offset is evaluated.
    /// </summary>
    public bool IsRightHandOutsideRightGuide(float extraMarginMeters = 0f)
    {
        if (!TryGetRightHandLateralVsGuide(out float lateral, out float halfW))
            return false;
        return lateral > halfW + extraMarginMeters;
    }

    void SetColor(Color c)
    {
        if (lineRight != null)
        {
            lineRight.startColor = c;
            lineRight.endColor = c;
        }
        if (lineLeft != null)
        {
            lineLeft.startColor = c;
            lineLeft.endColor = c;
        }
    }

    public void SetExpandedState(bool expanded) => SetColor(expanded ? expandedColor : normalColor);
    public void SetExpandedColor() => SetExpandedState(true);
    public void SetNormalColor() => SetExpandedState(false);

    public void OnReactionRefreshGuide()
    {
        if (_layoutLocked) return;
        if (head == null || leftHand == null || rightHand == null) return;
        UpdateGuide();
    }

    public bool TryGetHorizontalGuideLineDirection(out Vector3 alongGuides)
    {
        alongGuides = Vector3.forward;

        static Vector3 SegDir(LineRenderer lr)
        {
            if (lr == null || lr.positionCount < 2) return Vector3.zero;
            Vector3 d = lr.GetPosition(1) - lr.GetPosition(0);
            d.y = 0f;
            return d;
        }

        Vector3 dR = SegDir(lineRight);
        Vector3 dL = SegDir(lineLeft);
        Vector3 d;
        if (dR.sqrMagnitude > 1e-8f && dL.sqrMagnitude > 1e-8f)
            d = 0.5f * (dR.normalized + dL.normalized);
        else if (dR.sqrMagnitude > 1e-8f)
            d = dR;
        else if (dL.sqrMagnitude > 1e-8f)
            d = dL;
        else
            return false;

        if (d.sqrMagnitude < 1e-8f) return false;
        alongGuides = d.normalized;
        return true;
    }

    public bool TryGetOrthogonalForwardToGuideLines(Vector3 fromPositionWorld, Transform faceToward, out Vector3 forwardWorld)
    {
        forwardWorld = Vector3.forward;
        if (!TryGetHorizontalGuideLineDirection(out var along)) return false;

        Vector3 leftOfAlong = Vector3.Cross(Vector3.up, along).normalized;
        Vector3 rightOfAlong = Vector3.Cross(along, Vector3.up).normalized;
        forwardWorld = leftOfAlong;

        if (faceToward != null)
        {
            Vector3 flat = faceToward.position - fromPositionWorld;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-8f)
            {
                flat.Normalize();
                forwardWorld = Vector3.Dot(rightOfAlong, flat) > Vector3.Dot(leftOfAlong, flat) ? rightOfAlong : leftOfAlong;
            }
        }

        return true;
    }

    void OnDrawGizmosSelected()
    {
        if (!showAnchorSpheres || head == null || leftHand == null || rightHand == null) return;
        if (!TryGetBodyHandsParallelGuide(out var bodyFlat, out _, out var perpU)) return;

        float half = EffectiveLineOffsetFromAxis();
        Gizmos.color = leftAnchorColor;
        Gizmos.DrawSphere(bodyFlat - perpU * half, anchorSphereRadius);
        Gizmos.color = rightAnchorColor;
        Gizmos.DrawSphere(bodyFlat + perpU * half, anchorSphereRadius);
    }
}
