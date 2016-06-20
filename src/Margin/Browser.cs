using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Markdig;
using mshtml;

namespace MarkdownEditor
{
    public class Browser : IDisposable
    {
        private string _file;
        private HTMLDocument _htmlDocument;
        private MarkdownPipeline _pipeline;
        private string _htmlTemplate;
        private int _zoomFactor;
        private double _cachedPosition = 0,
                       _cachedHeight = 0,
                       _positionPercentage = 0;

        public Browser(string file)
        {
            var builder = new MarkdownPipelineBuilder()
                .UseEmojiAndSmiley()
                .UseAdvancedExtensions();

            _pipeline = builder.Build();
            _zoomFactor = GetZoomFactor();
            _file = file;
            _htmlTemplate = GetHtmlTemplate();

            InitBrowser();
        }

        public WebBrowser Control { get; private set; }

        private void InitBrowser()
        {
            Control = new WebBrowser();
            Control.HorizontalAlignment = HorizontalAlignment.Stretch;
            Control.LoadCompleted += (s, e) =>
            {
                Zoom(_zoomFactor);
                _htmlDocument = Control.Document as HTMLDocument;
                _cachedHeight = _htmlDocument.body.offsetHeight;
                _htmlDocument.documentElement.setAttribute("scrollTop", _positionPercentage * _cachedHeight / 100);

                foreach (IHTMLElement link in _htmlDocument.links)
                {
                    HTMLAnchorElement anchor = link as HTMLAnchorElement;
                    if (anchor == null || anchor.protocol != "file:")
                        continue;

                    HTMLAnchorEvents_Event handler = anchor as HTMLAnchorEvents_Event;
                    if (handler == null)
                        continue;

                    string file = anchor.pathname.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    if (!File.Exists(file))
                    {
                        anchor.title = "The file does not exist";
                        return;
                    }

                    handler.onclick += new HTMLAnchorEvents_onclickEventHandler(delegate ()
                    {
                        ProjectHelpers.OpenFileInPreviewTab(file);
                        return true;
                    });
                }
            };
        }

        private static int GetZoomFactor()
        {
            using (Graphics g = Graphics.FromHwnd(Process.GetCurrentProcess().MainWindowHandle))
            {
                var baseLine = 96;
                var dpi = g.DpiX;

                if (baseLine == dpi)
                    return 100;

                // 150% scaling => 225
                // 250% scaling => 400

                double scale = dpi * ((dpi - baseLine) / baseLine + 1);
                return Convert.ToInt32(Math.Ceiling(scale / 25)) * 25; // round up to nearest 25
            }
        }

        public void UpdateBrowser(string markdown)
        {
            if (_htmlDocument != null)
            {
                _cachedPosition = _htmlDocument.documentElement.getAttribute("scrollTop");
                _cachedHeight = Math.Max(1.0, _htmlDocument.body.offsetHeight);
                _positionPercentage = _cachedPosition * 100 / _cachedHeight;
            }

            var html = Markdown.ToHtml(markdown, _pipeline);
            var template = string.Format(CultureInfo.InvariantCulture, _htmlTemplate, html);

            Control.NavigateToString(template);
        }

        private static string GetFolder()
        {
            string assembly = Assembly.GetExecutingAssembly().Location;
            string folder = Path.GetDirectoryName(assembly);
            return folder;
        }

        private string GetHtmlTemplate()
        {
            var baseHref = Path.GetDirectoryName(_file).Replace("\\", "/");
            string folder = GetFolder();
            string cssPath = Path.Combine(folder, "margin\\highlight.css");
            string scriptPath = Path.Combine(folder, "margin\\prism.js");

            return $@"<!DOCTYPE html>
<html lang=""en"">
    <head>
        <meta http-equiv=""X-UA-Compatible"" content=""IE=Edge"" />
        <meta charset=""utf-8"" />
        <base href=""file:///{baseHref}/"" />
        <title>Markdown Preview</title>
        <link rel=""stylesheet"" href=""{cssPath}"" />
</head>
    <body class=""markdown-body"">
        {{0}}
        <script src=""{scriptPath}"" async defer></script>
    </body>
</html>";
        }


        private void Zoom(int zoomFactor)
        {
            if (zoomFactor == 100)
                return;

            dynamic OLECMDEXECOPT_DODEFAULT = 0;
            dynamic OLECMDID_OPTICAL_ZOOM = 63;
            FieldInfo fiComWebBrowser = typeof(WebBrowser).GetField("_axIWebBrowser2", BindingFlags.Instance | BindingFlags.NonPublic);

            if (fiComWebBrowser == null)
                return;

            object objComWebBrowser = fiComWebBrowser.GetValue(Control);

            if (objComWebBrowser == null)
                return;

            objComWebBrowser.GetType().InvokeMember("ExecWB", BindingFlags.InvokeMethod, null, objComWebBrowser, new object[] {
                OLECMDID_OPTICAL_ZOOM,
                OLECMDEXECOPT_DODEFAULT,
                zoomFactor,
                IntPtr.Zero
            });
        }

        public void Dispose()
        {
            if (Control != null)
                Control.Dispose();

            _htmlDocument = null;
        }
    }
}
