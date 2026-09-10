using UnityEngine;

namespace LongLeap.XsollaId
{
    // Итог загрузки картинки профиля. Форма и повадки — как у XsollaIdResult:
    // приватный конструктор, вложенные sealed наследники, разбирают через
    // `result is AvatarResult.Success ok`.
    //
    // Наружу из IXsollaIdAvatarLoader исключения не летят НИКОГДА. Загрузку
    // опрашивает UI, и повисшее там исключение — это пустое место на экране без
    // единой строчки в логе.
    public abstract class AvatarResult
    {
        private AvatarResult() { }

        public sealed class Success : AvatarResult
        {
            // Спрайт ОБЩИЙ: один адрес — один экземпляр на всё время страницы.
            // Его нельзя Destroy'ить и нельзя менять, см.
            // IXsollaIdAvatarLoader.
            public Sprite Sprite { get; }

            public Success(Sprite sprite) => Sprite = sprite;
        }

        public sealed class Failure : AvatarResult
        {
            public XsollaIdErrorCode Code { get; }

            // Текст для лога, не для игрока. Адреса картинки не содержит: тот
            // может нести подписанный токен CDN.
            public string Message { get; }

            public Failure(XsollaIdErrorCode code, string message)
            {
                Code = code;
                Message = message;
            }
        }

        // Картинки у аккаунта нет — это НЕ сбой и не повод для строчки в логе.
        // Клейм picture необязателен, и вернёт ли его конкретный инстанс Xsolla
        // ID, заранее неизвестно.
        public static AvatarResult NoPicture() =>
            new Failure(XsollaIdErrorCode.PictureUnavailable, "The account has no usable picture url.");

        public static AvatarResult Unavailable() =>
            new Failure(XsollaIdErrorCode.Unavailable, "Avatar loading is not available in this build.");
    }
}
