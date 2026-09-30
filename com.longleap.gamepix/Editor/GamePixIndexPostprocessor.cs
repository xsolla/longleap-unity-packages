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
    // LongLeap, который едет на GamePix.
    //
    // Делает две вещи:
    //
    // 1. Резиновый канвас — всегда. Шаблон Minimal прибивает канвасу жёсткие
    //    "width: 960px; height: 600px" и не умеет подстраиваться под контейнер.
    //    В портале игра живёт в чужом iframe (в тест-тулките 800x450), и всё,
    //    что не влезло, обрезается — вместе с кнопками у правого и нижнего краёв.
    //    Правила площадки требуют обратного: вписываться вплоть до 640x480.
    //
    // 2. Тег SDK — всегда, первым скриптом в <head>. Это обязательный пункт
    //    официального гайда GamePix ("Import the SDK inside your index.html
    //    MANDATORY ... as first script in the <head> section"). Без объекта
    //    window.GamePix плагин молча не стартует (в его glue стоит
    //    `typeof GamePix === "undefined"` → return), и игра показывает пустой
    //    канвас.
    public sealed class GamePixIndexPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // URL из официального гайда GamePix.
        private const string SdkUrl = "https://integration.gamepix.com/sdk/v3/gamepix.sdk.js";

        private const string SdkTag = "<script src=\"" + SdkUrl + "\"></script>";

        // Старый dev-SDK, который раньше вставлял этот постпроцесс по дефайну
        // GAMEPIX_INJECT_SDK. У него вложенный API без методов рекламы, так что
        // рядом с настоящим SDK он только мешает — вычищаем, если попался.
        private static readonly Regex LegacySdkTag = new Regex(
            @"[ \t]*<script src=""https?://gamepix\.blob\.core\.windows\.net/gpxlib/[^""]*""></script>\r?\n?",
            RegexOptions.IgnoreCase);

        private static readonly Regex HeadOpen = new Regex(@"<head(\s[^>]*)?>", RegexOptions.IgnoreCase);

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
            html = LegacySdkTag.Replace(html, string.Empty);

            Match head = HeadOpen.Match(html);

            if (!head.Success)
            {
                Debug.LogError("GamePixIndexPostprocessor: в index.html нет <head> — " +
                               "обязательный тег GamePix SDK не вставлен.");
                return html;
            }

            // Тег уже стоит первым элементом <head> — повторный постпроцесс
            // той же папки ничего не меняет.
            int afterHead = head.Index + head.Length;
            if (html.Substring(afterHead).TrimStart().StartsWith(SdkTag))
                return html;

            // Стоит, но не первым — переносим наверх: гайд требует первый скрипт.
            html = html.Replace(SdkTag, string.Empty);
            html = html.Insert(afterHead, "\n    " + SdkTag);

            Debug.Log("GamePixIndexPostprocessor: тег GamePix SDK вставлен первым скриптом в <head>.");
            return html;
        }
    }
}
#endif
