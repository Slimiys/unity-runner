using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Дефолтная стратегия snap: привязывает Y монеты к земле через Raycast вниз.
    /// </summary>
    public class RaycastCoinGroundSnapper : MonoBehaviour, ICoinGroundSnapper
    {
        /// <summary>
        /// Привязка высоты монеты к земле через Raycast вниз.
        /// </summary>
        /// <param name="worldPosition">Позиция монеты (изменяется).</param>
        /// <param name="groundMask">Маска слоёв для Raycast.</param>
        /// <param name="raycastStartHeight">Высота старта луча относительно текущей позиции.</param>
        /// <param name="raycastDistance">Длина луча Raycast вниз.</param>
        /// <param name="spawnHeight">Сдвиг монеты над найденной точкой поверхности.</param>
        public void SnapToGround(
            ref Vector3 worldPosition,
            LayerMask groundMask,
            float raycastStartHeight,
            float raycastDistance,
            float spawnHeight)
        {
            // Начинаем луч выше и стреляем вниз, чтобы найти поверхность трассы даже на неровностях.
            var rayOrigin = worldPosition + Vector3.up * raycastStartHeight;
            var ray = new Ray(rayOrigin, Vector3.down);

            // Если маска не задана (value == 0), Raycast по умолчанию будет попадать во всё.
            var mask = groundMask.value != 0 ? groundMask.value : Physics.DefaultRaycastLayers;

            if (Physics.Raycast(ray, out var hit, raycastDistance, mask, QueryTriggerInteraction.Ignore))
            {
                worldPosition.y = hit.point.y + spawnHeight;
            }
        }
    }
}

