using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyCiv.Core
{
    /// <summary>
    /// The single authority over game rules. The UI only calls the commands below and reads <see cref="State"/>.
    /// Pure C#: no engine dependency, so it can be driven headless for balance tests.
    /// </summary>
    public sealed class Simulation
    {
        static readonly string[] Names =
        {
            "Ana", "Bruno", "Cora", "Darío", "Elia", "Fito", "Gala", "Hugo", "Iria", "Jon",
            "Kira", "Leo", "Mara", "Nilo", "Olga", "Pau", "Quim", "Rosa", "Saúl", "Tula",
            "Uxía", "Vera", "Xoel", "Yago", "Zoe",
        };

        readonly GameConfig cfg;
        readonly Random rng;
        int nextId;
        int nameIndex;

        public GameConfig Config => cfg;
        public GameState State { get; private set; }

        /// <summary>Raised after any change the presentation layer should reflect.</summary>
        public event Action Changed;

        public Simulation(GameConfig config, int? seed = null)
        {
            cfg = config;
            rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        // ------------------------------------------------------------------ setup

        public void NewGame()
        {
            State = new GameState
            {
                Food = cfg.StartFood,
                Water = cfg.StartWater,
                Happiness = cfg.StartHappiness,
                Housing = cfg.StartHousing,
            };
            nextId = 0;
            nameIndex = 0;
            foreach (var cls in cfg.StartingVillagers) AddVillager(cls);
            Log("Tu pequeña civilización empieza con " + State.Population + " habitantes.");
            BeginDay();
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- queries

        public int FoodConsumption => State.Population * cfg.FoodPerVillager;
        public int WaterConsumption => State.Population * cfg.WaterPerVillager;
        public int AvailableWorkers => State.Villagers.Count(v => v.CanWork);

        /// <summary>Villagers who can work today and have no assignment yet.</summary>
        public IEnumerable<Villager> Pool => State.Villagers.Where(v => v.CanWork && v.Assignment == null);

        /// <summary>Activities the player can assign to right now. Repair only appears while the well is broken.</summary>
        public IEnumerable<ActivityDef> ActiveActivities
        {
            get
            {
                if (State.WellBroken) yield return ActivityCatalog.Get(ActivityType.RepairWell);
                foreach (var def in ActivityCatalog.All)
                    if (def.Type != ActivityType.RepairWell) yield return def;
            }
        }

        public IEnumerable<Villager> Assigned(ActivityType t) => State.Villagers.Where(v => v.Assignment == t);

        public int Points(ActivityType t)
        {
            var def = ActivityCatalog.Get(t);
            return Assigned(t).Sum(def.PointsFor);
        }

        /// <summary>Specialists of the task, or any Smart villager, lower the accident chance.</summary>
        public bool HasRiskSpecialist(ActivityType t)
        {
            var def = ActivityCatalog.Get(t);
            return Assigned(t).Any(v => def.IsSpecialist(v.Class) || v.Class == VillagerClass.Smart);
        }

        public float Risk(ActivityType t) => RiskFor(ActivityCatalog.Get(t), HasRiskSpecialist(t), Points(t));

        /// <summary>What the activity would yield tonight with the current assignment (0 for non-producing tasks).</summary>
        public int ExpectedOutput(ActivityType t)
        {
            var def = ActivityCatalog.Get(t);
            if (def.BaseOutput == 0) return 0;
            if (t == ActivityType.Water && State.WellBroken && Points(ActivityType.RepairWell) < ActivityCatalog.Get(ActivityType.RepairWell).MinPoints) return 0;
            int points = Points(t);
            if (points == 0) return 0;
            int output = OutputFor(def, points);
            if (State.Luck == 10 && points >= def.MinPoints && (t == ActivityType.Farming || t == ActivityType.Water))
                output += cfg.LuckyBonusProduction;
            return output;
        }

        public int Score => State.Population * 10 + State.Food + State.Water + State.Happiness / 2 + State.Housing;

        public static string LuckDescription(int luck)
        {
            if (luck <= 1) return "Día desastroso";
            if (luck <= 3) return "Día difícil";
            if (luck <= 6) return "Día normal";
            if (luck <= 8) return "Buen día";
            if (luck == 9) return "Día excepcional";
            return "Día extraordinario";
        }

        public string Summary()
        {
            var s = State;
            if (s.Result == GameResult.Victory)
                return "Tu civilización ha sobrevivido " + cfg.TotalDays + " días.\n" +
                       "Habitantes: " + s.Population + "   Comida: " + s.Food + "   Agua: " + s.Water + "\n" +
                       "Felicidad: " + s.Happiness + "   Vivienda: " + s.Housing + "\n\n" +
                       "Puntuación: " + Score;
            if (s.Result == GameResult.Defeat)
            {
                string last = s.Population == 1 ? "Solo queda " + s.Villagers[0].Name + "." : "No queda nadie.";
                return "Tu civilización se ha extinguido en el día " + s.Day + ".\n" + last;
            }
            return string.Empty;
        }

        // --------------------------------------------------------------- commands

        public bool Assign(Villager v, ActivityType t)
        {
            if (State.Phase != GamePhase.Planning || !v.CanWork) return false;
            if (!ActiveActivities.Any(a => a.Type == t)) return false;
            v.Assignment = t;
            Changed?.Invoke();
            return true;
        }

        public bool Unassign(Villager v)
        {
            if (State.Phase != GamePhase.Planning || v.Assignment == null) return false;
            v.Assignment = null;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Runs every activity. Accidents queue emergencies; when none remain, the night resolves.</summary>
        public void EndDay()
        {
            if (State.Phase != GamePhase.Planning) return;
            ExecuteActivities();
            if (State.Emergencies.Count > 0) State.Phase = GamePhase.Emergency;
            else ResolveNight();
            Changed?.Invoke();
        }

        public void ChooseOption(int index)
        {
            if (State.Phase != GamePhase.Emergency) return;
            var emergency = State.CurrentEmergency;
            if (emergency == null || index < 0 || index >= emergency.Options.Count) return;
            var option = emergency.Options[index];
            if (!option.CanAfford(State)) return;

            State.Food -= option.FoodCost;
            State.Water -= option.WaterCost;
            State.Emergencies.Dequeue();
            Log(emergency.Title + ": " + option.Label + (option.FoodCost + option.WaterCost > 0 ? " (" + option.CostText + ")" : "") + ".");
            option.Apply?.Invoke();

            if (State.Emergencies.Count == 0) ResolveNight();
            Changed?.Invoke();
        }

        public void NextDay()
        {
            if (State.Phase != GamePhase.NightReport) return;
            BeginDay();
            Changed?.Invoke();
        }

        // -------------------------------------------------------------------- day

        void BeginDay()
        {
            var s = State;
            s.Day++;
            s.Luck = RollLuck();
            s.DefendedTonight = false;
            s.DefensePoints = 0;

            foreach (var v in s.Villagers)
            {
                v.Assignment = null;
                v.AvailableToday = false;
            }

            var healthy = s.Villagers.Where(v => v.InjuredDays == 0).ToList();
            int count = (int)Math.Ceiling(healthy.Count * s.Luck / 10.0);
            bool unhappy = s.Happiness < cfg.UnhappyThreshold;
            if (unhappy) count--;
            count = Math.Min(healthy.Count, Math.Max(1, count));
            Shuffle(healthy);
            for (int i = 0; i < count; i++) healthy[i].AvailableToday = true;

            s.Phase = GamePhase.Planning;
            Log("--- Día " + s.Day + " ---");
            Log("Número de la suerte: " + s.Luck + " (" + LuckDescription(s.Luck) + "). " +
                count + " de " + s.Population + " habitantes pueden trabajar hoy.");
            if (s.Luck == 10) Log("¡Todos trabajan y la producción recibe un extra!");
            if (unhappy) Log("La gente está desanimada: uno menos acude a trabajar.");
            foreach (var v in s.Villagers.Where(v => v.InjuredDays > 0))
                Log(v.Name + " sigue herido y descansa.");
            if (s.WellBroken) Log("El pozo sigue roto: no habrá agua hasta repararlo.");
        }

        int RollLuck()
        {
            int total = cfg.LuckWeights.Sum();
            int roll = rng.Next(total);
            for (int i = 0; i < cfg.LuckWeights.Length; i++)
            {
                roll -= cfg.LuckWeights[i];
                if (roll < 0) return i + 1;
            }
            return cfg.LuckWeights.Length;
        }

        void ExecuteActivities()
        {
            var s = State;
            bool lucky = s.Luck == 10;

            foreach (var type in ActivityCatalog.ExecutionOrder)
            {
                var def = ActivityCatalog.Get(type);
                var workers = Assigned(type).ToList();
                if (workers.Count == 0) continue;

                int points = workers.Sum(def.PointsFor);
                bool complete = points >= def.MinPoints;
                bool specialist = workers.Any(v => def.IsSpecialist(v.Class) || v.Class == VillagerClass.Smart);
                bool accident = def.AccidentChance > 0 && rng.NextDouble() < RiskFor(def, specialist, points);
                string progress = def.Name + " (" + points + "/" + def.MinPoints + " puntos" + (complete ? ")" : ", parcial)");

                switch (type)
                {
                    case ActivityType.RepairWell:
                        if (complete)
                        {
                            s.WellBroken = false;
                            Log(progress + ": ¡pozo reparado!");
                        }
                        else Log(progress + ": no basta para reparar el pozo.");
                        break;

                    case ActivityType.Farming:
                    {
                        int food = OutputFor(def, points) + (lucky && complete ? cfg.LuckyBonusProduction : 0);
                        if (!accident)
                        {
                            s.Food += food;
                            Log(progress + ": +" + food + " comida.");
                            break;
                        }
                        Log(progress + ": ¡una plaga ataca los cultivos!");
                        var plague = new Emergency
                        {
                            Source = type,
                            Title = "Plaga en los cultivos",
                            Description = "Una plaga devora la cosecha de hoy (" + food + " de comida). ¿Qué hacemos?",
                        };
                        plague.Options.Add(Option("Fumigar con agua", "Se salva toda la cosecha", 0, 3, () =>
                        {
                            s.Food += food;
                            Log("Plaga contenida: +" + food + " comida.");
                        }));
                        plague.Options.Add(Option("Ignorarla", "Se pierde la mitad de la cosecha", 0, 0, () =>
                        {
                            s.Food += food / 2;
                            Log("La plaga arrasa media cosecha: +" + food / 2 + " comida.");
                        }));
                        Raise(plague);
                        break;
                    }

                    case ActivityType.Water:
                    {
                        if (s.WellBroken)
                        {
                            Log(progress + ": el pozo está roto, no se recoge agua.");
                            break;
                        }
                        int water = OutputFor(def, points) + (lucky && complete ? cfg.LuckyBonusProduction : 0);
                        if (!accident)
                        {
                            s.Water += water;
                            Log(progress + ": +" + water + " agua.");
                            break;
                        }
                        int salvaged = water / 2;
                        s.Water += salvaged;
                        s.WellBroken = true;
                        Log(progress + ": ¡el pozo se ha roto! Solo se recogen " + salvaged + " de agua.");
                        var well = new Emergency
                        {
                            Source = type,
                            Title = "Pozo roto",
                            Description = "El pozo se ha derrumbado y sin él no habrá agua. Un artesano puede repararlo hoy a cambio de comida, " +
                                          "o tus trabajadores lo reparan mañana (tarea Reparar pozo, 2 puntos).",
                        };
                        well.Options.Add(Option("Pagar al artesano", "Reparado hoy mismo", 4, 0, () =>
                        {
                            s.WellBroken = false;
                            Log("El artesano repara el pozo.");
                        }));
                        well.Options.Add(Option("Repararlo mañana", "Aparece la tarea Reparar pozo", 0, 0, () =>
                            Log("El pozo seguirá roto hasta que lo reparen tus trabajadores.")));
                        Raise(well);
                        break;
                    }

                    case ActivityType.Entertainment:
                    {
                        int joy = OutputFor(def, points);
                        if (!accident)
                        {
                            AddHappiness(joy);
                            Log(progress + ": +" + joy + " felicidad.");
                            break;
                        }
                        Log(progress + ": ¡una discusión acaba en pelea!");
                        var fight = new Emergency
                        {
                            Source = type,
                            Title = "Pelea en la fiesta",
                            Description = "Una discusión ha acabado en pelea y ha estropeado el ambiente.",
                        };
                        fight.Options.Add(Option("Mediar con un banquete", "Todos se reconcilian y la fiesta continúa", 2, 0, () =>
                        {
                            AddHappiness(joy);
                            Log("El banquete reconcilia a todos: +" + joy + " felicidad.");
                        }));
                        fight.Options.Add(Option("Dejarlo estar", "Felicidad -15", 0, 0, () =>
                        {
                            AddHappiness(-15);
                            Log("La pelea deja mal ambiente: -15 felicidad.");
                        }));
                        Raise(fight);
                        break;
                    }

                    case ActivityType.Defense:
                    {
                        s.DefensePoints = points;
                        s.DefendedTonight = complete;
                        Log(progress + (complete ? ": la aldea estará protegida esta noche." : ": protección parcial esta noche."));
                        if (!accident) break;
                        var hurt = workers[rng.Next(workers.Count)];
                        Log(hurt.Name + " se ha hecho daño en la patrulla.");
                        var injury = new Emergency
                        {
                            Source = type,
                            Title = "Herido en la patrulla",
                            Description = hurt.Name + " se ha lesionado vigilando la aldea.",
                        };
                        injury.Options.Add(Option("Cuidarlo", hurt.Name + " vuelve al trabajo mañana", 2, 2, () =>
                        {
                            hurt.InjuredDays = 1;
                            Log(hurt.Name + " recibe cuidados y se recupera esta noche.");
                        }));
                        injury.Options.Add(Option("Que descanse", hurt.Name + " estará 2 días sin trabajar", 0, 0, () =>
                        {
                            hurt.InjuredDays = 3;
                            Log(hurt.Name + " descansará dos días.");
                        }));
                        Raise(injury);
                        break;
                    }

                    case ActivityType.Construction:
                    {
                        if (!accident)
                        {
                            AddConstruction(points, progress);
                            break;
                        }
                        Log(progress + ": ¡parte de la estructura se ha derrumbado!");
                        var collapse = new Emergency
                        {
                            Source = type,
                            Title = "Derrumbe en la obra",
                            Description = "Parte de la estructura se ha venido abajo. Reforzarla cuesta materiales y comida para los obreros.",
                        };
                        collapse.Options.Add(Option("Reforzar la obra", "Se conserva el trabajo de hoy", 3, 0, () =>
                            AddConstruction(points, "Obra reforzada")));
                        collapse.Options.Add(Option("Abandonar", "Se pierde el trabajo de hoy", 0, 0, () =>
                            Log("La obra de hoy se pierde.")));
                        Raise(collapse);
                        break;
                    }
                }
            }
        }

        void AddConstruction(int points, string prefix)
        {
            var s = State;
            var def = ActivityCatalog.Get(ActivityType.Construction);
            s.ConstructionProgress += points;
            while (s.ConstructionProgress >= def.MinPoints)
            {
                s.ConstructionProgress -= def.MinPoints;
                s.Housing += cfg.HousingPerConstruction;
                Log(prefix + ": ¡nueva vivienda! Capacidad: " + s.Housing + ".");
                prefix = "Construcción";
            }
            if (s.ConstructionProgress > 0)
                Log(prefix + ": progreso " + s.ConstructionProgress + "/" + def.MinPoints + ".");
        }

        int OutputFor(ActivityDef def, int points)
        {
            if (points >= def.MinPoints) return def.BaseOutput + (points - def.MinPoints) * def.OutputPerExtraPoint;
            return (int)Math.Floor(def.BaseOutput * points / (double)def.MinPoints * cfg.PartialEfficiency);
        }

        float RiskFor(ActivityDef def, bool specialist, int points)
        {
            if (def.AccidentChance <= 0 || points == 0) return 0f;
            float chance = def.AccidentChance;
            if (specialist) chance *= cfg.SpecialistRiskMultiplier;
            chance -= Math.Max(0, points - def.MinPoints) * cfg.RiskReductionPerExtraPoint;
            return Math.Max(cfg.MinRisk, chance);
        }

        // ------------------------------------------------------------------ night

        void ResolveNight()
        {
            var s = State;
            Log("--- Noche " + s.Day + " ---");

            bool foodShort = Consume(ref s.Food, FoodConsumption, ref s.Hunger, "comida", "Hambre");
            bool waterShort = Consume(ref s.Water, WaterConsumption, ref s.Thirst, "agua", "Sed");

            ResolveRaid();

            int deaths = 0;
            if (s.Hunger >= cfg.StarvationNights && s.Population > 0)
            {
                Kill("de hambre");
                s.Hunger = cfg.StarvationNights - 1;
                deaths++;
            }
            if (s.Thirst >= cfg.StarvationNights && s.Population > 0)
            {
                Kill("de sed");
                s.Thirst = cfg.StarvationNights - 1;
                deaths++;
            }

            int mood = -cfg.HappinessDecay;
            if (foodShort) mood -= cfg.HappinessPerShortage;
            if (waterShort) mood -= cfg.HappinessPerShortage;
            mood -= deaths * cfg.HappinessPerDeath;
            bool crowded = s.Population > s.Housing;
            if (crowded) mood -= cfg.HappinessOvercrowding;
            AddHappiness(mood);
            if (crowded) Log("Falta vivienda: la gente duerme al raso.");

            if (!foodShort && !waterShort && deaths == 0 && s.Happiness >= cfg.BirthMinHappiness && s.Population < s.Housing)
            {
                double chance = Math.Min(cfg.BirthMaxChance, (s.Happiness - cfg.BirthChanceBase) / 100.0);
                if (rng.NextDouble() < chance)
                {
                    var baby = AddVillager(RandomClass());
                    AddHappiness(cfg.HappinessPerBirth);
                    Log("¡Ha nacido " + baby.Name + " (" + baby.Class.DisplayName() + ")!");
                }
            }

            foreach (var v in s.Villagers)
                if (v.InjuredDays > 0) v.InjuredDays--;

            Log("Felicidad: " + s.Happiness + ".  Comida: " + s.Food + ".  Agua: " + s.Water + ".");

            if (s.Population <= 1)
            {
                s.Result = GameResult.Defeat;
                s.Phase = GamePhase.GameOver;
                Log("Tu civilización se ha extinguido.");
            }
            else if (s.Day >= cfg.TotalDays)
            {
                s.Result = GameResult.Victory;
                s.Phase = GamePhase.GameOver;
                Log("¡Has sobrevivido " + cfg.TotalDays + " días!");
            }
            else s.Phase = GamePhase.NightReport;
        }

        bool Consume(ref int stock, int need, ref int streak, string resource, string needName)
        {
            if (stock >= need)
            {
                stock -= need;
                streak = Math.Max(0, streak - 1);
                Log("Se consumen " + need + " de " + resource + ".");
                return false;
            }
            int missing = need - stock;
            stock = 0;
            streak++;
            Log("¡Faltan " + missing + " de " + resource + "! " + needName + ": " + streak + "/" + cfg.StarvationNights + ".");
            return true;
        }

        void ResolveRaid()
        {
            var s = State;
            if (rng.NextDouble() >= cfg.RaidChance) return;

            int minPoints = ActivityCatalog.Get(ActivityType.Defense).MinPoints;
            if (s.DefendedTonight)
            {
                Log("Unos bandidos rondan la aldea, pero la guardia los ahuyenta.");
                return;
            }
            if (s.DefensePoints > 0 && rng.NextDouble() < s.DefensePoints / (double)minPoints)
            {
                Log("Unos bandidos intentan robar, pero la vigilancia parcial los asusta.");
                return;
            }

            int food = s.Food > 0 ? Math.Max(1, (int)Math.Floor(s.Food * cfg.RaidTheftFraction)) : 0;
            int water = s.Water > 0 ? Math.Max(1, (int)Math.Floor(s.Water * cfg.RaidTheftFraction)) : 0;
            s.Food -= food;
            s.Water -= water;
            Log("¡Bandidos! " + (s.DefensePoints > 0 ? "La vigilancia no basta y roban " : "Sin defensa roban ")
                + food + " de comida y " + water + " de agua.");
        }

        void Kill(string cause)
        {
            var s = State;
            var victim = s.Villagers[rng.Next(s.Villagers.Count)];
            s.Villagers.Remove(victim);
            Log(victim.Name + " (" + victim.Class.DisplayName() + ") ha muerto " + cause + ".");
        }

        // ---------------------------------------------------------------- helpers

        Villager AddVillager(VillagerClass cls)
        {
            string name = Names[nameIndex % Names.Length];
            int round = nameIndex / Names.Length;
            if (round > 0) name += " " + new string('I', round);
            nameIndex++;
            var v = new Villager(nextId++, name, cls);
            State.Villagers.Add(v);
            return v;
        }

        VillagerClass RandomClass()
        {
            int roll = rng.Next(100);
            if (roll < 40) return VillagerClass.Normal;
            if (roll < 60) return VillagerClass.Strong;
            if (roll < 80) return VillagerClass.Smart;
            return VillagerClass.Fast;
        }

        void AddHappiness(int delta) => State.Happiness = Math.Max(0, Math.Min(100, State.Happiness + delta));

        void Raise(Emergency e) => State.Emergencies.Enqueue(e);

        void Log(string line) => State.Log.Add(line);

        static EmergencyOption Option(string label, string detail, int food, int water, Action apply) =>
            new EmergencyOption { Label = label, Detail = detail, FoodCost = food, WaterCost = water, Apply = apply };

        void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
