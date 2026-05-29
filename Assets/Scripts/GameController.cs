using System;
using System.Collections;
using System.Text.RegularExpressions;
using Meta.WitAi.Dictation.Data;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Main experience flow controller: UI start → calibration → dictation → "ready" keyword →
/// layout lock (<see cref="ReactionEventHub.DecideLayoutFromHub"/>) → experience start.
/// Returns to menu if dictation ends without "ready" or if maxWaitReadySeconds elapses.
/// </summary>
public class GameController : MonoBehaviour
{
    [Header("References (Inspector)")]
    [SerializeField] DictationExperienceController dictationController;
    [SerializeField] ReactionEventHub reactionHub;
    [Tooltip("Root GameObject for start/calibration UI (Canvas or Panel). Hidden when the experience begins.")]
    [SerializeField] GameObject menuUiRoot;

    [Header("Ready keyword")]
    [Tooltip("Matches if the keyword appears in partial or full transcription (e.g. I'm ready → 'ready').")]
    [SerializeField] string readyKeyword = "ready";
    [SerializeField] bool readyWholeWordOnly = true;

    [Header("Return to menu")]
    [Tooltip(
        "When > 0, return to menu if 'ready' is not detected within this many seconds (fallback when OnDictationSessionStopped never fires, e.g. multi-phrase mode).")]
    [SerializeField] float maxWaitReadySeconds;

    [Header("Events (Inspector)")]
    [Tooltip("Fires once after 'ready' is detected, just before layout lock (DecideLayout). Main game start.")]
    [SerializeField] UnityEvent onGameStart;
    [Tooltip("First step when called from a UI button. Wire calibration (MRUK, etc.) here.")]
    [SerializeField] UnityEvent onCalibrationStarted;
    [Tooltip("Fires after 'ready' is detected and DecideLayout completes.")]
    [SerializeField] UnityEvent onExperienceStarted;
    [Tooltip("Fires after dictation ends without 'ready' and the menu UI is shown again.")]
    [SerializeField] UnityEvent onReturnedToMenuUi;

    bool _waitingForReady;
    bool _experienceStarted;
    /// <summary>Latest partial/full transcription; used to re-check when session stop arrives before the final transcript.</summary>
    string _latestDictationText;
    Coroutine _sessionStoppedWhileWaitingRoutine;
    Coroutine _waitReadyTimeoutRoutine;

    void OnEnable()
    {
        SubscribeDictationEvents(true);
    }

    void OnDisable()
    {
        if (_sessionStoppedWhileWaitingRoutine != null)
        {
            StopCoroutine(_sessionStoppedWhileWaitingRoutine);
            _sessionStoppedWhileWaitingRoutine = null;
        }
        if (_waitReadyTimeoutRoutine != null)
        {
            StopCoroutine(_waitReadyTimeoutRoutine);
            _waitReadyTimeoutRoutine = null;
        }
        SubscribeDictationEvents(false);
    }

    void SubscribeDictationEvents(bool subscribe)
    {
        var app = dictationController != null ? dictationController.AppDictationExperience : null;
        if (app == null) return;

        if (subscribe)
        {
            app.DictationEvents.OnPartialTranscription.AddListener(OnDictationTranscription);
            app.DictationEvents.OnFullTranscription.AddListener(OnDictationTranscription);
            app.DictationEvents.OnDictationSessionStopped.AddListener(OnDictationSessionStopped);
            app.DictationEvents.OnStoppedListeningDueToDeactivation.AddListener(OnStoppedListeningDueToDeactivate);
        }
        else
        {
            app.DictationEvents.OnPartialTranscription.RemoveListener(OnDictationTranscription);
            app.DictationEvents.OnFullTranscription.RemoveListener(OnDictationTranscription);
            app.DictationEvents.OnDictationSessionStopped.RemoveListener(OnDictationSessionStopped);
            app.DictationEvents.OnStoppedListeningDueToDeactivation.RemoveListener(OnStoppedListeningDueToDeactivate);
        }
    }

    /// <summary>Assign to a UI Button OnClick event.</summary>
    public void BeginCalibrationFromUiButton()
    {
        _experienceStarted = false;
        _waitingForReady = true;
        _latestDictationText = null;

        SubscribeDictationEvents(false);
        SubscribeDictationEvents(true);

        onCalibrationStarted?.Invoke();

        if (menuUiRoot != null)
            menuUiRoot.SetActive(false);

        if (dictationController != null)
            dictationController.StartDictation();

        if (_waitReadyTimeoutRoutine != null)
            StopCoroutine(_waitReadyTimeoutRoutine);
        _waitReadyTimeoutRoutine = null;
        if (maxWaitReadySeconds > 0f)
            _waitReadyTimeoutRoutine = StartCoroutine(CoWaitReadyTimeout());
    }

