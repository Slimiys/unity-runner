using System;

namespace Sandbox
{
    /// <summary>
    /// Сервис начисления очков.
    /// </summary>
    public interface IScoreService
    {
        /// <summary>
        /// Увеличивает счёт на заданное количество.
        /// </summary>
        /// <param name="amount">Величина увеличения.</param>
        void AddScore(int amount);
    }
}

