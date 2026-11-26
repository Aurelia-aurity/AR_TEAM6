using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Compact screen-space controls, independent of camera mirroring and debug overlays.</summary>
[DisallowMultipleComponent]
public sealed class ScreenControlBar : MonoBehaviour
{
    [SerializeField] private Font koreanFont;
    [SerializeField, Min(1)] private float requestTimeoutSeconds = 15;
    public ScreenControlState State { get; } = new ScreenControlState();
    private static readonly string[] Instruments = { "장구", "북", "징", "꽹과리" };
    private RectTransform root;
    private GameObject menu;
    private Button runButton;
    private Text runLabel, instrumentLabel, modeLabel, messageLabel;
    private Button instrumentButton;
    private Font ownedFont;
    private float pendingSince;
    private bool wasPending;
    private GameObject ownedEventSystem;
    private readonly Color textColor = new Color32(233, 240, 247, 255);

    private void Awake()
    {
        if (koreanFont == null)
        {
            ownedFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 16);
            koreanFont = ownedFont;
        }
        Build();
        Refresh();
    }

    private void OnEnable()
    {
        State.Changed += Refresh;
        if (root != null) root.gameObject.SetActive(true);
        Refresh();
    }

    private void OnDisable()
    {
        State.CancelPending("화면이 닫혀 요청 대기를 종료했습니다.");
        State.Changed -= Refresh;
        if (menu != null) menu.SetActive(false);
        if (root != null) root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (ownedFont != null) Destroy(ownedFont);
        if (ownedEventSystem != null) Destroy(ownedEventSystem);
    }

    private void Update()
    {
        // Respect display cutouts and the system navigation area.
        Rect safe = Screen.safeArea;
        if (Screen.width > 0 && Screen.height > 0)
        {
            root.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            root.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        }
        if (State.IsPending && Time.unscaledTime - pendingSince >= requestTimeoutSeconds)
            State.CancelPending("응답 시간이 초과되었습니다. 상태를 확인한 뒤 다시 시도해 주세요.");
    }

    private void Refresh()
    {
        if (runLabel == null) return;
        if (State.IsPending && !wasPending) pendingSince = Time.unscaledTime;
        wasPending = State.IsPending;
        runLabel.text = State.IsPending ? (State.IsRunning ? "정지 중…" : "시작 중…")
            : State.IsRunning ? "정지" : "실행";
        runButton.interactable = !State.IsPending;
        instrumentButton.interactable = !State.IsPending;
        instrumentLabel.text = Instruments[(int)State.Instrument] + "  ▴";
        modeLabel.text = State.Mode == ScreenMode.User ? "사용자 모드" : "관리자 모드";
        messageLabel.text = State.Message;
        if (State.IsPending) menu.SetActive(false);
    }

    private void Build()
    {
        bool isNestedCanvas = GetComponentInParent<Canvas>() != null;
        RectTransform canvasRect = Rect("ScreenControls", transform);
        if (isNestedCanvas)
            Stretch(canvasRect);
        var canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;
        if (!isNestedCanvas)
        {
            var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();
        root = Rect("SafeArea", canvasRect);
        Stretch(root);

        RectTransform bar = Rect("BottomBar", root);
        bar.anchorMin = Vector2.zero;
        bar.anchorMax = new Vector2(1, 0);
        bar.pivot = new Vector2(.5f, 0);
        bar.sizeDelta = new Vector2(0, 64);
        bar.gameObject.AddComponent<Image>().color = new Color32(18, 27, 39, 248);
        runButton = MakeButton("RunStop", bar, "실행", new Vector2(16, 12), new Vector2(88, 40), out runLabel);
        runButton.image.color = new Color32(34, 113, 110, 255);
        runButton.onClick.AddListener(State.ToggleRun);
        instrumentButton = MakeButton("Instrument", bar, "장구  ▴", new Vector2(114, 12), new Vector2(128, 40), out instrumentLabel);
        Button mode = MakeButton("Mode", bar, "사용자 모드", new Vector2(-16, 12), new Vector2(144, 40), out modeLabel);
        var modeRect = (RectTransform)mode.transform;
        modeRect.anchorMin = modeRect.anchorMax = new Vector2(1, 0);
        modeRect.pivot = new Vector2(1, 0);
        mode.onClick.AddListener(() => { menu.SetActive(false); State.ToggleMode(); });

        RectTransform message = Rect("Status", root);
        message.anchorMin = Vector2.zero;
        message.anchorMax = new Vector2(1, 0);
        message.pivot = new Vector2(.5f, 0);
        message.anchoredPosition = new Vector2(0, 72);
        message.sizeDelta = new Vector2(-32, 28);
        messageLabel = Label(message, "", 14);
        messageLabel.alignment = TextAnchor.MiddleRight;

        // Full-screen transparent blocker closes the upward menu on outside click.
        RectTransform popup = Rect("InstrumentPopup", root);
        Stretch(popup);
        menu = popup.gameObject;
        Image blocker = popup.gameObject.AddComponent<Image>();
        blocker.color = Color.clear;
        Button dismiss = popup.gameObject.AddComponent<Button>();
        dismiss.targetGraphic = blocker;
        dismiss.transition = Selectable.Transition.None;
        dismiss.onClick.AddListener(CloseMenu);
        RectTransform panel = Rect("Options", popup);
        panel.anchorMin = panel.anchorMax = Vector2.zero;
        panel.pivot = Vector2.zero;
        panel.anchoredPosition = new Vector2(114, 70);
        panel.sizeDelta = new Vector2(156, 184);
        panel.gameObject.AddComponent<Image>().color = new Color32(25, 37, 53, 255);
        for (int i = 0; i < Instruments.Length; i++)
        {
            int index = i;
            Button option = MakeButton(Instruments[i], panel, Instruments[i],
                new Vector2(6, 138 - i * 44), new Vector2(144, 40), out _);
            option.onClick.AddListener(() => { State.SelectInstrument((ScreenInstrument)index); CloseMenu(); });
        }
        instrumentButton.onClick.AddListener(() =>
        {
            menu.SetActive(!menu.activeSelf);
            if (menu.activeSelf)
                EventSystem.current?.SetSelectedGameObject(panel.GetChild((int)State.Instrument).gameObject);
        });
        menu.SetActive(false);
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            ownedEventSystem = new GameObject("ScreenControlsEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }

    private void CloseMenu()
    {
        menu.SetActive(false);
        EventSystem.current?.SetSelectedGameObject(instrumentButton.gameObject);
    }

    private Button MakeButton(string name, Transform parent, string title, Vector2 position, Vector2 size, out Text label)
    {
        RectTransform rect = Rect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color32(42, 57, 76, 255);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        RectTransform text = Rect("Label", rect);
        Stretch(text);
        label = Label(text, title, 16);
        return button;
    }

    private Text Label(RectTransform rect, string title, int size)
    {
        Text label = rect.gameObject.AddComponent<Text>();
        label.font = koreanFont;
        label.fontSize = size;
        label.color = textColor;
        label.text = title;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        return label;
    }

    internal static ScreenControlBar Resolve(Component host, ScreenControlBar explicitBar)
    {
        if (explicitBar != null)
            return explicitBar;
        if (host != null)
        {
            ScreenControlBar local = host.GetComponentInParent<ScreenControlBar>();
            if (local != null)
                return local;
        }
        return FindFirstObjectByType<ScreenControlBar>(FindObjectsInactive.Include);
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
