using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace WebDriver.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    public virtual void Dispose()
    {
        // Override in derived classes if needed
    }
}
