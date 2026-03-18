using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Дефолтная стратегия выбора полосы: случайная полоса из диапазона.
    /// </summary>
    public class RandomCoinLaneSelector : MonoBehaviour, ICoinLaneSelector
    {
        /// <summary>
        /// Возвращает случайный индекс полосы.
        /// </summary>
        /// <param name="laneCount">Количество полос на трассе.</param>
        /// <returns>Индекс выбранной полосы.</returns>
        public int GetLaneIndex(int laneCount)
        {
            return Random.Range(0, Mathf.Max(1, laneCount));
        }
    }
}

