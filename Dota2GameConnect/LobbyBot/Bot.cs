﻿#define SERVERS_VERBOSE
#define TEST_IMPL

using System;
using System.Linq;
using System.Threading;
using Dota2;
using Dota2.Engine;
using Dota2.GC.Dota.Internal;
using Dota2.GC.Internal;
using Dota2GameConnect.LobbyBot.Enums;
using Dota2GameConnect.Utils;
using log4net;
using Stateless;
using SteamKit2;
using Timer = System.Timers.Timer;

namespace Dota2GameConnect.LobbyBot
{
    /// <summary>
    /// An instance of a DOTA 2 lobby/game bot
    /// </summary>
    public class Bot
    {
        #region Clients

        /// <summary>
        /// Steam client
        /// </summary>
        public SteamClient SteamClient { get; private set; }

        /// <summary>
        /// Steam users handler
        /// </summary>
        public SteamUser SteamUser { get; private set; }

        /// <summary>
        /// Steam friends
        /// </summary>
        public SteamFriends SteamFriends { get; private set; }

        /// <summary>
        /// Dota2 game coordinator
        /// </summary>
        public DotaGCHandler DotaGCHandler { get; private set; }

        /// <summary>
        /// The lobby before the current update
        /// </summary>
        public CSODOTALobby Lobby { get; private set; }

        /// <summary>
        /// DOTA 2 game client
        /// </summary>
        public DotaGameClient GameClient { get; private set; }

        #endregion
        #region Private

        private SteamUser.LogOnDetails _logonDetails;
        private readonly ILog log;
        private StateMachine<State, Trigger> _state;
        private Timer _reconnectTimer;
        private CallbackManager _cbManager;
        private bool _shouldReconnect = true;
        private int _cbThreadCtr = 0;
        private Thread _procThread;

        #endregion
        #region Constructor
        /// <summary>
        /// Create a new game bot
        /// </summary>
        /// <param name="details">Auth info</param>
        /// <param name="reconnectDelay">Delay between reconnection attempts to steam, set to a negative value to disable reconnecting</param>
        public Bot(SteamUser.LogOnDetails details, double reconnectDelay=3000)
        {
            log = LogManager.GetLogger("Bot " + details.Username);
            log.Debug("Initializing a new LobbyBot w/username " + details.Username);

            _logonDetails = details;

            if (reconnectDelay < 0)
            {
                reconnectDelay = 10;
                _shouldReconnect = false;
            }

            _reconnectTimer = new Timer(reconnectDelay);
            _reconnectTimer.Elapsed += (sender, args) => _state.Fire(Trigger.ConnectRequested);

            _state = new StateMachine<State, Trigger>(State.SignedOff);
            _state.OnTransitioned((transition =>
            {
                log.DebugFormat("{0} => {1}", transition.Source.ToString("G"), transition.Destination.ToString("G"));
            }));

            _state.Configure(State.Conceived)
                .Permit(Trigger.ShutdownRequested, State.SignedOff);

            _state.Configure(State.SignedOff)
                .SubstateOf(State.Conceived)
                //.OnEntryFrom(Trigger.SteamInvalidCreds, () => ) todo: handle invalid login
                .Permit(Trigger.ConnectRequested, State.Steam);

            _state.Configure(State.RetryConnection)
                .SubstateOf(State.SignedOff)
                .OnExit(() => _reconnectTimer.Stop())
                .OnEntry(() => _reconnectTimer.Start())
                .Permit(Trigger.ConnectRequested, State.Steam);

            _state.Configure(State.Steam)
                .SubstateOf(State.Conceived)
                .Permit(Trigger.SteamConnected, State.Dota)
                .PermitDynamic(Trigger.SteamDisconnected, () => _shouldReconnect ? State.RetryConnection : State.SignedOff)
                .Permit(Trigger.SteamInvalidCreds, State.SignedOff)
                .OnEntry(StartSteamConnection)
                .OnExit(ReleaseSteamConnection);

            _state.Configure(State.Dota)
                .SubstateOf(State.Steam)
                .Permit(Trigger.DotaConnected, State.DotaMenu)
                .PermitReentry(Trigger.DotaDisconnected)
                .Permit(Trigger.DotaEnteredLobbyUI, State.DotaLobby)
                .Permit(Trigger.DotaEnteredLobbyPlay, State.DotaPlay)
                .OnEntryFrom(Trigger.SteamConnected, StartDotaGCConnection);
                //.OnExit(ReleaseDotaGCConnection);

            _state.Configure(State.DotaMenu)
                .SubstateOf(State.Dota)
                .OnEntryFrom(Trigger.DotaConnected, UpdatePersona)
                .Permit(Trigger.DotaEnteredLobbyUI, State.DotaLobby)
                .Permit(Trigger.DotaEnteredLobbyPlay, State.DotaPlay)
#if TEST_IMPL
                .OnEntry(CreateLobby);
#endif
            ;

            _state.Configure(State.DotaLobby)
                .SubstateOf(State.Dota)
                .Ignore(Trigger.DotaEnteredLobbyUI)
                .Permit(Trigger.DotaEnteredLobbyPlay, State.DotaPlay)
                .Permit(Trigger.DotaNoLobby, State.DotaMenu)
#if TEST_IMPL
                .OnEntry(JoinLobbySlot)
#endif
                ;

            _state.Configure(State.DotaPlay)
                .SubstateOf(State.Dota)
                .Ignore(Trigger.DotaEnteredLobbyPlay)
                .Permit(Trigger.DotaEnteredLobbyUI, State.DotaLobby)
                .Permit(Trigger.DotaNoLobby, State.DotaMenu)
                .OnEntry(StartDotaGameConnection)
                .OnExit(ReleaseDotaGameConnection);
        }
        #endregion
#if TEST_IMPL
        #region Test Methods

