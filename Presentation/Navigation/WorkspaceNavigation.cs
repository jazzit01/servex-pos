using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ServeX.ViewModels;

namespace ServeX.Presentation.Navigation
{
    public sealed class WorkspaceNavigation : ViewModelBase
    {
        private readonly Stack<(ViewModelBase Page, TaskCompletionSource Completion)> _details = new();
        private WorkspaceDestination _current = WorkspaceDestination.Tables;

        public ViewModelBase Detail => _details.TryPeek(out var entry) ? entry.Page : null;

        public Task OpenDetailAsync(ViewModelBase page)
        {
            ArgumentNullException.ThrowIfNull(page);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _details.Push((page, completion));
            OnPropertyChanged(nameof(Detail));
            return completion.Task;
        }

        public void CloseDetail()
        {
            if (!_details.TryPop(out var entry)) return;
            OnPropertyChanged(nameof(Detail));
            entry.Completion.SetResult();
        }

        public void CloseDetails()
        {
            if (_details.Count == 0) return;
            var closed = _details.ToArray();
            _details.Clear();
            OnPropertyChanged(nameof(Detail));
            foreach (var entry in closed) entry.Completion.SetResult();
        }

        public WorkspaceDestination Current => _current;
        public bool CanGoBack => Current != WorkspaceDestination.Tables;
        public bool Navigate(WorkspaceDestination destination)
        {
            if (!Enum.IsDefined(destination)) throw new ArgumentOutOfRangeException(nameof(destination));
            if (!SetProperty(ref _current, destination, nameof(Current))) return false;
            OnPropertyChanged(nameof(CanGoBack));
            return true;
        }
        public void GoBack() => Navigate(WorkspaceDestination.Tables);
    }
}
