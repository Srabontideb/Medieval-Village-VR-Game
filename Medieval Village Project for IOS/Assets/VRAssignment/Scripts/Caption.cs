using System;
using UnityEngine;

namespace VRAssignment
{
    /// <summary>
    /// "Video captions": short on-screen descriptions of what you just did
    /// (e.g. "Pinch → zoom (FOV 52°)"), so a screen recording explains itself
    /// without a voice-over. Shown at the bottom in mono, inside both eyes in Cardboard.
    /// Switch on/off in Menu → Video captions (saved).
    /// </summary>
    public static class Caption
    {
        const string Key = "vr_captions";
        static bool? enabled;

        public static bool Enabled
        {
            get { if (!enabled.HasValue) enabled = PlayerPrefs.GetInt(Key, 1) == 1; return enabled.Value; }
            set { enabled = value; PlayerPrefs.SetInt(Key, value ? 1 : 0); }
        }

        /// <summary>The UI listens to this and displays the text.</summary>
        public static event Action<string> Shown;

        public static void Show(string text)
        {
            if (Enabled) Shown?.Invoke(text);
        }
    }
}
