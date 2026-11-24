using ServeX.Models;

namespace ServeX.ViewModels
{
    public class TableFloorItemViewModel : ViewModelBase
    {
        private bool _isSelected;
        private int _floorSlot;
        private TableStatus _status;

        public const int CellWidth = 94;
        public const int CellHeight = 74;

        public int FloorSlot
        {
            get => _floorSlot;
            set
            {
                if (SetProperty(ref _floorSlot, value))
                {
                    OnPropertyChanged(nameof(FloorLeft));
                    OnPropertyChanged(nameof(FloorTop));
                }
            }
        }

        public int FloorLeft => FloorSlot % TableFloorLayout.Columns * CellWidth + 4;

        public int FloorTop => FloorSlot / TableFloorLayout.Columns * CellHeight + 4;

        public int Id { get; set; }

        public string TableNumber { get; set; }

        public TableArea Area { get; set; }

        public TableStatus Status
        {
            get => _status;
            set
            {
                if (!SetProperty(ref _status, value)) return;
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(TileDescription));
                OnPropertyChanged(nameof(ServiceCaption));
            }
        }

        public string CustomerName { get; set; }

        public bool HasActiveOrder { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string Heading => string.Format("Table {0}", TableNumber);

        public string TileDescription => $"{TableNumber} · {StatusLabel}";

        public string StatusLabel => Status.ToString();

        public string CustomerDisplayName => string.IsNullOrWhiteSpace(CustomerName)
            ? string.Empty
            : CustomerName.Trim();

        public string ServiceCaption => HasActiveOrder
            ? "Active service"
            : Status == TableStatus.Cleaning ? "Needs cleaning" : "Ready for service";
    }
}
