using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Services;

namespace ServeX.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private int _menuItemsCount;
        private int _tableCount;
        private int _roomCount;
        private int _orderCount;

        public MainViewModel()
        {
            Menu = new MenuViewModel(new MenuService());
            Menu.MenuItemsChanged += async (sender, args) => await LoadSummaryAsync();
        }

        public string Title => "ServeX";

        public string WelcomeMessage => "Menu management and restaurant operations";

        public MenuViewModel Menu { get; }

        public int MenuItemsCount
        {
            get => _menuItemsCount;
            private set
            {
                if (SetProperty(ref _menuItemsCount, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public int TableCount
        {
            get => _tableCount;
            private set
            {
                if (SetProperty(ref _tableCount, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public int OrderCount
        {
            get => _orderCount;
            private set
            {
                if (SetProperty(ref _orderCount, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public int RoomCount
        {
            get => _roomCount;
            private set
            {
                if (SetProperty(ref _roomCount, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public string Summary => string.Format(
            "Menu Items: {0}   Tables: {1}   Rooms: {2}   Orders: {3}",
            MenuItemsCount,
            TableCount,
            RoomCount,
            OrderCount);

        public async Task LoadAsync()
        {
            await Menu.LoadAsync();
            await LoadSummaryAsync();
        }

        private async Task LoadSummaryAsync()
        {
            using (var context = new ServeXDbContext())
            {
                MenuItemsCount = await context.MenuItems.CountAsync();
                TableCount = await context.Tables.CountAsync();
                RoomCount = await context.Rooms.CountAsync();
                OrderCount = await context.Orders.CountAsync();
            }
        }
    }
}
