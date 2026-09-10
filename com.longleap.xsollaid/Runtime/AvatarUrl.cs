using System;

namespace LongLeap.XsollaId
{
    // Проверенный адрес картинки профиля (клейм picture из userinfo).
    //
    // Отдельный тип, а не string, по той же причине, что XsollaUserId: адрес
    // приезжает от сервера, и «проверен» здесь — свойство типа, а не обещание
    // вызывающего. Загрузчику картинок нельзя подсунуть сырой текст.
    //
    // В отличие от XsollaUserId конструктор НЕ бросает. sub мы обязаны
    // проверить сами и отказать, если его нет; picture — необязательный клейм,
    // и мусор в нём не должен ронять ни разбор userinfo, ни вход. Непригодное
    // значение просто становится None, а UI покажет заглушку.
    public readonly struct AvatarUrl : IEquatable<AvatarUrl>
    {
        public string Value { get; }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        private AvatarUrl(string value)
        {
            Value = value;
        }

        public static AvatarUrl None => default;

        public static AvatarUrl TryParse(string raw) =>
            IsAcceptable(raw) ? new AvatarUrl(raw.Trim()) : None;

        // Принимаем только абсолютный https. По убыванию строгости:
        //
        //   http на https-странице браузер режет как mixed content и внятно об
        //     этом не сообщает — отказать сразу честнее;
        //   data: и javascript: UnityWebRequest в WebGL всё равно не откроет, а
        //     пускать в загрузчик произвольную схему из ответа сервера не стоит
        //     и по соображениям безопасности;
        //   относительный адрес не к чему приложить: базы у нас нет.
        public static bool IsAcceptable(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            return Uri.TryCreate(raw.Trim(), UriKind.Absolute, out Uri uri) &&
                   uri.Scheme == Uri.UriSchemeHttps;
        }

        public bool Equals(AvatarUrl other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AvatarUrl other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;

        // Адрес может содержать подписанный токен CDN — в лог его не льём,
        // печатаем только факт наличия. Ровно как AccessToken и XsollaUserId.
        public override string ToString() => IsValid ? "AvatarUrl(set)" : "AvatarUrl(none)";

        public static bool operator ==(AvatarUrl left, AvatarUrl right) => left.Equals(right);
        public static bool operator !=(AvatarUrl left, AvatarUrl right) => !left.Equals(right);
    }
}
