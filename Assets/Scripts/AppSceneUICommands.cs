using UnityEngine;

/// <summary>
/// Optional UI bridge: exposes scene-load methods for Unity UI buttons.
/// Assign an <see cref="AppSceneManager"/> or leave empty to find one in the scene at runtime.
/// </summary>
public class AppSceneUICommands : MonoBehaviour
{
    [SerializeField] private AppSceneManager _manager;

    private AppSceneManager Resolve()
    {
        if (_manager != null)
            return _manager;
        return FindFirstObjectByType<AppSceneManager>();
    }

    public void LoadHealthMenu() => Resolve()?.LoadHealthMenu();

    public void LoadHealthArms() => Resolve()?.LoadHealthArms();

    public void LoadHealthLegs() => Resolve()?.LoadHealthLegs();
}
