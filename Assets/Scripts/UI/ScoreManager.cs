using TMPro;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Управляет отображением очков игрока.
    /// </summary>
    public class ScoreManager : MonoBehaviour
    {
        /// <summary>
        /// Глобальный экземпляр менеджера очков для доступа из других компонентов.
        /// </summary>
        public static ScoreManager Instance { get; private set; }

        /// <summary>
        /// Текстовое поле для отображения очков.
        /// </summary>
        [SerializeField]
        private TMP_Text _scoreText;

        private int _score;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            UpdateScoreText();
        }

        /// <summary>
        /// Увеличивает счёт на заданное значение.
        /// </summary>
        /// <param name="amount">Величина увеличения счёта.</param>
        public void AddScore(int amount)
        {
            _score += amount;
            UpdateScoreText();
        }

        private void UpdateScoreText()
        {
            _scoreText.text = $"Score: {_score}";
        }
    }
}