using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads HealthMenu / HealthArms / HealthLegs scenes with optional OVRScreenFade transition.
/// Wire UI buttons to <see cref="LoadHealthMenu"/>, <see cref="LoadHealthArms"/>, or <see cref="LoadHealthLegs"/>.
/// </summary>
public class AppSceneManager : MonoBehaviour
{
    public const string HealthMenuSceneName = "HealthMenu";
    public const string HealthArmsSceneName = "HealthArms";
    public const string HealthLegsSceneName = "HealthLegs";

    [Header("OVRScreenFade (optional)")]
    [Tooltip("ON: FadeOut → wait → LoadScene. Uses _screenFade if set, else OVRScreenFade.instance on the current camera rig.")]
    [SerializeField] private bool _useOvrScreenFade = true;
    [Tooltip("Empty = use OVRScreenFade.instance. You can assign the CenterEyeAnchor OVRScreenFade for this scene.")]
    [SerializeField] private OVRScreenFade _screenFade;

    [Header("Optional")]
    [SerializeField] private UnityEvent _onBeforeLoad;

    public event Action<string> BeforeSceneLoad;

    private bool _isLoadingScene;

    public void LoadHealthMenu() => LoadScene(HealthMenuSceneName);

    public void LoadHealthArms() => LoadScene(HealthArmsSceneName);

    public void LoadHealthLegs() => LoadScene(HealthLegsSceneName);

    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning($"{nameof(AppSceneManager)}: scene name is empty.");
            return;
        }

        if (_isLoadingScene)
            return;

        sceneName = sceneName.Trim();

        if (_useOvrScreenFade && TryResolveScreenFade(out var fade))
        {
            StartCoroutine(LoadSceneAfterFadeOut(sceneName, fade));
            return;
        }

        if (_useOvrScreenFade)
            Debug.LogWarning($"{nameof(AppSceneManager)}: OVRScreenFade not found. Assign _screenFade or add OVRScreenFade to the camera. Loading without fade.");

        LoadSceneInternal(sceneName);
    }

    private void LoadSceneInternal(string sceneName)
    {
        _isLoadingScene = true;
        BeforeSceneLoad?.Invoke(sceneName);
        _onBeforeLoad?.Invoke();
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private bool TryResolveScreenFade(out OVRScreenFade fade)
    {
        if (_screenFade != null)
        {
            fade = _screenFade;
            return true;
        }

        fade = OVRScreenFade.instance;
        return fade != null;
    }

    private IEnumerator LoadSceneAfterFadeOut(string sceneName, OVRScreenFade fade)
    {
        _isLoadingScene = true;
        fade.FadeOut();
        yield return new WaitForSeconds(fade.fadeTime);

        BeforeSceneLoad?.Invoke(sceneName);
        _onBeforeLoad?.Invoke();
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
}
