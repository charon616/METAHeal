using System;
using System.Collections.Generic;
using Meta.XR;
using Meta.XR.MRUtilityKit;
using Meta.XR.UnityQuestVisionKitSamples.QRCodeDetection;
using UnityEngine;
using UnityEngine.Rendering;
#if ZXING_ENABLED
using System.Threading.Tasks;
using ZXing;
using ZXing.Common;
using ZXing.Multi;
using ZXing.QrCode;
#endif

/// <summary>
/// Tracks QR codes (MRUK or passthrough camera + ZXing), spawns prefabs at QR world positions,
/// and drives reel visuals (ProceduralReelUShape) from head/QR geometry.
/// </summary>
public class QRCodePrefabSpawner : MonoBehaviour
{
    public enum QrWorldPoseSource
    {
        MrukQrTrackables,
        VisionKitPassthroughCamera,
    }

    enum ReelHeightMode
    {
        AverageHeadAndQr,
        UseHeadY,
        UseQrY,
        FixedY
    }

    enum ReelScaleAxis
    {
        X,
        Y,
        Z
    }

    enum ReelRotationMode
    {
        PitchOnlyFromHeight,
        YawFromXZ
    }

    [Header("QR pose source")]
    [SerializeField]
    QrWorldPoseSource qrWorldPoseSource = QrWorldPoseSource.VisionKitPassthroughCamera;

#if ZXING_ENABLED
    [SerializeField]
    EnvironmentRaycastManager visionKitEnvironmentRaycast;

    [Tooltip("Downsample divisor for PCA texture (higher = lighter but harder to read).")]
    [Min(1)]
    [SerializeField]
    int visionKitSampleFactor = 2;

    [SerializeField]
    QrCodeDetectionMode visionKitDetectionMode = QrCodeDetectionMode.Single;

    [Tooltip("Minimum interval (seconds) between Vision Kit frame decode calls.")]
    [Min(0.02f)]
    [SerializeField]
    float visionKitMinScanIntervalSeconds = 0.1f;

    [Tooltip("Tracking expires if no good pose is received within this many seconds.")]
    [Min(0.05f)]
    [SerializeField]
    float visionKitLostPoseTimeoutSeconds = 0.5f;
#endif

    [Header("References")]
    [Tooltip("Required for MrukQrTrackables. Optional when using Vision Kit only.")]
    [SerializeField]
    MRUK mruk;

    [Tooltip("Parent for spawned objects. World root when unset.")]
    [SerializeField]
    Transform spawnParent;

    [Header("Prefab sets")]
    [Tooltip("Prefabs spawned at smoothed QR position (disabled when empty). Cycled in order.")]
    [SerializeField]
    GameObject[] prefabsAtSmoothedQrPosition;

    [Header("Timing")]
    [Min(0f)]
    [SerializeField]
    float respawnCooldownSeconds = 5f;

    [Header("Spawn Limits")]
    [Tooltip("Max spawned children under spawnParent; oldest removed when exceeded. 0 or less = unlimited.")]
    [SerializeField]
    int maxSpawnedChildren = 5;
    [Header("Smoothing")]
    [Min(0f)]
    [SerializeField]
    float positionSmoothTimeSeconds = 0.03f;

    [Tooltip("Reset SmoothDamp velocity each frame (faster catch-up after jumps). Usually leave off.")]
    [SerializeField]
    bool resetSmoothVelocityWhenQrChanges = true;

    [Header("Filter")]
    [SerializeField]
    bool spawnOnlyWhileTracked = true;

    [SerializeField]
    bool copyQrRotation = true;

    [Header("Feature Toggles")]
    [SerializeField]
    bool enableSpawning = false;
    [SerializeField]
    bool enableReelControl = true;

    [Header("Spawn Position Source")]
    [Tooltip("When set, prefab spawn position/orientation uses this Transform. Does not affect reel or QR tracking.")]
    [SerializeField]
    Transform spawnPositionReference;

    [Header("QR Detection Visuals (optional)")]
    [SerializeField]
    bool showQrDebugVisuals = false;
    [Tooltip("Visual prefab spawned as child at tracking origin on QR detect. Vision Kit: child of position proxy.")]
    [SerializeField]
    GameObject qrCodeDetectedPrefab;
    [Tooltip("Optional Transform to follow the actual QR world position.")]
    [SerializeField]
    Transform qrCodeFollower;
    [SerializeField]
    bool followQrFollowerRotation = true;
    [Tooltip("When on, follower tracks smoothed position instead of raw position.")]
    [SerializeField]
    bool useSmoothedPositionForQrFollower;
    [Tooltip("Follower position offset for reel attachment. Change at runtime via SetQrFollowerTargetOffset().")]
    [SerializeField]
    Vector3 qrFollowerTargetOffset = Vector3.zero;

    [Header("Editor Fallback")]
    [Tooltip("In Editor Play mode, spawn periodically from spawnParent when no QR is detected.")]
    [SerializeField]
    bool spawnFromSpawnParentWhenNoQrInEditorPlay = true;

