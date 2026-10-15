using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameTimer : MonoBehaviour
{
    public static float ElapsedSeconds { get; private set; }
    public static string FormattedTime => Format(ElapsedSeconds);

    static GameTimer instance;

    Text hudText;
    TMP_Text hudTmp;
    GameObject timerRoot;
    Canvas hudCanvas;
    bool running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        ElapsedSeconds = 0f;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BindIfPresent()
    {
        EnsureExists();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ElapsedSeconds = 0f;
        EnsureExists();
    }

    public static GameTimer EnsureExists()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindFirstObjectByType<GameTimer>();
        if (instance != null)
        {
            return instance;
        }

        GameObject holder = new GameObject("GameTimer");
        instance = holder.AddComponent<GameTimer>();
        return instance;
    }

    public static void StartTiming()
    {
        EnsureExists().Begin();
    }

    public static void StopTiming()
    {
        if (instance != null)
        {
            instance.Freeze();
        }
    }

    void Awake()
    {
        instance = this;
        ElapsedSeconds = 0f;
        running = false;
        BindHud();
        SetHudVisible(false);
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    void Update()
    {
        if (ExperienceRestart.IsActive)
        {
            if (!running)
            {
                Begin();
            }

            ElapsedSeconds += Time.deltaTime;
            RefreshHud();
            return;
        }

        if (running)
        {
            Freeze();
        }
    }

    void Begin()
    {
        running = true;
        ElapsedSeconds = 0f;
        SetHudVisible(true);
        RefreshHud();
    }

    void Freeze()
    {
        running = false;
        RefreshHud();
        if (timerRoot == null)
        {
            SetHudVisible(false);
        }
    }

    public static bool ShowHudOnEndScreen()
    {
        GameTimer timer = EnsureExists();
        if (timer.timerRoot == null)
        {
            return false;
        }

        timer.running = false;
        timer.RefreshHud();
        timer.SetHudVisible(true);
        timer.RaiseAboveEndScreen();
        return true;
    }

    void RaiseAboveEndScreen()
    {
        Canvas canvas = timerRoot.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = timerRoot.AddComponent<Canvas>();
        }

        canvas.overrideSorting = true;
        canvas.sortingOrder = 120;
    }

    void RefreshHud()
    {
        string time = FormattedTime;
        if (hudTmp != null)
        {
            hudTmp.text = time;
        }

        if (hudText != null)
        {
            hudText.text = time;
        }
    }

    void SetHudVisible(bool visible)
    {
        if (timerRoot != null)
        {
            if (visible)
            {
                ActivateParentCanvas(timerRoot);
            }

            timerRoot.SetActive(visible);
            return;
        }

        if (hudCanvas != null)
        {
            hudCanvas.enabled = visible;
        }
    }

    void BindHud()
    {
        GameObject textObject = FindNamed("timer_text");
        if (textObject != null)
        {
            Transform parent = textObject.transform.parent;
            timerRoot = parent != null && NameIs(parent.name, "timer") ? parent.gameObject : textObject;
        }
        else
        {
            timerRoot = FindNamed("timer");
            textObject = timerRoot;
        }

        if (textObject != null)
        {
            hudTmp = textObject.GetComponent<TMP_Text>();
            if (hudTmp == null)
            {
                hudTmp = textObject.GetComponentInChildren<TMP_Text>(true);
            }

            hudText = textObject.GetComponent<Text>();
            if (hudText == null && hudTmp == null)
            {
                hudText = textObject.GetComponentInChildren<Text>(true);
            }
        }

        if (hudTmp != null || hudText != null)
        {
            RefreshHud();
            return;
        }

        timerRoot = null;
        BuildFallbackHud();
    }

    void BuildFallbackHud()
    {
        if (hudCanvas != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("TimerCanvas");
        canvasObject.transform.SetParent(transform, false);

        hudCanvas = canvasObject.AddComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 50;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        hudText = CreateTimeText(canvasObject.transform, "TimerLabel", 48);
        hudText.text = Format(0f);
    }

    public static Text CreateTimeText(Transform parent, string name, int fontSize)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -28f);
        rect.sizeDelta = new Vector2(640f, 96f);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.UpperCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        Outline outline = textObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2.5f, -2.5f);

        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(2f, -2f);

        return text;
    }

    static GameObject FindNamed(string exactName)
    {
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && NameIs(transforms[i].name, exactName))
            {
                return transforms[i].gameObject;
            }
        }

        return null;
    }

    static bool NameIs(string objectName, string exactName)
    {
        return string.Equals(objectName, exactName, System.StringComparison.OrdinalIgnoreCase);
    }

    static void ActivateParentCanvas(GameObject uiObject)
    {
        Canvas canvas = uiObject.GetComponentInParent<Canvas>(true);
        if (canvas != null)
        {
            canvas.gameObject.SetActive(true);
        }
    }

    public static string Format(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int totalSeconds = Mathf.FloorToInt(seconds);
        int s = totalSeconds % 60;
        int m = totalSeconds / 60;
        return $"{m}:{s:00}";
    }
}
