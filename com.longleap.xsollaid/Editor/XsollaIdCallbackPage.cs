#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("LongLeap.XsollaId.Editor.Tests")]

namespace LongLeap.XsollaId.Editor
{
    // Renders xid-callback.html for a build: fills the page's list of origins
    // the sign-in result may be posted to with allowedGameOrigins from the
    // config asset.
    //
    // Why the list comes from the config: the page ships inside the package,
    // and a package installed from Git is read-only, so a game cannot edit the
    // page to add its own domains. The config already holds the list of
    // origins the game is served from (OriginGuard checks it before sign-in
    // opens), and it is the same list the page needs.
    //
    // A pure function without UnityEngine/UnityEditor, so it can be unit-tested.
    internal static class XsollaIdCallbackPage
    {
        // Marker inside the ALLOWED_GAME_ORIGINS array of the template. A JS
        // comment, so a page copied without rendering is still valid JS (with
        // an empty list: it then posts only to its own origin).
        public const string Placeholder = "/*ALLOWED_GAME_ORIGINS*/";

        // Returns the page with the origins in place of the placeholder.
        // Entries are trimmed; empty and duplicate entries are dropped.
        // Throws FormatException when the template has no placeholder.
        public static string Render(string template, IEnumerable<string> origins)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            int index = template.IndexOf(Placeholder, StringComparison.Ordinal);

            if (index < 0)
                throw new FormatException($"The callback page template has no {Placeholder} marker.");

            var literals = new List<string>();

            foreach (string origin in Normalize(origins))
                literals.Add(ToJsString(origin));

            return template.Substring(0, index)
                   + string.Join(", ", literals)
                   + template.Substring(index + Placeholder.Length);
        }

        public static List<string> Normalize(IEnumerable<string> origins)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (origins == null)
                return result;

            foreach (string raw in origins)
            {
                string origin = raw?.Trim();

                if (string.IsNullOrEmpty(origin) || !seen.Add(origin))
                    continue;

                result.Add(origin);
            }

            return result;
        }

        // A single-quoted JS string literal. '<' and '>' are escaped too, so a
        // value cannot close the surrounding <script> element.
        public static string ToJsString(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('\'');

            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '\'': builder.Append("\\'"); break;
                    default:
                        if (c < 0x20 || c == '<' || c == '>' || c == 0x2028 || c == 0x2029)
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            builder.Append(c);
                        break;
                }
            }

            builder.Append('\'');
            return builder.ToString();
        }
    }
}
#endif
