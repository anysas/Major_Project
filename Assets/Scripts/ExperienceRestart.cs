using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-200)]
public class ExperienceRestart : MonoBehaviour
{
    public static bool HasStarted { get; private set; } = true;
    public static bool IsEnded { get; private set; }
    public static bool IsActive => HasStarted && !IsEnded;

    [SerializeField] Button restartButton;
    [SerializeField] Text finishedTimeText;
    [SerializeField, Min(0.15f)] float blurDistance = 1.5f;
    [SerializeField, Range(0.5f, 1.5f)] float blurRadius = 1.5f;

    CanvasGroup canvasGroup;
    Volume blurVolume;
    GameObject fallbackRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        HasStarted = true;
        IsEnded = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BindIfPresent()
    {
        GameObject screen = FindRestartScreen();
        if (screen == null)
        {
            return;
        }

        if (screen.GetComponent<ExperienceRestart>() == null)
        {
            screen.AddComponent<ExperienceRestart>();
        }
    }

    public static void HoldUntilStart()
    {
        HasStarted = false;
        IsEnded = false;
    }

    public static void NotifyStarted()
    {
        if (IsEnded)
        {
            return;
        }

        HasStarted = true;
        GameTimer.StartTiming();
    }

    public static void NotifyFailed()
    {
        if (IsEnded)
        {
            return;
        }

        GameTimer.StopTiming();
        GetOrCreate().Show();
    }

    [System.Obsolete("Use NotifyFailed.")]
    public static void NotifyBorderTouched()
    {
        NotifyFailed();
    }

    void Awake()
    {
        EnsureEventSystem();
        BindScreen();
        if (!IsEnded)
        {
            Hide();
        }
    }

    void OnDestroy()
    {
        if (blurVolume != null && blurVolume.profile != null)
        {
            Destroy(blurVolume.profile);
        }
    }

    void Update()
    {
        if (!IsEnded)
        {
            return;
        }

        if (GameInput.ConfirmPressedThisFrame)
        {
            Restart();
        }
    }

    void Show()
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        IsEnded = true;
        Reveal();
        ShowFinishedTime();
        SetupBlur();
    }

    void Restart()
    {
        HasStarted = false;
        IsEnded = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void BindScreen()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null && IsRestartScreen(gameObject))
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.sortingOrder = 100;
        }

        if (restartButton == null)
        {
            Transform buttonTransform = transform.Find("restartButton");
            if (buttonTransform == null)
            {
                buttonTransform = transform.Find("RestartButton");
            }

            if (buttonTransform != null)
            {
                restartButton = buttonTransform.GetComponent<Button>();
                if (restartButton == null)
                {
                    restartButton = buttonTransform.gameObject.AddComponent<Button>();
                }
            }
        }

        if (restartButton != null)
        {
            Graphic graphic = restartButton.GetComponent<Graphic>();
            if (graphic != null)
            {
                restartButton.targetGraphic = graphic;
            }

            restartButton.onClick.RemoveListener(Restart);
            restartButton.onClick.AddListener(Restart);
            EnsureFinishedTimeText();
            return;
        }

        if (!IsRestartScreen(gameObject))
        {
            BuildFallbackButton();
        }

        EnsureFinishedTimeText();
    }

    void ShowFinishedTime()
    {
        EnsureFinishedTimeText();
        if (finishedTimeText == null)
        {
            return;
        }

        finishedTimeText.text = "Time  " + GameTimer.FormattedTime;
        finishedTimeText.gameObject.SetActive(true);
        finishedTimeText.transform.SetAsLastSibling();
    }

    void EnsureFinishedTimeText()
    {
        if (finishedTimeText != null)
        {
            return;
        }

        Transform existing = transform.Find("FinishedTime");
        if (existing != null)
        {
            finishedTimeText = existing.GetComponent<Text>();
            if (finishedTimeText != null)
            {
                return;
            }
        }

        Transform parent = transform;
        if (fallbackRoot != null)
        {
            parent = fallbackRoot.transform.parent;
        }

        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null && parent == transform)
        {
            return;
        }

        finishedTimeText = GameTimer.CreateTimeText(parent, "FinishedTime", 56);
        finishedTimeText.text = "";
        finishedTimeText.gameObject.SetActive(false);
    }

    void Hide()
    {
        SetVisible(false);
    }

    void Reveal()
    {
        SetVisible(true);
    }

    void SetVisible(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.enabled = visible;
        }

        if (fallbackRoot != null)
        {
            fallbackRoot.SetActive(visible);
        }

        if (restartButton != null)
        {
            restartButton.interactable = visible;
        }

        if (finishedTimeText != null)
        {
            finishedTimeText.gameObject.SetActive(visible);
        }
    }

    void SetupBlur()
    {
        if (blurVolume != null)
        {
            blurVolume.weight = 1f;
            blurVolume.enabled = true;
            return;
        }

        GameObject volumeObject = new GameObject("RestartScreenBlur");
        volumeObject.transform.SetParent(transform, false);

        blurVolume = volumeObject.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 100f;
        blurVolume.weight = 1f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        DepthOfField depthOfField = profile.Add<DepthOfField>(true);
        depthOfField.active = true;
        depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
        depthOfField.gaussianStart.Override(0f);
        depthOfField.gaussianEnd.Override(blurDistance);
        depthOfField.gaussianMaxRadius.Override(blurRadius);
        depthOfField.highQualitySampling.Override(true);
        blurVolume.profile = profile;
    }

    void BuildFallbackButton()
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

        fallbackRoot = new GameObject("RestartButton");
        fallbackRoot.transform.SetParent(canvasObject.transform, false);

        RectTransform buttonRect = fallbackRoot.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(140f, 36f);

        Image image = fallbackRoot.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.85f);

        restartButton = fallbackRoot.AddComponent<Button>();
        restartButton.targetGraphic = image;
        restartButton.onClick.AddListener(Restart);

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(fallbackRoot.transform, false);

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

    static ExperienceRestart GetOrCreate()
    {
        ExperienceRestart instance = FindFirstObjectByType<ExperienceRestart>(FindObjectsInactive.Include);
        if (instance != null)
        {
            return instance;
        }

        GameObject screen = FindRestartScreen();
        if (screen != null)
        {
            instance = screen.GetComponent<ExperienceRestart>();
            if (instance == null)
            {
                instance = screen.AddComponent<ExperienceRestart>();
            }

            return instance;
        }

        GameObject holder = new GameObject("ExperienceRestart");
        return holder.AddComponent<ExperienceRestart>();
    }

    static GameObject FindRestartScreen()
    {
        GameObject screen = GameObject.Find("RestartScreen");
        if (screen != null)
        {
            return screen;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return null;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == "RestartScreen")
            {
                return roots[i];
            }
        }

        return null;
    }

    static bool IsRestartScreen(GameObject target)
    {
        return target != null && target.name == "RestartScreen";
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
