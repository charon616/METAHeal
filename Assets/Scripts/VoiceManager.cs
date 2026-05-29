using Oculus.Voice;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using System.Text.RegularExpressions;

/// <summary>
/// Listens for a trigger word via Wit.ai voice, then switches to dictation mode and fires events.
/// Supports continuous listening, hand-pinch to start, and optional TMP text output.
/// </summary>
public class VoiceManager : MonoBehaviour
{
    [Header("Wit Configuration")]
    [SerializeField] private AppVoiceExperience appVoiceExperience;

    [Header("Trigger")]
    [SerializeField] private string dictationTriggerWord = "dictation";
    [SerializeField] private bool detectOnPartialTranscription = true;
    [SerializeField] private bool wholeWordMatchOnly = true;
    [SerializeField] private bool triggerOnlyOncePerRequest = true;

    [Header("Voice Events")]
    [SerializeField] private UnityEvent onDictationTriggered;

    [Header("UI Output")]
    [SerializeField] private TMP_Text listeningText;
    [SerializeField] private TMP_Text dictationText;

    [Header("Behavior")]
    [SerializeField] private bool startListeningOnStart = true;
    [SerializeField] private bool keepListeningContinuously = true;
    [SerializeField] private bool clearDictationTextWhenTriggered = true;
    [Min(0f)]
    [SerializeField] private float triggerEventDelaySeconds = 0.8f;

    [Header("Hand Pinch Input")]
    [SerializeField] private bool useHandPinchToStartListening = true;
    [SerializeField] private OVRHand leftHand;
    [SerializeField] private OVRHand rightHand;

    [Tooltip("All the text requests to be called when 'Send' method is called")]
    [SerializeField] private string[] _requests;

    private bool _dictationModeActive;
    private bool _triggeredInCurrentRequest;
    private bool _wasPinching;
    private Coroutine _pendingTriggerCoroutine;

    private void OnEnable()
    {
        if (appVoiceExperience == null)
        {
            Debug.LogError("VoiceManager: AppVoiceExperience is not assigned.", this);
            return;
        }

        appVoiceExperience.VoiceEvents.OnRequestCompleted.AddListener(HandleRequestCompleted);
        appVoiceExperience.VoiceEvents.OnPartialTranscription.AddListener(HandlePartialTranscription);
        appVoiceExperience.VoiceEvents.OnFullTranscription.AddListener(HandleFullTranscription);
    }

    private void Start()
    {
        if (startListeningOnStart)
        {
            StartListening();
        }
    }

    private void OnDisable()
    {
        if (appVoiceExperience == null)
        {
            return;
        }

        appVoiceExperience.VoiceEvents.OnRequestCompleted.RemoveListener(HandleRequestCompleted);
        appVoiceExperience.VoiceEvents.OnPartialTranscription.RemoveListener(HandlePartialTranscription);
        appVoiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(HandleFullTranscription);

        if (_pendingTriggerCoroutine != null)
        {
            StopCoroutine(_pendingTriggerCoroutine);
            _pendingTriggerCoroutine = null;
        }
    }

    private void Update()
    {
        if (!useHandPinchToStartListening)
        {
            return;
        }

        bool isPinching = IsIndexPinching(leftHand) || IsIndexPinching(rightHand);
        if (isPinching && !_wasPinching)
        {
            StartListening();
        }

        _wasPinching = isPinching;
    }

    private static bool IsIndexPinching(OVRHand hand)
    {
        if (hand == null || !hand.IsTracked || hand.HandConfidence == OVRHand.TrackingConfidence.Low)
        {
            return false;
        }

        return hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
    }

    public void StartListening()
    {
        if (appVoiceExperience == null)
        {
            return;
        }

        if (!appVoiceExperience.Active)
        {
            appVoiceExperience.Activate();
        }
    }

    public void StopListening()
    {
        if (appVoiceExperience == null)
        {
            return;
        }

        if (appVoiceExperience.Active)
        {
            appVoiceExperience.Deactivate();
        }
    }

    public void ResetDictationMode()
    {
        _dictationModeActive = false;
    }

    private void HandleRequestCompleted()
    {
        _triggeredInCurrentRequest = false;

        if (!keepListeningContinuously || appVoiceExperience == null)
        {
            return;
        }

        if (!appVoiceExperience.Active)
        {
            appVoiceExperience.Activate();
        }
    }

    private void HandlePartialTranscription(string transcription)
    {
        if (!detectOnPartialTranscription)
        {
            return;
        }

        HandleTranscription(transcription, false);
    }

    private void HandleFullTranscription(string transcription)
    {
        HandleTranscription(transcription, true);
    }

    private void HandleTranscription(string transcription, bool updateDictationText)
    {
        if (string.IsNullOrWhiteSpace(transcription))
        {
            return;
        }

        if (listeningText != null)
        {
            listeningText.text = transcription;
        }

        if (!_dictationModeActive && ContainsTriggerWord(transcription))
        {
            if (triggerOnlyOncePerRequest && _triggeredInCurrentRequest)
            {
                return;
            }

            _dictationModeActive = true;
            _triggeredInCurrentRequest = true;
            if (clearDictationTextWhenTriggered && dictationText != null)
            {
                dictationText.text = string.Empty;
            }

            if (_pendingTriggerCoroutine != null)
            {
                StopCoroutine(_pendingTriggerCoroutine);
            }
            _pendingTriggerCoroutine = StartCoroutine(InvokeTriggerWithDelay());
            return;
        }

        if (_dictationModeActive && updateDictationText && dictationText != null)
        {
            if (string.IsNullOrEmpty(dictationText.text))
            {
                dictationText.text = transcription;
            }
            else
            {
                dictationText.text += "\n" + transcription;
            }
        }
    }

    private bool ContainsTriggerWord(string transcription)
    {
        if (string.IsNullOrWhiteSpace(dictationTriggerWord))
        {
            return false;
        }

        if (wholeWordMatchOnly)
        {
            var pattern = $"\\b{Regex.Escape(dictationTriggerWord)}\\b";
            return Regex.IsMatch(transcription, pattern, RegexOptions.IgnoreCase);
        }

        return transcription.IndexOf(dictationTriggerWord, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private IEnumerator InvokeTriggerWithDelay()
    {
        if (triggerEventDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(triggerEventDelaySeconds);
        }

        onDictationTriggered?.Invoke();
        _pendingTriggerCoroutine = null;
    }
}
