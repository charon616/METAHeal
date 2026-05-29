using UnityEngine;

/// <summary>
/// Procedurally scales bottom + left/right wall transforms to form a U-shaped reel that fits a target length.
/// Assumes the root extends along local +Z.
/// </summary>
public class ProceduralReelUShape : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Parts")]
    [SerializeField] Transform bottom;
    [SerializeField] Transform leftWall;
    [SerializeField] Transform rightWall;

    [Header("Look")]
    [SerializeField]
    Color surfaceColor = Color.white;

    [Header("Shape (meters)")]
    [Min(0.001f)]
    [SerializeField] float innerWidth = 0.2f;
    [Min(0.001f)]
    [SerializeField] float wallThickness = 0.01f;
    [Min(0.001f)]
    [SerializeField] float wallHeight = 0.05f;

    [Header("Length")]
    [Min(0.001f)]
    [SerializeField] float currentLength = 0.3f;
    [Min(0.001f)]
    [SerializeField] float minLength = 0.05f;
    [Min(0.001f)]
    [SerializeField] float maxLength = 3f;

    void Awake()
    {
        ApplySurfaceColor();
    }

    void OnValidate()
    {
        ApplyLength(currentLength);
        ApplySurfaceColor();
    }

    [ContextMenu("Apply Current Length")]
    public void ApplyCurrentLength()
    {
        ApplyLength(currentLength);
    }

    public void SetLength(float lengthMeters)
    {
        ApplyLength(lengthMeters);
    }

    void ApplyLength(float lengthMeters)
    {
        float l = Mathf.Clamp(lengthMeters, Mathf.Min(minLength, maxLength), Mathf.Max(minLength, maxLength));
        currentLength = l;

        ApplySurfaceColor();

        if (!bottom || !leftWall || !rightWall)
        {
            return;
        }

        float outerWidth = innerWidth + wallThickness * 2f;
        float zCenter = l * 0.5f;

        bottom.localPosition = new Vector3(0f, 0f, zCenter);
        bottom.localRotation = Quaternion.identity;
        bottom.localScale = new Vector3(outerWidth, wallThickness, l);

        float wallX = innerWidth * 0.5f + wallThickness * 0.5f;
        float wallY = wallThickness * 0.5f + wallHeight * 0.5f;
        Vector3 wallScale = new Vector3(wallThickness, wallHeight, l);

        leftWall.localPosition = new Vector3(-wallX, wallY, zCenter);
        leftWall.localRotation = Quaternion.identity;
        leftWall.localScale = wallScale;

        rightWall.localPosition = new Vector3(wallX, wallY, zCenter);
        rightWall.localRotation = Quaternion.identity;
        rightWall.localScale = wallScale;
    }

    void ApplySurfaceColor()
    {
        var block = new MaterialPropertyBlock();
        foreach (Transform part in new[] { bottom, leftWall, rightWall })
        {
            if (!part)
            {
                continue;
            }

            MeshRenderer mr = part.GetComponent<MeshRenderer>();
            if (!mr)
            {
                continue;
            }

            mr.GetPropertyBlock(block);
            block.SetColor(BaseColorId, surfaceColor);
            block.SetColor(ColorId, surfaceColor);
            mr.SetPropertyBlock(block);
        }
    }
}
