using System;

namespace LuckyCiv.Core
{
    /// <summary>Balance knobs. Serializable so Unity can expose it in the inspector; no engine dependency.</summary>
    [Serializable]
    public sealed class GameConfig
    {
        public int TotalDays = 7;

        public VillagerClass[] StartingVillagers =
        {
            VillagerClass.Normal, VillagerClass.Normal, VillagerClass.Normal,
            VillagerClass.Strong, VillagerClass.Smart, VillagerClass.Fast,
        };

        public int StartFood = 12;
        public int StartWater = 12;
        public int StartHappiness = 60;
        public int StartHousing = 8;

        public int FoodPerVillager = 1;
        public int WaterPerVillager = 1;

        /// <summary>Relative weight of each luck result 1..10.</summary>
        public int[] LuckWeights = { 4, 8, 10, 14, 16, 14, 12, 10, 7, 5 };

        /// <summary>Extra output on completed food/water activities when luck is 10.</summary>
        public int LuckyBonusProduction = 2;

        /// <summary>Output multiplier when an activity does not reach its minimum points.</summary>
        public float PartialEfficiency = 0.5f;

        public float SpecialistRiskMultiplier = 0.5f;
        public float RiskReductionPerExtraPoint = 0.02f;
        public float MinRisk = 0.02f;

        public int HappinessDecay = 5;
        public int HappinessPerShortage = 10;
        public int HappinessPerDeath = 10;
        public int HappinessOvercrowding = 10;
        public int HappinessPerBirth = 10;

        /// <summary>Births need at least this happiness; chance = (happiness - BirthChanceBase) / 100, capped.</summary>
        public int BirthMinHappiness = 50;
        public int BirthChanceBase = 40;
        public float BirthMaxChance = 0.5f;

        /// <summary>Consecutive nights of shortage before someone dies.</summary>
        public int StarvationNights = 2;

        public float RaidChance = 0.35f;
        public float RaidTheftFraction = 0.4f;

        /// <summary>Below this happiness one fewer villager shows up for work each day.</summary>
        public int UnhappyThreshold = 30;

        public int HousingPerConstruction = 2;
    }
}
