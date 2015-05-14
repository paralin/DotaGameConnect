﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Appccelerate.StateMachine;
using Appccelerate.StateMachine.Machine;
using Dota2.Engine;
using Dota2.GC.Dota.Internal;
using Dota2LobbyDump.Bots.DOTABot;
using log4net;
using log4net.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;
using WLNetwork.BotEnums;
using WLNetwork.Bots.DOTABot.Enums;

namespace Dota2LobbyDump
{
    internal class Program
    {
        private static readonly ILog log = LogManager.GetLogger("Program");
        private static readonly ILog glog = LogManager.GetLogger("GameClient");
        private static void Main(string[] args)
        {
            XmlConfigurator.Configure();

            Console.Write("Enter a username: ");
            string username = Console.ReadLine();
            Console.Write("Enter a password: ");
            string password = Console.ReadLine();

            string lpass = "cow";

            bool keepRunning = true;

            var bot = new LobbyBot(new SteamUser.LogOnDetails { Username = username, Password = password });
            bot.fsm.AddExtension(new BotExtension(bot, lpass));
            bot.Start();

            DotaGameClient gameClient = new DotaGameClient(bot.dota, bot.manager);
            gameClient.OnLog += (sender, eventArgs) =>
            {
                glog.Debug(eventArgs.Message);
            };

            CSODOTALobby llob = null;

            bot.LobbyUpdate += lobby =>
            {
                if (llob == null && lobby != null && lobby.members.All(m => m.team != DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS))
                {
                    log.Debug("Joining radiant.");
                    bot.dota.JoinTeam(DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS);
                }
                llob = lobby;
                if (lobby == null) return;
                if (
                    lobby.members.Count(
                        m =>
                            m.team == DOTA_GC_TEAM.DOTA_GC_TEAM_BAD_GUYS ||
                            m.team == DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS) >= 2 && lobby.state == CSODOTALobby.State.UI)
                    bot.StartGame();
                if (lobby.state == CSODOTALobby.State.POSTGAME)
                {
                    log.Debug("Leaving game, finished.");
                    bot.leaveLobby();
                    bot.Destroy();
                    keepRunning = false;
                }
            };

            while (keepRunning)
            {
                Thread.Sleep(1000);
            }
        }

        private class BotExtension : IExtension<States, Events>
        {
            private readonly LobbyBot bot;
            private readonly ILog log;
            private readonly string password;

            public BotExtension(LobbyBot bot, string password)
            {
                this.password = password;
                this.bot = bot;
                log = LogManager.GetLogger("GameBotE");
            }

            public void StartedStateMachine(IStateMachineInformation<States, Events> stateMachine)
            {
            }

            public void StoppedStateMachine(IStateMachineInformation<States, Events> stateMachine)
            {
            }

            public void EventQueued(IStateMachineInformation<States, Events> stateMachine, Events eventId,
                object eventArgument)
            {
            }

            public void EventQueuedWithPriority(IStateMachineInformation<States, Events> stateMachine, Events eventId,
                object eventArgument)
            {
            }

            public void SwitchedState(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> oldState,
                IState<States, Events> newState)
            {
                log.Debug("Switched state to " + newState.Id);
                if (newState.Id == States.DotaMenu)
                {
                    bot.CreateLobby(password);
                }
            }

            public void InitializingStateMachine(IStateMachineInformation<States, Events> stateMachine,
                ref States initialState)
            {
            }

            public void InitializedStateMachine(IStateMachineInformation<States, Events> stateMachine,
                States initialState)
            {
            }

            public void EnteringInitialState(IStateMachineInformation<States, Events> stateMachine, States state)
            {
            }

            public void EnteredInitialState(IStateMachineInformation<States, Events> stateMachine, States state,
                ITransitionContext<States, Events> context)
            {
            }

            public void FiringEvent(IStateMachineInformation<States, Events> stateMachine, ref Events eventId,
                ref object eventArgument)
            {
            }

            public void FiredEvent(IStateMachineInformation<States, Events> stateMachine,
                ITransitionContext<States, Events> context)
            {
            }

            public void HandlingEntryActionException(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> state, ITransitionContext<States, Events> context,
                ref Exception exception)
            {
            }

            public void HandledEntryActionException(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> state, ITransitionContext<States, Events> context,
                Exception exception)
            {
            }

            public void HandlingExitActionException(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> state, ITransitionContext<States, Events> context,
                ref Exception exception)
            {
            }

            public void HandledExitActionException(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> state, ITransitionContext<States, Events> context,
                Exception exception)
            {
            }

            public void HandlingGuardException(IStateMachineInformation<States, Events> stateMachine,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> transitionContext, ref Exception exception)
            {
            }

            public void HandledGuardException(IStateMachineInformation<States, Events> stateMachine,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> transitionContext, Exception exception)
            {
            }

            public void HandlingTransitionException(IStateMachineInformation<States, Events> stateMachine,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> context, ref Exception exception)
            {
            }

            public void HandledTransitionException(IStateMachineInformation<States, Events> stateMachine,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> transitionContext, Exception exception)
            {
            }

            public void SkippedTransition(IStateMachineInformation<States, Events> stateMachineInformation,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> context)
            {
            }

            public void ExecutedTransition(IStateMachineInformation<States, Events> stateMachineInformation,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> context)
            {
            }

            public void ExecutingTransition(IStateMachineInformation<States, Events> stateMachineInformation,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> context)
            {
            }
        }
    }
}