    [Header("Reel References")]
    [SerializeField]
    Transform head;
    [Tooltip("Transform moved/rotated as the toe root pivot. Defaults to this object when unset.")]
    [SerializeField]
    Transform reelRoot;
    [Tooltip("Visual scaled along length. Defaults to reelRoot when unset.")]
    [SerializeField]
    Transform reelVisual;
    [Tooltip("When set, reel length updates apply to this Procedural U-shape component.")]
    [SerializeField]
    Component proceduralReelUShape;
    [Tooltip("Rotation pivot marker under reelRoot. When set, reelRoot is shifted so this aligns with estimated toe root.")]
    [SerializeField]
    Transform reelPivotMarker;

    [Header("Reel Hip Estimation (XZ weighted midpoint)")]
    [Min(0f)]
    [SerializeField]
    float headWeight = 4f;
    [Min(0f)]
    [SerializeField]
    float qrWeight = 6f;
    [SerializeField]
    ReelHeightMode reelHeightMode = ReelHeightMode.AverageHeadAndQr;
    [SerializeField]
    float fixedReelY;
    [Tooltip("When on, overrides hip Y with head.y + hipHeadYOffsetMeters.")]
    [SerializeField]
    bool useHeadBasedHipY = true;
    [Tooltip("Y offset when useHeadBasedHipY is on. Negative = below head.")]
    [SerializeField]
    float hipHeadYOffsetMeters = -0.08f;

    [Header("Reel Length")]
    [Min(0.001f)]
    [SerializeField]
    float reelBaseLengthMetersAtScale1 = 1f;
    [SerializeField]
    ReelScaleAxis reelLengthScaleAxis = ReelScaleAxis.Z;
    [Min(0.01f)]
    [SerializeField]
    float reelMinLengthMeters = 0.1f;
    [Min(0.01f)]
    [SerializeField]
    float reelMaxLengthMeters = 3f;
    [SerializeField]
    Vector3 reelFallbackHorizontalDirection = Vector3.forward;
    [Tooltip("When on, adjusts reelVisual.localPosition to keep toe-root end fixed when length changes.")]
    [SerializeField]
    bool keepReelStartAnchoredWhenScaling = true;
    [Tooltip("Positive direction along length axis. Use -1 for reversed mesh.")]
    [SerializeField]
    float reelLengthAxisDirection = 1f;

    [Header("Reel Rotation")]
    [Tooltip("When on, aim directly along toe root → QR 3D vector (length matched to that segment).")]
    [SerializeField]
    bool directFitBetweenHipAndQr = true;
    [SerializeField]
    ReelRotationMode reelRotationMode = ReelRotationMode.PitchOnlyFromHeight;
    [SerializeField]
    float reelPitchMinDegrees = -45f;
    [SerializeField]
    float reelPitchMaxDegrees = 45f;

    MRUKTrackable _activeQr;

#if ZXING_ENABLED
    Transform _visionKitPoseProxy;
    bool _visionKitPoseValid;
    float _lastVisionKitPoseTime;
    bool _visionKitScanInFlight;
    float _nextVisionKitScanTime;
    GameObject _visionKitQrVisualInstance;

    PassthroughCameraAccess _passthroughCameraAccess;
    RenderTexture _visionKitDownsampleRt;
    ComputeShader _visionKitDownsampleCs;
    QRCodeReader _visionKitQrReader;
    bool _visionKitDecodeBusy;
    bool _visionKitPipelineReady;

    static readonly int Vk_Input1 = Shader.PropertyToID("_Input");
    static readonly int Vk_Output = Shader.PropertyToID("_Output");
    static readonly int Vk_InputWidth = Shader.PropertyToID("_InputWidth");
    static readonly int Vk_InputHeight = Shader.PropertyToID("_InputHeight");
    static readonly int Vk_OutputWidth = Shader.PropertyToID("_OutputWidth");
    static readonly int Vk_OutputHeight = Shader.PropertyToID("_OutputHeight");

    struct VisionKitCaptureFrame
    {
        public Texture Texture;
        public Pose Pose;
        public PassthroughCameraAccess.CameraIntrinsics Intrinsics;
        public Vector2Int Resolution;
    }
#endif

    Vector3 _smoothedWorldPosition;

    Vector3 _smoothVelocity;

    int _smoothPrefabIndex;
    float _nextAllowedSpawnTime;

    readonly List<MRUKTrackable> _trackableScratch = new();
    readonly Queue<GameObject> _spawnedQueue = new();
    readonly Dictionary<MRUKTrackable, GameObject> _qrVisualInstances = new();
    Vector3 _reelInitialVisualScale = Vector3.one;
    Vector3 _reelInitialVisualLocalPosition = Vector3.zero;
    Vector3 _lastValidReelDirection = Vector3.forward;
    Quaternion _reelBaseRotation = Quaternion.identity;
    bool _reelConfigWarningLogged;

