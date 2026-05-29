using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a ScrollRect pinned to the bottom when content grows (e.g. live dictation / chat log).
/// Optionally only auto-scrolls when the user is already near the bottom.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(ScrollRect))]
public class ScrollRectAutoBottom : MonoBehaviour
{
    [SerializeField] private bool autoScrollOnStart = true;
    [SerializeField] private bool autoScrollOnlyWhenNearBottom = false;
    [SerializeField] [Range(0f, 1f)] private float nearBottomThreshold = 0.08f;

    private ScrollRect scrollRect;
    private RectTransform contentRect;
    private float lastContentHeight = -1f;
    private bool pendingScrollToBottom;

    private void Awake()
    {
        scrollRect = GetComponent<ScrollRect>();
        contentRect = scrollRect.content;
    }

    private void OnEnable()
    {
        if (scrollRect == null)
        {
            scrollRect = GetComponent<ScrollRect>();
        }

        if (contentRect == null && scrollRect != null)
        {
            contentRect = scrollRect.content;
        }

        lastContentHeight = contentRect != null ? contentRect.rect.height : -1f;
    }

    private void Start()
    {
        if (autoScrollOnStart)
        {
            RequestScrollToBottom();
        }
    }

    private void LateUpdate()
    {
        if (contentRect == null)
        {
            return;
        }

        float currentHeight = contentRect.rect.height;
        if (!Mathf.Approximately(currentHeight, lastContentHeight))
        {
            Debug.Log($"[ScrollRectAutoBottom] Content height changed: {lastContentHeight} -> {currentHeight}", this);

            bool shouldAutoScroll = !autoScrollOnlyWhenNearBottom || IsNearBottom();
            if (shouldAutoScroll)
            {
                pendingScrollToBottom = true;
            }

            lastContentHeight = currentHeight;
        }

        if (pendingScrollToBottom)
        {
            ScrollToBottomImmediate();
            pendingScrollToBottom = false;
        }
    }

    public void RequestScrollToBottom()
    {
        pendingScrollToBottom = true;
    }

    public void ScrollToBottomImmediate()
    {
        if (scrollRect == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private bool IsNearBottom()
    {
        if (scrollRect == null)
        {
            return true;
        }

        return scrollRect.verticalNormalizedPosition <= nearBottomThreshold;
    }
}
