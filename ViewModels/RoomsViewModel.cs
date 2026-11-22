using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public sealed class RoomsViewModel : ViewModelBase
    {
        private readonly RoomService _rooms;
        private readonly OrderWorkspaceService _orders;
        private readonly LogService _log;
        private IReadOnlyList<Room> _items = Array.Empty<Room>();
        private Room _selected;
        private string _number;
        private RoomStatus _status = RoomStatus.Available;
        private string _message;

        public RoomsViewModel(RoomService rooms, OrderWorkspaceService orders, LogService log)
        {
            _rooms = rooms;
            _orders = orders;
            _log = log;
            NewCommand = new RelayCommand(_ => { Selected = null; Number = string.Empty; Status = RoomStatus.Available; });
            SaveCommand = new AsyncRelayCommand(_ => RunAsync(async () =>
            {
                await _rooms.SaveAsync(Selected?.Id, Number, Status);
                await LoadAsync();
                Message = "Room saved.";
            }));
            OpenOrderCommand = new AsyncRelayCommand(_ => RunAsync(async () =>
            {
                if (Selected != null) await _orders.ShowForRoomAsync(Selected);
                await LoadAsync();
            }), _ => Selected != null);
        }

        public RelayCommand NewCommand { get; }
        public AsyncRelayCommand SaveCommand { get; }
        public AsyncRelayCommand OpenOrderCommand { get; }
        public RoomStatus[] StatusOptions { get; } = Enum.GetValues<RoomStatus>();
        public IReadOnlyList<Room> Items => _items;
        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string Number { get => _number; set => SetProperty(ref _number, value); }
        public RoomStatus Status { get => _status; set => SetProperty(ref _status, value); }

        public Room Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value)) return;
                if (value != null)
                {
                    Number = value.RoomNumber;
                    Status = value.Status;
                }
                OpenOrderCommand.RaiseCanExecuteChanged();
            }
        }

        public Task LoadAsync() => RunAsync(async () =>
        {
            _items = await _rooms.ListAsync();
            OnPropertyChanged(nameof(Items));
        });

        private async Task RunAsync(Func<Task> action)
        {
            try { await action(); }
            catch (Exception exception)
            {
                _log.LogError("Room operation failed", exception);
                Message = "Could not complete the room action. Check the room number, status, and active orders.";
            }
        }
    }
}