    bool UsesMrukQrTrackables => qrWorldPoseSource == QrWorldPoseSource.MrukQrTrackables;

#if ZXING_ENABLED
    bool UsesVisionKitQr => qrWorldPoseSource == QrWorldPoseSource.VisionKitPassthroughCamera;
#else
    const bool UsesVisionKitQr = false;
#endif

#if UNITY_EDITOR
    void OnValidate()
    {
#if !ZXING_ENABLED
        if (qrWorldPoseSource == QrWorldPoseSource.VisionKitPassthroughCamera)
        {
            qrWorldPoseSource = QrWorldPoseSource.MrukQrTrackables;
        }
#endif
        if (!mruk && FindAnyObjectByType<MRUK>() is { } found && found.gameObject.scene == gameObject.scene)
        {
            mruk = found;
        }
#if ZXING_ENABLED
        if (!visionKitEnvironmentRaycast)
        {
            visionKitEnvironmentRaycast = GetComponent<EnvironmentRaycastManager>();
        }
#endif
    }
#endif

    void OnEnable()
    {
        if (!spawnParent)
        {
            spawnParent = transform;
        }

        if (!reelVisual && reelRoot)
        {
            reelVisual = reelRoot;
        }
        if (reelVisual)
        {
            _reelInitialVisualScale = reelVisual.localScale;
            _reelInitialVisualLocalPosition = reelVisual.localPosition;
        }
        if (!proceduralReelUShape && reelVisual)
        {
            proceduralReelUShape = reelVisual.GetComponent("ProceduralReelUShape");
        }
        if (reelRoot)
        {
            _reelBaseRotation = reelRoot.rotation;
        }

#if ZXING_ENABLED
        if (UsesVisionKitQr)
        {
            if (!showQrDebugVisuals && _visionKitQrVisualInstance)
            {
                Destroy(_visionKitQrVisualInstance);
                _visionKitQrVisualInstance = null;
            }
            var proxyGo = new GameObject("VisionKitQrPoseProxy");
            proxyGo.transform.SetParent(transform, false);
            _visionKitPoseProxy = proxyGo.transform;
            _visionKitPoseValid = false;
            _nextVisionKitScanTime = Time.time;
            VisionKitInitializePipeline();
        }
#endif

        if (UsesMrukQrTrackables)
        {
            if (!mruk)
            {
                Debug.LogWarning($"{nameof(QRCodePrefabSpawner)} on {name}: MRUK is not assigned.", this);
                return;
            }

            mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
            RefreshActiveQrFromScene();
        }
    }

    void OnDisable()
    {
        if (UsesMrukQrTrackables && mruk)
        {
            mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        }

#if ZXING_ENABLED
        VisionKitReleaseGpuResources();

        if (_visionKitPoseProxy)
        {
            Destroy(_visionKitPoseProxy.gameObject);
            _visionKitPoseProxy = null;
        }

        _visionKitScanInFlight = false;
#endif
    }

    void OnDestroy()
    {
        foreach (var kv in _qrVisualInstances)
        {
            if (kv.Value)
            {
                Destroy(kv.Value);
            }
        }
        _qrVisualInstances.Clear();

#if ZXING_ENABLED
        VisionKitReleaseGpuResources();
        if (_visionKitQrVisualInstance)
        {
            Destroy(_visionKitQrVisualInstance);
            _visionKitQrVisualInstance = null;
        }
#endif
    }

    void Update()
    {
#if ZXING_ENABLED
        if (UsesVisionKitQr)
        {
            if (!showQrDebugVisuals && _visionKitQrVisualInstance)
            {
                Destroy(_visionKitQrVisualInstance);
                _visionKitQrVisualInstance = null;
            }
            if (_visionKitPoseValid && Time.time - _lastVisionKitPoseTime > visionKitLostPoseTimeoutSeconds)
            {
                _visionKitPoseValid = false;
                if (_visionKitQrVisualInstance)
                {
                    Destroy(_visionKitQrVisualInstance);
                    _visionKitQrVisualInstance = null;
                }
            }

            if (_visionKitPipelineReady && visionKitEnvironmentRaycast &&
                !_visionKitScanInFlight && Time.time >= _nextVisionKitScanTime)
            {
                _visionKitScanInFlight = true;
                RunVisionKitScanAsync();
            }
        }
#endif

        Transform trackingSource = GetTrackingSourceTransform();

        // Follower updates independently of spawn eligibility.
        if (trackingSource)
        {
            Vector3 rawFollowerPos = trackingSource.position;
            _smoothedWorldPosition = SmoothOrSnap(_smoothedWorldPosition, rawFollowerPos);
            UpdateQrFollower(rawFollowerPos, trackingSource);
        }

        if (!TryResolveSpawnPose(out var rawWorld, out var rotation, out var hasActiveQr))
        {
            return;
        }

        // spawnPositionReference overrides spawn only; reel always uses actual QR tracking position.
        Vector3 qrWorldForReel = rawWorld;

        Quaternion spawnRotation = rotation;
        Vector3 spawnWorldPosition = _smoothedWorldPosition;
        if (spawnPositionReference)
        {
            spawnWorldPosition = spawnPositionReference.position;
            if (copyQrRotation)
            {
                spawnRotation = spawnPositionReference.rotation;
            }
        }

        if (!hasActiveQr)
        {
            // During editor fallback, pin to reference to avoid smoothing drift accumulation.
            _smoothedWorldPosition = rawWorld;
            _smoothVelocity = Vector3.zero;
        }

        if (enableReelControl)
        {
            UpdateReel(qrWorldForReel);
        }

        if (!enableSpawning)
        {
            return;
        }

        if (Time.time < _nextAllowedSpawnTime)
        {
            return;
        }

        bool spawnedAny = TrySpawnCycle(prefabsAtSmoothedQrPosition, spawnWorldPosition, spawnRotation, ref _smoothPrefabIndex);
        if (spawnedAny)
        {
            _nextAllowedSpawnTime = Time.time + Mathf.Max(0f, respawnCooldownSeconds);
        }
    }

#if ZXING_ENABLED
    void VisionKitInitializePipeline()
    {
        _passthroughCameraAccess = GetComponent<PassthroughCameraAccess>();
        _visionKitDownsampleCs = Resources.Load<ComputeShader>("Downsample");
        if (!_visionKitDownsampleCs)
        {
            Debug.LogError($"{nameof(QRCodePrefabSpawner)}: Downsample.compute not found in any Resources folder.", this);
        }

        _visionKitQrReader = new QRCodeReader();
        _visionKitPipelineReady = _passthroughCameraAccess && _visionKitDownsampleCs;
    }

