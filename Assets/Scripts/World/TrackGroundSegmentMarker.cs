using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Маркер для сегментов земли, созданных <see cref="TrackGroundGenerator"/>.
    /// </summary>
    /// <remarks>
    /// Нужен, чтобы в редакторе можно было надёжно находить и удалять ранее сгенерированные сегменты
    /// даже после перекомпиляции (когда списки в компонентах сбрасываются).
    /// </remarks>
    public sealed class TrackGroundSegmentMarker : MonoBehaviour
    {
        /// <summary>
        /// Дистанция начала сегмента вдоль пути (для отладки).
        /// </summary>
        public float StartDistance { get; set; }

        /// <summary>
        /// Дистанция конца сегмента вдоль пути (для отладки).
        /// </summary>
        public float EndDistance { get; set; }
    }
}

