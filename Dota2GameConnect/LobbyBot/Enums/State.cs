namespace Dota2GameConnect.LobbyBot.Enums
{
    /// <summary>
    /// Bot state
    /// </summary>
    public enum State
    {
        /// <summary>
        /// Overall container state
        /// </summary>
        Conceived,

        #region SignedOff

        /// <summary>
        /// Container state for NOT connected to steam
        /// </summary>
        SignedOff,

        /// <summary>
        /// Waiting for retry attempt
        /// </summary>
        RetryConnection,

        #endregion
        #region Steam

        /// <summary>
        /// Connecting to Steam / Using steam
        /// </summary>
        Steam,

        #region DOTA

        /// <summary>
        /// Currently using DOTA2
        /// </summary>
        Dota,

        /// <summary>
        /// Connecting to DOTA2
        /// </summary>
        DotaConnect,

        /// <summary>
        /// Main menu of DOTA
        /// </summary>
        DotaMenu,

        /// <summary>
        /// Currently in lobby UI
        /// </summary>
        DotaLobby,

        /// <summary>
        /// Currently game in progress
        /// </summary>
        DotaPlay,
        #endregion
        #endregion
    }
}
