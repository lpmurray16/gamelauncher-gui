using System.Windows.Forms;

namespace GameLauncher.Services;

public sealed class FolderPicker
{
    private Form? _owner;
    public void Attach(Form owner) => _owner = owner;

    public Task<string?> PickAsync()
    {
        var owner = OwnerOrThrow();
        return RunDialog(owner, () =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose a folder to scan for games, emulators, or tools",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
        });
    }

    public Task<string?> PickBrowserAsync()
    {
        var owner = OwnerOrThrow();
        return RunDialog(owner, () =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Choose your browser executable",
                Filter = "Browser executable (*.exe)|*.exe",
                CheckFileExists = true,
                Multiselect = false
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        });
    }

    public Task<string?> PickImageAsync()
    {
        var owner = OwnerOrThrow();
        return RunDialog(owner, () =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Choose an image",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp",
                CheckFileExists = true,
                Multiselect = false
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        });
    }

    private Form OwnerOrThrow()
    {
        var owner = _owner;
        if (owner is null || owner.IsDisposed || !owner.IsHandleCreated)
            throw new InvalidOperationException("The desktop window is not ready.");
        return owner;
    }

    private static Task<string?> RunDialog(Form owner, Func<string?> show)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            owner.Invoke((Action)(() =>
            {
                try { completion.TrySetResult(show()); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromException<string?>(ex);
        }
        return completion.Task;
    }
}