        /// <summary>
        /// Creates a test lobby
        /// </summary>
        private void CreateLobby()
        {
            //DotaGCHandler.LeaveLobby();
            log.Debug("Requested lobby creation...");
            DotaGCHandler.CreateLobby("wltest", new CMsgPracticeLobbySetDetails()
            {
                allchat = false,
#if DEBUG
                allow_cheats = true,
#else
                allow_cheats = false,
#endif
                allow_spectating = true,
                fill_with_bots = false,
                game_mode = (uint)DOTA_GameMode.DOTA_GAMEMODE_AP,
                game_name = "Test Lobby",
                game_version = DOTAGameVersion.GAME_VERSION_CURRENT,
                server_region = 2 // useast
            });
        }

        /// <summary>
        /// Join the correct slot
        /// </summary>
        private void JoinLobbySlot()
        {
            DotaGCHandler.JoinTeam(DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS);
        }

        #endregion
#endif
        #region Internal Methods

        /// <summary>
        /// Start connecting to Steam
        /// </summary>
        private void StartSteamConnection()
        {
            ReleaseSteamConnection();

// see https://github.com/SteamRE/SteamKit/issues/120
#if HAS_SERVERS_SETTER
            try
            {
                if (File.Exists("steamservers.json"))
                {
                    string[] ips = JObject.Parse(File.ReadAllText("steamservers.json")).ToObject<string[]>();
                    if (ips.Length > 1)
                    {
                        SteamClient.Servers =
                            new ReadOnlyCollection<IPEndPoint>(ips.Select(IPEndpointEx.IPFromString).ToList());
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error("Issue parsing steamservers.json cache", ex);
            }
#endif

            var c = SteamClient = new SteamClient();
            DotaGCHandler.Bootstrap(c);
            SteamUser = c.GetHandler<SteamUser>();
            SteamFriends = c.GetHandler<SteamFriends>();
            
            var cb = _cbManager = new CallbackManager(c);

            SetupSteamCallbacks(cb);
            SetupDotaGCCallbacks(cb);

            c.Connect();
            _cbThreadCtr++;
            _procThread = new Thread(SteamThread);
            _procThread.Start(this);
        }

        /// <summary>
        /// Make sure every client is shutdown completely
        /// </summary>
        private void ReleaseSteamConnection()
        {
            ReleaseDotaGCConnection();

            SteamFriends?.SetPersonaState(EPersonaState.Offline);
            SteamFriends = null;

            SteamUser?.LogOff();
            SteamUser = null;

            SteamClient?.Disconnect();
            SteamClient = null;

            _cbThreadCtr++;
        }

        /// <summary>
        /// Start connecting to the DOTA 2 game coordinator
        /// </summary>
        private void StartDotaGCConnection()
        {
            DotaGCHandler = SteamClient.GetHandler<DotaGCHandler>();
            DotaGCHandler.Start();
            var cli = GameClient = new DotaGameClient(DotaGCHandler, _cbManager);
            cli.OnLog += (sender, args) => log.Debug("[GameClient] " + args.Message);
        }

        /// <summary>
        /// Completely disconnect from the DOTA gc
        /// </summary>
        private void ReleaseDotaGCConnection()
        {
            ReleaseDotaGameConnection();

            GameClient?.Dispose();
            GameClient = null;

            DotaGCHandler?.Stop();
            DotaGCHandler = null;
        }

        /// <summary>
        /// Start connecting to the DOTA 2 game server
        /// </summary>
        private void StartDotaGameConnection()
        {
            GameClient.Connect();
        }

        /// <summary>
        /// Completely disconnect from the DOTA 2 game server
        /// </summary>
        private void ReleaseDotaGameConnection()
        {
            GameClient?.Disconnect();
        }

        private void UpdatePersona()
        {
            log.Debug("Changed persona name to wltest from "+SteamFriends.GetPersonaName());
            SteamFriends.SetPersonaName("wltest");
            SteamFriends.SetPersonaState(EPersonaState.Online);
        }

        /// <summary>
        /// Internal thread
        /// </summary>
        /// <param name="state"></param>
        private static void SteamThread(object state)
        {
            Bot bot = state as Bot;
            int tid = bot._cbThreadCtr;
            var ts = TimeSpan.FromSeconds(1);
            while (tid == bot._cbThreadCtr)
            {
                try
                {
                    bot._cbManager.RunWaitCallbacks(ts);
                }
                catch (Exception ex)
                {
                    bot.log.Error("Error in Steam thread!", ex);
                }
            }
        }


#endregion
#region Callbacks

        /// <summary>
        /// Setup steam client callbacks
        /// </summary>
        /// <param name="cb"></param>
        private void SetupSteamCallbacks(CallbackManager cb)
        {
            // Handle general connection stuff
            cb.Add<SteamClient.ConnectedCallback>(a => SteamUser.LogOn(_logonDetails));
            cb.Add<SteamClient.DisconnectedCallback>(a => _state.Fire(Trigger.SteamDisconnected));
            cb.Add<SteamUser.LoggedOnCallback>(a =>
            {
                log.DebugFormat("Steam signin result: {0}", a.Result.ToString("G"));
                switch (a.Result)
                {
                    case EResult.OK:
                        _state.Fire(Trigger.SteamConnected);
                        break;

                    case EResult.ServiceUnavailable:
                    case EResult.ServiceReadOnly:
                    case EResult.TryAnotherCM:
                    case EResult.AccountLoginDeniedThrottle:
                    case EResult.AlreadyLoggedInElsewhere:
                    case EResult.BadResponse:
                    case EResult.Busy:
                    case EResult.ConnectFailed:
                        _state.Fire(Trigger.SteamDisconnected); //retry state
                        break;
                    default:
                        _state.Fire(Trigger.SteamInvalidCreds);
                        break;
                }
            });
            /* Need to understand this before it's useful
            cb.Add<SteamClient.ServerListCallback>(a => {
#if SERVERS_VERBOSE
                log.DebugFormat("Steam servers: {0}", JArray.FromObject(a.Servers.Keys).ToString());
#else
                log.DebugFormat("Received updated list of {0} STEAM servers.", a.Servers[EServerType.CM].Count);
#endif
                if(a.Servers.ContainsKey(EServerType.CM))
                    File.WriteAllText("steamservers.json", JObject.FromObject(a.Servers[EServerType.CM].Select(m=>m.ToString())).ToString(Formatting.Indented));
            });
            */
        }

        /// <summary>
        /// Setup DOTA 2 game coordinator callbacks
        /// </summary>
        /// <param name="cb">Manager</param>
        private void SetupDotaGCCallbacks(CallbackManager cb)
        {
            cb.Add<DotaGCHandler.GCWelcomeCallback>(a => { log.Debug("GC session welcomed"); _state.Fire(Trigger.DotaConnected); });
            cb.Add<DotaGCHandler.ConnectionStatus>(a =>
            {
                log.DebugFormat("GC connection status: {0}", a.result.status.ToString("G"));
                if (a.result.status == GCConnectionStatus.GCConnectionStatus_HAVE_SESSION)
                    _state.Fire(Trigger.DotaConnected);
                else _state.Fire(Trigger.DotaDisconnected);
            });
            cb.Add<DotaGCHandler.Popup>(a => log.DebugFormat("GC popup message: {0}", a.result.id.ToString("G")));
            cb.Add<DotaGCHandler.PracticeLobbySnapshot>(a => HandleLobbyUpdate(a.lobby));
            cb.Add<DotaGCHandler.PracticeLobbyLeave>(a => HandleLobbyUpdate(null));
            cb.Add<DotaGCHandler.PracticeLobbyUpdate>(a => HandleLobbyUpdate(a.lobby));
        }

        private void HandleLobbyUpdate(CSODOTALobby lobby)
        {
            if (Lobby == null && lobby != null)
            {
                log.DebugFormat("Entered lobby {0} with state {1}.", lobby.lobby_id, lobby.state.ToString("G"));
                if(lobby.pass_key != "wltest") DotaGCHandler.LeaveLobby();
            }else if (Lobby != null && lobby == null)
            {
                log.DebugFormat("Exited lobby {0}.", Lobby.lobby_id);
            }

            if(lobby != null)
                _state.Fire(lobby.state == CSODOTALobby.State.UI || string.IsNullOrEmpty(lobby.connect) ? Trigger.DotaEnteredLobbyUI : Trigger.DotaEnteredLobbyPlay);
            else
                _state.Fire(Trigger.DotaNoLobby);

#if TEST_IMPL
            if (lobby != null)
            {
                if (lobby.state == CSODOTALobby.State.UI &&
                    lobby.members.Count(
                        m =>
                            m.team == DOTA_GC_TEAM.DOTA_GC_TEAM_BAD_GUYS ||
                            m.team == DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS) >= 2) 
                    DotaGCHandler.LaunchLobby();
            }
#endif

            Lobby = lobby;
        }
#endregion
#region Public Methods

        /// <summary>
        /// Start the bot
        /// </summary>
        public void Start()
        {
            _state.Fire(Trigger.ConnectRequested);
        }

        /// <summary>
        /// Shutdown the bot completely
        /// </summary>
        public void Stop()
        {
            _state.Fire(Trigger.ShutdownRequested);
        }

#endregion
    }
}