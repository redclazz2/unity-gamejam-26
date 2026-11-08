namespace LuckyCiv.Core
{
    public enum VillagerClass { Normal, Strong, Smart, Fast }

    public static class VillagerClassExtensions
    {
        public static string DisplayName(this VillagerClass c)
        {
            switch (c)
            {
                case VillagerClass.Strong: return "Fuerte";
                case VillagerClass.Smart: return "Inteligente";
                case VillagerClass.Fast: return "Rápido";
                default: return "Normal";
            }
        }

        public static string Initial(this VillagerClass c)
        {
            switch (c)
            {
                case VillagerClass.Strong: return "F";
                case VillagerClass.Smart: return "I";
                case VillagerClass.Fast: return "R";
                default: return "N";
            }
        }
    }

    /// <summary>A single inhabitant. Occupies one worker slot; contributes work points by class.</summary>
    public sealed class Villager
    {
        public int Id { get; }
        public string Name { get; }
        public VillagerClass Class { get; }

        /// <summary>Nights left before this villager can work again. 0 = healthy.</summary>
        public int InjuredDays { get; set; }

        /// <summary>Chosen by the daily luck roll.</summary>
        public bool AvailableToday { get; set; }

        public ActivityType? Assignment { get; set; }

        public bool CanWork => AvailableToday && InjuredDays == 0;

        public Villager(int id, string name, VillagerClass cls)
        {
            Id = id;
            Name = name;
            Class = cls;
        }

        public override string ToString() => Name + " (" + Class.Initial() + ")";
    }
}
