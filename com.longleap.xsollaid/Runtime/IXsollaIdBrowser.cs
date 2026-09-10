using System;
using System.Threading.Tasks;

namespace LongLeap.XsollaId
{
    // Шов над браузером: открыть окно авторизации и дождаться, пока страница
    // обратного вызова пришлёт code и state.
    //
    // Вынесен в незаблокированную дефайном сборку, чтобы EditMode-тесты
    // подставляли подделку и обходились без браузера, без WebGL и без
    // зарегистрированного clientId. Настоящая реализация (jslib + popup)
    // живёт в сборке под XSOLLA_ID.
    public interface IXsollaIdBrowser
    {
        // На этой платформе окно открыть можно.
        bool IsSupported { get; }

        // Открывает authorizeUrl и ждёт ответ.
        //
        // ВАЖНО: реализация обязана вызвать window.open синхронно внутри
        // обработчика клика — если между кликом и открытием окна успеет
        // произойти await, браузер посчитает окно непрошеным и заблокирует.
        // Поэтому authorizeUrl приходит сюда уже готовой строкой: собирать её
        // (и тем более ходить за discovery) здесь нельзя.
        Task<BrowserAuthResult> AuthorizeAsync(string authorizeUrl, string expectedState, TimeSpan timeout);
    }

    // Что вернулось со страницы обратного вызова.
    public readonly struct BrowserAuthResult
    {
        // Одноразовый код авторизации. Значим только при Error == null.
        public string Code { get; }

        // state, как его вернул сервер. Сверять с ожидаемым — задача
        // вызывающего: шов не решает, что делать при расхождении.
        public string State { get; }

        // null — успех.
        public XsollaIdErrorCode? Error { get; }

        public string Message { get; }

        public bool IsSuccess => Error == null;

        private BrowserAuthResult(string code, string state, XsollaIdErrorCode? error, string message)
        {
            Code = code;
            State = state;
            Error = error;
            Message = message;
        }

        public static BrowserAuthResult Ok(string code, string state) =>
            new BrowserAuthResult(code, state, null, null);

        public static BrowserAuthResult Failed(XsollaIdErrorCode code, string message) =>
            new BrowserAuthResult(null, null, code, message);
    }
}
