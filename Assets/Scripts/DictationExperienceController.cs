using Oculus.Voice.Dictation;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Thin wrapper around AppDictationExperience: start/stop/toggle dictation and expose UnityEvents.
/// Used by <see cref="GameController"/> to listen for the "ready" keyword after calibration.
/// </summary>
public class DictationExperienceController : MonoBehaviour
{
    [Header("Dictation")]
    [SerializeField] private AppDictationExperience appDictationExperience;

    /// <summary>For listeners that need dictation transcription / session events (e.g. game flow controller).</summary>
    public AppDictationExperience AppDictationExperience => appDictationExperience;

    [Header("Behavior")]
    [SerializeField] private bool activateOnStart;

    [Header("Events")]
    [SerializeField] private UnityEvent onDictationActivated;
    [SerializeField] private UnityEvent onDictationDeactivated;

    private void Start()
    {
        if (activateOnStart)
        {
            StartDictation();
        }
    }

    public void StartDictation()
    {
        if (appDictationExperience == null)
        {
            Debug.LogError("DictationExperienceController: AppDictationExperience is not assigned.", this);
            return;
        }

        if (!appDictationExperience.Active)
        {
            appDictationExperience.Activate();
            onDictationActivated?.Invoke();
        }
    }

    public void StartDictationImmediately()
    {
        if (appDictationExperience == null)
        {
            Debug.LogError("DictationExperienceController: AppDictationExperience is not assigned.", this);
            return;
        }

        if (!appDictationExperience.Active)
        {
            appDictationExperience.ActivateImmediately();
            onDictationActivated?.Invoke();
        }
    }

    public void StopDictation()
    {
        if (appDictationExperience == null)
        {
            Debug.LogError("DictationExperienceController: AppDictationExperience is not assigned.", this);
            return;
        }

        if (appDictationExperience.Active)
        {
            appDictationExperience.Deactivate();
            onDictationDeactivated?.Invoke();
        }
    }

    public void ToggleDictation()
    {
        if (appDictationExperience == null)
        {
            Debug.LogError("DictationExperienceController: AppDictationExperience is not assigned.", this);
            return;
        }

        if (appDictationExperience.Active)
        {
            StopDictation();
        }
        else
        {
            StartDictation();
        }
    }

    public bool IsDictationActive()
    {
        return appDictationExperience != null && appDictationExperience.Active;
    }
}
