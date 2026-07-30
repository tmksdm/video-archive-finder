using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Desktop.Controls;

public sealed class HighlightedTextBlock
    : TextBlock
{
    private const string HighlightBackgroundBrushKey =
        "SearchHighlightBackgroundBrush";

    private const string HighlightForegroundBrushKey =
        "SearchHighlightForegroundBrush";

    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.Register(
            nameof(Segments),
            typeof(IEnumerable<FolderNameTextSegment>),
            typeof(HighlightedTextBlock),
            new FrameworkPropertyMetadata(
                default(IEnumerable<FolderNameTextSegment>),
                OnSegmentsChanged));

    public IEnumerable<FolderNameTextSegment>? Segments
    {
        get => (IEnumerable<FolderNameTextSegment>?)
            GetValue(SegmentsProperty);

        set => SetValue(SegmentsProperty, value);
    }

    private static void OnSegmentsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var textBlock =
            (HighlightedTextBlock)dependencyObject;

        textBlock.UpdateInlines(
            eventArgs.NewValue as
                IEnumerable<FolderNameTextSegment>);
    }

    private void UpdateInlines(
        IEnumerable<FolderNameTextSegment>? segments)
    {
        Inlines.Clear();

        if (segments is null)
        {
            return;
        }

        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);

            if (segment.IsHighlighted)
            {
                run.SetResourceReference(
                    TextElement.BackgroundProperty,
                    HighlightBackgroundBrushKey);

                run.SetResourceReference(
                    TextElement.ForegroundProperty,
                    HighlightForegroundBrushKey);

                run.FontWeight = FontWeights.SemiBold;
            }

            Inlines.Add(run);
        }
    }
}
