using ServeX.Models;

namespace ServeX.ViewModels
{
    public class TableFloorItemViewModel : ViewModelBase
    {
        private bool _isSelected;

        public int Id { get; set; }

        public string TableNumber { get; set; }

        public TableArea Area { get; set; }

        public TableStatus Status { get; set; }

        public string CustomerName { get; set; }

        public bool HasActiveOrder { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string Heading => string.Format("Table {0}", TableNumber);

        public string StatusLabel => Status.ToString();

        public string CustomerDisplayName => string.IsNullOrWhiteSpace(CustomerName)
            ? "Atithi"
            : CustomerName.Trim();

        public string ServiceCaption => HasActiveOrder
            ? "Active service"
            : Status == TableStatus.Cleaning ? "Needs cleaning" : "Ready for service";
    }
}
