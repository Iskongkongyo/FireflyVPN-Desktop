using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Renders a deliberately small, safe HTML subset as Avalonia controls. It
/// never creates a browser surface or executes scripts. Images are decoded
/// from bounded HTTPS/data-image payloads instead of browser content.
/// </summary>
internal static class FireflyRichText
{
    private const int MaxImageBytes = 5 * 1024 * 1024;
    private const int MaxDecodedImageWidth = 600;
    private static readonly HttpClient ImageClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static readonly Regex UnsafeBlockRegex = new(
        @"<(script|style|iframe|object|embed)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex TokenRegex = new(
        @"<!--.*?-->|<[^>]*>|[^<]+",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex TagRegex = new(
        @"^<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9]*)\b([^>]*)>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex HrefRegex = new(
        """\bhref\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SrcRegex = new(
        """\bsrc\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AltRegex = new(
        """\balt\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DataImageRegex = new(
        @"^data:image/(?:png|jpe?g|gif|webp|bmp);base64,(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant);

    private readonly record struct TextStyle(
        bool Bold = false,
        bool Italic = false,
        bool Underline = false,
        bool Strikethrough = false,
        bool Code = false,
        bool Preformatted = false,
        bool Marked = false,
        double FontSize = 0,
        string? Link = null);

    private sealed class RichFlow
    {
        private WrapPanel? _currentLine;

        public StackPanel Root { get; } = new()
        {
            Width = 310,
            Spacing = 4,
        };

        public void AddInline(Control control)
        {
            _currentLine ??= CreateLine();
            _currentLine.Children.Add(control);
        }

        public void AddBlock(Control control)
        {
            _currentLine = null;
            Root.Children.Add(control);
        }

        public void NewLine()
        {
            _currentLine = null;
        }

        private WrapPanel CreateLine()
        {
            var line = new WrapPanel
            {
                Width = 310,
                Orientation = Avalonia.Layout.Orientation.Horizontal,
            };
            Root.Children.Add(line);
            return line;
        }
    }

    public static Control Create(string? html)
    {
        var flow = new RichFlow();
        Render(flow, html ?? string.Empty);
        return flow.Root;
    }

    private static void Render(RichFlow flow, string html)
    {
        html = UnsafeBlockRegex.Replace(html, string.Empty);
        var frames = new Stack<(string Tag, TextStyle Previous)>();
        var lists = new List<(bool Ordered, int Next)>();
        var style = new TextStyle();
        var atLineStart = true;

        foreach (Match tokenMatch in TokenRegex.Matches(html))
        {
            var token = tokenMatch.Value;
            if (!token.StartsWith('<'))
            {
                var text = WebUtility.HtmlDecode(token);
                if (!style.Preformatted)
                {
                    text = WhitespaceRegex.Replace(text, " ");
                    if (atLineStart)
                    {
                        text = text.TrimStart();
                    }
                }

                if (text.Length > 0)
                {
                    AddText(flow, text, style);
                    atLineStart = text.EndsWith('\n');
                }
                continue;
            }

            if (token.StartsWith("<!--", StringComparison.Ordinal))
            {
                continue;
            }

            var tagMatch = TagRegex.Match(token);
            if (!tagMatch.Success)
            {
                continue;
            }

            // The optional capture participates even when it matched an empty
            // string, so Group.Success cannot distinguish <tag> from </tag>.
            var closing = tagMatch.Groups[1].Value.Length > 0;
            var tag = tagMatch.Groups[2].Value.ToLowerInvariant();
            var attributes = tagMatch.Groups[3].Value;
            var selfClosing = token.TrimEnd().EndsWith("/>", StringComparison.Ordinal);

            if (closing)
            {
                CloseTag(tag, frames, lists, ref style, flow, ref atLineStart);
                continue;
            }

            if (tag is "br")
            {
                AddLineBreak(flow, ref atLineStart);
                continue;
            }
            if (tag is "hr")
            {
                AddLineBreak(flow, ref atLineStart);
                AddText(flow, "────────────", style);
                atLineStart = false;
                AddLineBreak(flow, ref atLineStart);
                continue;
            }
            if (tag == "img")
            {
                var image = CreateImage(attributes, style.Link);
                if (image is not null)
                {
                    AddLineBreak(flow, ref atLineStart);
                    flow.AddBlock(image);
                    atLineStart = false;
                    AddLineBreak(flow, ref atLineStart);
                }
                continue;
            }
            if (tag is "source" or "video" or "audio")
            {
                continue;
            }

            if (IsBlock(tag))
            {
                AddLineBreak(flow, ref atLineStart);
            }

            if (tag is "ul" or "ol")
            {
                lists.Add((tag == "ol", 1));
            }
            else if (tag == "li")
            {
                AddLineBreak(flow, ref atLineStart);
                var prefix = "• ";
                if (lists.Count > 0 && lists[^1].Ordered)
                {
                    var list = lists[^1];
                    prefix = $"{list.Next}. ";
                    lists[^1] = (true, list.Next + 1);
                }
                AddText(flow, prefix, style with { Bold = true });
                atLineStart = false;
            }
            else if (tag == "blockquote")
            {
                AddText(flow, "❯ ", style with { Bold = true });
                atLineStart = false;
            }

            frames.Push((tag, style));
            style = ApplyTag(style, tag, attributes);

            if (selfClosing)
            {
                CloseTag(tag, frames, lists, ref style, flow, ref atLineStart);
            }
        }
    }

    private static TextStyle ApplyTag(TextStyle style, string tag, string attributes)
    {
        return tag switch
        {
            "b" or "strong" => style with { Bold = true },
            "i" or "em" => style with { Italic = true },
            "u" or "ins" => style with { Underline = true },
            "s" or "strike" or "del" => style with { Strikethrough = true },
            "code" or "kbd" => style with { Code = true },
            "pre" => style with { Code = true, Preformatted = true },
            "mark" => style with { Marked = true },
            "h1" => style with { Bold = true, FontSize = 22 },
            "h2" => style with { Bold = true, FontSize = 20 },
            "h3" => style with { Bold = true, FontSize = 18 },
            "h4" or "h5" or "h6" => style with { Bold = true, FontSize = 16 },
            "a" => style with { Underline = true, Link = GetSafeLink(attributes) },
            _ => style,
        };
    }

    private static void CloseTag(
        string tag,
        Stack<(string Tag, TextStyle Previous)> frames,
        List<(bool Ordered, int Next)> lists,
        ref TextStyle style,
        RichFlow flow,
        ref bool atLineStart)
    {
        while (frames.Count > 0)
        {
            var frame = frames.Pop();
            style = frame.Previous;
            if (frame.Tag == tag)
            {
                break;
            }
        }

        if (tag is "ul" or "ol")
        {
            if (lists.Count > 0)
            {
                lists.RemoveAt(lists.Count - 1);
            }
            AddLineBreak(flow, ref atLineStart);
        }
        else if (IsBlock(tag) || tag == "li")
        {
            AddLineBreak(flow, ref atLineStart);
        }
    }

    private static void AddText(RichFlow flow, string text, TextStyle style)
    {
        if (style.Link is not null)
        {
            var uri = style.Link;
            var label = new TextBlock
            {
                Text = text,
                FontStyle = style.Italic ? FontStyle.Italic : FontStyle.Normal,
                FontWeight = style.Bold ? FontWeight.Bold : FontWeight.Normal,
                Foreground = Brushes.DodgerBlue,
                TextDecorations = TextDecorations.Underline,
            };
            var button = new Button
            {
                MinHeight = 0,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = label,
            };
            ToolTip.SetTip(button, uri);
            button.Click += (_, _) => ProcUtils.ProcessStart(uri);
            flow.AddInline(button);
            return;
        }

        var run = new TextBlock
        {
            MaxWidth = 310,
            Text = text,
            FontStyle = style.Italic ? FontStyle.Italic : FontStyle.Normal,
            FontWeight = style.Bold ? FontWeight.Bold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
        };
        if (style.FontSize > 0)
        {
            run.FontSize = style.FontSize;
        }
        if (style.Code)
        {
            run.FontFamily = new FontFamily("Consolas");
            run.Background = new SolidColorBrush(Color.FromArgb(32, 100, 116, 139));
        }
        if (style.Marked)
        {
            run.Background = new SolidColorBrush(Color.FromArgb(96, 250, 204, 21));
        }
        if (style.Strikethrough)
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }
        else if (style.Underline)
        {
            run.TextDecorations = TextDecorations.Underline;
        }
        flow.AddInline(run);
    }

    private static void AddLineBreak(RichFlow flow, ref bool atLineStart)
    {
        if (atLineStart)
        {
            return;
        }
        flow.NewLine();
        atLineStart = true;
    }

    private static bool IsBlock(string tag)
    {
        return tag is "p" or "div" or "section" or "article" or "header" or "footer"
            or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
            or "blockquote" or "pre";
    }

    private static string? GetSafeLink(string attributes)
    {
        var value = GetAttribute(HrefRegex, attributes);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;
    }

    private static Control? CreateImage(string attributes, string? link)
    {
        var source = GetAttribute(SrcRegex, attributes);
        if (!TryGetSafeImageSource(source, out var imageSource))
        {
            return null;
        }

        var alt = GetAttribute(AltRegex, attributes);
        var placeholder = new TextBlock
        {
            Text = alt.IsNotEmpty() ? alt : "图片加载中…",
            FontStyle = FontStyle.Italic,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
        };
        var image = new Image
        {
            IsVisible = false,
            MaxWidth = 300,
            MaxHeight = 220,
            Stretch = Stretch.Uniform,
        };
        var panel = new StackPanel
        {
            MaxWidth = 300,
            Children =
            {
                placeholder,
                image,
            },
        };

        _ = LoadImageAsync(imageSource!, alt, image, placeholder);

        if (link is null)
        {
            return panel;
        }

        var linkedImage = new Button
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = panel,
        };
        ToolTip.SetTip(linkedImage, link);
        linkedImage.Click += (_, _) => ProcUtils.ProcessStart(link);
        return linkedImage;
    }

    private static async Task LoadImageAsync(
        string source,
        string alt,
        Image image,
        TextBlock placeholder)
    {
        try
        {
            var bytes = source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? DecodeDataImage(source)
                : await DownloadImageAsync(new Uri(source, UriKind.Absolute));
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = Bitmap.DecodeToWidth(stream, MaxDecodedImageWidth);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                image.Source = bitmap;
                image.IsVisible = true;
                placeholder.IsVisible = false;
            });
        }
        catch (Exception exception) when (exception is HttpRequestException
                                           or TaskCanceledException
                                           or IOException
                                           or FormatException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or NotSupportedException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                placeholder.Text = alt.IsNotEmpty() ? alt : "图片加载失败";
                placeholder.IsVisible = true;
            });
        }
    }

    private static async Task<byte[]> DownloadImageAsync(Uri uri)
    {
        using var response = await ImageClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps
            || response.Content.Headers.ContentLength > MaxImageBytes
            || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new InvalidDataException("The remote image response is not allowed.");
        }

        await using var input = await response.Content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }
            if (output.Length + read > MaxImageBytes)
            {
                throw new InvalidDataException("The remote image is too large.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return output.ToArray();
    }

    private static byte[] DecodeDataImage(string source)
    {
        var match = DataImageRegex.Match(source);
        if (!match.Success || match.Groups[1].Value.Length > MaxImageBytes * 2)
        {
            throw new InvalidDataException("The embedded image is invalid or too large.");
        }

        var bytes = Convert.FromBase64String(WhitespaceRegex.Replace(match.Groups[1].Value, string.Empty));
        return bytes.Length <= MaxImageBytes
            ? bytes
            : throw new InvalidDataException("The embedded image is too large.");
    }

    private static bool TryGetSafeImageSource(string value, out string? source)
    {
        source = null;
        if (DataImageRegex.IsMatch(value))
        {
            source = value;
            return true;
        }
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            source = uri.AbsoluteUri;
            return true;
        }
        return false;
    }

    private static string GetAttribute(Regex expression, string attributes)
    {
        var match = expression.Match(attributes);
        var value = match.Groups.Cast<Group>().Skip(1).FirstOrDefault(group => group.Success)?.Value;
        return WebUtility.HtmlDecode(value).TrimEx();
    }
}