    void VisionKitReleaseGpuResources()
    {
        if (_visionKitDownsampleRt)
        {
            _visionKitDownsampleRt.Release();
            Destroy(_visionKitDownsampleRt);
            _visionKitDownsampleRt = null;
        }
    }

    /// <summary>For QuestVisionKit samples: shares the same internal scan lock.</summary>
    public Task<QrCodeResult[]> ScanVisionKitFrameAsync() => VisionKitScanFrameAsyncInternal();

    async void RunVisionKitScanAsync()
    {
        try
        {
            if (!_visionKitPipelineReady || !visionKitEnvironmentRaycast || !_visionKitPoseProxy)
            {
                return;
            }

            QrCodeResult[] results = await VisionKitScanFrameAsyncInternal();
            _nextVisionKitScanTime = Time.time + Mathf.Max(0.02f, visionKitMinScanIntervalSeconds);

            if (results == null || results.Length == 0)
            {
                return;
            }

            foreach (QrCodeResult r in results)
            {
                if (TryVisionKitRaycastWorldPoint(r, out Vector3 world))
                {
                    _visionKitPoseProxy.SetPositionAndRotation(world, Quaternion.identity);
                    _visionKitPoseValid = true;
                    _lastVisionKitPoseTime = Time.time;
                    if (resetSmoothVelocityWhenQrChanges)
                    {
                        _smoothVelocity = Vector3.zero;
                    }

                    _smoothedWorldPosition = world;
                    ApplyQrFollowerAfterVisionKitWorldPoint(world);
                    if (showQrDebugVisuals)
                    {
                        EnsureVisionKitQrVisual();
                    }
                    break;
                }
            }
        }
        finally
        {
            _visionKitScanInFlight = false;
        }
    }

    async Task<QrCodeResult[]> VisionKitScanFrameAsyncInternal()
    {
        if (_visionKitDecodeBusy || !_visionKitDownsampleCs)
        {
            return Array.Empty<QrCodeResult>();
        }

        _visionKitDecodeBusy = true;
        try
        {
            VisionKitCaptureFrame frame = await VisionKitAcquireFrameAsync();
            (int targetWidth, int targetHeight) = VisionKitGetTargetDimensions(frame.Texture);
            if (!VisionKitEnsureDownsampleTarget(targetWidth, targetHeight))
            {
                return Array.Empty<QrCodeResult>();
            }

            VisionKitDispatchDownsample(frame.Texture, targetWidth, targetHeight);
            byte[] grayBytes = await VisionKitReadPixelsAsync(_visionKitDownsampleRt);
            if (grayBytes == null || grayBytes.Length == 0)
            {
                return Array.Empty<QrCodeResult>();
            }

            QrCodeResult[] decoded = await Task.Run(() => VisionKitDecodeFrame(frame, grayBytes, targetWidth, targetHeight));
            return decoded ?? Array.Empty<QrCodeResult>();
        }
        finally
        {
            _visionKitDecodeBusy = false;
        }
    }

    QrCodeResult VisionKitProcessDecodeResult(Result decodeResult, int targetWidth, int targetHeight, VisionKitCaptureFrame frame)
    {
        ResultPoint[] points = decodeResult.ResultPoints;
        var uvCorners = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            uvCorners[i] = new Vector3(points[i].X / targetWidth, points[i].Y / targetHeight, 0);
        }

