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
            // В зону может войти как корневой объект игрока, так и его дочерний коллайдер.
            // Поэтому проверяем тег сначала у вошедшего коллайдера, а затем у родителя.
            if (!other.CompareTag(PlayerTag) && (other.transform.parent == null || !other.transform.parent.CompareTag(PlayerTag)))
            {
                return;
            }

            // IRespawnable (например PlayerController) обычно находится на корневом объекте.
            // Если в триггер вошёл дочерний объект, ищем компонент вверх по иерархии.
            var respawnable = other.GetComponentInParent<IRespawnable>();
            if (respawnable == null)
            {
                return;
            }

            respawnable.Respawn();
        }
    }
}
