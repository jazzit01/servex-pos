using System;
using ServeX.ViewModels;

namespace ServeX.Presentation.Navigation
{
    public sealed class WorkspaceNavigation : ViewModelBase
    {
        private WorkspaceDestination _current;
        public WorkspaceDestination Current => _current;
        public bool CanGoBack => Current != WorkspaceDestination.Dashboard;
        public bool Navigate(WorkspaceDestination destination)
        {
            if (!Enum.IsDefined(destination)) throw new ArgumentOutOfRangeException(nameof(destination));
            if (!SetProperty(ref _current, destination, nameof(Current))) return false;
            OnPropertyChanged(nameof(CanGoBack));
            return true;
        }
        // Existing Back behavior returns to the dashboard.
        public void GoBack() => Navigate(WorkspaceDestination.Dashboard);
    }
}
