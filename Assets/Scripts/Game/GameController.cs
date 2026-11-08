using LuckyCiv.Core;
using UnityEngine;

namespace LuckyCiv.Game
{
    /// <summary>
    /// Owns the one <see cref="Simulation"/> instance for the scene. The HUD and the village view
    /// read it from this component; nothing else creates or mutates game rules.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] GameConfig config = new GameConfig();

        [Tooltip("0 = semilla aleatoria. Otro valor repite siempre la misma partida.")]
        [SerializeField] int seed = 0;

        public Simulation Sim { get; private set; }

        void Awake()
        {
            Sim = new Simulation(config, seed == 0 ? (int?)null : seed);
            Sim.NewGame();
        }

        /// <summary>Resets the same simulation instance so listeners keep their subscriptions.</summary>
        public void Restart() => Sim.NewGame();
    }
}
