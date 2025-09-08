using Autodesk.AutoCAD.Colors;
using System.Windows.Media;
using LayerSync.Core;
using Autodesk.AutoCAD.ApplicationServices;
using System.Windows.Input;
using Color = Autodesk.AutoCAD.Colors.Color; // Added for ShowAlertDialog

namespace LayerSync.UI.ViewModels
{
    /// <summary>
    /// ViewModel representing a single layer in the list.
    /// </summary>
    public class LayerItemViewModel : ViewModelBase
    {
        private string _name;
        private bool _isOn;
        private bool _isFrozen;
        private bool _isCurrent;
        private Color _acadColor;
        private SolidColorBrush _displayBrush;
        private bool _isUpdatingFromAcad = false; // Flag to prevent recursive updates
        private bool _isEditing;
        private string _originalName;

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                if (_isEditing)
                {
                    _originalName = Name;
                }
                OnPropertyChanged();
                (CommitRenameCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand CommitRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand StartEditCommand { get; }

        public LayerItemViewModel()
        {
            CommitRenameCommand = new RelayCommand(ExecuteCommitRename, CanExecuteCommitRename);
            CancelRenameCommand = new RelayCommand(ExecuteCancelRename);
            StartEditCommand = new RelayCommand(p => IsEditing = true);
        }

        private bool CanExecuteCommitRename(object obj)
        {
            return IsEditing;
        }

        private void ExecuteCommitRename(object newNameObj)
        {
            string newName = newNameObj as string;
            if (string.IsNullOrWhiteSpace(newName) || newName == _originalName)
            {
                IsEditing = false;
                return;
            }

            if (AcadService.RenameLayer(_originalName, newName))
            {
                Name = newName;
                IsEditing = false;
            }
        }

        private void ExecuteCancelRename(object obj)
        {
            Name = _originalName;
            IsEditing = false;
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                OnPropertyChanged();
            }
        }

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value || _isUpdatingFromAcad) return;
                _isOn = value;
                OnPropertyChanged();
                // Update the layer in AutoCAD if the change came from the UI
                AcadService.UpdateLayerProperty(Name, ltr => ltr.IsOff = !value);
            }
        }

        public bool IsFrozen
        {
            get => _isFrozen;
            set
            {
                if (_isFrozen == value || _isUpdatingFromAcad) return;

                // --- FIX: PREVENT FREEZING THE CURRENT LAYER ---
                // If the user tries to set IsFrozen to true (value == true) and the layer is current
                if (value && IsCurrent)
                {
                    // Show an informative message to the user
                    Application.ShowAlertDialog("The current layer cannot be frozen.");
                    // Revert the checkbox state by notifying the UI to re-read the original value.
                    OnPropertyChanged();
                    return;
                }

                // Also check for layer "0"
                if (value && Name.Equals("0", System.StringComparison.OrdinalIgnoreCase))
                {
                    Application.ShowAlertDialog("Layer \"0\" cannot be frozen.");
                    OnPropertyChanged();
                    return;
                }

                _isFrozen = value;
                OnPropertyChanged();
                AcadService.UpdateLayerProperty(Name, ltr => ltr.IsFrozen = value);
            }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent == value) return;
                _isCurrent = value;
                OnPropertyChanged();
            }
        }

        public Color AcadColor
        {
            get => _acadColor;
            set
            {
                if (_acadColor == value) return;
                _acadColor = value;
                // Use a try-catch as color conversion can sometimes fail with special colors
                try
                {
                    DisplayBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(value.ColorValue.R, value.ColorValue.G, value.ColorValue.B));
                }
                catch
                {
                    // Default to black if conversion fails
                    DisplayBrush = new SolidColorBrush(System.Windows.Media.Colors.Black);
                }
                OnPropertyChanged();
            }
        }

        public SolidColorBrush DisplayBrush
        {
            get => _displayBrush;
            private set { _displayBrush = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Updates ViewModel properties based on data from AutoCAD.
        /// </summary>
        public void UpdateFrom(LayerItemViewModel source)
        {
            _isUpdatingFromAcad = true; // Set the flag
            try
            {
                IsOn = source.IsOn;
                IsFrozen = source.IsFrozen;
                AcadColor = source.AcadColor;
                IsCurrent = source.IsCurrent;
            }
            finally
            {
                _isUpdatingFromAcad = false; // Reset the flag
            }
        }
    }
}

