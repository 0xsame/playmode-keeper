using UnityEngine;

namespace PlayModeKeeper
{
    internal static class PlayModeKeeperLog
    {
        private const string Prefix = "[PlayMode Keeper] ";

        internal static void Info(string message) => Debug.Log(Prefix + message);
        internal static void Warning(string message) => Debug.LogWarning(Prefix + message);
        internal static void Error(string message) => Debug.LogError(Prefix + message);
    }
}
