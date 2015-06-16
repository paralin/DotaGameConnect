﻿//#define HARDCODE_LOGIN
using System;
using System.Threading;
using Dota2GameConnect.LobbyBot;
using log4net;
using log4net.Config;
using SteamKit2;

namespace Dota2GameConnect
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

            bool keepRunning = true;

            Bot bot = new Bot(new SteamUser.LogOnDetails() {Username = username, Password = password});
            bot.Start();

            while (keepRunning)
            {
                Thread.Sleep(500);
            }
        }
    }
}