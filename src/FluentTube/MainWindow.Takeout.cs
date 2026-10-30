using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace FluentTube;

public sealed partial class MainWindow
{
    private bool importingTakeout;
    private async void ImportTakeout(object sender, RoutedEventArgs args)
    {
        if (importingTakeout) return;
        importingTakeout = true;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".csv"); picker.FileTypeFilter.Add(".zip");
            var file = await picker.PickSingleFileAsync();
            if (file is null || closed) return;
            ShowMessage("Reading subscription export…");
            var result = await Task.Run(() => TakeoutImport.Read(file.Path));
            if (closed) return;
            if (result.Channels.Count == 0) throw new InvalidOperationException($"No valid channel subscriptions were found. {result.InvalidRows} invalid row(s) were skipped. Choose the original subscriptions export from Takeout.");
            var existing = library.Subscriptions.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var additions = result.Channels.Where(c => !existing.Contains(c.Id)).ToList();
            var skipped = result.DuplicateRows + result.Channels.Count - additions.Count;
            var summary = $"{additions.Count} new channel(s) · {skipped} already followed or duplicate · {result.InvalidRows} invalid row(s)";
            if (additions.Count == 0)
            {
                await new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Subscriptions already imported", Content = summary, CloseButtonText = "Close" }.ShowAsync();
                ShowMessage("No new subscriptions to import."); return;
            }
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = "Your existing follows are kept. This does not change your YouTube account.", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new ListView { ItemsSource = additions.Take(8).Select(c => c.Name).ToList(), SelectionMode = ListViewSelectionMode.None, MaxHeight = 230 });
            if (additions.Count > 8) content.Children.Add(new TextBlock { Text = $"And {additions.Count - 8} more…" });
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Import subscriptions?", Content = content, PrimaryButtonText = $"Import {additions.Count}", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) { if (!closed) ShowMessage("Import canceled. Your follows are unchanged."); return; }
            // Merge against the current list again in case a follow changed while reading.
            var previous = library.Subscriptions;
            library.Subscriptions = TakeoutImport.Merge(previous, additions);
            try { library.Save(); }
            catch { library.Subscriptions = previous; throw; }
            RefreshSubscriptionNavigation(); UpdateFollowButtons();
            ShowMessage($"Imported {library.Subscriptions.Count - previous.Count} channel(s). {skipped} duplicates and {result.InvalidRows} invalid rows skipped. Choose Refresh in Subscriptions to load uploads.");
        }
        catch (Exception e)
        {
            if (!closed) ShowMessage(e is InvalidOperationException ? e.Message : "Could not import this file. Choose the original Takeout ZIP or UTF-8 subscriptions.csv and check disk access.", true);
        }
        finally { importingTakeout = false; }
    }
}
