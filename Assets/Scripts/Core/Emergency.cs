using System;
using System.Collections.Generic;

namespace LuckyCiv.Core
{
    public sealed class EmergencyOption
    {
        public string Label;
        public string Detail;
        public int FoodCost;
        public int WaterCost;
        public Action Apply;

        public bool CanAfford(GameState s) => s.Food >= FoodCost && s.Water >= WaterCost;

        public string CostText
        {
            get
            {
                if (FoodCost == 0 && WaterCost == 0) return "gratis";
                var parts = new List<string>();
                if (FoodCost > 0) parts.Add("-" + FoodCost + " comida");
                if (WaterCost > 0) parts.Add("-" + WaterCost + " agua");
                return string.Join(", ", parts);
            }
        }
    }

    /// <summary>A problem raised by an activity that the player must answer before the night resolves.</summary>
    public sealed class Emergency
    {
        public ActivityType Source;
        public string Title;
        public string Description;
        public readonly List<EmergencyOption> Options = new List<EmergencyOption>();
    }
}