        return new QrCodeResult
        {
            text = decodeResult.Text,
            corners = uvCorners,
            cameraPose = frame.Pose,
            Intrinsics = frame.Intrinsics,
            captureResolution = frame.Resolution
        };
    }

    Task<byte[]> VisionKitReadPixelsAsync(RenderTexture rt)
    {
        var tcs = new TaskCompletionSource<byte[]>();
        AsyncGPUReadback.Request(rt, 0, TextureFormat.R8, request =>
        {
            if (request.hasError)
            {
                tcs.SetException(new Exception("GPU readback error."));
            }
            else
            {
                tcs.SetResult(request.GetData<byte>().ToArray());
            }
        });
        return tcs.Task;
    }

    async Task<VisionKitCaptureFrame> VisionKitAcquireFrameAsync()
    {
        while (true)
        {
            if (_passthroughCameraAccess && _passthroughCameraAccess.IsPlaying)
            {
                Texture texture = _passthroughCameraAccess.GetTexture();
                if (texture)
                {
                    return new VisionKitCaptureFrame
                    {
                        Texture = texture,
                        Pose = _passthroughCameraAccess.GetCameraPose(),
                        Intrinsics = _passthroughCameraAccess.Intrinsics,
                        Resolution = _passthroughCameraAccess.CurrentResolution
                    };
                }
            }

            await Task.Delay(16);
        }
    }

    (int width, int height) VisionKitGetTargetDimensions(Texture texture)
    {
        int divisor = Mathf.Max(1, visionKitSampleFactor);
        return (Mathf.Max(1, texture.width / divisor), Mathf.Max(1, texture.height / divisor));
    }

    bool VisionKitEnsureDownsampleTarget(int width, int height)
    {
        if (_visionKitDownsampleRt && _visionKitDownsampleRt.width == width && _visionKitDownsampleRt.height == height)
        {
            return true;
        }

        if (_visionKitDownsampleRt)
        {
            _visionKitDownsampleRt.Release();
        }

        _visionKitDownsampleRt = new RenderTexture(width, height, 0, RenderTextureFormat.R8)
        {
            enableRandomWrite = true
        };
        _visionKitDownsampleRt.Create();
        return true;
    }

    void VisionKitDispatchDownsample(Texture source, int targetWidth, int targetHeight)
    {
        int kernel = _visionKitDownsampleCs.FindKernel("CSMain");
        _visionKitDownsampleCs.SetTexture(kernel, Vk_Input1, source);
        _visionKitDownsampleCs.SetTexture(kernel, Vk_Output, _visionKitDownsampleRt);
        _visionKitDownsampleCs.SetInt(Vk_InputWidth, source.width);
        _visionKitDownsampleCs.SetInt(Vk_InputHeight, source.height);
        _visionKitDownsampleCs.SetInt(Vk_OutputWidth, targetWidth);
        _visionKitDownsampleCs.SetInt(Vk_OutputHeight, targetHeight);

        int threadGroupsX = Mathf.CeilToInt(targetWidth / 8f);
        int threadGroupsY = Mathf.CeilToInt(targetHeight / 8f);
        _visionKitDownsampleCs.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
    }

    QrCodeResult[] VisionKitDecodeFrame(VisionKitCaptureFrame frame, byte[] grayBytes, int targetWidth, int targetHeight)
    {
        try
        {
            var luminanceSource = new RGBLuminanceSource(grayBytes, targetWidth, targetHeight, RGBLuminanceSource.BitmapFormat.Gray8);
            var binaryBitmap = new BinaryBitmap(new HybridBinarizer(luminanceSource));

            if (visionKitDetectionMode == QrCodeDetectionMode.Single)
            {
                Result decodeResult = _visionKitQrReader.decode(binaryBitmap);
                if (decodeResult != null)
                {
                    return new[] { VisionKitProcessDecodeResult(decodeResult, targetWidth, targetHeight, frame) };
                }
            }
            else
            {
                var multiReader = new GenericMultipleBarcodeReader(_visionKitQrReader);
                Result[] decodeResults = multiReader.decodeMultiple(binaryBitmap);
                if (decodeResults != null)
                {
                    var results = new List<QrCodeResult>(decodeResults.Length);
                    foreach (Result dr in decodeResults)
                    {
                        results.Add(VisionKitProcessDecodeResult(dr, targetWidth, targetHeight, frame));
                    }

                    return results.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(QRCodePrefabSpawner)}] ZXing: {ex.Message}");
        }

        return Array.Empty<QrCodeResult>();
    }

    bool TryVisionKitRaycastWorldPoint(QrCodeResult result, out Vector3 worldPoint)
    {
        worldPoint = default;
        if (result.corners == null || result.corners.Length == 0 || !visionKitEnvironmentRaycast)
        {
            return false;
        }

        Vector2 centerUv = Vector2.zero;
        foreach (Vector3 c in result.corners)
        {
            centerUv += new Vector2(c.x, c.y);
        }

        centerUv /= result.corners.Length;

        Ray ray = VisionKitBuildWorldRay(result, centerUv);
        if (!visionKitEnvironmentRaycast.Raycast(ray, out EnvironmentRaycastHit hit))
        {
            return false;
        }

        worldPoint = hit.point;
        return true;
    }

    void EnsureVisionKitQrVisual()
    {
        if (!showQrDebugVisuals || !qrCodeDetectedPrefab || !_visionKitPoseProxy)
        {
            return;
        }

        if (_visionKitQrVisualInstance)
        {
            return;
        }

        _visionKitQrVisualInstance = Instantiate(qrCodeDetectedPrefab, _visionKitPoseProxy, false);
        _visionKitQrVisualInstance.transform.localPosition = Vector3.zero;
        _visionKitQrVisualInstance.transform.localRotation = Quaternion.identity;
    }

    static Vector2 VisionKitToViewport(Vector2 uv) => new(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));

    static Ray VisionKitBuildWorldRay(QrCodeResult result, Vector2 uv)
    {
        Vector2 viewport = VisionKitToViewport(uv);
        PassthroughCameraAccess.CameraIntrinsics intrinsics = result.Intrinsics;
        var sensorResolution = (Vector2)intrinsics.SensorResolution;
        Vector2 currentResolution = (Vector2)result.captureResolution;
        if (currentResolution == Vector2.zero)
        {
            currentResolution = sensorResolution;
        }

        Rect crop = VisionKitComputeSensorCrop(sensorResolution, currentResolution);
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

    static Rect VisionKitComputeSensorCrop(Vector2 sensorResolution, Vector2 currentResolution)
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

#endif

    bool TrySpawnCycle(GameObject[] prefabs, Vector3 position, Quaternion rotation, ref int cycleIndex)
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            return false;
        }

        GameObject prefab = prefabs[cycleIndex % prefabs.Length];
        cycleIndex++;

        if (!prefab)
        {
            return false;
        }

        Transform parent = GetEffectiveSpawnParent();
        GameObject spawned = Instantiate(prefab, position, rotation, parent);
        RegisterSpawned(spawned);
        return true;
    }

    void RegisterSpawned(GameObject spawned)
    {
        if (!spawned)
        {
            return;
        }

        Transform parent = GetEffectiveSpawnParent();
        if (spawned.transform.parent != parent)
        {
            spawned.transform.SetParent(parent, true);
        }

        _spawnedQueue.Enqueue(spawned);
        PruneSpawnedIfNeeded();
    }

    void PruneSpawnedIfNeeded()
    {
        while (_spawnedQueue.Count > 0 && _spawnedQueue.Peek() == null)
        {
            _spawnedQueue.Dequeue();
        }

        if (maxSpawnedChildren <= 0)
        {
            return;
        }

        while (_spawnedQueue.Count > maxSpawnedChildren)
        {
            GameObject oldest = _spawnedQueue.Dequeue();
            if (oldest)
            {
                Destroy(oldest);
            }
        }
    }

    Transform GetEffectiveSpawnParent() => spawnParent ? spawnParent : transform;

    void UpdateQrFollower(Vector3 qrRawWorldPosition, Transform rotationSource)
    {
        if (!qrCodeFollower)
        {
            return;
        }

        qrCodeFollower.position = useSmoothedPositionForQrFollower
            ? _smoothedWorldPosition
            : qrRawWorldPosition;

        if (followQrFollowerRotation && rotationSource)
        {
            qrCodeFollower.rotation = rotationSource.rotation;
        }
    }

