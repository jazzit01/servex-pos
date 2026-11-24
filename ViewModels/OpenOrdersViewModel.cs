using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    public sealed class OpenOrdersViewModel : ViewModelBase
    {
        private readonly OrderHistoryService _history;
        private readonly LogService _log;
        private IReadOnlyList<HistoryRow> _orders = Array.Empty<HistoryRow>();
        private string _message = string.Empty;

        public OpenOrdersViewModel(OrderHistoryService history, LogService log)
        {
            _history = history;
            _log = log;
            NewTakeawayCommand = new AsyncRelayCommand(_ => RunActionAsync(() => OpenTakeaway(null)), _ => !IsOpening);
            ResumeCommand = new AsyncRelayCommand(row => RunActionAsync(() => Resume((HistoryRow)row)),
                row => !IsOpening && row is HistoryRow);
            RefreshCommand = new AsyncRelayCommand(_ => LoadAsync(), _ => !IsOpening);
        }

        internal Func<int?, Task> OpenTakeaway { get; set; } = _ => Task.CompletedTask;
        internal Func<HistoryRow, Task> Resume { get; set; } = _ => Task.CompletedTask;
        public AsyncRelayCommand NewTakeawayCommand { get; }
        public AsyncRelayCommand ResumeCommand { get; }
        public AsyncRelayCommand RefreshCommand { get; }
        public bool IsOpening { get; private set; }
        public IReadOnlyList<HistoryRow> Orders { get => _orders; private set => SetProperty(ref _orders, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }

        public async Task LoadAsync()
        {
            try
            {
                Orders = await _history.GetOpenOrdersAsync();
                Message = Orders.Count == 0 ? "No open orders." : string.Empty;
            }
            catch (Exception exception)
            {
                _log.LogError("Could not load open orders", exception);
                Message = "Could not load open orders. Try Refresh.";
            }
        }

        private async Task RunActionAsync(Func<Task> action)
        {
            IsOpening = true;
            NewTakeawayCommand.RaiseCanExecuteChanged();
            ResumeCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            try
            {
                await action();
                await LoadAsync();
            }
            catch (Exception exception)
            {
                _log.LogError("Could not open order", exception);
                Message = "Could not open the order. Try Refresh.";
            }
            finally
            {
                IsOpening = false;
                NewTakeawayCommand.RaiseCanExecuteChanged();
                ResumeCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }
}
