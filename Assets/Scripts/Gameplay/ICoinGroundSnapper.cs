using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Стратегия привязки (snap) монеты по высоте к поверхности трассы.
    /// </summary>
    public interface ICoinGroundSnapper
    {
        /// <summary>
        /// Пытается привязать высоту монеты к земле.
        /// Реализация должна изменить <paramref name="worldPosition"/>, если требуется "прилипание".
        /// </summary>
        /// <param name="worldPosition">Текущая позиция монеты (будет изменена).</param>
        /// <param name="groundMask">Маска слоёв для Raycast.</param>
        /// <param name="raycastStartHeight">Высота старта луча относительно текущей позиции.</param>
        /// <param name="raycastDistance">Длина луча Raycast вниз.</param>
        /// <param name="spawnHeight">Константа высоты монеты над найденной точкой поверхности.</param>
        void SnapToGround(
            ref Vector3 worldPosition,
            LayerMask groundMask,
            float raycastStartHeight,
            float raycastDistance,
            float spawnHeight);
    }
}

