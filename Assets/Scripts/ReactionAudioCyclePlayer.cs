using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cycles through a list of AudioClips on each <see cref="PlayNext"/> call (wraps at end).
/// Similar pattern to <see cref="PoseActiveVisualCycleController"/>; wire to ReactionEventHub events.
/// </summary>
[DisallowMultipleComponent]
public class ReactionAudioCyclePlayer : MonoBehaviour
{
    [Header("Playback")]
    [Tooltip("If unset, uses the AudioSource on the same GameObject.")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] List<AudioClip> clips = new List<AudioClip>();
    [Tooltip("When true, stops the previous clip before playing the next (no overlap).")]
    [SerializeField] bool stopPrevious = true;
    [Tooltip("When true, the first PlayNext always starts at the list head (or StartAtIndex).")]
    [SerializeField] bool startFromFirstOnFirstTrigger = true;
    [Tooltip("Start index on first trigger only (0-based). -1 means index 0.")]
    [SerializeField] int startAtIndex = -1;

    [Header("State (read / debug)")]
    [SerializeField] int currentIndex = -1;

    bool _hasTriggered;

    void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    /// <summary>Event entry: selects and plays the next clip.</summary>
    public void PlayNext()
    {
        if (clips == null || clips.Count <= 0) return;
        if (audioSource == null) return;

        if (!_hasTriggered && startFromFirstOnFirstTrigger)
        {
            currentIndex = startAtIndex >= 0 ? WrapIndex(startAtIndex, clips.Count) : 0;
        }
        else
        {
            currentIndex = WrapIndex(currentIndex + 1, clips.Count);
        }

        _hasTriggered = true;
        PlayClipAtCurrent();
    }

    /// <summary>Plays the clip at the given index on the single AudioSource.</summary>
    public void PlayAt(int index)
    {
        if (clips == null || clips.Count <= 0) return;
        if (audioSource == null) return;

        currentIndex = WrapIndex(index, clips.Count);
        _hasTriggered = true;
        PlayClipAtCurrent();
    }

    /// <summary>Resets cycle state; the next <see cref="PlayNext"/> treats as first trigger.</summary>
    public void ResetCycle()
    {
        _hasTriggered = false;
        currentIndex = -1;
    }

    /// <summary>Alias for Inspector / UnityEvent wiring.</summary>
    public void OnReactionPlayNextClip() => PlayNext();

    public int CurrentIndex => currentIndex;

    void PlayClipAtCurrent()
    {
        var clip = clips[currentIndex];
        if (clip == null) return;

        if (stopPrevious)
            audioSource.Stop();

        audioSource.clip = clip;
        audioSource.Play();
    }

    static int WrapIndex(int value, int count)
    {
        if (count <= 0) return -1;
        int m = value % count;
        return m < 0 ? m + count : m;
    }
}
