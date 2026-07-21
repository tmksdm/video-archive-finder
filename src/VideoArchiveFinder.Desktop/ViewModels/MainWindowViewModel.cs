using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoArchiveFinder.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    public string StatusText => "Источники архива ещё не добавлены";
}
