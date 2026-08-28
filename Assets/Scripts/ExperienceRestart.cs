using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ExperienceRestart : MonoBehaviour
{
    public static bool IsEnded { get; private set; }

    GameObject buttonRoot;

    public static void NotifyBorderTouched()
    {
        if (IsEnded)
        {
            return;
        }

        ExperienceRestart instance = FindFirstObjectByType<ExperienceRestart>();
        if (instance == null)
        {
            GameObject holder = new GameObject("ExperienceRestart");
            instance = holder.AddComponent<ExperienceRestart>();
        }

        instance.Show();
    }

    void Awake()
    {
        IsEnded = false;
        EnsureEventSystem();
        BuildButton();
        buttonRoot.SetActive(false);
    }

    void Show()
    {
        IsEnded = true;
        buttonRoot.SetActive(true);
    }

    void Restart()
    {
        IsEnded = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void BuildButton()
    {
        GameObject canvasObject = new GameObject("RestartCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasObject.AddComponent<GraphicRaycaster>();

        buttonRoot = new GameObject("RestartButton");
        buttonRoot.transform.SetParent(canvasObject.transform, false);

        RectTransform buttonRect = buttonRoot.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(140f, 36f);

        Image image = buttonRoot.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.85f);

        Button button = buttonRoot.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(Restart);

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(buttonRoot.transform, false);

        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textObject.AddComponent<Text>();
        text.text = "Restart";
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.black;
        text.fontSize = 22;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.raycastTarget = false;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }
}
