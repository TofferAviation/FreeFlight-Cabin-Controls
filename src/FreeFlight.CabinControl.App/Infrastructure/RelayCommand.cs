using System.Windows.Input;

namespace FreeFlight.CabinControl.App.Infrastructure;

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(
    Func<Task> execute,
    Action<Exception>? onError = null) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
        {
            return;
        }

        try
        {
            _isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            await execute();
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
        }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
