using UnityEngine;

namespace LongLeap.Firebase
{
    /// <summary>
    /// Firebase project settings. Values come from the Firebase console
    /// (Project settings → General). None of them are secrets: they ship inside the client.
    /// </summary>
    [CreateAssetMenu(menuName = "LongLeap Services/Firebase Config")]
    public sealed class FirebaseConfigSO : ScriptableObject
    {
        [SerializeField] private string _apiKey;
        [SerializeField] private string _databaseUrl;

        [Header("Remote Config")]
        [SerializeField] private string _googleAppId;
        [SerializeField] private long _projectNumber;

        /// <summary>Web API key. Used by Auth and Remote Config.</summary>
        public string ApiKey => _apiKey;

        /// <summary>Realtime Database root URL. Used by cloud saves and leaderboards.</summary>
        public string DatabaseUrl => _databaseUrl;

        /// <summary>App ID (<c>1:&lt;number&gt;:&lt;platform&gt;:&lt;hash&gt;</c>). Required by Remote Config.</summary>
        public string GoogleAppId => _googleAppId;

        /// <summary>Project number (numeric, not the project ID). Required by Remote Config.</summary>
        public long ProjectNumber => _projectNumber;
    }
}
