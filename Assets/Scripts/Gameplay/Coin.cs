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

            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.AddScore(1);
            }

            Destroy(gameObject);
        }
    }
}