    IEnumerator CoWaitReadyTimeout()
    {
        yield return new WaitForSecondsRealtime(maxWaitReadySeconds);
        _waitReadyTimeoutRoutine = null;
        if (!_waitingForReady || _experienceStarted)
            yield break;

        ReturnToMenuAfterDictationEndedWithoutReady();
    }

    void OnDictationTranscription(string text)
    {
        _latestDictationText = text;

        if (!_waitingForReady || _experienceStarted) return;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!ContainsReadyKeyword(text)) return;

        BeginExperienceAfterReady();
    }

    void OnStoppedListeningDueToDeactivate()
    {
        // Deactivate() can arrive before session completion; OnDictationSessionStopped alone may not return to menu.
        if (!_waitingForReady || _experienceStarted) return;
        ReturnToMenuAfterDictationEndedWithoutReady();
    }

    void OnDictationSessionStopped(DictationSession session)
    {
        if (!_waitingForReady || _experienceStarted) return;

        // On some platforms SessionStopped arrives before FinalTranscription; defer to avoid missing 'ready'.
        if (_sessionStoppedWhileWaitingRoutine != null)
            StopCoroutine(_sessionStoppedWhileWaitingRoutine);
        _sessionStoppedWhileWaitingRoutine = StartCoroutine(CoResolveSessionStoppedWhileWaiting());
    }

    IEnumerator CoResolveSessionStoppedWhileWaiting()
    {
        const int deferFrames = 4;
        for (var i = 0; i < deferFrames; i++)
        {
            if (!_waitingForReady || _experienceStarted)
            {
                _sessionStoppedWhileWaitingRoutine = null;
                yield break;
            }

            if (TryBeginExperienceFromLatestTranscript())
            {
                _sessionStoppedWhileWaitingRoutine = null;
                yield break;
            }

            yield return null;
        }

        if (!_waitingForReady || _experienceStarted)
        {
            _sessionStoppedWhileWaitingRoutine = null;
            yield break;
        }

        ReturnToMenuAfterDictationEndedWithoutReady();
        _sessionStoppedWhileWaitingRoutine = null;
    }

    bool TryBeginExperienceFromLatestTranscript()
    {
        if (string.IsNullOrWhiteSpace(_latestDictationText)) return false;
        if (!ContainsReadyKeyword(_latestDictationText)) return false;
        BeginExperienceAfterReady();
        return true;
    }

    void ReturnToMenuAfterDictationEndedWithoutReady()
    {
        if (_sessionStoppedWhileWaitingRoutine != null)
        {
            StopCoroutine(_sessionStoppedWhileWaitingRoutine);
            _sessionStoppedWhileWaitingRoutine = null;
        }

        _waitingForReady = false;

        if (menuUiRoot != null)
            menuUiRoot.SetActive(true);

        onReturnedToMenuUi?.Invoke();

        // If still active, stop it (do nothing if already Deactivated externally).
        if (dictationController != null && dictationController.IsDictationActive())
            dictationController.StopDictation();
    }

    void BeginExperienceAfterReady()
    {
        if (_experienceStarted) return;

        _waitingForReady = false;
        _experienceStarted = true;

        if (_sessionStoppedWhileWaitingRoutine != null)
        {
            StopCoroutine(_sessionStoppedWhileWaitingRoutine);
            _sessionStoppedWhileWaitingRoutine = null;
        }

        if (_waitReadyTimeoutRoutine != null)
        {
            StopCoroutine(_waitReadyTimeoutRoutine);
            _waitReadyTimeoutRoutine = null;
        }

        onGameStart?.Invoke();

        if (reactionHub != null)
            reactionHub.DecideLayoutFromHub();

        onExperienceStarted?.Invoke();

        if (dictationController != null)
            dictationController.StopDictation();
    }

    bool ContainsReadyKeyword(string transcription)
    {
        if (string.IsNullOrWhiteSpace(readyKeyword))
            return false;

        if (readyWholeWordOnly)
        {
            var pattern = $"\\b{Regex.Escape(readyKeyword)}\\b";
            return Regex.IsMatch(transcription, pattern, RegexOptions.IgnoreCase);
        }

        return transcription.IndexOf(readyKeyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