#if ZXING_ENABLED
    /// <summary>
    /// Vision Kit scan is async and may finish after Update; apply follower in that frame too.
    /// </summary>
    void ApplyQrFollowerAfterVisionKitWorldPoint(Vector3 worldPoint)
    {
        if (!qrCodeFollower)
        {
            return;
        }

        if (useSmoothedPositionForQrFollower)
        {
            qrCodeFollower.position = _smoothedWorldPosition;
        }
        else
        {
            qrCodeFollower.position = worldPoint;
        }

        if (followQrFollowerRotation && _visionKitPoseProxy)
        {
            qrCodeFollower.rotation = _visionKitPoseProxy.rotation;
        }
    }
#endif

    void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (!UsesMrukQrTrackables || trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
        {
            return;
        }

        _activeQr = trackable;
        SyncSmoothedStateToTrackable(resetVelocity: resetSmoothVelocityWhenQrChanges);
        TryInstantiateQrDetectedVisual(trackable);
    }

    void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (!UsesMrukQrTrackables)
        {
            return;
        }

        if (trackable != _activeQr)
        {
            CleanupQrDetectedVisual(trackable);
            return;
        }

        _activeQr = null;
        CleanupQrDetectedVisual(trackable);
        RefreshActiveQrFromScene();
    }

    void TryInstantiateQrDetectedVisual(MRUKTrackable trackable)
    {
        if (!showQrDebugVisuals || !UsesMrukQrTrackables || !trackable || !qrCodeDetectedPrefab)
        {
            return;
        }
        if (_qrVisualInstances.ContainsKey(trackable))
        {
            return;
        }

        GameObject instance = Instantiate(qrCodeDetectedPrefab, trackable.transform);
        _qrVisualInstances[trackable] = instance;

        if (instance.TryGetComponent<QRCode>(out var qrCode))
        {
            qrCode.Initialize(trackable);
        }
        if (instance.TryGetComponent<Bounded2DVisualizer>(out var visualizer))
        {
            visualizer.Initialize(trackable);
        }

    }

    void CleanupQrDetectedVisual(MRUKTrackable trackable)
    {
        if (!trackable || !_qrVisualInstances.TryGetValue(trackable, out var instance))
        {
            return;
        }

        if (instance)
        {
            Destroy(instance);
        }
        _qrVisualInstances.Remove(trackable);
    }

    void RefreshActiveQrFromScene()
    {
        if (!UsesMrukQrTrackables || !mruk)
        {
            return;
        }

        mruk.GetTrackables(_trackableScratch);
        foreach (MRUKTrackable t in _trackableScratch)
        {
            if (t && t.TrackableType == OVRAnchor.TrackableType.QRCode)
            {
                _activeQr = t;
                SyncSmoothedStateToTrackable(resetVelocity: true);
                return;
            }
        }

        _activeQr = null;
    }

    void SyncSmoothedStateToTrackable(bool resetVelocity)
    {
        if (!_activeQr)
        {
            return;
        }

        Vector3 p = _activeQr.transform.position;
        _smoothedWorldPosition = p;
        if (resetVelocity)
        {
            _smoothVelocity = Vector3.zero;
        }
    }

    bool TryResolveSpawnPose(out Vector3 worldPosition, out Quaternion rotation, out bool hasActiveQr)
    {
        Transform trackingSource = GetTrackingSourceTransform();
        hasActiveQr = trackingSource != null;
        if (trackingSource)
        {
            if (UsesMrukQrTrackables && _activeQr &&
                spawnOnlyWhileTracked && !_activeQr.IsTracked)
            {
                worldPosition = default;
                rotation = default;
                return false;
            }

#if ZXING_ENABLED
            if (UsesVisionKitQr && spawnOnlyWhileTracked && !_visionKitPoseValid)
            {
                worldPosition = default;
                rotation = default;
                return false;
            }
#endif

            worldPosition = trackingSource.position;
            rotation = copyQrRotation ? trackingSource.rotation : Quaternion.identity;
            return true;
        }

#if UNITY_EDITOR
        if (Application.isPlaying &&
            spawnFromSpawnParentWhenNoQrInEditorPlay &&
            spawnParent)
        {
            worldPosition = spawnParent.position;
            rotation = copyQrRotation ? spawnParent.rotation : Quaternion.identity;
            return true;
        }
#endif

        worldPosition = default;
        rotation = default;
        return false;
    }

    Transform GetTrackingSourceTransform()
    {
#if ZXING_ENABLED
        if (UsesVisionKitQr && _visionKitPoseProxy && _visionKitPoseValid)
        {
            return _visionKitPoseProxy;
        }
#endif

        return _activeQr ? _activeQr.transform : null;
    }

    void UpdateReel(Vector3 qrWorldPosition)
    {
        if (!reelRoot || !reelVisual)
        {
            if (!_reelConfigWarningLogged)
            {
                Debug.LogWarning(
                    $"{nameof(QRCodePrefabSpawner)} on {name}: reelRoot / reelVisual required for reel control. Skipping reel update.",
                    this);
                _reelConfigWarningLogged = true;
            }
            return;
        }
        _reelConfigWarningLogged = false;

        // 1) Move reel to toe root (hip)
        if (head)
        {
            Vector3 desiredHip = ComputeReelHipPosition(qrWorldPosition);
            if (reelPivotMarker)
            {
                Vector3 deltaToHip = desiredHip - reelPivotMarker.position;
                reelRoot.position += deltaToHip;
            }
            else
            {
                reelRoot.position = desiredHip;
            }
        }

        // 2) Compute length and angle from that point to QR
        Vector3 pivotWorld = reelPivotMarker ? reelPivotMarker.position : reelRoot.position;
        Vector3 qrTargetPosition = GetQrTargetPositionForReel(qrWorldPosition);
        float reelLength = Vector3.Distance(pivotWorld, qrTargetPosition);
        reelLength = Mathf.Clamp(reelLength, Mathf.Min(reelMinLengthMeters, reelMaxLengthMeters), Mathf.Max(reelMinLengthMeters, reelMaxLengthMeters));
        Quaternion targetRotation = ComputeTargetReelRotation(pivotWorld, qrTargetPosition);

        // 3) Apply length
        ApplyReelLengthScale(reelLength);
        // 4) Apply rotation
        reelRoot.rotation = targetRotation;
    }

    Vector3 GetQrTargetPositionForReel(Vector3 fallbackQrWorldPosition)
    {
        if (qrCodeFollower)
        {
            return qrCodeFollower.position + qrFollowerTargetOffset;
        }

        return fallbackQrWorldPosition + qrFollowerTargetOffset;
    }

    public void SetQrFollowerTargetOffset(Vector3 newOffset)
    {
        qrFollowerTargetOffset = newOffset;
    }

    Quaternion ComputeTargetReelRotation(Vector3 hipWorld, Vector3 qrWorld)
    {
        if (directFitBetweenHipAndQr)
        {
            Vector3 direct = qrWorld - hipWorld;
            if (direct.sqrMagnitude < 1e-8f)
            {
                return reelRoot ? reelRoot.rotation : _reelBaseRotation;
            }
            return Quaternion.LookRotation(direct.normalized, Vector3.up);
        }

        if (reelRotationMode == ReelRotationMode.PitchOnlyFromHeight)
        {
            float horizontalDistance = Vector3.ProjectOnPlane(qrWorld - hipWorld, Vector3.up).magnitude;
            float pitchDeg = Mathf.Atan2(qrWorld.y - hipWorld.y, Mathf.Max(0.0001f, horizontalDistance)) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Clamp(pitchDeg, Mathf.Min(reelPitchMinDegrees, reelPitchMaxDegrees), Mathf.Max(reelPitchMinDegrees, reelPitchMaxDegrees));
            return _reelBaseRotation * Quaternion.AngleAxis(-pitchDeg, Vector3.right);
        }

        Vector3 qrAtHipHeight = new(qrWorld.x, hipWorld.y, qrWorld.z);
        Vector3 horizontalDir = Vector3.ProjectOnPlane(qrAtHipHeight - hipWorld, Vector3.up);

        if (horizontalDir.sqrMagnitude < 1e-8f)
        {
            Vector3 fallback = Vector3.ProjectOnPlane(reelFallbackHorizontalDirection, Vector3.up);
            horizontalDir = fallback.sqrMagnitude > 1e-8f ? fallback.normalized : _lastValidReelDirection;
        }
        else
        {
            horizontalDir.Normalize();
            _lastValidReelDirection = horizontalDir;
        }

        return Quaternion.LookRotation(horizontalDir, Vector3.up);
    }

    Vector3 ComputeReelHipPosition(Vector3 qrWorldPosition)
    {
        float weightSum = Mathf.Max(0.0001f, headWeight + qrWeight);
        Vector3 headXZ = new(head.position.x, 0f, head.position.z);
        Vector3 qrXZ = new(qrWorldPosition.x, 0f, qrWorldPosition.z);
        Vector3 weightedXZ = (headXZ * headWeight + qrXZ * qrWeight) / weightSum;

        float y = reelHeightMode switch
        {
            ReelHeightMode.UseHeadY => head.position.y,
            ReelHeightMode.UseQrY => qrWorldPosition.y,
            ReelHeightMode.FixedY => fixedReelY,
            _ => 0.5f * (head.position.y + qrWorldPosition.y)
        };
        if (useHeadBasedHipY)
        {
            y = head.position.y + hipHeadYOffsetMeters;
        }

        return new Vector3(weightedXZ.x, y, weightedXZ.z);
    }

    void ApplyReelLengthScale(float targetLengthMeters)
    {
        if (proceduralReelUShape)
        {
            proceduralReelUShape.SendMessage("SetLength", targetLengthMeters, SendMessageOptions.DontRequireReceiver);
            return;
        }

        float factor = targetLengthMeters / Mathf.Max(0.0001f, reelBaseLengthMetersAtScale1);
        Vector3 s = _reelInitialVisualScale;

        switch (reelLengthScaleAxis)
        {
            case ReelScaleAxis.X:
                s.x = _reelInitialVisualScale.x * factor;
                break;
            case ReelScaleAxis.Y:
                s.y = _reelInitialVisualScale.y * factor;
                break;
            default:
                s.z = _reelInitialVisualScale.z * factor;
                break;
        }

        reelVisual.localScale = s;

        if (!keepReelStartAnchoredWhenScaling)
        {
            return;
        }

        // Scale is center-based; shift half the length delta along axis to keep start anchored.
        float deltaLength = targetLengthMeters - reelBaseLengthMetersAtScale1;
        float halfShift = 0.5f * deltaLength * (reelLengthAxisDirection >= 0f ? 1f : -1f);
        Vector3 anchoredPos = _reelInitialVisualLocalPosition;

        switch (reelLengthScaleAxis)
        {
            case ReelScaleAxis.X:
                anchoredPos.x = _reelInitialVisualLocalPosition.x + halfShift;
                break;
            case ReelScaleAxis.Y:
                anchoredPos.y = _reelInitialVisualLocalPosition.y + halfShift;
                break;
            default:
                anchoredPos.z = _reelInitialVisualLocalPosition.z + halfShift;
                break;
        }

        reelVisual.localPosition = anchoredPos;
    }

    Vector3 SmoothOrSnap(Vector3 current, Vector3 target)
    {
        if (positionSmoothTimeSeconds <= 0f)
        {
            _smoothVelocity = Vector3.zero;
            return target;
        }

        return Vector3.SmoothDamp(current, target, ref _smoothVelocity, positionSmoothTimeSeconds);
    }
}
