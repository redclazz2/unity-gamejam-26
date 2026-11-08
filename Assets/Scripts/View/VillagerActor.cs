using UnityEngine;

namespace LuckyCiv.View
{
    /// <summary>Wanders around an anchor point so the village looks alive. Purely visual.</summary>
    public sealed class VillagerActor : MonoBehaviour
    {
        const float Speed = 1.8f;

        Vector3 anchor;
        float radius = 1.2f;
        Vector3 target;
        float wait;

        public void SetAnchor(Vector3 point, float wanderRadius)
        {
            anchor = point;
            radius = wanderRadius;
            PickTarget();
            wait = 0f;
        }

        void Update()
        {
            var pos = transform.position;
            var goal = new Vector3(target.x, pos.y, target.z);
            var delta = goal - pos;
            if (delta.sqrMagnitude > 0.01f)
            {
                transform.position = Vector3.MoveTowards(pos, goal, Speed * Time.deltaTime);
                transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
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
