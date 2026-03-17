namespace Sandbox
{
    /// <summary>
    /// Интерфейс для объектов, которые можно восстановить в исходное состояние.
    /// </summary>
    public interface IRespawnable
    {
        /// <summary>
        /// Выполняет восстановление объекта в исходное состояние (респаун).
        /// </summary>
        void Respawn();
    }
}

