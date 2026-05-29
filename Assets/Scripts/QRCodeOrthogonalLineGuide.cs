using UnityEngine;

/// <summary>
/// Draws one horizontal LineRenderer through the QR target, perpendicular to the head→QR direction.
/// Line Y is fixed at the average of head and QR heights.
/// </summary>
public class QRCodeOrthogonalLineGuide : MonoBehaviour
{
    [Header("References")]
    [Tooltip("HMD / Center Eye anchor.")]
    [SerializeField] Transform head;
    [Tooltip("Transform representing the current QR code world position.")]
    [SerializeField] Transform qrTarget;
    [SerializeField] LineRenderer line;

    [Header("Line")]
    [Min(0.01f)]
    [SerializeField] float lineLength = 0.6f;
    [SerializeField] bool updateEveryLateUpdate = true;

    [Header("Fallback")]
    [Tooltip("Fallback horizontal direction when head↔QR distance is near zero.")]
    [SerializeField] Vector3 fallbackHorizontalDirection = Vector3.forward;

    void OnEnable()
    {
        UpdateLine();
    }

    void LateUpdate()
    {
        if (updateEveryLateUpdate)
        {
            UpdateLine();
        }
    }

    public void UpdateLine()
    {
        if (!line || !head || !qrTarget)
        {
            return;
        }

        float avgY = 0.5f * (head.position.y + qrTarget.position.y);
        Vector3 center = new Vector3(qrTarget.position.x, avgY, qrTarget.position.z);
        Vector3 headFlat = new Vector3(head.position.x, avgY, head.position.z);

        Vector3 headToQrHorizontal = center - headFlat;
        if (headToQrHorizontal.sqrMagnitude < 1e-8f)
        {
            headToQrHorizontal = Vector3.ProjectOnPlane(fallbackHorizontalDirection, Vector3.up);
            if (headToQrHorizontal.sqrMagnitude < 1e-8f)
            {
                headToQrHorizontal = Vector3.forward;
            }
        }

        headToQrHorizontal.Normalize();
        Vector3 orthogonal = Vector3.Cross(Vector3.up, headToQrHorizontal).normalized;

        float half = lineLength * 0.5f;
        Vector3 p0 = center - orthogonal * half;
        Vector3 p1 = center + orthogonal * half;

        line.positionCount = 2;
        line.SetPosition(0, p0);
        line.SetPosition(1, p1);
    }
}
