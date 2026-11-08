using System;
using System.Collections.Generic;
using System.Linq;
using LuckyCiv.Core;

// Headless balance harness: drives the pure simulation with two policies and checks invariants.
static class Program
{
    sealed class Stats
    {
        public int Games, Wins, Deaths, Births, Emergencies, Raids;
        public int[] LossDay = new int[9];
        public List<int> FinalPop = new List<int>();
        public List<int> Score = new List<int>();
    }

    enum Policy { Smart, Basic, Random }

    static int Main()
    {
        const int games = 3000;
        var cfg = new GameConfig();

        Console.WriteLine("Determinism check: " + (SameLog(cfg, 123) ? "OK" : "FAILED"));

        Print("Heuristic policy (plans stock, defense, housing)", Run(cfg, games, Policy.Smart));
        Print("Basic policy (food + water only, pays emergencies)", Run(cfg, games, Policy.Basic));
        Print("Random policy", Run(cfg, games, Policy.Random));
        return 0;
    }

    static bool SameLog(GameConfig cfg, int seed)
    {
        var a = new Simulation(cfg, seed);
        var b = new Simulation(cfg, seed);
        Play(a, Policy.Smart, new Random(1), new Stats());
        Play(b, Policy.Smart, new Random(1), new Stats());
        return a.State.Log.SequenceEqual(b.State.Log);
    }

    static Stats Run(GameConfig cfg, int games, Policy policy)
    {
        var stats = new Stats();
        for (int seed = 1; seed <= games; seed++)
        {
            var sim = new Simulation(cfg, seed);
            Play(sim, policy, new Random(seed), stats);
        }
        return stats;
    }

    static void Play(Simulation sim, Policy policy, Random r, Stats stats)
    {
        sim.NewGame();
        int guard = 0;
        int popBefore = sim.State.Population;
        while (sim.State.Phase != GamePhase.GameOver)
        {
            if (++guard > 500) throw new Exception("Game did not finish (seed loop)");
            var s = sim.State;
            switch (s.Phase)
            {
                case GamePhase.Planning:
                    if (policy == Policy.Smart) PlanSmart(sim);
                    else if (policy == Policy.Basic) PlanBasic(sim);
                    else PlanRandom(sim, r);
                    CheckInvariants(sim);
                    sim.EndDay();
                    break;
                case GamePhase.Emergency:
                    stats.Emergencies++;
                    var e = s.CurrentEmergency;
                    int idx = policy == Policy.Smart ? ChooseSmart(sim, e)
                        : policy == Policy.Basic ? (e.Options[0].CanAfford(s) ? 0 : e.Options.Count - 1)
                        : r.Next(e.Options.Count);
                    if (!e.Options[idx].CanAfford(s)) idx = e.Options.Count - 1;
                    sim.ChooseOption(idx);
                    break;
                case GamePhase.NightReport:
                    sim.NextDay();
                    break;
            }
            CheckInvariants(sim);
        }

        var st = sim.State;
        stats.Games++;
        stats.Raids += st.Log.Count(l => l.StartsWith("¡Bandidos!"));
        stats.Deaths += st.Log.Count(l => l.Contains("ha muerto"));
        stats.Births += st.Log.Count(l => l.StartsWith("¡Ha nacido"));
        if (st.Result == GameResult.Victory)
        {
            if (st.Day != sim.Config.TotalDays) throw new Exception("Victory on wrong day " + st.Day);
            stats.Wins++;
            stats.FinalPop.Add(st.Population);
            stats.Score.Add(sim.Score);
        }
        else
        {
            if (st.Population > 1) throw new Exception("Defeat with population " + st.Population);
            stats.LossDay[st.Day]++;
        }
    }

    static void CheckInvariants(Simulation sim)
    {
        var s = sim.State;
        if (s.Food < 0 || s.Water < 0) throw new Exception("Negative resources");
        if (s.Happiness < 0 || s.Happiness > 100) throw new Exception("Happiness out of range");
        if (s.Phase == GamePhase.Planning)
            foreach (var v in s.Villagers)
                if (v.Assignment != null && !v.CanWork) throw new Exception("Unavailable villager assigned");
        if (s.Phase == GamePhase.GameOver && s.Result == GameResult.None) throw new Exception("GameOver without result");
    }

