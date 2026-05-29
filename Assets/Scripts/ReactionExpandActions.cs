using UnityEngine;

/// <summary>
/// Single entry point wired to <see cref="ReactionEventHub"/> UnityEvents.
/// Triggers scale pulses and audio clip cycling together on expand reactions.
/// </summary>
[DisallowMultipleComponent]
public class ReactionExpandActions : MonoBehaviour
{
    [Header("Invoke from ReactionEventHub")]
    [Tooltip("Skipped when unassigned.")]
    [SerializeField] ReactionScalePulseGroup scalePulseGroup;
    [SerializeField] ReactionAudioCyclePlayer audioCycle;

    /// <summary>Combined expand reaction; wire from Inspector UnityEvents.</summary>
    public void OnExpandReact()
    {
        if (scalePulseGroup != null)
            scalePulseGroup.PlayAllRandomized();

        if (audioCycle != null)
            audioCycle.PlayNext();
    }

    /// <summary>Alias matching project naming conventions.</summary>
    public void OnReactionExpand() => OnExpandReact();
}
