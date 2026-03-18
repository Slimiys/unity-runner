using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Монетка, которую игрок может собрать для получения очков.
    /// </summary>
    public class Coin : MonoBehaviour
    {
        private const string PlayerTag = "Player";

        /// <summary>
        /// Скорость вращения монетки вокруг собственной оси Y в градусах в секунду.
        /// </summary>
        [SerializeField]
        private float _rotationSpeed = 180f;

        /// <summary>
        /// Максимально допустимая высота игрока над монетой, при которой сбор всё ещё происходит.
        /// </summary>
        /// <remarks>
        /// Нужна, чтобы монетку можно было перепрыгнуть: если игрок слишком высоко, сбор не засчитывается.
        /// Подбирается под размеры коллайдера игрока и высоту спавна монет.
        /// </remarks>
        [SerializeField]
        [Min(0f)]
        private float _maxPlayerHeightAboveCoinToCollect = 0.6f;

        private void Update()
        {
            var angle = _rotationSpeed * Time.deltaTime;
            transform.Rotate(0f, 0f, angle, Space.Self);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(PlayerTag))
            {
                return;
            }

            // Если игрок подпрыгнул над монетой, его коллайдер всё ещё может пересечь триггер монеты,
            // особенно если коллайдер высокий. Ограничиваем сбор по высоте.
            var playerY = other.transform.position.y;
            var coinY = transform.position.y;
            var playerAboveCoin = playerY - coinY;
            if (playerAboveCoin > _maxPlayerHeightAboveCoinToCollect)
            {
                return;
            }

            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.AddScore(1);
            }

            Destroy(gameObject);
        }
    }
}

