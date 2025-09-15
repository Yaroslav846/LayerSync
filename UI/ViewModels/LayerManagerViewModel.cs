using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using LayerSync.Core;
using LayerSync.UI.Core;
using Autodesk.AutoCAD.Windows;
using ColorDialog = Autodesk.AutoCAD.Windows.ColorDialog;

namespace LayerSync.UI.ViewModels
{
    /// <summary>
    /// Main ViewModel for the layer manager window.
    /// </summary>
    public class LayerManagerViewModel : ViewModelBase
    {
        private ObservableCollection<LayerItemViewModel> _layers;
        private LayerItemViewModel _selectedLayer;
        private string _searchText = "";
        private List<LayerItemViewModel> _allLayers;


        public ObservableCollection<LayerItemViewModel> Layers
        {
            get => _layers;
            set { _layers = value; OnPropertyChanged(); }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterLayers();
            }
        }

        public LayerItemViewModel SelectedLayer
        {
            get => _selectedLayer;
            set
            {
                _selectedLayer = value;
                OnPropertyChanged();

                // --- ВОТ ИЗМЕНЕНИЕ: Включаем подсветку объектов ---
                AcadService.HighlightEntitiesOnLayer(_selectedLayer?.Name);

                ((RelayCommand)SetCurrentCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ChangeColorCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SelectByColorCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand SetCurrentCommand { get; }
        public ICommand ChangeColorCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SelectByColorCommand { get; }
        public ICommand NewLayerCommand { get; }
        public ICommand CreateLayerCommand { get; }
        public ICommand CancelNewLayerCommand { get; }
        public ICommand DeleteLayersCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        private bool _isNewLayerModeActive;
        public bool IsNewLayerModeActive
        {
            get => _isNewLayerModeActive;
            set { _isNewLayerModeActive = value; OnPropertyChanged(); }
        }

        private string _newLayerName;
        public string NewLayerName
        {
            get => _newLayerName;
            set { _newLayerName = value; OnPropertyChanged(); }
        }

        public List<LayerItemViewModel> SelectedItems { get; } = new List<LayerItemViewModel>();

        public LayerManagerViewModel()
        {
            Layers = new ObservableCollection<LayerItemViewModel>();
            _allLayers = new List<LayerItemViewModel>();
            SetCurrentCommand = new RelayCommand(ExecuteSetCurrent, CanExecuteSetCurrent);
            ChangeColorCommand = new RelayCommand(ExecuteChangeColor, CanExecuteChangeColor);
            RefreshCommand = new RelayCommand(ExecuteRefresh);
            SelectByColorCommand = new RelayCommand(ExecuteSelectByColor, CanExecuteSelectByColor);
            NewLayerCommand = new RelayCommand(p => IsNewLayerModeActive = true);
            CreateLayerCommand = new RelayCommand(ExecuteCreateLayer, p => !string.IsNullOrWhiteSpace(NewLayerName));
            CancelNewLayerCommand = new RelayCommand(p => IsNewLayerModeActive = false);
            DeleteLayersCommand = new RelayCommand(ExecuteDeleteLayers, p => SelectedItems.Count > 0);
            ToggleThemeCommand = new RelayCommand(ExecuteToggleTheme);


            LoadLayers();
            AcadService.SubscribeToAcadEvents();
            AcadService.LayerChanged += OnAcadLayerChanged;
        }

        private bool CanExecuteSelectByColor(object obj)
        {
            return SelectedLayer != null;
        }

        private void ExecuteToggleTheme(object parameter)
        {
            if (parameter is System.Windows.Window window)
            {
                ThemeManager.ToggleTheme(window);
            }
        }

        private void ExecuteSelectByColor(object obj)
        {
            if (SelectedLayer != null)
            {
                AcadService.HighlightEntitiesByColor(SelectedLayer.AcadColor);
            }
        }

        public void UpdateSelection(System.Collections.IList selectedItems)
        {
            SelectedItems.Clear();
            foreach (LayerItemViewModel item in selectedItems)
            {
                SelectedItems.Add(item);
            }
            (DeleteLayersCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ExecuteCreateLayer(object obj)
        {
            if (AcadService.CreateLayer(NewLayerName))
            {
                NewLayerName = string.Empty;
                IsNewLayerModeActive = false;
                LoadLayers();
            }
        }

        private void ExecuteDeleteLayers(object obj)
        {
            var layerNames = SelectedItems.Select(i => i.Name).ToList();
            AcadService.DeleteLayers(layerNames);
            LoadLayers();
        }

        public void SetFrozenStateForSelection(LayerItemViewModel clickedItem, bool newState)
        {
            if (SelectedItems.Count <= 1 || !SelectedItems.Contains(clickedItem))
            {
                clickedItem.IsFrozen = newState;
                return;
            }

            var layerNames = SelectedItems.Select(i => i.Name).ToList();
            AcadService.BulkUpdateLayerProperties(layerNames, ltr => ltr.IsFrozen = newState);

            foreach (var item in SelectedItems)
            {
                item.SetIsFrozenFromManager(newState);
            }
        }

        public void SetOnStateForSelection(LayerItemViewModel clickedItem, bool newState)
        {
            if (SelectedItems.Count <= 1 || !SelectedItems.Contains(clickedItem))
            {
                clickedItem.IsOn = newState;
                return;
            }

            var layerNames = SelectedItems.Select(i => i.Name).ToList();
            AcadService.BulkUpdateLayerProperties(layerNames, ltr => ltr.IsOff = !newState);

            foreach (var item in SelectedItems)
            {
                item.SetIsOnFromManager(newState);
            }
        }

        private void FilterLayers()
        {
            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? _allLayers
                : _allLayers.Where(l => l.Name.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0);

            Layers.Clear();
            foreach (var layer in filtered)
            {
                Layers.Add(layer);
            }
        }

        /// <summary>
        /// Loads or reloads the list of layers.
        /// </summary>
        private void LoadLayers()
        {
            _allLayers = AcadService.GetAllLayers();
            FilterLayers();
        }

        private void OnAcadLayerChanged(object sender, string layerName)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var updatedLayerData = AcadService.GetAllLayers().FirstOrDefault(l => l.Name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase));
                var vmToUpdate = _allLayers.FirstOrDefault(l => l.Name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase));

                if (vmToUpdate != null && updatedLayerData != null)
                {
                    vmToUpdate.UpdateFrom(updatedLayerData);
                    FilterLayers();
                }
                else
                {
                    LoadLayers();
                }
            });
        }

