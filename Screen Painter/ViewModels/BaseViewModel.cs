using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Screen_Painter.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
    private bool _isBusy;
    private string _title = string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Async command that observes the returned Task. MAUI's built-in <see cref="Command"/>
/// treats <c>async () => await ...</c> as <c>async void</c> and does NOT observe the task,
/// so any exception inside the handler becomes an unhandled async exception that crashes
/// the app. This command awaits the task, logs failures, and surfaces a user alert.
/// </summary>
public class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private bool _isExecuting;

    public AsyncCommand(Func<Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
    }

    public AsyncCommand(Func<object?, Task> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
            return;

        _isExecuting = true;
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Command Error]: {ex.Message}");
            await ShellHelper.DisplayAlert("Something Went Wrong", ex.Message, "OK");
        }
        finally
        {
            _isExecuting = false;
        }
    }
}

/// <summary>
/// Parameterized variant of <see cref="AsyncCommand"/> that strongly types the command
/// parameter (e.g. the item tapped in a list). Exceptions are caught, logged, and shown
/// to the user instead of crashing the app.
/// </summary>
public class AsyncCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private bool _isExecuting;

    public AsyncCommand(Func<T?, Task> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
            return;

        _isExecuting = true;
        try
        {
            await _execute(parameter is T t ? t : default);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Command Error]: {ex.Message}");
            await ShellHelper.DisplayAlert("Something Went Wrong", ex.Message, "OK");
        }
        finally
        {
            _isExecuting = false;
        }
    }
}
