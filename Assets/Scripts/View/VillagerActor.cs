using UnityEngine;

namespace LuckyCiv.View
{
    /// <summary>
    /// Pasea alrededor de un punto de anclaje para que la aldea parezca viva. Puramente visual:
    /// al andar bota y se balancea un poco, al llegar se queda quieto y recto.
    /// </summary>
    public sealed class VillagerActor : MonoBehaviour
    {
        const float Speed = 1.8f;
        const float StepsPerSecond = 11f;
        const float BobHeight = 0.06f;
        const float SwayDegrees = 7f;

        Vector3 anchor;
        float radius = 1.2f;
        Vector3 target;
        Vector3? waypoint;
        float wait;
        float baseY;
        float stepPhase;

        void Awake()
        {
            baseY = transform.position.y;
            stepPhase = Random.value * 10f;
        }

        /// <summary>Nuevo punto de anclaje; si se indica un punto de paso, va primero allí en línea recta.</summary>
        public void SetAnchor(Vector3 point, float wanderRadius, Vector3? via = null)
        {
            anchor = point;
            radius = wanderRadius;
            waypoint = via;
            if (waypoint.HasValue) target = waypoint.Value;
            else PickTarget();
            wait = 0f;
        }

        void Update()
        {
            var pos = transform.position;
            var flat = new Vector3(pos.x, baseY, pos.z);
            var goal = new Vector3(target.x, baseY, target.z);
            var delta = goal - flat;
            if (delta.sqrMagnitude > 0.01f)
            {
                flat = Vector3.MoveTowards(flat, goal, Speed * Time.deltaTime);
                stepPhase += Time.deltaTime * StepsPerSecond;
                transform.position = flat + Vector3.up * (Mathf.Abs(Mathf.Sin(stepPhase)) * BobHeight);
                transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(stepPhase) * SwayDegrees);
                return;
            }

            transform.position = flat;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (waypoint.HasValue)
            {
                waypoint = null;
                PickTarget();
                return;
            }
            wait -= Time.deltaTime;
            if (wait > 0f) return;
            PickTarget();
            wait = Random.Range(1f, 3f);
        }

        void PickTarget()
        {
            var offset = Random.insideUnitCircle * radius;
            target = anchor + new Vector3(offset.x, 0f, offset.y);
        }
    }
}
