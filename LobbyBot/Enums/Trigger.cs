namespace Dota2.Samples.GameConnect.LobbyBot.Enums
{
    /// <summary>
    /// Triggers for the Lobby bot state machine
    /// </summary>
    public enum Trigger
    {
        /// <summary>
        /// Request to start connecting to Steam
        /// </summary>
        ConnectRequested,

        /// <summary>
        /// Request to immediately completely shut down the bot
        /// </summary>
        ShutdownRequested,

        /// <summary>
        /// Connected to the Steam network
        /// </summary>
        SteamConnected,

        /// <summary>
        /// Disconnected from the Steam network
        /// </summary>
        SteamDisconnected,

        /// <summary>
        /// Invalid login details
        /// </summary>
        SteamInvalidCreds,

        /// <summary>
        /// DOTA 2 ready
        /// </summary>
        DotaConnected,

        /// <summary>
        /// Dota 2 disconnected from GC
        /// </summary>
        DotaDisconnected,

        /// <summary>
        /// Dota2 entered lobby UI
        /// </summary>
        DotaEnteredLobbyUI,

        /// <summary>
        /// Dota2 entered lobby play
        /// </summary>
        DotaEnteredLobbyPlay,

        /// <summary>
        /// Dota2 no lobby
        /// </summary>
        DotaNoLobby,
    }
}
