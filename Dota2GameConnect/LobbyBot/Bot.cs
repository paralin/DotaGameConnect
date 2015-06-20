#define SERVERS_VERBOSE
#define TEST_IMPL

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dota2;
using Dota2.Engine;
using Dota2.Engine.Control;
using Dota2.Engine.Game;
using Dota2.Engine.Game.Data;
using Dota2.Engine.Game.Entities.Dota;
using Dota2.Engine.Session.State.Enums;
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

        /// <summary>
        /// State of the bot
        /// </summary>
        public State State => _state.State;

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
        private int _connAttempts = 0;
        private const int maxAttempts = 3;
        private ulong lobbyChannelId;

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

            _state.Configure(State.DotaMenu)
                .SubstateOf(State.Dota)
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
                .OnEntry(JoinLobbyChat)
                .OnExit(LeaveLobbyChat)
                ;

            _state.Configure(State.DotaPlay)
                .SubstateOf(State.Dota)
                .Ignore(Trigger.DotaEnteredLobbyPlay)
                .Permit(Trigger.DotaEnteredLobbyUI, State.DotaLobby)
                .Permit(Trigger.DotaNoLobby, State.DotaMenu)
                .OnEntry(()=> _connAttempts = 0)
                .OnEntry(() =>
                {
                    //Brief delay before starting connection
                    Task.Run(() =>
                    {
                        Thread.Sleep(500);
                        if(_state.State == State.DotaPlay) StartDotaGameConnection();
                    });
                })
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
            //DotaGCHandler.JoinTeam(DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS);
            DotaGCHandler.JoinBroadcastChannel();
        }

        /// <summary>
        /// Join the lobby chat channel
        /// </summary>
        private void JoinLobbyChat()
        {
            if (DotaGCHandler.Lobby == null)
            {
                log.Warn("JoinLobbyChat called with no lobby!");
                return;
            }

            DotaGCHandler.JoinChatChannel("Lobby_"+DotaGCHandler.Lobby.lobby_id, DOTAChatChannelType_t.DOTAChannelType_Lobby);
        }

        /// <summary>
        /// Leave a lobby chat channel
        /// </summary>
        private void LeaveLobbyChat()
        {
            if (lobbyChannelId != 0)
            {
                DotaGCHandler.LeaveChatChannel(lobbyChannelId);
                lobbyChannelId = 0;
            }
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

            var c = SteamClient = new SteamClient();
            DotaGCHandler.Bootstrap(c);
            SteamUser = c.GetHandler<SteamUser>();
            SteamFriends = c.GetHandler<SteamFriends>();
            
            var cb = _cbManager = new CallbackManager(c);

            SetupSteamCallbacks(cb);
            SetupDotaGCCallbacks(cb);
            SetupDotaClientCallbacks(cb);

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
            cli.RegisterController(new BotGameController(this));
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
            ReleaseDotaGameConnection();
            _connAttempts++;
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
            var cname = SteamFriends.GetPersonaName();
            var tname = "WebLeagueBot";
            if (cname != tname)
            {
                log.DebugFormat("Changed persona name to {0} from {1}.", tname, cname);
                SteamFriends.SetPersonaName(tname);
            }
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
            cb.Add<SteamUser.AccountInfoCallback>(a =>
            {
                log.DebugFormat("Current name is: {0}, flags {1}, ", a.PersonaName, a.AccountFlags.ToString("G"));
                UpdatePersona();
            });
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
            cb.Add<DotaGCHandler.JoinChatChannelResponse>(a =>
            {
                if (DotaGCHandler.Lobby != null && a.result.channel_id != 0 &&
                    a.result.channel_name == "Lobby_" + DotaGCHandler.Lobby.lobby_id)
                    lobbyChannelId = a.result.channel_id;
            });
            cb.Add<DotaGCHandler.ChatMessage>(
                a =>
                {
                    log.DebugFormat("[Chat][" +
                                    (a.result.channel_id == lobbyChannelId ? "Lobby" : a.result.channel_id + "") + "] " +
                                    a.result.persona_name + ": " + a.result.text);
                    if (a.result.channel_id == lobbyChannelId)
                    {
                        if(a.result.text.Contains("!start")) DotaGCHandler.LaunchLobby();
                    }
                });
        }

        /// <summary>
        /// Setup the DOTA client callbacks
        /// </summary>
        /// <param name="cb"></param>
        private void SetupDotaClientCallbacks(CallbackManager cb)
        {
            cb.Add<DotaGameClient.HandshakeRejected>(rej =>
            {
                log.ErrorFormat("Connection to the game rejected with reason {0}. Attempts {1}/{2}.", rej.reason, _connAttempts, maxAttempts);
            });
            cb.Add<DotaGameClient.SessionStateTransition>(tra =>
            {
                log.Debug("[GameClient] "+tra.OldStatus.ToString("G")+" => "+tra.NewStatus.ToString("G"));

                if (tra.NewStatus == States.PLAY)
                {
                    _connAttempts = 0;
                }

                if (tra.NewStatus != States.DISCONNECTED || _state.State != State.DotaPlay) return;
                log.WarnFormat("Client has disconnected, attempts {0}/{1}.{2}", _connAttempts, maxAttempts, _connAttempts<maxAttempts ? " Retrying." : " Not retrying.");
                if (_connAttempts < maxAttempts)
                    StartDotaGameConnection();
            });
            cb.Add<DotaGameClient.LogMessage>(msg =>
            {
                log.Debug("[GameClient] "+msg.message);
            });
        }

        private void HandleLobbyUpdate(CSODOTALobby lobby)
        {
            if (Lobby == null && lobby != null)
            {
                log.DebugFormat("Entered lobby {0} with state {1}.", lobby.lobby_id, lobby.state.ToString("G"));
                if(lobby.pass_key != "wltest" || lobby.state >= CSODOTALobby.State.POSTGAME) DotaGCHandler.LeaveLobby();

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
            /*
                if (lobby.state == CSODOTALobby.State.UI &&
                    lobby.members.Count(
                        m =>
                            m.team == DOTA_GC_TEAM.DOTA_GC_TEAM_BAD_GUYS ||
                            m.team == DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS) >= 2) 
                    DotaGCHandler.LaunchLobby();
                    */

                if (lobby.game_state == DOTA_GameState.DOTA_GAMERULES_STATE_POST_GAME)
                {
                    log.Debug("Client has connected successfully and we've entered postgame.. Testing shutdown.");
                    Stop();
                }
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
#region Client Controller

        /// <summary>
        /// Controls a spectator bot
        /// </summary>
        private class BotGameController : IDotaGameController
        {
            private ILog log;

            private ulong _steamId;
            private DotaGameState _state;
            private IDotaGameCommander _commander;
            private Bot _bot;

            private bool _hasSentHello = false;
            private DOTA_GameState oldState = DOTA_GameState.DOTA_GAMERULES_STATE_INIT;

            public BotGameController(Bot bot)
            {
                log = bot.log;
                _bot = bot;
            }

            /// <summary>
            /// Initialize the controller as the client begins to connect.
            /// </summary>
            /// <param name="id">Steam ID</param>
            /// <param name="state">Emulated DOTA game client state</param>
            /// <param name="commander">Command generator</param>
            public void Initialize(ulong id, DotaGameState state, IDotaGameCommander commander)
            {
                _steamId = id;
                _state = state;
                _commander = commander;
            }

            public void Say(string msg)
            {
                _commander.Submit("say \""+msg+"\"");
            }

            /// <summary>
            /// Called every tick. Must return near-instantly.
            /// </summary>
            public void Tick()
            {
                if (!_state.EntityPool.Has<GameRules>()) return;
                var gr = _state.EntityPool.GetSingle<GameRules>();
                var gs = gr.GameState.Value;
                if (gs != oldState)
                {
                    log.DebugFormat("State {0} => {1}", oldState.ToString("G"), gs.ToString("G"));
                    oldState = gr.GameState.Value;
                }
                if (gs >= DOTA_GameState.DOTA_GAMERULES_STATE_HERO_SELECTION && !_hasSentHello)
                {
                    _hasSentHello = true;
                    Say("Hello, welcome to DOTA!");
                    log.DebugFormat("Sent message to all chat.");
                }
                foreach (var msg in _state.ChatMessages)
                {
                    log.Debug("[ALLCHAT] " + msg.prefix + ": " + msg.text);
                    if (msg.text.Contains("!pause"))
                    {
                        if (gr.PauseTeam.Value == GameRules.DOTA_ServerTeam.DIRE ||
                            gr.PauseTeam.Value == GameRules.DOTA_ServerTeam.RADIANT)
                        {
                            Say("The game is already paused by " +
                                (gr.PauseTeam.Value == GameRules.DOTA_ServerTeam.RADIANT ? "radiant." : "dire."));
                        }
                        else
                        {
                            Say("Pausing the game by request from " + msg.prefix + "!");
                            _commander.Submit("dota_pause");
                        }
                    }else if (msg.text.Contains("!whoami"))
                    {
                        Say("You are "+msg.prefix+"!");
                    }else if (msg.text.Contains("!time"))
                    {
                        Say("Current game time is "+gr.GameTime.Value+", game started at "+gr.GameStartTime.Value+".");
                    }else if (msg.text.Contains("!timeofday"))
                    {
                        Say("Time of day is: "+gr.NetTimeOfDay.Value);
                    }
                }
                _state.ChatMessages.Clear();
                foreach (var msg in _state.ChatEvents)
                {
                    log.Debug("[CHATEVENT] " + msg.type.ToString("G") + ": " + msg.value);
                    switch (msg.type)
                    {
                        case DOTA_CHAT_MESSAGE.CHAT_MESSAGE_FIRSTBLOOD:
                            Say("Nice firstblood Kappa.");
                            break;
                        case DOTA_CHAT_MESSAGE.CHAT_MESSAGE_RECONNECT:
                        case DOTA_CHAT_MESSAGE.CHAT_MESSAGE_CONNECT:
                            Say("Welcome back "+msg.value+".");
                            break;
                        case DOTA_CHAT_MESSAGE.CHAT_MESSAGE_HERO_KILL:
                            Say("Wow, that guy is totally feeding.");
                            break;
                        case DOTA_CHAT_MESSAGE.CHAT_MESSAGE_TOWER_KILL:
                            Say("Boom! The tower went down.");
                            break;
                    }
                }
                _state.ChatEvents.Clear();
                /*foreach (var msg in _state.GameEvents)
                {
                    log.Debug("[GAMEEVENT] "+msg.eventid);
                }*/
                _state.GameEvents.Clear();
            }
        }
#endregion
    }
}
