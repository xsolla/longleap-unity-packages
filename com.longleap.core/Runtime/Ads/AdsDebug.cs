using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Core.Ads
{
    // Ring buffer of ad events for an on-screen debug overlay.
    //
    // In a portal build the game runs inside someone else's iframe, where the
    // browser console is awkward to reach, yet the outcome of each show matters
    // — especially refusals and timeouts, which otherwise look like "the button
    // does nothing".
    //
    // Providers write here, not call sites: only the provider knows about the
    // timeout and about the portal swallowing a repeated request.
    public static class AdsDebug
    {
        private const int Capacity = 12;

        private static readonly List<string> _lines = new List<string>();

        // The overlay redraws on this event instead of polling every frame.
        public static event Action Changed;

        public static IReadOnlyList<string> Lines => _lines;

        public static void Record(string message)
        {
            // Wall-clock time (DateTime, not Time.realtimeSinceStartup): the line
            // is for a human, and its time should match the browser console.
            string formattedMessage = DateTime.Now.ToString("HH:mm:ss") + "  " + message;

            _lines.Add(formattedMessage);

            // Also written to the console, not only to the overlay: on a published
            // page the log can be exported and inspected later, while the overlay
            // lives only as long as the tab and is disabled in review builds.
            // The timestamp stays because the browser console does not add one.
            Debug.Log(formattedMessage);

            if (_lines.Count > Capacity)
                _lines.RemoveAt(0);

            Changed?.Invoke();
        }

        public static void Clear()
        {
            _lines.Clear();

            Changed?.Invoke();
        }
    }
}
