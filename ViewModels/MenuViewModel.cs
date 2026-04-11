using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    public class MenuViewModel : ViewModelBase, IDataErrorInfo
    {
        private readonly MenuService _menuService;
        private readonly RelayCommand _beginAddCommand;
        private readonly RelayCommand _editCommand;
        private readonly RelayCommand _deleteCommand;
        private readonly RelayCommand _saveCommand;
        private readonly RelayCommand _cancelCommand;
        private bool _isBusy;
        private bool _isEditorOpen;
        private int? _editingItemId;
        private string _editorName = string.Empty;
        private string _editorCategory = string.Empty;
        private string _editorPrice = string.Empty;
        private bool _editorIsAvailable = true;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public MenuViewModel(MenuService menuService)
        {
            _menuService = menuService ?? throw new ArgumentNullException(nameof(menuService));
            MenuItems = new ObservableCollection<MenuItem>();
            MenuItems.CollectionChanged += OnMenuItemsCollectionChanged;

            _beginAddCommand = new RelayCommand(_ => OpenAddPanel(), _ => !IsBusy);
            _editCommand = new RelayCommand(item => OpenEditPanel(item as MenuItem), item => !IsBusy && item is MenuItem);
            _deleteCommand = new RelayCommand(async item => await DeleteAsync(item as MenuItem), item => !IsBusy && item is MenuItem);
            _saveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanSave);
            _cancelCommand = new RelayCommand(_ => CloseEditor(), _ => !IsBusy);
        }

        public event EventHandler MenuItemsChanged;

        public ObservableCollection<MenuItem> MenuItems { get; }

        public RelayCommand BeginAddCommand => _beginAddCommand;

        public RelayCommand EditCommand => _editCommand;

        public RelayCommand DeleteCommand => _deleteCommand;

        public RelayCommand SaveCommand => _saveCommand;

        public RelayCommand CancelCommand => _cancelCommand;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    NotifyCommandStates();
                }
            }
        }

        public bool IsEditorOpen
        {
            get => _isEditorOpen;
            private set
            {
                if (SetProperty(ref _isEditorOpen, value))
                {
                    OnPropertyChanged(nameof(EditorTitle));
                }
            }
        }

        public bool IsEditMode => _editingItemId.HasValue;

        public string EditorTitle => IsEditMode ? "Edit menu item" : "Add a menu item";

        public string EditorName
        {
            get => _editorName;
            set
            {
                if (SetProperty(ref _editorName, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    NotifyValidationStates();
                }
            }
        }

        public string EditorCategory
        {
            get => _editorCategory;
            set
            {
                if (SetProperty(ref _editorCategory, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    NotifyValidationStates();
                }
            }
        }

        public string EditorPrice
        {
            get => _editorPrice;
            set
            {
                if (SetProperty(ref _editorPrice, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    NotifyValidationStates();
                }
            }
        }

        public bool EditorIsAvailable
        {
            get => _editorIsAvailable;
            set => SetProperty(ref _editorIsAvailable, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (SetProperty(ref _statusMessage, value))
                {
                    OnPropertyChanged(nameof(HasStatusMessage));
                }
            }
        }

        public bool IsStatusError
        {
            get => _isStatusError;
            private set => SetProperty(ref _isStatusError, value);
        }

        public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

        public bool IsEmpty => MenuItems.Count == 0;

        public bool CanSave => !IsBusy && IsFormValid();

        public string Error => string.Empty;

        public async Task LoadAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                var items = await _menuService.GetMenuItemsAsync();
                MenuItems.Clear();
                foreach (var item in items)
                {
                    MenuItems.Add(item);
                }
            }, "Menu items could not be loaded.");
        }

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(EditorName):
                        if (string.IsNullOrWhiteSpace(EditorName))
                        {
                            return "Name is required.";
                        }

                        if (EditorName.Trim().Length > 100)
                        {
                            return "Name must be 100 characters or fewer.";
                        }

                        return string.Empty;

                    case nameof(EditorCategory):
                        if (string.IsNullOrWhiteSpace(EditorCategory))
                        {
                            return "Category is required.";
                        }

                        if (EditorCategory.Trim().Length > 50)
                        {
                            return "Category must be 50 characters or fewer.";
                        }

                        return string.Empty;

                    case nameof(EditorPrice):
                        if (string.IsNullOrWhiteSpace(EditorPrice))
                        {
                            return "Price is required.";
                        }

                        if (!TryParsePrice(EditorPrice, out var price))
                        {
                            return "Enter a valid price.";
                        }

                        if (price <= 0)
                        {
                            return "Price must be greater than zero.";
                        }

                        return string.Empty;

                    default:
                        return string.Empty;
                }
            }
        }

        private async Task SaveAsync()
        {
            TouchValidation();
            if (!IsFormValid())
            {
                SetStatus("Please fix the highlighted fields before saving.", true);
                return;
            }

            if (!TryParsePrice(EditorPrice, out var price))
            {
                SetStatus("Please enter a valid price before saving.", true);
                return;
            }

            var menuItem = new MenuItem
            {
                Id = _editingItemId.GetValueOrDefault(),
                Name = EditorName.Trim(),
                Category = EditorCategory.Trim(),
                Price = decimal.Round(price, 2),
                IsAvailable = EditorIsAvailable
            };

            await ExecuteBusyActionAsync(async () =>
            {
                var successMessage = IsEditMode
                    ? "Menu item updated successfully."
                    : "Menu item added successfully.";

                if (IsEditMode)
                {
                    await _menuService.UpdateMenuItemAsync(menuItem);
                }
                else
                {
                    await _menuService.AddMenuItemAsync(menuItem);
                }

                await RefreshAsync();
                CloseEditor(false);
                SetStatus(successMessage, false);
                RaiseMenuItemsChanged();
            }, "The menu item could not be saved.");
        }

        private async Task DeleteAsync(MenuItem menuItem)
        {
            if (menuItem == null)
            {
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                await _menuService.DeleteMenuItemAsync(menuItem.Id);
                if (_editingItemId == menuItem.Id)
                {
                    CloseEditor();
                }

                await RefreshAsync();
                SetStatus("Menu item deleted successfully.", false);
                RaiseMenuItemsChanged();
            }, "The menu item could not be deleted. Items linked to orders cannot be removed.");
        }

        private void OpenAddPanel()
        {
            ResetEditor();
            _editingItemId = null;
            EditorIsAvailable = true;
            IsEditorOpen = true;
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(EditorTitle));
            ClearStatus();
            NotifyCommandStates();
        }

        private void OpenEditPanel(MenuItem menuItem)
        {
            if (menuItem == null)
            {
                return;
            }

            _editingItemId = menuItem.Id;
            EditorName = menuItem.Name;
            EditorCategory = menuItem.Category;
            EditorPrice = menuItem.Price.ToString("0.##", CultureInfo.InvariantCulture);
            EditorIsAvailable = menuItem.IsAvailable;
            IsEditorOpen = true;
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(EditorTitle));
            ClearStatus();
            NotifyCommandStates();
        }

        private void CloseEditor(bool clearStatus = true)
        {
            ResetEditor();
            IsEditorOpen = false;
            if (clearStatus)
            {
                ClearStatus();
            }

            NotifyCommandStates();
        }

        private async Task RefreshAsync()
        {
            var items = await _menuService.GetMenuItemsAsync();
            MenuItems.Clear();
            foreach (var item in items)
            {
                MenuItems.Add(item);
            }
        }

        private async Task ExecuteBusyActionAsync(Func<Task> action, string fallbackMessage)
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;

            try
            {
                await action();
            }
            catch (DbUpdateException)
            {
                SetStatus(fallbackMessage, true);
            }
            catch (Exception)
            {
                SetStatus(fallbackMessage, true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ResetEditor()
        {
            _editingItemId = null;
            EditorName = string.Empty;
            EditorCategory = string.Empty;
            EditorPrice = string.Empty;
            EditorIsAvailable = true;
            TouchValidation();
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(EditorTitle));
        }

        private bool IsFormValid()
        {
            return string.IsNullOrEmpty(this[nameof(EditorName)]) &&
                   string.IsNullOrEmpty(this[nameof(EditorCategory)]) &&
                   string.IsNullOrEmpty(this[nameof(EditorPrice)]);
        }

        private void TouchValidation()
        {
            OnPropertyChanged(nameof(EditorName));
            OnPropertyChanged(nameof(EditorCategory));
            OnPropertyChanged(nameof(EditorPrice));
            NotifyCommandStates();
        }

        private void NotifyValidationStates()
        {
            OnPropertyChanged(nameof(EditorName));
            OnPropertyChanged(nameof(EditorCategory));
            OnPropertyChanged(nameof(EditorPrice));
            NotifyCommandStates();
        }

        private void NotifyCommandStates()
        {
            _beginAddCommand.RaiseCanExecuteChanged();
            _editCommand.RaiseCanExecuteChanged();
            _deleteCommand.RaiseCanExecuteChanged();
            _saveCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
        }

        private void SetStatus(string message, bool isError)
        {
            StatusMessage = message;
            IsStatusError = isError;
        }

        private void ClearStatus()
        {
            StatusMessage = string.Empty;
            IsStatusError = false;
        }

        private void RaiseMenuItemsChanged()
        {
            MenuItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static bool TryParsePrice(string value, out decimal price)
        {
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out price) ||
                   decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out price);
        }

        private void OnMenuItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
