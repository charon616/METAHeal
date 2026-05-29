using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wire <see cref="PlayAllRandomized"/> to <see cref="ReactionEventHub"/> UnityEvents.
/// Registers <see cref="ReactionPulse"/> targets at runtime (e.g. from <see cref="HandBetweenModelSpawner"/>).
/// Optional inspector slots still work for objects that exist before play.
/// Local Y bob (<see cref="ReactionPulse.ScheduleBob"/>): hardcoded 9-cycle, steps 3, 4, 8, 9 only.
/// </summary>
public class ReactionScalePulseGroup : MonoBehaviour
{
    const int CycleLength = 9;

    /// <summary>1-based indices within each 9-invocation cycle when local Y bob runs.</summary>
    static readonly int[] BobStepsOneBased = { 3, 4, 8, 9 };

    [Header("Optional: references known before play")]
    [Tooltip("Leave empty if HandBetweenModelSpawner registers pulses at runtime.")]
    [SerializeField] ReactionPulse[] editorPulses;

    [Header("Random range (each event, each pulse)")]
    [SerializeField] float delaySecondsMin;
    [SerializeField] float delaySecondsMax = 0.15f;
    [Tooltip("Peak localScale.y = current rest Y × multiplier.")]
    [SerializeField] float peakYMultiplierMin = 1.15f;
    [SerializeField] float peakYMultiplierMax = 1.55f;

    [Header("Optional: local Y bob (ReactionPulse on same GO)")]
    [Tooltip("Random delay range for bob (only on cycle steps 3, 4, 8, 9).")]
    [SerializeField] float bobDelaySecondsMin;
    [SerializeField] float bobDelaySecondsMax = 0.12f;

    readonly List<ReactionPulse> _registered = new List<ReactionPulse>();
    int _playInvocationCount;

    void Awake()
    {
        if (editorPulses == null) return;
        for (int i = 0; i < editorPulses.Length; i++)
            RegisterPulse(editorPulses[i]);
    }

    void OnValidate()
    {
        delaySecondsMax = Mathf.Max(delaySecondsMax, delaySecondsMin);
        peakYMultiplierMax = Mathf.Max(peakYMultiplierMax, peakYMultiplierMin);
        bobDelaySecondsMax = Mathf.Max(bobDelaySecondsMax, bobDelaySecondsMin);
    }

    /// <summary>Add a spawned instance’s pulse component (duplicate registrations are ignored).</summary>
    public void RegisterPulse(ReactionPulse pulse)
    {
        if (pulse == null || _registered.Contains(pulse)) return;
        _registered.Add(pulse);
    }

    public void UnregisterPulse(ReactionPulse pulse)
    {
        if (pulse == null) return;
        _registered.Remove(pulse);
    }

    /// <summary>Clears all runtime registrations (call before destroying spawned objects).</summary>
    public void ClearRegisteredPulses() => _registered.Clear();

    /// <summary>Assign to ReactionEventHub On Expand Start / End.</summary>
    public void PlayAllRandomized()
    {
        _registered.RemoveAll(p => p == null);

        _playInvocationCount++;
        int stepOneBased = ((_playInvocationCount - 1) % CycleLength) + 1;
        bool bobThisInvocation = ShouldBobOnStep(stepOneBased);

        for (int i = 0; i < _registered.Count; i++)
        {
            ReactionPulse pulse = _registered[i];
            if (pulse == null) continue;
            float delay = Random.Range(delaySecondsMin, delaySecondsMax);
            float peakMul = Random.Range(peakYMultiplierMin, peakYMultiplierMax);
            pulse.SchedulePulse(delay, peakMul);

            if (bobThisInvocation)
            {
                float bobDelay = Random.Range(bobDelaySecondsMin, bobDelaySecondsMax);
                pulse.ScheduleBob(bobDelay);
            }
        }
    }

    static bool ShouldBobOnStep(int stepOneBased)
    {
        for (int i = 0; i < BobStepsOneBased.Length; i++)
        {
            if (BobStepsOneBased[i] == stepOneBased)
                return true;
        }

        return false;
    }

    /// <summary>Inspector-friendly alias.</summary>
    public void OnReactionPlayPulses() => PlayAllRandomized();
}
