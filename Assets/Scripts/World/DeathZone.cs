using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Зона падения, возвращающая объекты, поддерживающие респаун, в исходное состояние.
    /// </summary>
    public class DeathZone : MonoBehaviour
    {
        private const string PlayerTag = "Player";

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(PlayerTag))
            {
                return;
            }

            var respawnable = other.GetComponent<IRespawnable>();
            if (respawnable == null)
            {
                return;
            }

            respawnable.Respawn();
        }
    }
}
