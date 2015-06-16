using System;
using SteamKit2;

namespace Dota2GameConnect.Utils
{
    public static class SteamClientEx
    {
        /// <summary>
        /// Add a callback
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="manager">callback manager</param>
        /// <param name="cb">callback</param>
        /// <returns></returns>
        public static Callback<T> Add<T>(this CallbackManager manager, Action<T> cb)
            where T : CallbackMsg
        {
            return new Callback<T>(cb, manager);
        }
    }
}
