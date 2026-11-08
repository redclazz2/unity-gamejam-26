using System.Collections.Generic;

namespace LuckyCiv.Core
{
    public enum GamePhase { Planning, Emergency, NightReport, GameOver }

    public enum GameResult { None, Victory, Defeat }

    /// <summary>All mutable game data. Owned and mutated only by <see cref="Simulation"/>.</summary>
    public sealed class GameState
    {
        public int Day;
        public GamePhase Phase;
        public GameResult Result;

        public int Luck;

        public int Food;
        public int Water;
        public int Happiness;
        public int Housing;

        /// <summary>Consecutive nights without enough food / water.</summary>
        public int Hunger;
        public int Thirst;

        public bool WellBroken;
        public int ConstructionProgress;

        /// <summary>Set by the Defense activity during the day; consumed by the night.</summary>
        public bool DefendedTonight;
        public int DefensePoints;

        public readonly List<Villager> Villagers = new List<Villager>();
        public readonly Queue<Emergency> Emergencies = new Queue<Emergency>();
        public readonly List<string> Log = new List<string>();

        public int Population => Villagers.Count;

        public Emergency CurrentEmergency => Emergencies.Count > 0 ? Emergencies.Peek() : null;
    }
}
