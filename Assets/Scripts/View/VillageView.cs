using System;
using System.Collections.Generic;
using System.Linq;
using LuckyCiv.Core;
using LuckyCiv.Game;
using LuckyCiv.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace LuckyCiv.View
{
    /// <summary>
    /// Modelos de la aldea (FBX generados con Tools/Blender/build_models.py).
    /// Si falta alguno, <see cref="VillageView"/> coloca una caja de color en su lugar y el juego sigue funcionando.
    /// </summary>
    [Serializable]
    public sealed class VillageModels
    {
        public GameObject VillagerNormal, VillagerStrong, VillagerSmart, VillagerFast;
        public GameObject House, Farm, Well, Plaza, Tower, Scaffold;
        public GameObject TreeRound, TreePine, Bush, Rock, Ground, Alert;

        public GameObject Villager(VillagerClass cls)
        {
            switch (cls)
            {
                case VillagerClass.Strong: return VillagerStrong;
                case VillagerClass.Smart: return VillagerSmart;
                case VillagerClass.Fast: return VillagerFast;
                default: return VillagerNormal;
            }
        }
    }

    /// <summary>
    /// Aldea 3D: un edificio por actividad, una casa por cada bloque de vivienda, un habitante por aldeano
    /// y transición día/noche. Solo lee <see cref="Simulation.State"/>; nunca calcula reglas.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public sealed class VillageView : MonoBehaviour
    {
        [SerializeField] VillageModels models = new VillageModels();

        const float CameraPitch = 50f;
        const float CameraFov = 38f;
        const float FrameMargin = 0.03f;

        static readonly Vector3 FarmPos = new Vector3(-6f, 0f, 3f);
        static readonly Vector3 WellPos = new Vector3(0.5f, 0f, 5f);
        static readonly Vector3 PlazaPos = new Vector3(0f, 0f, -0.5f);
        static readonly Vector3 TowerPos = new Vector3(6.5f, 0f, 3f);
        static readonly Vector3 HousesOrigin = new Vector3(-6f, 0f, -5f);
        const int HousesPerRow = 6;
        const float HouseSpacing = 2.4f;
        const float AlertHeight = 4.2f;

        static readonly Vector3[] TreeSpots =
        {
            new Vector3(-11f, 0f, 7f), new Vector3(-9f, 0f, 9f), new Vector3(-4f, 0f, 9.5f), new Vector3(5f, 0f, 9f),
            new Vector3(10f, 0f, 8f), new Vector3(11.5f, 0f, 1f), new Vector3(10.5f, 0f, -6f), new Vector3(-11f, 0f, -3f),
            new Vector3(-10f, 0f, -9f), new Vector3(3f, 0f, -10.5f),
        };
        static readonly Vector3[] BushSpots =
        {
            new Vector3(-3.6f, 0f, 7.2f), new Vector3(3.4f, 0f, 7.6f), new Vector3(-9.2f, 0f, 0.5f),
            new Vector3(9.6f, 0f, -2.5f), new Vector3(-1.5f, 0f, -10f), new Vector3(8.8f, 0f, 5.8f),
        };
        static readonly Vector3[] RockSpots =
        {
            new Vector3(-9.6f, 0f, 5.2f), new Vector3(9.4f, 0f, 0.4f), new Vector3(-8.6f, 0f, -7.2f), new Vector3(5.2f, 0f, -10.4f),
        };

        static readonly Color DaySun = new Color(1f, 0.96f, 0.88f);
        static readonly Color NightSun = new Color(0.45f, 0.55f, 0.95f);
        static readonly Color DayAmbient = new Color(0.55f, 0.58f, 0.62f);
        static readonly Color NightAmbient = new Color(0.08f, 0.1f, 0.18f);
        static readonly Color DaySky = new Color(0.55f, 0.78f, 0.92f);
        static readonly Color NightSky = new Color(0.04f, 0.05f, 0.11f);
        static readonly Color SadBanner = new Color(0.5f, 0.55f, 0.65f);
        static readonly Color HappyBanner = new Color(0.9f, 0.25f, 0.2f);

        static readonly Color Grass = new Color(0.42f, 0.6f, 0.3f);
        static readonly Color Stone = new Color(0.55f, 0.55f, 0.58f);
        static readonly Color Wood = new Color(0.72f, 0.52f, 0.32f);
        static readonly Color Leaf = new Color(0.25f, 0.5f, 0.24f);
        static readonly Color AlertRed = new Color(1f, 0.2f, 0.15f);

        Simulation sim;
        Material baseMaterial;
        Material banner;
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        readonly Dictionary<Villager, VillagerActor> actors = new Dictionary<Villager, VillagerActor>();
        readonly List<GameObject> houses = new List<GameObject>();
        readonly System.Random decor = new System.Random(7);
        Transform root;
        GameObject scaffold;
        GameObject wellAlert;
        Light sun;
        Camera cam;
        GameHUD hud;
        Vector2Int framedScreen;
        Rect framedViewport;
        int framedHouses = -1;
        float night;

        void Start()
        {
            sim = GetComponent<GameController>().Sim;
            root = new GameObject("Village").transform;

            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMaterial = probe.GetComponent<Renderer>().sharedMaterial;
            Destroy(probe);

            Place(models.Ground, root, Vector3.zero, new Vector3(44f, 0.02f, 44f), Grass);
            Place(models.Farm, root, FarmPos, new Vector3(5f, 0.3f, 4f), new Color(0.45f, 0.3f, 0.18f));
            Place(models.Well, root, WellPos, new Vector3(1.8f, 1f, 1.8f), Stone);
            Place(models.Tower, root, TowerPos, new Vector3(1.3f, 3.2f, 1.3f), Stone);
            var plaza = Place(models.Plaza, root, PlazaPos, new Vector3(4.8f, 0.06f, 4.8f), new Color(0.85f, 0.75f, 0.5f));
            banner = FindMaterial(plaza, "Banner");
            BuildDecoration();
            scaffold = Place(models.Scaffold, root, HousePos(0), new Vector3(1.6f, 1.6f, 1.6f), Wood);
            wellAlert = models.Alert != null
                ? Place(models.Alert, root, WellPos + Vector3.up * AlertHeight, Vector3.one, AlertRed)
                : Make(PrimitiveType.Sphere, root, WellPos + Vector3.up * AlertHeight, Vector3.one * 0.5f, AlertRed);

            SetupCamera();
            SetupLight();

            sim.Changed += OnChanged;
            OnChanged();
        }

        void OnDestroy()
        {
            if (sim != null) sim.Changed -= OnChanged;
        }

        void Update()
        {
            FrameIfNeeded();

            var phase = sim.State.Phase;
            float target = phase == GamePhase.NightReport || phase == GamePhase.GameOver ? 1f : 0f;
            night = Mathf.MoveTowards(night, target, Time.deltaTime / 1.5f);

            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(2f, 0.25f, night);
                sun.color = Color.Lerp(DaySun, NightSun, night);
                sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(50f, 15f, night), -30f, 0f);
            }
            RenderSettings.ambientLight = Color.Lerp(DayAmbient, NightAmbient, night);
            if (cam != null) cam.backgroundColor = Color.Lerp(DaySky, NightSky, night);

            if (wellAlert.activeSelf)
            {
                wellAlert.transform.position = WellPos + new Vector3(0f, AlertHeight + Mathf.Sin(Time.time * 4f) * 0.2f, 0f);
                wellAlert.transform.Rotate(0f, 90f * Time.deltaTime, 0f);
            }
        }

        // ------------------------------------------------------------------ sync

        void OnChanged()
        {
            var s = sim.State;
            SyncHouses(s);
            scaffold.SetActive(s.ConstructionProgress > 0);
            scaffold.transform.position = HousePos(houses.Count);
            wellAlert.SetActive(s.WellBroken);
            if (banner != null) banner.color = Color.Lerp(SadBanner, HappyBanner, s.Happiness / 100f);
            SyncVillagers(s);
        }

        void SyncHouses(GameState s)
        {
            int wanted = Mathf.Max(0, s.Housing / Mathf.Max(1, sim.Config.HousingPerConstruction));
            while (houses.Count < wanted)
                houses.Add(Place(models.House, root, HousePos(houses.Count), new Vector3(1.6f, 1.3f, 1.6f), new Color(0.85f, 0.75f, 0.6f), Rand(-6f, 6f)));
            while (houses.Count > wanted)
            {
                Destroy(houses[houses.Count - 1]);
                houses.RemoveAt(houses.Count - 1);
            }
        }

        void SyncVillagers(GameState s)
        {
            foreach (var villager in s.Villagers)
            {
                if (!actors.TryGetValue(villager, out var actor))
                {
                    actor = BuildActor(villager);
                    actors[villager] = actor;
                }
                bool working = s.Phase == GamePhase.Planning || s.Phase == GamePhase.Emergency;
                var anchor = AnchorFor(villager, s, working);
                bool atHome = anchor == HomePos(villager);
                actor.SetAnchor(anchor, atHome ? 0.35f : 1.1f, ViaFor(actor.transform.position, anchor));
            }

            foreach (var gone in actors.Keys.Where(v => !s.Villagers.Contains(v)).ToList())
            {
                Destroy(actors[gone].gameObject);
                actors.Remove(gone);
            }
        }

        Vector3 AnchorFor(Villager v, GameState s, bool working)
        {
            var home = HomePos(v);
            if (!working || !v.CanWork) return home;
            if (v.Assignment == null) return PlazaPos;
            switch (v.Assignment.Value)
            {
                case ActivityType.Farming: return FarmPos + new Vector3(0f, 0f, -0.8f);
                case ActivityType.Water:
                case ActivityType.RepairWell: return WellPos + new Vector3(1.8f, 0f, -0.5f);
                case ActivityType.Entertainment: return PlazaPos;
                case ActivityType.Defense: return TowerPos + new Vector3(-1.8f, 0f, -0.5f);
                case ActivityType.Construction: return scaffold.transform.position + new Vector3(0f, 0f, -1.5f);
                default: return PlazaPos;
            }
        }

        /// <summary>Cada habitante duerme delante del hueco que hay a la derecha de su casa.</summary>
        Vector3 HomePos(Villager v) => HousePos(v.Id % Mathf.Max(1, houses.Count)) + new Vector3(HouseSpacing * 0.5f, 0f, -1.6f);

        /// <summary>
        /// Las casas forman una fila entre las camas y el resto de la aldea; para cruzarla sin atravesar paredes,
        /// el habitante pasa primero por el hueco entre casas más cercano a su lado de la fila.
        /// </summary>
        static Vector3? ViaFor(Vector3 from, Vector3 to)
        {
            float crossZ = HousesOrigin.z + 1f;
            bool fromFront = from.z < crossZ, toFront = to.z < crossZ;
            if (fromFront == toFront) return null;
            float sideX = fromFront ? from.x : to.x;
            float gap = Mathf.Round((sideX - HousesOrigin.x) / HouseSpacing - 0.5f) + 0.5f;
            return new Vector3(HousesOrigin.x + gap * HouseSpacing, 0f, crossZ);
        }

        static Vector3 HousePos(int slot) =>
            HousesOrigin + new Vector3(slot % HousesPerRow * HouseSpacing, 0f, -(slot / HousesPerRow) * HouseSpacing);

        // ----------------------------------------------------------------- build

        void BuildDecoration()
        {
            for (int i = 0; i < TreeSpots.Length; i++)
            {
                var model = i % 3 == 1 ? models.TreePine : models.TreeRound;
                Place(model, root, TreeSpots[i], new Vector3(1.2f, 2.5f, 1.2f), Leaf, Rand(0f, 360f), Rand(0.85f, 1.2f));
            }
            foreach (var spot in BushSpots)
                Place(models.Bush, root, spot, new Vector3(0.8f, 0.6f, 0.8f), Leaf, Rand(0f, 360f), Rand(0.8f, 1.2f));
            foreach (var spot in RockSpots)
                Place(models.Rock, root, spot, new Vector3(1f, 0.5f, 1f), Stone, Rand(0f, 360f), Rand(0.8f, 1.3f));
        }

        VillagerActor BuildActor(Villager v)
        {
            var home = HomePos(v);
            var model = models.Villager(v.Class);
            var body = model != null
                ? Place(model, root, home, Vector3.one, Color.white, Rand(0f, 360f))
                : Make(PrimitiveType.Capsule, root, home + new Vector3(0f, 0.45f, 0f), Vector3.one * 0.45f, ClassColor(v.Class));
            body.name = "Villager " + v.Name;
            return body.AddComponent<VillagerActor>();
        }

        /// <summary>Instancia un modelo en la aldea o, si no hay modelo, una caja del tamaño aproximado.</summary>
        GameObject Place(GameObject model, Transform parent, Vector3 localPos, Vector3 fallbackSize, Color fallbackColor, float yaw = 0f, float scale = 1f)
        {
            GameObject go;
            if (model != null)
            {
                go = Instantiate(model, parent, false);
                go.name = model.name;
            }
            else
            {
                go = new GameObject("Placeholder");
                go.transform.SetParent(parent, false);
                Make(PrimitiveType.Cube, go.transform, new Vector3(0f, fallbackSize.y / 2f, 0f), fallbackSize, fallbackColor);
            }
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static Material FindMaterial(GameObject go, string namePrefix)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null && shared[i].name.StartsWith(namePrefix, StringComparison.Ordinal))
                        return renderer.materials[i];
            }
            return null;
        }

        static Color ClassColor(VillagerClass cls)
        {
            switch (cls)
            {
                case VillagerClass.Strong: return new Color(0.85f, 0.3f, 0.25f);
                case VillagerClass.Smart: return new Color(0.3f, 0.5f, 0.9f);
                case VillagerClass.Fast: return new Color(0.9f, 0.8f, 0.25f);
                default: return new Color(0.8f, 0.75f, 0.65f);
            }
        }

        float Rand(float min, float max) => min + (float)decor.NextDouble() * (max - min);

        // --------------------------------------------------------------- helpers

        void SetupCamera()
        {
            cam = Camera.main;
            hud = GetComponent<GameHUD>();
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.rotation = Quaternion.Euler(CameraPitch, 0f, 0f);
        }

        /// <summary>Reencuadra cuando cambia el tamaño de pantalla, el hueco libre de la interfaz o el número de casas.</summary>
        void FrameIfNeeded()
        {
            if (cam == null) return;
            var screen = new Vector2Int(Screen.width, Screen.height);
            var viewport = hud != null ? hud.WorldViewport : new Rect(0f, 0f, 1f, 1f);
            if (screen == framedScreen && viewport == framedViewport && houses.Count == framedHouses) return;
            framedScreen = screen;
            framedViewport = viewport;
            framedHouses = houses.Count;
            FrameVillage(viewport);
        }

        /// <summary>
        /// Desplaza el centro de proyección al centro del área que deja libre la interfaz y aleja la cámara lo justo
        /// para que toda la aldea quepa ahí, de modo que se lea igual en cualquier relación de aspecto.
        /// </summary>
        void FrameVillage(Rect viewport)
        {
            cam.ResetProjectionMatrix();
            cam.ResetAspect();
            cam.fieldOfView = CameraFov;
            var projection = cam.projectionMatrix;
            projection.m02 = 1f - 2f * viewport.center.x;
            projection.m12 = 1f - 2f * viewport.center.y;
            cam.projectionMatrix = projection;

            var points = FramePoints();
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            var target = new Vector3(bounds.center.x, 0f, bounds.center.z);

            var inner = Rect.MinMaxRect(viewport.xMin + FrameMargin, viewport.yMin + FrameMargin,
                viewport.xMax - FrameMargin, viewport.yMax - FrameMargin);
            float near = 4f, far = 120f;
            for (int i = 0; i < 24; i++)
            {
                float mid = (near + far) * 0.5f;
                PlaceCamera(target, mid);
                if (AllInside(points, inner)) far = mid; else near = mid;
            }
            PlaceCamera(target, far);
        }

        void PlaceCamera(Vector3 target, float distance) =>
            cam.transform.position = target - cam.transform.forward * distance;

        bool AllInside(List<Vector3> points, Rect area)
        {
            foreach (var p in points)
            {
                var v = cam.WorldToViewportPoint(p);
                if (v.z <= 0f || !area.Contains(new Vector2(v.x, v.y))) return false;
            }
            return true;
        }

        /// <summary>Puntos que deben quedar en pantalla: edificios con su altura y las casas con las camas delante.</summary>
        List<Vector3> FramePoints()
        {
            var points = new List<Vector3>
            {
                FarmPos + new Vector3(-2.6f, 0f, 2.1f), FarmPos + new Vector3(-2.6f, 0f, -2.1f), FarmPos + new Vector3(2.6f, 0f, 2.1f),
                WellPos + new Vector3(-1.25f, 3.5f, 0.75f), WellPos + new Vector3(1.25f, 3.5f, 0.75f), WellPos + new Vector3(0f, AlertHeight + 0.6f, 0f),
                TowerPos + new Vector3(-1.4f, 5.2f, 1.4f), TowerPos + new Vector3(1.4f, 5.2f, 1.4f), TowerPos + new Vector3(1.4f, 0f, -1.4f),
                PlazaPos + new Vector3(0f, 0f, -2.4f), PlazaPos + new Vector3(1.6f, 2.9f, 1.2f),
            };
            int slots = Mathf.Max(houses.Count + 1, HousesPerRow);
            for (int i = 0; i < slots; i++)
            {
                var h = HousePos(i);
                points.Add(h + new Vector3(-1f, 2.1f, 0.95f));
                points.Add(h + new Vector3(1f, 2.1f, 0.95f));
                points.Add(h + new Vector3(-1f, 0f, -2f));
                points.Add(h + new Vector3(HouseSpacing * 0.5f + 0.4f, 0f, -2f));
            }
            return points;
        }

        void SetupLight()
        {
            sun = RenderSettings.sun != null ? RenderSettings.sun : FindAnyObjectByType<Light>();
            RenderSettings.ambientMode = AmbientMode.Flat;
        }

        GameObject Make(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Material(color);
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        Material Material(Color color)
        {
            if (materials.TryGetValue(color, out var mat)) return mat;
            mat = new Material(baseMaterial) { color = color };
            materials[color] = mat;
            return mat;
        }
    }
}
