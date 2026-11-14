using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ServeX.ViewModels
{
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object, Task> _execute;
        private readonly Predicate<object> _canExecute;
        public AsyncRelayCommand(Func<object, Task> execute, Predicate<object> canExecute = null)
        { _execute = execute ?? throw new ArgumentNullException(nameof(execute)); _canExecute = canExecute; }
        public bool IsRunning { get; private set; }
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => !IsRunning && (_canExecute?.Invoke(parameter) ?? true);
        public async Task ExecuteAsync(object parameter = null)
        {
            if (!CanExecute(parameter)) return;
            IsRunning = true;
            RaiseCanExecuteChanged();
            try { await _execute(parameter); }
            finally { IsRunning = false; RaiseCanExecuteChanged(); }
        }
        // Only the ICommand adapter is async void. Test/calling code awaits ExecuteAsync.
        // Unexpected failures reach the application's logged Dispatcher exception boundary.
        public async void Execute(object parameter) => await ExecuteAsync(parameter);
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