        // --- Command Logic ---

        private bool CanExecuteSetCurrent(object obj) => SelectedLayer != null && !SelectedLayer.IsCurrent;
        private void ExecuteSetCurrent(object obj)
        {
            AcadService.SetCurrentLayer(SelectedLayer.Name);
            foreach (var layer in Layers)
            {
                layer.IsCurrent = (layer.Name == SelectedLayer.Name);
            }
        }

        private bool CanExecuteChangeColor(object obj) => SelectedLayer != null;
        private void ExecuteChangeColor(object obj)
        {
            ColorDialog acdColorDialog = new ColorDialog();
            acdColorDialog.IncludeByBlockByLayer = false;

            acdColorDialog.Color = SelectedLayer.AcadColor;

            if (acdColorDialog.ShowDialog() == DialogResult.OK)
            {
                var newColor = acdColorDialog.Color;
                AcadService.UpdateLayerProperty(SelectedLayer.Name, ltr => ltr.Color = newColor);
                SelectedLayer.AcadColor = newColor;
            }
        }

        private void ExecuteRefresh(object obj)
        {
            LoadLayers();
        }

        /// <summary>
        /// Method to clean up resources, called when the window is closed.
        /// </summary>
        public void Cleanup()
        {
            // --- ВОТ ИЗМЕНЕНИЕ: Снимаем подсветку при закрытии ---
            AcadService.HighlightEntitiesOnLayer(null);
            AcadService.UnsubscribeFromAcadEvents();
            AcadService.LayerChanged -= OnAcadLayerChanged;
        }
    }
}

