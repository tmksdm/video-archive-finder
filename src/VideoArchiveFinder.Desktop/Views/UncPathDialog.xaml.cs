using System.Windows;
using System.Windows.Controls;
using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Desktop.Views;

public partial class UncPathDialog : Window
{
    public UncPathDialog()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            PathTextBox.Focus();
            PathTextBox.SelectAll();
        };
    }

    public string? EnteredPath { get; private set; }

    private void AddButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var enteredPath = PathTextBox.Text.Trim();

        try
        {
            var source = ArchiveSource.Create(enteredPath);

            if (source.SourceType != ArchiveSourceType.UncPath)
            {
                ShowValidationError();
                return;
            }

            EnteredPath = source.FullPath;
            DialogResult = true;
        }
        catch (ArgumentException)
        {
            ShowValidationError();
        }
    }

    private void PathTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (ValidationText is not null)
        {
            ValidationText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowValidationError()
    {
        ValidationText.Visibility = Visibility.Visible;
        PathTextBox.Focus();
        PathTextBox.SelectAll();
    }
}
