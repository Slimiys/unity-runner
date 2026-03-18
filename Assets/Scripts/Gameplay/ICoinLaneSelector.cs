using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Стратегия выбора полосы (lane) для спавна монеты.
    /// </summary>
    public interface ICoinLaneSelector
    {
        /// <summary>
        /// Возвращает индекс полосы в диапазоне <c>0..laneCount-1</c>.
        /// </summary>
        /// <param name="laneCount">Количество полос на трассе.</param>
        /// <returns>Индекс полосы.</returns>
        int GetLaneIndex(int laneCount);
    }
}

