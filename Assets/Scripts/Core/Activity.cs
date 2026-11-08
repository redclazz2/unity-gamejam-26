using System;
using System.Linq;

namespace LuckyCiv.Core
{
    public enum ActivityType { Farming, Water, Entertainment, Defense, Construction, RepairWell }

    /// <summary>Static definition of an activity: requirement, output curve, risk and specialists.</summary>
    public sealed class ActivityDef
    {
        public ActivityType Type { get; }
        public string Name { get; }
        public string Summary { get; }
        public string OutputUnit { get; }
        public int MinPoints { get; }
        public int BaseOutput { get; }
        public int OutputPerExtraPoint { get; }
        public float AccidentChance { get; }
        public string AccidentName { get; }
        public VillagerClass[] Specialists { get; }

        public ActivityDef(ActivityType type, string name, string summary, string outputUnit, int minPoints,
            int baseOutput, int outputPerExtraPoint, float accidentChance, string accidentName,
            params VillagerClass[] specialists)
        {
            Type = type;
            Name = name;
            Summary = summary;
            OutputUnit = outputUnit;
            MinPoints = minPoints;
            BaseOutput = baseOutput;
            OutputPerExtraPoint = outputPerExtraPoint;
            AccidentChance = accidentChance;
            AccidentName = accidentName;
            Specialists = specialists;
        }

        public bool IsSpecialist(VillagerClass c) => Array.IndexOf(Specialists, c) >= 0;

        /// <summary>Work points one villager contributes to this activity.</summary>
        public int PointsFor(Villager v) => IsSpecialist(v.Class) ? 2 : 1;

        public string SpecialistNames => string.Join("/", Specialists.Select(s => s.DisplayName()));
    }

    public static class ActivityCatalog
    {
        public static readonly ActivityDef[] All =
        {
            new ActivityDef(ActivityType.Farming, "Agricultura", "Produce comida", "comida",
                3, 7, 2, 0.10f, "Plaga", VillagerClass.Fast),
            new ActivityDef(ActivityType.Water, "Agua", "Recoge agua del pozo", "agua",
                2, 6, 2, 0.15f, "Pozo roto", VillagerClass.Fast),
            new ActivityDef(ActivityType.Entertainment, "Entretenimiento", "Sube la felicidad", "felicidad",
                2, 15, 5, 0.10f, "Pelea", VillagerClass.Smart),
            new ActivityDef(ActivityType.Defense, "Defensa", "Protege de los bandidos esta noche", "",
                3, 0, 0, 0.20f, "Herido", VillagerClass.Strong),
            new ActivityDef(ActivityType.Construction, "Construcción", "Cada 4 puntos acumulados: +2 de vivienda", "",
                4, 0, 0, 0.15f, "Derrumbe", VillagerClass.Strong),
            new ActivityDef(ActivityType.RepairWell, "Reparar pozo", "Restablece la recogida de agua", "",
                2, 0, 0, 0f, "", VillagerClass.Strong),
        };

        /// <summary>Resolution order at the end of the day. Repair goes first so water can flow the same day.</summary>
        public static readonly ActivityType[] ExecutionOrder =
        {
            ActivityType.RepairWell, ActivityType.Farming, ActivityType.Water,
            ActivityType.Entertainment, ActivityType.Defense, ActivityType.Construction,
        };

        public static ActivityDef Get(ActivityType t) => All[(int)t];
    }
}
