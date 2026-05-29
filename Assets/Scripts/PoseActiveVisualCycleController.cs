using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cycles through indexed pose visuals (particle + label) on each event trigger.
/// Visual data is applied once at spawn; switching is SetActive-only for performance.
/// </summary>
[DisallowMultipleComponent]
public class PoseActiveVisualCycleController : MonoBehaviour
{
    [Header("Source Data")]
    [Tooltip("Prefab duplicated per index (expects ParticleSystemRenderer + TMP).")]
    [SerializeField] GameObject poseActiveVisualPrefab;
    [SerializeField] List<Material> onSelectIcons = new List<Material>();
    [SerializeField] List<Color> particleStartColors = new List<Color>();
    [SerializeField] List<string> words = new List<string>();

    [Header("Behavior")]
    [SerializeField] bool hideAllOnStart = true;
    [SerializeField] bool startFromFirstOnFirstTrigger = true;
    [SerializeField] int currentIndex = -1;

    readonly List<GameObject> _spawnedVisuals = new List<GameObject>();
    bool _hasTriggered;

    void Start()
    {
        RebuildVisuals();
        if (hideAllOnStart)
            HideAll();
    }

    void RebuildVisuals()
    {
        for (var i = 0; i < _spawnedVisuals.Count; i++)
            if (_spawnedVisuals[i] != null)
                Destroy(_spawnedVisuals[i]);
        _spawnedVisuals.Clear();

        var count = Mathf.Max(onSelectIcons.Count, words.Count, particleStartColors.Count);
        if (poseActiveVisualPrefab == null || count <= 0)
            return;

        for (var i = 0; i < count; i++)
        {
            var visual = Instantiate(poseActiveVisualPrefab, transform);
            visual.name = $"{poseActiveVisualPrefab.name}_{i}";
            PoseActiveVisualPresentation.ApplyIndexedVisual(visual, i, onSelectIcons, particleStartColors, words);
            visual.SetActive(false);
            _spawnedVisuals.Add(visual);
        }
    }

    public void ShowNext()
    {
        var n = _spawnedVisuals.Count;
        if (n <= 0)
            return;

        if (!_hasTriggered && startFromFirstOnFirstTrigger)
            currentIndex = 0;
        else
            currentIndex = WrapIndex(currentIndex + 1, n);

        _hasTriggered = true;
        ShowAt(currentIndex);
    }

    public void ShowAt(int index)
    {
        var n = _spawnedVisuals.Count;
        if (n <= 0)
            return;

        currentIndex = WrapIndex(index, n);

        for (var i = 0; i < _spawnedVisuals.Count; i++)
        {
            var go = _spawnedVisuals[i];
            if (go == null)
                continue;

            var on = i == currentIndex;
            go.SetActive(on);
        }
    }

    public void HideAll()
    {
        for (var i = 0; i < _spawnedVisuals.Count; i++)
            if (_spawnedVisuals[i] != null)
                _spawnedVisuals[i].SetActive(false);

        _hasTriggered = false;
        currentIndex = -1;
    }

    public void OnPoseActiveEvent() => ShowNext();

    public int CurrentIndex => currentIndex;

    static int WrapIndex(int value, int count)
    {
        if (count <= 0)
            return -1;
        var m = value % count;
        return m < 0 ? m + count : m;
    }
}
