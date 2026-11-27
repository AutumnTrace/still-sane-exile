using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Releases;

namespace StillSaneExile.Desktop.Views;

/// <summary>Fills a TextBlock from release-note spans (<see cref="ReleaseNotes"/>): bold, code and links
/// become Bold, Runs and Hyperlinks, so the changelog's small markdown subset needs no markdown package.
/// A link opens in the browser only when clicked.</summary>
public static class NoteText
{
    public static readonly DependencyProperty SpansProperty = DependencyProperty.RegisterAttached(
        "Spans", typeof(IReadOnlyList<NoteSpan>), typeof(NoteText), new PropertyMetadata(null, OnSpansChanged));

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static IReadOnlyList<NoteSpan>? GetSpans(DependencyObject element) => (IReadOnlyList<NoteSpan>?)element.GetValue(SpansProperty);

    public static void SetSpans(DependencyObject element, IReadOnlyList<NoteSpan>? value) => element.SetValue(SpansProperty, value);

    private static void OnSpansChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text)
        {
            return;
        }

        text.Inlines.Clear();
        Hyperlink? link = null;
        string? linkUrl = null;
        foreach (var span in e.NewValue as IReadOnlyList<NoteSpan> ?? [])
        {
            var inline = Styled(span);
            if (span.Url is not { } url)
            {
                text.Inlines.Add(inline);
                link = null;
                continue;
            }

            // A link whose text mixes styles arrives as several spans with the same address: keep it one link.
            if (link is null || linkUrl != url)
            {
                link = new Hyperlink { ToolTip = url };
                link.Click += (_, _) => Browser.Open(url);
                linkUrl = url;
                text.Inlines.Add(link);
            }

            link.Inlines.Add(inline);
        }
    }

    /// <summary>Bold and code read a step brighter than the body text, except inside a link, which keeps
    /// the link colour.</summary>
    private static Inline Styled(NoteSpan span)
    {
        Inline inline = new Run(span.Text);
        if (span.Style.HasFlag(NoteStyle.Code))
        {
            inline.FontFamily = Mono;
            inline.Background = (Brush)Application.Current.Resources["Surface4"];
        }

        if (span.Style.HasFlag(NoteStyle.Bold))
        {
            inline = new Bold(inline);
        }

        if (span.Style != NoteStyle.None && span.Url is null)
        {
            inline.Foreground = (Brush)Application.Current.Resources["Text"];
        }

        return inline;
    }
}
