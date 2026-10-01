using System.Windows.Forms;

namespace GameLauncher.Services;

public sealed class FolderPicker
{
    private Form? _owner;
    private int _busy;
    public void Attach(Form owner) => _owner = owner;

    public Task<string?> PickAsync()
    {
        var owner = _owner;
        if (owner is null || owner.IsDisposed || !owner.IsHandleCreated)
            throw new InvalidOperationException("The desktop window is not ready.");
        if (Interlocked.Exchange(ref _busy, 1) != 0)
            throw new InvalidOperationException("A folder dialog is already open.");
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            owner.BeginInvoke((Action)(() =>
            {
                try
                {
                    using var dialog = new FolderBrowserDialog
                    {
                        Description = "Choose a folder to scan for games, emulators, or tools",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = false
                    };
                    completion.TrySetResult(dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            }));
        }
        catch
        {
            Interlocked.Exchange(ref _busy, 0);
            throw;
        }
        return completion.Task;
    }
}
