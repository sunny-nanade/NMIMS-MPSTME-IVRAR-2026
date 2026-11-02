using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CampusQuest
{
    /// <summary>
    /// Mirrors the desktop orientation, mission, lift, and canteen screens on a tracked XR canvas.
    /// The desktop OnGUI screens remain active when XR input is not selected.
    /// </summary>
    public sealed class XRPresentationUI : MonoBehaviour
    {
        public static bool IsActive { get; private set; }

        const float CanvasDistance = 1.0f;
        const float CanvasScale = 0.0012f;
        DesktopHUD hud;
        Canvas canvas;
        RectTransform canvasRect;
        Font font;
        Image fadeImage;
        string lastSignature;

        void Awake()
        {
            hud = GetComponent<DesktopHUD>();
        }

        void Start()
        {
            if (hud == null || hud.player == null || !hud.player.useXRInput)
            {
                enabled = false;
                return;
            }

            IsActive = true;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            EnsureCanvas();
            EnableRayUI();
            Refresh(true);
            UpdateCanvasPresentation();
        }

        void LateUpdate()
        {
            if (!IsActive || hud == null || hud.player == null)
                return;

            EnsureCanvas();
            UpdateCanvasPresentation();
            string signature = MakeSignature();
            if (!String.Equals(signature, lastSignature, StringComparison.Ordinal))
                Refresh(false);

            var transitions = SceneTransitionManager.Instance;
            if (fadeImage != null)
            {
                var color = fadeImage.color;
                color.a = transitions == null ? 0f : Mathf.Clamp01(transitions.Fade);
                fadeImage.color = color;
                fadeImage.gameObject.SetActive(color.a > 0.001f);
                fadeImage.transform.SetAsLastSibling();
            }
        }

        void OnDestroy()
        {
            if (hud != null && hud.player != null && hud.player.useXRInput)
                IsActive = false;
        }

        void EnsureEventSystem()
        {
            var eventSystem = FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
                eventSystem = new GameObject("XR UI Event System", typeof(EventSystem)).GetComponent<EventSystem>();

            foreach (var module in eventSystem.GetComponents<BaseInputModule>())
            {
                if (!(module is XRUIInputModule))
                    Destroy(module);
            }

            var xrModule = eventSystem.GetComponent<XRUIInputModule>();
            if (xrModule == null)
                xrModule = eventSystem.gameObject.AddComponent<XRUIInputModule>();
            xrModule.enableMouseInput = true;
            xrModule.enableXRInput = true;
            xrModule.enableBuiltinActionsAsFallback = true;
            eventSystem.sendNavigationEvents = true;
        }

        void EnsureCanvas()
        {
            var camera = hud != null && hud.player != null ? hud.player.view : null;
            if (camera == null)
                return;

            if (canvas != null)
            {
                canvas.worldCamera = camera;
                return;
            }

            var root = new GameObject("Campus Quest XR Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            root.layer = LayerMask.NameToLayer("UI");
            // Keep screens attached to the tracked camera. Detaching modal screens
            // left them behind when the user's head moved and made controller-ray
            // selection unreliable.
            root.transform.SetParent(camera.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, CanvasDistance);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * CanvasScale;

            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.sortingOrder = 50;
            canvasRect = root.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1500f, 900f);

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 12f;
            scaler.referencePixelsPerUnit = 100f;

            var fade = CreateImage("XR Screen Fade", root.transform, new Color(0f, 0f, 0f, 0f));
            Stretch(fade.rectTransform, 0f, 0f, 0f, 0f);
            fade.raycastTarget = false;
            fadeImage = fade;
            fadeImage.gameObject.SetActive(false);
        }

        void UpdateCanvasPresentation()
        {
            if (canvas == null || hud == null || hud.player == null || hud.player.view == null)
                return;

            var cameraTransform = hud.player.view.transform;
            if (canvas.transform.parent != cameraTransform)
                canvas.transform.SetParent(cameraTransform, false);
            canvas.transform.localPosition = new Vector3(0f, 0f, CanvasDistance);
            canvas.transform.localRotation = Quaternion.identity;
            canvas.transform.localScale = Vector3.one * CanvasScale;
        }

        void EnableRayUI()
        {
            var rig = hud.player.GetComponentsInChildren<XRRayInteractor>(true);
            foreach (var ray in rig)
                ray.enableUIInteraction = true;

            var nearFar = hud.player.GetComponentsInChildren<NearFarInteractor>(true);
            foreach (var interactor in nearFar)
                interactor.enableUIInteraction = true;
        }

        string MakeSignature()
        {
            var b = new StringBuilder(512);
            var missionUI = MissionUI.Instance;
            var mission = MissionManager.Instance;
            var order = CanteenOrderManager.Instance;

            b.Append(hud.OpenLift != null ? "lift:" + hud.OpenLift.cabinID : "-").Append('|')
                .Append(hud.OpenMenuBoard != null ? "menu" : "-").Append('|')
                .Append(hud.HasInfo ? hud.InfoTitle + hud.InfoText : "-").Append('|')
                .Append(hud.IsPaused).Append(hud.DebugMenuOpen).Append('|')
                .Append(missionUI != null && missionUI.ShowList).Append('|')
                .Append(mission == null ? "nomission" : mission.Started + "/" + mission.CompletedCount + "/" + mission.score.Total).Append('|')
                .Append(mission == null ? "" : mission.timer.RemainingText).Append('|')
                .Append(mission != null && mission.quiz.Active ? "quiz:" + mission.quiz.Current?.prompt + mission.quiz.Feedback + mission.quiz.Answered : "-").Append('|')
                .Append(mission != null && mission.results.Visible ? mission.results.Report : "-").Append('|')
                .Append(order == null ? "noorder" : order.Step + "/" + order.Feedback).Append('|')
                .Append(SceneTransitionManager.Instance == null ? "" : SceneTransitionManager.Instance.Status).Append('|')
                .Append(hud.textSize).Append('|')
                .Append(hud.player == null || hud.player.Target == null ? "" : hud.player.Target.prompt);

            if (mission != null && mission.Current != null)
            {
                b.Append('|').Append(mission.Current.title).Append('|').Append(mission.guide == null ? "" : mission.guide.Subtitle);
                foreach (var objective in mission.Current.objectives)
                    b.Append('|').Append(mission.checkpoints.Has(objective.Key));
            }

            return b.ToString();
        }

        void Refresh(bool force)
        {
            if (canvasRect == null)
                return;

            string signature = MakeSignature();
            if (!force && signature == lastSignature)
                return;
            lastSignature = signature;

            for (int i = canvasRect.childCount - 1; i >= 0; i--)
            {
                var child = canvasRect.GetChild(i);
                if (fadeImage != null && child == fadeImage.transform)
                    continue;
                Destroy(child.gameObject);
            }

            var missionUI = MissionUI.Instance;
            var mission = MissionManager.Instance;
            var order = CanteenOrderManager.Instance;

            if (hud.OpenLift != null)
                BuildLiftMenu(hud.OpenLift);
            else if (hud.OpenMenuBoard != null)
                BuildCanteenMenu(hud.OpenMenuBoard, order);
            else if (hud.HasInfo)
                BuildInfo(hud.InfoTitle, hud.InfoText);
            else if (hud.DebugMenuOpen)
                BuildDebugMenu();
            else if (hud.IsPaused)
                BuildPauseMenu();
            else if (mission != null && !mission.Started)
                BuildOrientationStart(missionUI, mission);
            else if (mission != null && mission.results.Visible)
                BuildResults(missionUI, mission);
            else if (mission != null && mission.quiz.Active)
                BuildQuiz(missionUI, mission);
            else if (missionUI != null && missionUI.ShowList)
                BuildMissionList(missionUI, mission);
            else
                BuildGameplayHud(mission, order);
        }

        void BuildGameplayHud(MissionManager mission, CanteenOrderManager order)
        {
            var tm = SceneTransitionManager.Instance;
            var left = CreatePanel("Level status", canvasRect, new Vector2(20f, -20f), new Vector2(560f, 150f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            AddLabel(left, (tm == null ? "Loading campus" : tm.Status) + "\nWASD (bound to left stick) — move in Simulator\nLeft stick — move on Quest · aim + trigger — interact", 25, FontStyle.Normal, 1f);

            if (mission != null && mission.Started)
            {
                var right = CreatePanel("Mission status", canvasRect, new Vector2(-20f, -20f), new Vector2(620f, 220f), new Vector2(1f, 1f), new Vector2(1f, 1f));
                var current = mission.Current;
                string text = "Mission " + (mission.CompletedCount + 1) + " / " + mission.missions.Length + "   |   " + mission.timer.RemainingText + "\nScore: " + mission.score.Total;
                if (current != null)
                {
                    var next = current.objectives.FirstOrDefault(o => !mission.checkpoints.Has(o.Key));
                    text += "\n" + current.title + "\nNEXT: " + (next == null ? "Read the campus check to continue." : next.instruction + (next.facility == null ? "" : " — " + next.facility.DisplayName));
                }
                AddLabel(right, text, 24, FontStyle.Normal, 1f);
            }

            if (hud.player != null && hud.player.Target != null)
            {
                var prompt = CreatePanel("Interaction prompt", canvasRect, new Vector2(0f, 115f), new Vector2(720f, 68f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
                AddLabel(prompt, "Trigger — " + hud.player.Target.prompt, 24, FontStyle.Normal, 1f);
            }

            if (order != null && tm != null && tm.CurrentLevel == -1)
            {
                var canteen = CreatePanel("Canteen status", canvasRect, new Vector2(20f, -190f), new Vector2(560f, 210f), new Vector2(0f, 1f), new Vector2(0f, 1f));
                AddLabel(canteen, "CANTEEN — " + order.Step + "\n" + order.Objective + "\n" + order.Feedback, 24, FontStyle.Normal, 1f);
            }

            var menuButton = CreateButton("Mission menu / pause", canvasRect, new Vector2(0f, 25f), new Vector2(360f, 72f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), () => hud.OpenPauseMenu());
            menuButton.transform.SetAsLastSibling();
        }

        void BuildOrientationStart(MissionUI ui, MissionManager mission)
        {
            var content = BeginModal("NMIMS Campus Quest");
            AddLabel(content, "Explore key facilities and complete ten orientation missions. Target: 28 minutes; maximum 30 minutes of active play.", 28, FontStyle.Normal, 1f);
            AddLabel(content, "WASD (bound to left-stick movement) — move in Meta XR Simulator\nLeft thumbstick — move on Quest\nAim a controller ray and press its trigger to select or interact", 27, FontStyle.Normal, 1f);
            AddLabel(content, "Look at red information buttons beside doors. Begin with the entrance, reception and CCTV security room.", 25, FontStyle.Normal, 1f);
            AddLabel(content, "Classroom 101's assignment and the AR/VR lab position are provisional practice targets, excluded from the quiz.", 25, FontStyle.Normal, 1f);
            AddButton(content, "Start orientation", () => { if (ui != null) { ui.ShowList = false; mission.StartRun(); } });
            AddButton(content, "Quit", Application.Quit);
        }

        void BuildResults(MissionUI ui, MissionManager mission)
        {
            var content = BeginModal("Orientation results");
            AddLabel(content, mission.results.Report, 26, FontStyle.Normal, 1f);
            AddButton(content, "Restart orientation", () => { if (ui != null) ui.ShowList = false; mission.Restart(); });
        }

        void BuildQuiz(MissionUI ui, MissionManager mission)
        {
            var content = BeginModal("Campus check");
            var question = mission.quiz.Current;
            if (question == null)
                return;
            AddLabel(content, question.prompt, 30, FontStyle.Bold, 1f);
            foreach (var pair in question.answers.Select((answer, index) => new { answer, index }))
            {
                int choice = pair.index;
                var button = AddButton(content, pair.answer, () => mission.quiz.Submit(choice));
                button.interactable = !mission.quiz.Answered;
            }
            AddLabel(content, mission.quiz.Feedback, 25, FontStyle.Normal, 1f);
            if (mission.quiz.Answered)
                AddButton(content, "Continue", () => mission.quiz.Next());
            else
                AddButton(content, "Review answer — no answer points", () => mission.quiz.ReviewAnswer());
        }

        void BuildMissionList(MissionUI ui, MissionManager mission)
        {
            var content = BeginModal("Mission list");
            if (mission != null && mission.Current != null)
            {
                AddLabel(content, "CURRENT: " + mission.Current.title, 28, FontStyle.Bold, 1f);
                foreach (var objective in mission.Current.objectives)
                    AddLabel(content, (mission.checkpoints.Has(objective.Key) ? "[Done] " : "[ ] ") + objective.instruction + (objective.facility == null ? "" : " — " + objective.facility.DisplayName), 24, FontStyle.Normal, 1f);
                AddLabel(content, "Guide: " + (mission.guide == null ? "" : mission.guide.Subtitle) + "\nHint: " + mission.Current.hint, 24, FontStyle.Normal, 1f);
            }
            if (mission != null)
                for (int i = 0; i < mission.missions.Length; i++)
                    AddLabel(content, (i < mission.CompletedCount ? "[Done] " : i == mission.CompletedCount ? "[Current] " : "[Later] ") + mission.missions[i].title, 23, FontStyle.Normal, 1f);
            AddButton(content, "Return to campus", () => ui.ShowList = false);
        }

        void BuildLiftMenu(LiftController lift)
        {
            var content = BeginModal("Lift " + lift.cabinID + " — choose floor");
            AddLabel(content, "Aim at a floor and press the trigger.", 25, FontStyle.Normal, 1f);
            var grid = new GameObject("Floor buttons", typeof(RectTransform), typeof(GridLayoutGroup));
            grid.transform.SetParent(content, false);
            var gridRect = grid.GetComponent<RectTransform>();
            gridRect.sizeDelta = new Vector2(1100f, 300f);
            var layout = grid.GetComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(190f, 105f);
            layout.spacing = new Vector2(14f, 14f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 5;
            var element = grid.AddComponent<LayoutElement>();
            element.preferredHeight = 230f;

            for (int row = 0; row < 2; row++)
            for (int col = 0; col < 5; col++)
            {
                int level = row * 5 + col - 1;
                AddButton(grid.transform, FloorConfig.Label(level), () => lift.Select(level));
            }
            AddButton(content, "Close", () => hud.CloseMenus());
        }

        void BuildCanteenMenu(MenuBoard board, CanteenOrderManager order)
        {
            var content = BeginModal("Canteen menu");
            if (order == null)
            {
                AddLabel(content, "Canteen ordering is unavailable.", 25, FontStyle.Normal, 1f);
                return;
            }
            foreach (var item in order.menuItems)
            {
                if (item == null || item.isPlaceholder || !item.isVerified)
                    continue;
                var selected = item;
                AddButton(content, item.displayName + " — INR " + item.priceINR, () => board.Select(selected));
            }
            AddLabel(content, "Choose one dish. No payment is taken.", 24, FontStyle.Normal, 1f);
            AddButton(content, "Close", () => hud.CloseMenus());
        }

        void BuildInfo(string title, string body)
        {
            var content = BeginModal(title);
            AddLabel(content, body, 26, FontStyle.Normal, 1f);
            AddButton(content, "Close / Resume", () => hud.CloseMenus());
        }

        void BuildPauseMenu()
        {
            var content = BeginModal("Paused");
            AddButton(content, "Mission list and current hint", () => { hud.CloseMenus(); if (MissionUI.Instance != null) MissionUI.Instance.ShowList = true; });
            AddButton(content, "Restart at ground entrance", () =>
            {
                if (MissionManager.Instance != null) MissionManager.Instance.Restart();
                else { if (CanteenOrderManager.Instance != null) CanteenOrderManager.Instance.ResetSession(); hud.CloseMenus(); if (SceneTransitionManager.Instance != null) SceneTransitionManager.Instance.Travel(0, "start"); }
            });
            AddButton(content, "Close / Resume", () => hud.CloseMenus());
            AddButton(content, "Quit", Application.Quit);
        }

        void BuildDebugMenu()
        {
            var content = BeginModal("Development tools");
            if (MissionManager.Instance != null)
                AddButton(content, "Skip current mission — practice only", () => { MissionManager.Instance.DebugSkip(); hud.CloseMenus(); });
            if (CanteenOrderManager.Instance != null)
                AddButton(content, "Restart canteen practice — keep points", () => { CanteenOrderManager.Instance.Replay(); hud.CloseMenus(); });
            for (int i = -1; i <= 8; i++)
            {
                int target = i;
                AddButton(content, "Jump to floor " + FloorConfig.Label(i), () =>
                {
                    if (MissionManager.Instance != null) MissionManager.Instance.MarkDebug();
                    hud.CloseMenus();
                    if (SceneTransitionManager.Instance != null) SceneTransitionManager.Instance.Travel(target, "start");
                });
            }
            AddButton(content, "Close", () => hud.CloseMenus());
        }

        RectTransform BeginModal(string title)
        {
            var backdrop = CreateImage("Modal backdrop", canvasRect, new Color(0f, 0f, 0f, 0.18f));
            Stretch(backdrop.rectTransform, 0f, 0f, 0f, 0f);
            backdrop.raycastTarget = true;

            var panel = CreateImage("Modal panel", canvasRect, new Color(0.035f, 0.055f, 0.08f, 0.78f));
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(1180f, 820f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(panel.transform, false);
            var viewRect = viewport.GetComponent<RectTransform>();
            Stretch(viewRect, 30f, 25f, 30f, 25f);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.015f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var vertical = content.GetComponent<VerticalLayoutGroup>();
            vertical.padding = new RectOffset(4, 4, 4, 18);
            vertical.spacing = 12f;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalNormalizedPosition = 1f;

            AddLabel(content.transform, title, 36, FontStyle.Bold, 1f);
            return contentRect;
        }

        void BuildPanelBackground(Transform parent, Vector2 position, Vector2 size, Vector2 anchor, Vector2 pivot)
        {
            var image = CreateImage("HUD panel", parent, new Color(0.035f, 0.055f, 0.08f, 0.9f));
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        RectTransform CreatePanel(string name, Transform parent, Vector2 position, Vector2 size, Vector2 anchor, Vector2 pivot)
        {
            var image = CreateImage(name, parent, new Color(0.035f, 0.055f, 0.08f, 0.9f));
            var panel = image.rectTransform;
            panel.anchorMin = panel.anchorMax = anchor;
            panel.pivot = pivot;
            panel.anchoredPosition = position;
            panel.sizeDelta = size;
            var textRoot = new GameObject("Panel content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            textRoot.transform.SetParent(panel, false);
            var rect = textRoot.GetComponent<RectTransform>();
            Stretch(rect, 18f, 14f, 18f, 14f);
            var layout = textRoot.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        void AddLabel(Transform parent, string value, int size, FontStyle style, float flexibleWidth)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = value ?? string.Empty;
            text.raycastTarget = false;
            var element = go.GetComponent<LayoutElement>();
            element.flexibleWidth = flexibleWidth;
            element.minHeight = Mathf.Max(44f, size * 1.5f);
            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        Button CreateButton(string label, Transform parent, Vector2 position, Vector2 size, Vector2 anchor, Vector2 pivot, Action action)
        {
            var button = AddButton(parent, label, action);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return button;
        }

        Button AddButton(Transform parent, string label, Action action)
        {
            var go = new GameObject("Button - " + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.17f, 0.23f, 0.30f, 1f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.interactable = true;
            button.onClick.AddListener(() => action?.Invoke());
            var element = go.GetComponent<LayoutElement>();
            element.minHeight = 68f;
            element.preferredHeight = 74f;
            element.flexibleWidth = 1f;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            Stretch(textRect, 14f, 5f, 14f, 5f);
            var text = textGo.GetComponent<Text>();
            text.font = font;
            text.fontSize = 26;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = label;
            text.raycastTarget = false;
            return button;
        }

        Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}


