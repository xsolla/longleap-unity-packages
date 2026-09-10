using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.XsollaId
{
    // Заглушка загрузчика на случай, когда картинок нет: портальная сборка или
    // выключенный дефайн XSOLLA_ID.
    //
    // Парная к UnavailableXsollaIdAuth и по той же причине. На GamePix T&C (3.3)
    // запрещают обращаться к любому адресу, кроме площадки, и проверяют это
    // инспекцией трафика — запрос к CDN аватарок такой же посторонний запрос,
    // как и обращение к id.xsolla.com.
    public sealed class UnavailableXsollaIdAvatarLoader : IXsollaIdAvatarLoader
    {
        public bool IsAvailable => false;

        public Task<AvatarResult> LoadAsync(AvatarUrl url) => Task.FromResult(AvatarResult.Unavailable());

        public bool TryGetCached(AvatarUrl url, out Sprite sprite)
        {
            sprite = null;
            return false;
        }
    }
}
