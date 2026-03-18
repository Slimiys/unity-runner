using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Настройки трассы раннера (полосы, ширина, центр).
    /// </summary>
    /// <remarks>
    /// Идея: параметры трассы должны храниться на самой трассе, а не на игроке.
    /// Игрок, спавнеры и другие системы берут значения отсюда, чтобы не было рассинхронизации.
    /// </remarks>
    public class TrackSettings : MonoBehaviour
    {
        /// <summary>
        /// Количество полос на трассе (обычно 3).
        /// </summary>
        [SerializeField]
        [Min(1)]
        private int _laneCount = 3;

        /// <summary>
        /// Ширина одной полосы (расстояние между центрами полос) в мировых единицах.
        /// </summary>
        [SerializeField]
        [Min(0.1f)]
        private float _laneWidth = 1.5f;

        /// <summary>
        /// Центр трассы по оси X (центр средней полосы).
        /// </summary>
        /// <remarks>
        /// Это значение используется как базовое смещение полос. Если в сцене трасса расположена так,
        /// что её центр не совпадает с X=0, укажи нужное значение здесь.
        /// </remarks>
        [SerializeField]
        private float _laneCenterX = 0f;

        /// <summary>
        /// Количество полос на трассе.
        /// </summary>
        public int LaneCount => _laneCount;

        /// <summary>
        /// Ширина одной полосы.
        /// </summary>
        public float LaneWidth => _laneWidth;

        /// <summary>
        /// Центр трассы по оси X.
        /// </summary>
        public float LaneCenterX => _laneCenterX;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Защита от случайных некорректных значений в инспекторе.
            _laneCount = Mathf.Max(1, _laneCount);
            _laneWidth = Mathf.Max(0.1f, _laneWidth);
        }
#endif
    }
}

