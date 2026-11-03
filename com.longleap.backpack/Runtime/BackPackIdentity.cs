using UnityEngine;

namespace LongLeap.Backpack
{
    /// <summary>
    /// Whose behalf events are sent to the BackPack server on.
    ///
    /// A separate holder rather than a service field: the xsollaID does not exist
    /// at startup — it appears when the player signs in (or a session is silently
    /// restored) and changes on sign-out/sign-in. <see cref="BackPackService"/>
    /// asks for it on every request through a delegate, so writing it here is
    /// enough — the service does not need to be rebuilt.
    ///
    /// Until the player signs in this is null: no events are sent and achievements
    /// are saved locally (see BackPackAchievementStateStore). Sign-in sets the id
    /// after successful authorization and clears it on sign-out.
    /// </summary>
    public static class BackPackIdentity
    {
        /// <summary>The signed-in player's id, or null until sign-in.</summary>
        public static string XsollaId { get; private set; }

        /// <summary>Whether there is someone to send on behalf of.</summary>
        public static bool HasXsollaId => !string.IsNullOrEmpty(XsollaId);

        /// <summary>The player signed in.</summary>
        public static void SetXsollaId(string xsollaId)
        {
            XsollaId = string.IsNullOrEmpty(xsollaId) ? null : xsollaId;
        }

        /// <summary>The player signed out: events go nowhere again.</summary>
        public static void Clear()
        {
            XsollaId = null;
        }

        /// <summary>Resets the static when Domain Reload is disabled.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            XsollaId = null;
        }
    }
}
