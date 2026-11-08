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
    /// Placeholder 3D village built from primitives: buildings per activity, one house per housing slot,
    /// a colored capsule per villager, and a day/night light transition. Swap primitives for imported
    /// models here without touching rules or UI.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public sealed class VillageView : MonoBehaviour
    {
        static readonly Vector3 FarmPos = new Vector3(-6f, 0f, 3f);
        static readonly Vector3 WellPos = new Vector3(0.5f, 0f, 5f);
        static readonly Vector3 PlazaPos = new Vector3(0f, 0f, -0.5f);
        static readonly Vector3 TowerPos = new Vector3(6.5f, 0f, 3f);
        static readonly Vector3 HousesOrigin = new Vector3(-6f, 0f, -5f);
        const int HousesPerRow = 6;
        const float HouseSpacing = 2.4f;

        static readonly Color DaySun = new Color(1f, 0.96f, 0.88f);
        static readonly Color NightSun = new Color(0.45f, 0.55f, 0.95f);
        static readonly Color DayAmbient = new Color(0.55f, 0.58f, 0.62f);
        static readonly Color NightAmbient = new Color(0.08f, 0.1f, 0.18f);
        static readonly Color DaySky = new Color(0.55f, 0.78f, 0.92f);
        static readonly Color NightSky = new Color(0.04f, 0.05f, 0.11f);

        Simulation sim;
        Material baseMaterial;
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        readonly Dictionary<Villager, VillagerActor> actors = new Dictionary<Villager, VillagerActor>();
        readonly List<GameObject> houses = new List<GameObject>();
        Transform root;
        GameObject scaffold;
        GameObject wellAlert;
        Light sun;
        Camera cam;
        GameHUD hud;
        float night;
        Vector2Int framedScreen;
        Rect framedViewport;
        int framedHouses = -1;

        void Start()
        {
            sim = GetComponent<GameController>().Sim;
            root = new GameObject("Village").transform;

            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMaterial = probe.GetComponent<Renderer>().sharedMaterial;
            Destroy(probe);

            BuildGround();
            BuildFarm();
            BuildWell();
            BuildPlaza();
            BuildTower();
            BuildTrees();
            scaffold = BuildScaffold();
            wellAlert = Make(PrimitiveType.Sphere, root, WellPos + new Vector3(0f, 3.6f, 0f), Vector3.one * 0.5f, new Color(1f, 0.2f, 0.15f));

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

            FrameIfNeeded();

            if (wellAlert.activeSelf)
                wellAlert.transform.position = WellPos + new Vector3(0f, 3.6f + Mathf.Sin(Time.time * 4f) * 0.25f, 0f);
        }

        // ------------------------------------------------------------------ sync

        void OnChanged()
        {
            var s = sim.State;
            SyncHouses(s);
            scaffold.SetActive(s.ConstructionProgress > 0);
            scaffold.transform.position = HousePos(houses.Count);
            wellAlert.SetActive(s.WellBroken);
            SyncVillagers(s);
        }

        void SyncHouses(GameState s)
        {
            int wanted = Mathf.Max(0, s.Housing / Mathf.Max(1, sim.Config.HousingPerConstruction));
            while (houses.Count < wanted) houses.Add(BuildHouse(HousePos(houses.Count)));
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
                actor.SetAnchor(AnchorFor(villager, s, working), working ? 1.1f : 0.5f);
            }

            foreach (var gone in actors.Keys.Where(v => !s.Villagers.Contains(v)).ToList())
            {
                Destroy(actors[gone].gameObject);
                actors.Remove(gone);
            }
        }

        Vector3 AnchorFor(Villager v, GameState s, bool working)
        {
            var home = HousePos(v.Id % Mathf.Max(1, houses.Count)) + new Vector3(0f, 0f, -1.3f);
            if (!working || !v.CanWork) return home;
            if (v.Assignment == null) return PlazaPos;
            switch (v.Assignment.Value)
            {
                case ActivityType.Farming: return FarmPos;
                case ActivityType.Water:
                case ActivityType.RepairWell: return WellPos + new Vector3(1.8f, 0f, -0.5f);
                case ActivityType.Entertainment: return PlazaPos;
                case ActivityType.Defense: return TowerPos + new Vector3(-1.8f, 0f, -0.5f);
                case ActivityType.Construction: return scaffold.transform.position + new Vector3(0f, 0f, -1.5f);
                default: return PlazaPos;
            }
        }

        static Vector3 HousePos(int slot) =>
            HousesOrigin + new Vector3(slot % HousesPerRow * HouseSpacing, 0f, -(slot / HousesPerRow) * HouseSpacing);

        // ----------------------------------------------------------------- build

        void BuildGround()
        {
            Make(PrimitiveType.Plane, root, Vector3.zero, new Vector3(8f, 1f, 8f), new Color(0.42f, 0.6f, 0.3f));
        }

        void BuildFarm()
        {
            var farm = Group("Farm", FarmPos);
            Make(PrimitiveType.Cube, farm, new Vector3(0f, 0.1f, 0f), new Vector3(5f, 0.2f, 4f), new Color(0.45f, 0.3f, 0.18f));
            var crop = new Color(0.3f, 0.65f, 0.25f);
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 3; z++)
                    Make(PrimitiveType.Cube, farm, new Vector3(-1.8f + x * 0.9f, 0.45f, -1.2f + z * 1.2f), new Vector3(0.4f, 0.5f, 0.4f), crop);
        }

        void BuildWell()
        {
            var well = Group("Well", WellPos);
            var stone = new Color(0.55f, 0.55f, 0.58f);
            var wood = new Color(0.4f, 0.26f, 0.15f);
            Make(PrimitiveType.Cylinder, well, new Vector3(0f, 0.5f, 0f), new Vector3(1.8f, 0.5f, 1.8f), stone);
            Make(PrimitiveType.Cylinder, well, new Vector3(0f, 1.0f, 0f), new Vector3(1.4f, 0.03f, 1.4f), new Color(0.25f, 0.5f, 0.9f));
            Make(PrimitiveType.Cube, well, new Vector3(-0.8f, 1.8f, 0f), new Vector3(0.15f, 1.6f, 0.15f), wood);
            Make(PrimitiveType.Cube, well, new Vector3(0.8f, 1.8f, 0f), new Vector3(0.15f, 1.6f, 0.15f), wood);
            Make(PrimitiveType.Cube, well, new Vector3(0f, 2.7f, 0f), new Vector3(2.2f, 0.15f, 1.3f), new Color(0.6f, 0.25f, 0.2f));
        }

        void BuildPlaza()
        {
            var plaza = Group("Plaza", PlazaPos);
            Make(PrimitiveType.Cylinder, plaza, new Vector3(0f, 0.03f, 0f), new Vector3(4.8f, 0.03f, 4.8f), new Color(0.85f, 0.75f, 0.5f));
            Make(PrimitiveType.Cube, plaza, new Vector3(1.6f, 1.2f, 1.2f), new Vector3(0.12f, 2.4f, 0.12f), new Color(0.4f, 0.26f, 0.15f));
            Make(PrimitiveType.Cube, plaza, new Vector3(1.95f, 2.2f, 1.2f), new Vector3(0.7f, 0.4f, 0.05f), new Color(0.9f, 0.25f, 0.2f));
        }

        void BuildTower()
        {
            var tower = Group("Tower", TowerPos);
            Make(PrimitiveType.Cube, tower, new Vector3(0f, 1.6f, 0f), new Vector3(1.3f, 3.2f, 1.3f), new Color(0.5f, 0.48f, 0.45f));
            Make(PrimitiveType.Cube, tower, new Vector3(0f, 3.3f, 0f), new Vector3(2f, 0.25f, 2f), new Color(0.4f, 0.26f, 0.15f));
            Make(PrimitiveType.Cube, tower, new Vector3(0f, 3.95f, 0f), new Vector3(1.6f, 0.9f, 1.6f), new Color(0.35f, 0.22f, 0.14f));
        }

        void BuildTrees()
        {
            var trunk = new Color(0.4f, 0.26f, 0.15f);
            var leaves = new Color(0.2f, 0.45f, 0.2f);
            Vector3[] spots =
            {
                new Vector3(-11f, 0f, 7f), new Vector3(-9f, 0f, 9f), new Vector3(-4f, 0f, 9.5f), new Vector3(5f, 0f, 9f),
                new Vector3(10f, 0f, 8f), new Vector3(11.5f, 0f, 1f), new Vector3(10.5f, 0f, -6f), new Vector3(-11f, 0f, -3f),
                new Vector3(-10f, 0f, -9f), new Vector3(3f, 0f, -10.5f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var tree = Group("Tree", spots[i]);
                float size = 1.2f + (i % 3) * 0.3f;
                Make(PrimitiveType.Cylinder, tree, new Vector3(0f, 0.5f, 0f), new Vector3(0.25f, 0.5f, 0.25f), trunk);
                Make(PrimitiveType.Sphere, tree, new Vector3(0f, 1.2f + size * 0.4f, 0f), Vector3.one * size, leaves);
            }
        }

        GameObject BuildHouse(Vector3 pos)
        {
            var house = Group("House", pos);
            Make(PrimitiveType.Cube, house, new Vector3(0f, 0.65f, 0f), new Vector3(1.6f, 1.3f, 1.6f), new Color(0.85f, 0.75f, 0.6f));
            Make(PrimitiveType.Cube, house, new Vector3(0f, 1.3f, 0f), new Vector3(1.25f, 1.25f, 1.8f), new Color(0.6f, 0.25f, 0.2f), new Vector3(0f, 0f, 45f));
            Make(PrimitiveType.Cube, house, new Vector3(0f, 0.35f, -0.81f), new Vector3(0.4f, 0.7f, 0.06f), new Color(0.3f, 0.18f, 0.1f));
            return house.gameObject;
        }

        GameObject BuildScaffold()
        {
            var site = Group("Scaffold", HousePos(0));
            var wood = new Color(0.75f, 0.6f, 0.4f);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Make(PrimitiveType.Cube, site, new Vector3(x * 0.75f, 0.8f, z * 0.75f), new Vector3(0.1f, 1.6f, 0.1f), wood);
            Make(PrimitiveType.Cube, site, new Vector3(0f, 1.6f, 0.75f), new Vector3(1.6f, 0.1f, 0.1f), wood);
            Make(PrimitiveType.Cube, site, new Vector3(0f, 1.6f, -0.75f), new Vector3(1.6f, 0.1f, 0.1f), wood);
            Make(PrimitiveType.Cube, site, new Vector3(0f, 0.15f, 0f), new Vector3(1.6f, 0.3f, 1.6f), new Color(0.85f, 0.75f, 0.6f));
            return site.gameObject;
        }

        VillagerActor BuildActor(Villager v)
        {
            var home = HousePos(v.Id % Mathf.Max(1, houses.Count));
            var body = Make(PrimitiveType.Capsule, root, home + new Vector3(0f, 0.45f, -1.3f), Vector3.one * 0.45f, ClassColor(v.Class));
            body.name = "Villager " + v.Name;
            return body.AddComponent<VillagerActor>();
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

        // --------------------------------------------------------------- helpers

        const float CameraPitch = 50f;
        const float CameraFov = 38f;
        const float FrameMargin = 0.03f;

        void SetupCamera()
        {
            cam = Camera.main;
            hud = GetComponent<GameHUD>();
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.rotation = Quaternion.Euler(CameraPitch, 0f, 0f);
        }

        /// <summary>Re-fits the camera when the screen size, the free HUD area or the number of houses changes.</summary>
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
        /// Shifts the projection center to the middle of the area the HUD leaves free and pulls the camera
        /// back just enough for the whole village to fit there, so it reads the same at any aspect ratio.
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

        /// <summary>Points that must stay on screen: buildings, roofs, and the houses with the villagers sleeping in front.</summary>
        List<Vector3> FramePoints()
        {
            var points = new List<Vector3>
            {
                FarmPos + new Vector3(-2.6f, 0f, 2.1f), FarmPos + new Vector3(-2.6f, 0f, -2.1f), FarmPos + new Vector3(2.6f, 0f, 2.1f),
                WellPos + new Vector3(-1.1f, 2.8f, 0.7f), WellPos + new Vector3(1.1f, 2.8f, 0.7f), WellPos + new Vector3(0f, 4f, 0f),
                TowerPos + new Vector3(-1f, 4.4f, 1f), TowerPos + new Vector3(1f, 4.4f, 1f), TowerPos + new Vector3(1f, 0f, -1f),
                PlazaPos + new Vector3(0f, 0f, -2.4f),
            };
            int slots = Mathf.Max(houses.Count + 1, HousesPerRow);
            for (int i = 0; i < slots; i++)
            {
                var h = HousePos(i);
                points.Add(h + new Vector3(-0.9f, 2.2f, 0.9f));
                points.Add(h + new Vector3(0.9f, 2.2f, 0.9f));
                points.Add(h + new Vector3(-0.9f, 0f, -1.9f));
                points.Add(h + new Vector3(0.9f, 0f, -1.9f));
            }
            return points;
        }

        void SetupLight()
        {
            sun = RenderSettings.sun != null ? RenderSettings.sun : FindAnyObjectByType<Light>();
            RenderSettings.ambientMode = AmbientMode.Flat;
        }

        Transform Group(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            return go.transform;
        }

        GameObject Make(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
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
