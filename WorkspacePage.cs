using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;

namespace CitrineLauncher;

/// <summary>An embedded management page; the workspace owns navigation and operation lifetime.</summary>
public class WorkspacePage : UserControl, IDisposable
{
    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        protected set { if (_busy == value) return; _busy = value; BusyChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? BusyChanged;
    public event EventHandler? BackRequested;
    protected Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;
    protected IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider
        ?? throw new InvalidOperationException("This page must be attached to the launcher.");
    protected void RequestBack() => BackRequested?.Invoke(this, EventArgs.Empty);
    public virtual void CancelOperation() { }
    public virtual void Dispose() { }
}
