using System;
using Meta.XR;
using UnityEngine;

/// <summary>
/// Decode result types. Frame capture and ZXing processing are integrated in <see cref="QRCodePrefabSpawner"/>.
/// </summary>
public enum QrCodeDetectionMode
{
    Single,
    Multiple
}

[Serializable]
public class QrCodeResult
{
    public string text;
    public Vector3[] corners;
    public Pose cameraPose;
    public PassthroughCameraAccess.CameraIntrinsics Intrinsics;
    public Vector2Int captureResolution;
}
