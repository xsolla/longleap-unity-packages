#if GAMEPIX
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LongLeap.GamePix.Editor
{
    // Правит index.html портальной сборки сразу после билда.
    //
    // Почему постпроцесс, а не свой WebGL-шаблон: плагин принудительно ставит
    // APPLICATION:Minimal (UnityConfigurator.SetPlayerSettings) на каждый билд,
    // так что любой свой шаблон будет затёрт. А этот хук — наш код в пакете: его
    // не сносит обновление плагина, и отрабатывает он ДО того, как Builder
    // упакует папку html5 в .gpx.
    //
    // Живёт в пакете, а не в Assets: обе правки не знают ничего про конкретную
    // игру — это чистая починка вендорного пайплайна, нужная любому проекту
    // LongLeap, который едет на GamePix. Здесь же лежит и меню, которое включает
    // дефайн GAMEPIX_INJECT_SDK, так что переключатель и его потребитель рядом.
    //
    // Делает две вещи:
    //
    // 1. Резиновый канвас — всегда. Шаблон Minimal прибивает канвасу жёсткие
    //    "width: 960px; height: 600px" и не умеет подстраиваться под контейнер.
    //    В портале игра живёт в чужом iframe (в тест-тулките 800x450), и всё,
    //    что не влезло, обрезается — вместе с кнопками у правого и нижнего краёв.
    //    Правила площадки требуют обратного: вписываться вплоть до 640x480.
    //
    // 2. Тег SDK — только под GAMEPIX_INJECT_SDK. Плагин без объекта
    //    window.GamePix молча не стартует (в его glue стоит
    //    `typeof GamePix === "undefined"` → return), и игра показывает пустой
    //    канвас. В .gpx на ревью тег не нужен: там SDK подставляет издательский
    //    конвейер площадки, и подсунуть ему не тот SDK было бы хуже.
    public sealed class GamePixIndexPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // ВНИМАНИЕ: это старый публичный SDK. У него вложенный API
        // (GamePix.game.gameLoaded, GamePix.on.*) и НЕТ методов рекламы, тогда
        // как плагин ждёт плоский v4-API (GamePix.rewardAd, GamePix.updateScore).
        // Игра с ним загрузится, но реклама работать не будет — именно поэтому
        // в тест-тулките показы всегда отклонялись.
        //
        // Настоящий SDK найден: это gamepix.sdk.js, который площадка сама
        // подключает на странице игры (UMD-бандл, ставит глобальный GamePix,
        // внутри есть rewardAd/interstitialAd/updateScore/gameLoaded/CONST).
        // Копия лежит в Toolkit/other_game/.../gamepix.sdk.js.download.
        // Точный URL получить не удалось: страницу Chrome сохранил с локальной
        // ссылкой, а h5.gamepix.com отдаёт 403 всем, кроме браузера. Судя по
        // разметке, он под https://h5.gamepix.com/sdk/ — посмотрите в DevTools
        // на вкладке Network на странице игры и подставьте сюда.
        //
        // Для загрузки на площадку тег НЕ нужен: в HTML5-сборках она вставляет
        // свой (в сохранённой странице чужой игры он стоит в конце body с async
        // и пустым id — почерк вставки на их стороне). Дефайн GAMEPIX_INJECT_SDK
        // нужен только для локального прогона и тест-тулкита.
        private const string SdkUrl = "https://gamepix.blob.core.windows.net/gpxlib/dev/gamepix.js";

        private const string LoaderTag = "<script src=\"Build/html5.loader.js\"></script>";

        // Ищем канвас по элементу, а НЕ по строке с конкретными пикселями.
        // Раньше здесь стоял литерал "width: 960px; height: 600px", и когда
        // разрешение в Player Settings поменяли на 1920x1080, правка молча
        // перестала применяться: канвас уехал в сборку жёстко прибитым.
        private static readonly Regex CanvasStyle = new Regex(
            "(<canvas[^>]*?id=\"unity-canvas\"[^>]*?style=\")([^\"]*)(\")",
            RegexOptions.IgnoreCase);

        private const string FluidCanvasStyle =
            "width: 100%; height: 100%; display: block; background: #231F20";

        private const string HeadClose = "</head>";

        // Без высоты у html/body проценты канваса не от чего считать.
        // Кладём в <head>, а не перед <body>: браузеры терпят стиль между
        // </head> и <body>, но валидной разметкой это не является, а собранную
        // страницу смотрит ревью площадки.
        private const string FluidStyle =
            "    <style>html,body{width:100%;height:100%;margin:0;padding:0;border:0;" +
            "overflow:hidden;background:#231F20}</style>\n  ";

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
                return;

            string indexPath = Path.Combine(report.summary.outputPath, "index.html");

            if (!File.Exists(indexPath))
                return;

            string html = File.ReadAllText(indexPath);
            string original = html;

            html = MakeCanvasFluid(html);
            html = FixTitle(html);
            html = InjectSdkTag(html);

            if (html == original)
                return;

            File.WriteAllText(indexPath, html);
        }

        private static string MakeCanvasFluid(string html)
        {
            Match canvas = CanvasStyle.Match(html);

            if (!canvas.Success)
            {
                // Молчать нельзя: именно так предыдущая версия и подвела —
                // не нашла свою строку и не сказала об этом.
                Debug.LogWarning("GamePixIndexPostprocessor: канвас #unity-canvas в index.html " +
                                 "не найден — страница осталась как есть. Поменялся шаблон WebGL?");
                return html;
            }

            string before = canvas.Groups[2].Value;

            if (before == FluidCanvasStyle)
                return html;

            html = CanvasStyle.Replace(html,
                m => m.Groups[1].Value + FluidCanvasStyle + m.Groups[3].Value, 1);

            if (!html.Contains("html,body{width:100%"))
            {
                if (html.Contains(HeadClose))
                    html = html.Replace(HeadClose, FluidStyle + HeadClose);
                else
                    Debug.LogWarning("GamePixIndexPostprocessor: в index.html нет </head> — " +
                                     "стиль резинового html/body не вставлен, канвас может " +
                                     "остаться нулевой высоты.");
            }

            Debug.Log("GamePixIndexPostprocessor: канвас сделан резиновым (было \"" + before +
                      "\") — игра впишется в кадр площадки вместо обрезки по краям.");

            return html;
        }

        // Шаблон печатает "Unity Web Player | <Имя>". В HTML5-загрузке площадка
        // отдаёт нашу же страницу, так что этот заголовок видит и игрок, и ревью,
        // а правила требуют убрать штатную обвязку Unity и держать имя игры
        // одинаковым везде.
        private static string FixTitle(string html)
        {
            var title = new Regex("<title>\\s*Unity Web Player\\s*\\|\\s*(.*?)</title>",
                RegexOptions.IgnoreCase);

            Match m = title.Match(html);

            if (!m.Success)
                return html;

            return title.Replace(html, "<title>" + m.Groups[1].Value.Trim() + "</title>", 1);
        }

        private static string InjectSdkTag(string html)
        {
#if GAMEPIX_INJECT_SDK
            if (html.Contains(SdkUrl))
                return html;

            if (!html.Contains(LoaderTag))
            {
                Debug.LogWarning("GamePixIndexPostprocessor: не найден тег загрузчика Unity — " +
                                 "SDK не вставлен, index.html изменился в новой версии Unity?");
                return html;
            }

            // Строго ПЕРЕД загрузчиком: плагин ищет window.GamePix уже на старте.
            html = html.Replace(LoaderTag,
                "<script src=\"" + SdkUrl + "\"></script>\n    " + LoaderTag);

            Debug.LogWarning("GamePixIndexPostprocessor: в index.html вставлен тег SDK для тест-тулкита. " +
                             "В сборку на ревью это попадать не должно — выключите GAMEPIX_INJECT_SDK.");
#endif
            return html;
        }
    }
}
#endif
