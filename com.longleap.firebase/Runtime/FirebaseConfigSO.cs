using UnityEngine;

namespace LongLeap.Firebase
{
    [CreateAssetMenu(menuName = "LongLeap Services/Firebase Config")]
    public sealed class FirebaseConfigSO : ScriptableObject
    {
        [SerializeField] private string _apiKey;
        [SerializeField] private string _databaseUrl;

        [Header("Remote Config")]
        [SerializeField] private string _googleAppId;
        [SerializeField] private long _projectNumber;

        public string ApiKey => _apiKey;
        public string DatabaseUrl => _databaseUrl;
        public string GoogleAppId => _googleAppId;
        public long ProjectNumber => _projectNumber;
    }
}
