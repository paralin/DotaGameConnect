﻿//#define HARDCODE_LOGIN

using System;
using System.Threading;
using Dota2.Samples.GameConnect.LobbyBot;
using Dota2.Samples.GameConnect.LobbyBot.Enums;
using log4net.Config;
using SteamKit2;

namespace Dota2.Samples.GameConnect
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            XmlConfigurator.Configure();

#if HARDCODE_LOGIN
            string username = "Kappa";
            string password = "Keepo";
#else
            Console.Write("Enter a username: ");
            string username = Console.ReadLine();
            Console.Write("Enter a password: ");
            string password = Console.ReadLine();
#endif

            Bot bot = new Bot(new SteamUser.LogOnDetails() {Username = username, Password = password});
            bot.Start();

            while (bot.State != State.SignedOff)
            {
                try
                {
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    break;
                }
            }
        }
    }
}