    static void PlanSmart(Simulation sim)
    {
        var s = sim.State;
        var pool = sim.Pool.ToList();
        var wants = new List<ActivityType>();
        if (s.WellBroken) wants.Add(ActivityType.RepairWell);

        int foodNeed = sim.FoodConsumption * 2 - s.Food;
        int waterNeed = sim.WaterConsumption * 2 - s.Water;
        if (waterNeed > foodNeed)
        {
            if (waterNeed > 0) wants.Add(ActivityType.Water);
            if (foodNeed > 0) wants.Add(ActivityType.Farming);
        }
        else
        {
            if (foodNeed > 0) wants.Add(ActivityType.Farming);
            if (waterNeed > 0) wants.Add(ActivityType.Water);
        }
        if (s.Happiness < 50) wants.Add(ActivityType.Entertainment);
        if (s.Population >= s.Housing - 1) wants.Add(ActivityType.Construction);
        if (s.Food + s.Water >= 3 * s.Population) wants.Add(ActivityType.Defense);

        foreach (var t in wants)
        {
            var def = ActivityCatalog.Get(t);
            while (sim.Points(t) < def.MinPoints && pool.Count > 0)
            {
                var v = pool.FirstOrDefault(x => def.IsSpecialist(x.Class))
                        ?? pool.FirstOrDefault(x => x.Class == VillagerClass.Normal)
                        ?? pool[0];
                if (!sim.Assign(v, t)) throw new Exception("Assign refused");
                pool.Remove(v);
            }
        }

        foreach (var v in pool.ToList())
        {
            var t = s.Food <= s.Water || s.WellBroken ? ActivityType.Farming : ActivityType.Water;
            sim.Assign(v, t);
        }
    }

    static void PlanBasic(Simulation sim)
    {
        var s = sim.State;
        var pool = sim.Pool.ToList();
        var wants = new List<ActivityType>();
        if (s.WellBroken) wants.Add(ActivityType.RepairWell);
        wants.Add(ActivityType.Farming);
        wants.Add(ActivityType.Water);
        foreach (var t in wants)
        {
            var def = ActivityCatalog.Get(t);
            while (sim.Points(t) < def.MinPoints && pool.Count > 0)
            {
                var v = pool[0];
                sim.Assign(v, t);
                pool.RemoveAt(0);
            }
        }
        int i = 0;
        foreach (var v in pool) sim.Assign(v, i++ % 2 == 0 ? ActivityType.Farming : ActivityType.Water);
    }

    static void PlanRandom(Simulation sim, Random r)
    {
        var active = sim.ActiveActivities.ToList();
        foreach (var v in sim.Pool.ToList())
        {
            if (r.Next(5) == 0) continue;
            sim.Assign(v, active[r.Next(active.Count)].Type);
        }
    }

    static int ChooseSmart(Simulation sim, Emergency e)
    {
        var s = sim.State;
        var pay = e.Options[0];
        bool safe = pay.CanAfford(s)
                    && s.Food - pay.FoodCost >= sim.FoodConsumption
                    && s.Water - pay.WaterCost >= sim.WaterConsumption;
        return safe ? 0 : e.Options.Count - 1;
    }

    static void Print(string title, Stats st)
    {
        Console.WriteLine();
        Console.WriteLine("== " + title + " (" + st.Games + " games) ==");
        Console.WriteLine("Win rate:        " + (100.0 * st.Wins / st.Games).ToString("0.0") + "%");
        Console.WriteLine("Deaths/game:     " + ((double)st.Deaths / st.Games).ToString("0.00"));
        Console.WriteLine("Births/game:     " + ((double)st.Births / st.Games).ToString("0.00"));
        Console.WriteLine("Emergencies/game:" + ((double)st.Emergencies / st.Games).ToString("0.00"));
        Console.WriteLine("Raids/game:      " + ((double)st.Raids / st.Games).ToString("0.00"));
        if (st.FinalPop.Count > 0)
        {
            Console.WriteLine("Final pop (wins): avg " + st.FinalPop.Average().ToString("0.0") + ", min " + st.FinalPop.Min() + ", max " + st.FinalPop.Max());
            Console.WriteLine("Score (wins):     avg " + st.Score.Average().ToString("0") + ", min " + st.Score.Min() + ", max " + st.Score.Max());
        }
        Console.WriteLine("Loss by day:     " + string.Join(" ", Enumerable.Range(1, 7).Select(d => "d" + d + "=" + st.LossDay[d])));
    }
}
