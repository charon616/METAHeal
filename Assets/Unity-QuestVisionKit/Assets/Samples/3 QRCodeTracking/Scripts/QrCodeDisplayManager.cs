using System.Collections.Generic;
using Meta.XR;
using UnityEngine;

public class QrCodeDisplayManager : MonoBehaviour
{
#if ZXING_ENABLED
    QRCodePrefabSpawner _qrSpawner;
    EnvironmentRaycastManager _envRaycastManager;
    readonly Dictionary<string, MarkerController> _activeMarkers = new();

    void Awake()
    {
        _qrSpawner = GetComponent<QRCodePrefabSpawner>();
        _envRaycastManager = GetComponent<EnvironmentRaycastManager>();
    }

    void Update() => RefreshMarkers();

    async void RefreshMarkers()
    {
        if (!_envRaycastManager || !_qrSpawner)
        {
            return;
        }

        QrCodeResult[] qrResults = await _qrSpawner.ScanVisionKitFrameAsync();
        if (qrResults == null || qrResults.Length == 0)
        {
            CleanupInactiveMarkers();
            return;
        }

        foreach (QrCodeResult qrResult in qrResults)
        {
            if (!TryRaycastCenterPoint(qrResult, out Vector3 worldPos))
            {
                continue;
            }

            MarkerController marker = GetOrCreateMarker(qrResult.text);
            if (!marker)
            {
                continue;
            }

            marker.UpdateMarker(worldPos, Quaternion.identity, Vector3.one * 0.15f, qrResult.text);
        }

        CleanupInactiveMarkers();
    }

    bool TryRaycastCenterPoint(QrCodeResult result, out Vector3 worldPos)
    {
        worldPos = default;
        if (result?.corners == null || result.corners.Length == 0)
        {
            return false;
        }

        Vector2 centerUv = Vector2.zero;
        foreach (Vector3 c in result.corners)
        {
            centerUv += new Vector2(c.x, c.y);
        }

        centerUv /= result.corners.Length;

        Ray ray = BuildWorldRay(result, centerUv);
        if (!_envRaycastManager.Raycast(ray, out EnvironmentRaycastHit hit))
        {
            return false;
        }

        worldPos = hit.point;
        return true;
    }

    static Vector2 ToViewport(Vector2 uv) => new(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));

    static Ray BuildWorldRay(QrCodeResult result, Vector2 uv)
    {
        Vector2 viewport = ToViewport(uv);
        PassthroughCameraAccess.CameraIntrinsics intrinsics = result.Intrinsics;
        var sensorResolution = (Vector2)intrinsics.SensorResolution;
        Vector2 currentResolution = (Vector2)result.captureResolution;
        if (currentResolution == Vector2.zero)
        {
            currentResolution = sensorResolution;
        }

        Rect crop = ComputeSensorCrop(sensorResolution, currentResolution);
        var sensorPoint = new Vector2(
            crop.x + crop.width * viewport.x,
            crop.y + crop.height * viewport.y);

        var localDirection = new Vector3(
            (sensorPoint.x - intrinsics.PrincipalPoint.x) / intrinsics.FocalLength.x,
            (sensorPoint.y - intrinsics.PrincipalPoint.y) / intrinsics.FocalLength.y,
            1f).normalized;

        Vector3 worldDirection = result.cameraPose.rotation * localDirection;
        return new Ray(result.cameraPose.position, worldDirection);
    }

    static Rect ComputeSensorCrop(Vector2 sensorResolution, Vector2 currentResolution)
    {
        if (sensorResolution == Vector2.zero)
        {
            return new Rect(0, 0, currentResolution.x, currentResolution.y);
        }

        var scaleFactor = new Vector2(
            currentResolution.x / sensorResolution.x,
            currentResolution.y / sensorResolution.y);
        float maxScale = Mathf.Max(scaleFactor.x, scaleFactor.y);
        if (maxScale <= 0)
        {
            maxScale = 1f;
        }

        scaleFactor /= maxScale;

        return new Rect(
            sensorResolution.x * (1f - scaleFactor.x) * 0.5f,
            sensorResolution.y * (1f - scaleFactor.y) * 0.5f,
            sensorResolution.x * scaleFactor.x,
            sensorResolution.y * scaleFactor.y);
    }

    MarkerController GetOrCreateMarker(string key)
    {
        if (_activeMarkers.TryGetValue(key, out MarkerController marker))
        {
            return marker;
        }

        GameObject markerGo = MarkerPool.Instance ? MarkerPool.Instance.GetMarker() : null;
        if (!markerGo)
        {
            return null;
        }

        marker = markerGo.GetComponent<MarkerController>();
        if (!marker)
        {
            return null;
        }

        _activeMarkers[key] = marker;
        return marker;
    }

    void CleanupInactiveMarkers()
    {
        var keysToRemove = new List<string>();
        foreach (KeyValuePair<string, MarkerController> kvp in _activeMarkers)
        {
            if (!kvp.Value || !kvp.Value.gameObject.activeSelf)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (string key in keysToRemove)
        {
            _activeMarkers.Remove(key);
        }
    }
#endif
}
