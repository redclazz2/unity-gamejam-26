using System;
using System.Linq;
using System.Text;
using LuckyCiv.Core;
using LuckyCiv.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace LuckyCiv.UI
{
    /// <summary>
    /// The whole management interface: HUD, assignment panel, worker pool, journal, emergency and end-game modals.
    /// It never computes rules; it only shows <see cref="Simulation.State"/> and sends commands.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public sealed class GameHUD : MonoBehaviour
    {
        const float SideWidth = 440f;
        const float TopBarHeight = 64f;
        const float BottomHeight = 215f;

        GameController controller;
        Canvas canvas;
        Simulation sim;
        Villager selected;
        bool scrollLogToBottom;

        Text dayText, luckText, workersText, foodText, waterText, happinessText, housingText, needsText;
        RectTransform activitiesContent, poolContent;
        Text logText;
        ScrollRect logScroll;
        Button primaryButton;
        Text primaryLabel;
        GameObject emergencyModal;
        Text emergencyTitle, emergencyDescription;
        RectTransform emergencyOptions;
        GameObject gameOverModal;
        Text gameOverTitle, gameOverSummary;

        void Start()
        {
            controller = GetComponent<GameController>();
            sim = controller.Sim;
            Build();
            sim.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (sim != null) sim.Changed -= Refresh;
        }

        /// <summary>
        /// Screen area not covered by the side, top and bottom panels, in viewport coordinates (0–1).
        /// The village camera centers and fits the village inside it.
        /// </summary>
        public Rect WorldViewport
        {
            get
            {
                if (canvas == null || Screen.width <= 0 || Screen.height <= 0) return new Rect(0f, 0f, 1f, 1f);
                float k = canvas.scaleFactor;
                float w = Screen.width, h = Screen.height;
                float xMin = SideWidth * k / w, xMax = 1f - SideWidth * k / w;
                float yMin = BottomHeight * k / h, yMax = 1f - TopBarHeight * k / h;
                if (xMax <= xMin || yMax <= yMin) return new Rect(0f, 0f, 1f, 1f);
                return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
        }

        void LateUpdate()
        {
            if (!scrollLogToBottom) return;
            scrollLogToBottom = false;
            // The journal grew this frame; rebuild its layout first or the scroll stops short of the new lines.
            Canvas.ForceUpdateCanvases();
            logScroll.verticalNormalizedPosition = 0f;
        }

        // ------------------------------------------------------------------ build

        void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                eventSystem.AddComponent<InputSystemUIInputModule>();
#else
                eventSystem.AddComponent<StandaloneInputModule>();
#endif
            }

            var root = (RectTransform)canvasGo.transform;
            BuildTopBar(root);
            BuildActivitiesPanel(root);
            BuildPoolPanel(root);
            BuildJournalPanel(root);
            BuildEmergencyModal(root);
            BuildGameOverModal(root);
        }

        void BuildTopBar(RectTransform root)
        {
            var bar = UIFactory.MakePanel(root, "TopBar", UIFactory.PanelColor);
            UIFactory.Anchor(bar, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -TopBarHeight), Vector2.zero);
            UIFactory.HStack(bar, 28f, 16);

            dayText = UIFactory.MakeLabel(bar, "", 28, UIFactory.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, false);
            luckText = UIFactory.MakeLabel(bar, "", 22, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold, false);
            workersText = UIFactory.MakeLabel(bar, "", 22, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            foodText = UIFactory.MakeLabel(bar, "", 22, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            waterText = UIFactory.MakeLabel(bar, "", 22, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            happinessText = UIFactory.MakeLabel(bar, "", 22, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            housingText = UIFactory.MakeLabel(bar, "", 22, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            needsText = UIFactory.MakeLabel(bar, "", 22, UIFactory.Bad, TextAnchor.MiddleLeft, FontStyle.Bold, false);
        }

        void BuildActivitiesPanel(RectTransform root)
        {
            var panel = UIFactory.MakePanel(root, "Activities", UIFactory.PanelColor);
            UIFactory.Anchor(panel, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, BottomHeight), new Vector2(SideWidth, -TopBarHeight));
            UIFactory.VStack(panel, 8f, 12);

            var title = UIFactory.MakeLabel(panel, "Actividades", 26, UIFactory.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(title, preferredHeight: 36f);

            activitiesContent = UIFactory.ScrollView(panel, out var scroll);
            UIFactory.Size(scroll, flexibleHeight: 1f);
        }

        void BuildPoolPanel(RectTransform root)
        {
            var panel = UIFactory.MakePanel(root, "Pool", UIFactory.PanelColor);
            UIFactory.Anchor(panel, Vector2.zero, new Vector2(1f, 0f), new Vector2(SideWidth, 0f), new Vector2(-SideWidth, BottomHeight));
            UIFactory.VStack(panel, 6f, 12);

            var title = UIFactory.MakeLabel(panel, "Habitantes", 26, UIFactory.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(title, preferredHeight: 34f);

            UIFactory.MakeLabel(panel,
                "Pulsa un habitante disponible y después \"+ Asignar\" en una actividad. Pulsa un habitante asignado para retirarlo.",
                16, UIFactory.MutedColor);
            UIFactory.MakeLabel(panel,
                "N Normal   ·   F Fuerte: defensa y construcción x2   ·   I Inteligente: entretenimiento x2 y menos accidentes   ·   R Rápido: agricultura y agua x2",
                16, UIFactory.MutedColor);

            poolContent = UIFactory.Container(panel, "Chips");
            UIFactory.Grid(poolContent, new Vector2(196f, 36f), 6f, 5);
            UIFactory.Size(poolContent, flexibleHeight: 1f);
        }

        void BuildJournalPanel(RectTransform root)
        {
            var panel = UIFactory.MakePanel(root, "Journal", UIFactory.PanelColor);
            UIFactory.Anchor(panel, new Vector2(1f, 0f), Vector2.one, new Vector2(-SideWidth, BottomHeight), new Vector2(0f, -TopBarHeight));
            UIFactory.VStack(panel, 8f, 12);

            var title = UIFactory.MakeLabel(panel, "Diario", 26, UIFactory.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(title, preferredHeight: 36f);

            var content = UIFactory.ScrollView(panel, out logScroll);
            UIFactory.Size(logScroll, flexibleHeight: 1f);
            logText = UIFactory.MakeLabel(content, "", 17, UIFactory.TextColor, TextAnchor.UpperLeft);

            primaryButton = UIFactory.MakeButton(panel, "", UIFactory.Accent, OnPrimary, 22, 56f, UIFactory.DarkText);
            primaryLabel = primaryButton.GetComponentInChildren<Text>();
            primaryLabel.fontStyle = FontStyle.Bold;
        }

        void BuildEmergencyModal(RectTransform root)
        {
            var dim = UIFactory.MakePanel(root, "EmergencyModal", new Color(0f, 0f, 0f, 0.65f));
            UIFactory.Stretch(dim);
            emergencyModal = dim.gameObject;

            var box = BuildModalBox(dim, new Color(0.14f, 0.1f, 0.1f, 0.98f));
            emergencyTitle = UIFactory.MakeLabel(box, "", 30, UIFactory.Bad, TextAnchor.MiddleLeft, FontStyle.Bold);
            emergencyDescription = UIFactory.MakeLabel(box, "", 20, UIFactory.TextColor);
            emergencyOptions = UIFactory.Container(box, "Options");
            UIFactory.VStack(emergencyOptions, 8f, 0);
        }

        void BuildGameOverModal(RectTransform root)
        {
            var dim = UIFactory.MakePanel(root, "GameOverModal", new Color(0f, 0f, 0f, 0.75f));
            UIFactory.Stretch(dim);
            gameOverModal = dim.gameObject;

            var box = BuildModalBox(dim, new Color(0.1f, 0.11f, 0.14f, 0.98f));
            gameOverTitle = UIFactory.MakeLabel(box, "", 36, UIFactory.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            gameOverSummary = UIFactory.MakeLabel(box, "", 22, UIFactory.TextColor, TextAnchor.MiddleCenter);
            UIFactory.MakeButton(box, "Jugar de nuevo", UIFactory.Accent, controller.Restart, 22, 56f, UIFactory.DarkText);
        }

        static RectTransform BuildModalBox(RectTransform dim, Color color)
        {
            var box = UIFactory.MakePanel(dim, "Box", color);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(760f, 0f);
            UIFactory.VStack(box, 14f, 28);
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return box;
        }

        // ---------------------------------------------------------------- refresh

        void Refresh()
        {
            var s = sim.State;
            if (selected != null && (selected.Assignment != null || !selected.CanWork || !s.Villagers.Contains(selected)))
                selected = null;

            dayText.text = "Día " + s.Day + " / " + sim.Config.TotalDays;
            luckText.text = "Suerte: " + s.Luck + "  -  " + Simulation.LuckDescription(s.Luck);
            workersText.text = "Trabajadores: " + sim.AvailableWorkers + " / " + s.Population;

            foodText.text = "Comida: " + s.Food + "  (-" + sim.FoodConsumption + "/noche)";
            foodText.color = s.Food < sim.FoodConsumption ? UIFactory.Bad : UIFactory.TextColor;

            waterText.text = "Agua: " + s.Water + "  (-" + sim.WaterConsumption + "/noche)" + (s.WellBroken ? "  POZO ROTO" : "");
            waterText.color = s.Water < sim.WaterConsumption || s.WellBroken ? UIFactory.Bad : UIFactory.TextColor;

            happinessText.text = "Felicidad: " + s.Happiness;
            happinessText.color = s.Happiness < 30 ? UIFactory.Bad : UIFactory.TextColor;

            housingText.text = "Vivienda: " + s.Population + " / " + s.Housing;
            housingText.color = s.Population > s.Housing ? UIFactory.Bad : UIFactory.TextColor;

            needsText.text = s.Hunger > 0 || s.Thirst > 0
                ? "Hambre " + s.Hunger + "/" + sim.Config.StarvationNights + "   Sed " + s.Thirst + "/" + sim.Config.StarvationNights
                : "";

            RebuildActivities();
            RebuildPool();
            RebuildLog();
            UpdatePrimary();
            UpdateEmergency();
            UpdateGameOver();
        }

        void RebuildActivities()
        {
            UIFactory.Clear(activitiesContent);
            foreach (var def in sim.ActiveActivities) BuildActivityRow(def);
        }

        void BuildActivityRow(ActivityDef def)
        {
            var s = sim.State;
            bool planning = s.Phase == GamePhase.Planning;
            int points = sim.Points(def.Type);
            bool complete = points >= def.MinPoints;

            var row = UIFactory.MakePanel(activitiesContent, def.Name, UIFactory.RowColor);
            UIFactory.VStack(row, 4f, 10);

            var header = UIFactory.Container(row, "Header");
            UIFactory.HStack(header, 8f, 0);
            UIFactory.Size(header, preferredHeight: 30f);
            var name = UIFactory.MakeLabel(header, def.Name, 22, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(name, flexibleWidth: 1f);
            string status = complete ? "  OK" : points > 0 ? "  parcial" : "";
            var statusColor = complete ? UIFactory.Good : points > 0 ? UIFactory.Accent : UIFactory.MutedColor;
            UIFactory.MakeLabel(header, "Puntos " + points + " / " + def.MinPoints + status, 20, statusColor, TextAnchor.MiddleRight, FontStyle.Bold, false);

            var info = new StringBuilder(def.Summary);
            if (def.BaseOutput > 0 && points > 0) info.Append("   ->   hoy +").Append(sim.ExpectedOutput(def.Type)).Append(' ').Append(def.OutputUnit);
            if (def.Type == ActivityType.Water && s.WellBroken) info.Append("   (pozo roto)");
            if (def.AccidentChance > 0f)
            {
                float risk = points > 0 ? sim.Risk(def.Type) : def.AccidentChance;
                info.Append("\nRiesgo de ").Append(def.AccidentName.ToLowerInvariant()).Append(": ").Append(Mathf.RoundToInt(risk * 100f)).Append('%');
            }
            info.Append("\nEspecialistas: ").Append(def.SpecialistNames).Append(" (2 puntos)");
            UIFactory.MakeLabel(row, info.ToString(), 16, UIFactory.MutedColor);

            var chips = UIFactory.Container(row, "Chips");
            UIFactory.Grid(chips, new Vector2(128f, 32f), 4f, 3);
            foreach (var worker in sim.Assigned(def.Type).ToList())
            {
                var captured = worker;
                var chip = UIFactory.MakeButton(chips, captured.Name + " (" + captured.Class.Initial() + ")", ClassColor(captured.Class),
                    () => sim.Unassign(captured), 16, 32f, UIFactory.DarkText);
                chip.interactable = planning;
            }
            var assign = UIFactory.MakeButton(chips, selected != null ? "+ " + selected.Name : "+ Asignar", UIFactory.ButtonColor,
                () => AssignSelected(def.Type), 16, 32f);
            assign.interactable = planning && selected != null;
        }

        void RebuildPool()
        {
            UIFactory.Clear(poolContent);
            var s = sim.State;
            bool planning = s.Phase == GamePhase.Planning;

            foreach (var villager in s.Villagers)
            {
                var captured = villager;
                string tag = captured.Name + " (" + captured.Class.Initial() + ")";
                if (captured.InjuredDays > 0)
                {
                    var chip = UIFactory.MakeButton(poolContent, tag + "  herido", UIFactory.ChipColor, null, 16, 36f, UIFactory.MutedColor);
                    chip.interactable = false;
                }
                else if (!captured.AvailableToday)
                {
                    var chip = UIFactory.MakeButton(poolContent, tag + "  descansa", UIFactory.ChipColor, null, 16, 36f, UIFactory.MutedColor);
                    chip.interactable = false;
                }
                else if (captured.Assignment != null)
                {
                    var chip = UIFactory.MakeButton(poolContent, tag + " -> " + ActivityCatalog.Get(captured.Assignment.Value).Name,
                        ClassColor(captured.Class), () => sim.Unassign(captured), 16, 36f, UIFactory.DarkText);
                    chip.interactable = planning;
                }
                else
                {
                    bool isSelected = captured == selected;
                    var chip = UIFactory.MakeButton(poolContent, (isSelected ? "> " : "") + tag,
                        isSelected ? UIFactory.SelectedColor : ClassColor(captured.Class), () => ToggleSelected(captured), 17, 36f, UIFactory.DarkText);
                    chip.interactable = planning;
                }
            }
        }

        void RebuildLog()
        {
            var lines = sim.State.Log;
            int start = Math.Max(0, lines.Count - 80);
            var sb = new StringBuilder();
            for (int i = start; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.StartsWith("---")) sb.Append("<color=#F5C242><b>").Append(line).Append("</b></color>");
                else if (line.Contains("ha muerto") || line.StartsWith("¡Bandidos") || line.StartsWith("¡Faltan")) sb.Append("<color=#F2665A>").Append(line).Append("</color>");
                else if (line.StartsWith("¡Ha nacido") || line.Contains("nueva vivienda") || line.Contains("reparado")) sb.Append("<color=#73D980>").Append(line).Append("</color>");
                else sb.Append(line);
                if (i < lines.Count - 1) sb.Append('\n');
            }
            logText.text = sb.ToString();
            scrollLogToBottom = true;
        }

        void UpdatePrimary()
        {
            var s = sim.State;
            switch (s.Phase)
            {
                case GamePhase.Planning:
                    primaryButton.gameObject.SetActive(true);
                    primaryLabel.text = sim.Pool.Any() ? "Terminar el día (hay gente sin asignar)" : "Terminar el día";
                    break;
                case GamePhase.NightReport:
                    primaryButton.gameObject.SetActive(true);
                    primaryLabel.text = "Comenzar el día " + (s.Day + 1);
                    break;
                default:
                    primaryButton.gameObject.SetActive(false);
                    break;
            }
        }

        void UpdateEmergency()
        {
            var s = sim.State;
            var emergency = s.CurrentEmergency;
            bool show = s.Phase == GamePhase.Emergency && emergency != null;
            emergencyModal.SetActive(show);
            if (!show) return;

            emergencyTitle.text = emergency.Title;
            emergencyDescription.text = emergency.Description;
            UIFactory.Clear(emergencyOptions);
            for (int i = 0; i < emergency.Options.Count; i++)
            {
                int index = i;
                var option = emergency.Options[i];
                bool affordable = option.CanAfford(s);
                string cost = option.FoodCost + option.WaterCost > 0 ? "   <color=#F5C242>(" + option.CostText + ")</color>" : "";
                string label = "<b>" + option.Label + "</b>" + cost + "\n<size=16>" + option.Detail + (affordable ? "" : "   -   no hay recursos") + "</size>";
                var button = UIFactory.MakeButton(emergencyOptions, label, UIFactory.ButtonColor, () => sim.ChooseOption(index), 19, 64f);
                button.interactable = affordable;
            }
        }

        void UpdateGameOver()
        {
            var s = sim.State;
            bool show = s.Phase == GamePhase.GameOver;
            gameOverModal.SetActive(show);
            if (!show) return;
            bool won = s.Result == GameResult.Victory;
            gameOverTitle.text = won ? "¡Victoria!" : "Derrota";
            gameOverTitle.color = won ? UIFactory.Accent : UIFactory.Bad;
            gameOverSummary.text = sim.Summary();
        }

        // --------------------------------------------------------------- actions

        void ToggleSelected(Villager villager)
        {
            selected = selected == villager ? null : villager;
            Refresh();
        }

        void AssignSelected(ActivityType type)
        {
            if (selected == null) return;
            var villager = selected;
            selected = null;
            if (!sim.Assign(villager, type)) Refresh();
        }

        void OnPrimary()
        {
            switch (sim.State.Phase)
            {
                case GamePhase.Planning: sim.EndDay(); break;
                case GamePhase.NightReport: sim.NextDay(); break;
            }
        }

        static Color ClassColor(VillagerClass cls)
        {
            switch (cls)
            {
                case VillagerClass.Strong: return new Color(0.9f, 0.5f, 0.42f);
                case VillagerClass.Smart: return new Color(0.55f, 0.7f, 0.95f);
                case VillagerClass.Fast: return new Color(0.92f, 0.85f, 0.45f);
                default: return new Color(0.82f, 0.78f, 0.7f);
            }
        }
    }
}
