using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using LayerSync.Core;
using Autodesk.AutoCAD.Windows;
using Microsoft.UI.Dispatching;

namespace LayerSync.UI.ViewModels
{
    public class LayerManagerViewModel : ViewModelBase
    {
        private ObservableCollection<LayerItemViewModel> _layers;
        private LayerItemViewModel _selectedLayer;
        private string _searchText = "";
        private List<LayerItemViewModel> _allLayers;

        public DispatcherQueue Dispatcher { get; set; }

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

                AcadService.HighlightEntitiesOnLayer(_selectedLayer?.Name);

                ((RelayCommand)SetCurrentCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ChangeColorCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SelectByColorCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteLayersCommand).RaiseCanExecuteChanged();
                ((RelayCommand)MoveSelectionToLayerCommand).RaiseCanExecuteChanged();
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
        public ICommand ToggleThemeCommand { get; set; }
        public ICommand MoveSelectionToLayerCommand { get; }

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
            NewLayerCommand = new RelayCommand(() => IsNewLayerModeActive = true);
            CreateLayerCommand = new RelayCommand(ExecuteCreateLayer, () => !string.IsNullOrWhiteSpace(NewLayerName));
            CancelNewLayerCommand = new RelayCommand(() => IsNewLayerModeActive = false);
            DeleteLayersCommand = new RelayCommand(ExecuteDeleteLayers, () => SelectedItems.Count > 0);
            MoveSelectionToLayerCommand = new RelayCommand(ExecuteMoveSelectionToLayer, CanExecuteMoveSelectionToLayer);

            // ToggleThemeCommand is set by the view.

            LoadLayers();
            AcadService.SubscribeToAcadEvents();
            AcadService.LayerChanged += OnAcadLayerChanged;
        }

        private bool CanExecuteSelectByColor(object obj) => SelectedLayer != null;
        private void ExecuteSelectByColor(object obj)
        {
            if (SelectedLayer != null)
            {
                AcadService.HighlightEntitiesByColor(SelectedLayer.AcadColor);
            }
        }

        private bool CanExecuteMoveSelectionToLayer(object obj) => SelectedLayer != null;
        private void ExecuteMoveSelectionToLayer(object obj)
        {
            if (SelectedLayer == null) return;
            AcadService.MoveSelectedObjectsToLayer(SelectedLayer.Name);
            LoadLayers();
        }

        public void UpdateSelection(System.Collections.IList selectedItems)
        {
            SelectedItems.Clear();
            if (selectedItems != null)
            {
                foreach (LayerItemViewModel item in selectedItems)
                {
                    SelectedItems.Add(item);
                }
            }
            ((RelayCommand)DeleteLayersCommand).RaiseCanExecuteChanged();
        }

        private void ExecuteCreateLayer()
        {
            if (AcadService.CreateLayer(NewLayerName))
            {
                NewLayerName = string.Empty;
                IsNewLayerModeActive = false;
                LoadLayers();
            }
        }

        private void ExecuteDeleteLayers()
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
                : _allLayers.Where(l => l.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            Layers.Clear();
            foreach (var layer in filtered)
            {
                Layers.Add(layer);
            }
        }

        private void LoadLayers()
        {
            var layers = AcadService.GetAllLayers();
            var counts = AcadService.GetObjectCountsForAllLayers();
            foreach (var layer in layers)
            {
                if (counts.TryGetValue(layer.Name, out int count))
                {
                    layer.ObjectCount = count;
                }
            }
            _allLayers = layers;
            FilterLayers();
        }

        private void OnAcadLayerChanged(object sender, string layerName)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                var updatedLayerData = AcadService.GetAllLayers().FirstOrDefault(l => l.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase));
                var vmToUpdate = _allLayers.FirstOrDefault(l => l.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase));
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
            var acdColorDialog = new ColorDialog
            {
                IncludeByBlockByLayer = false,
                Color = SelectedLayer.AcadColor
            };
            if (acdColorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var newColor = acdColorDialog.Color;
                AcadService.UpdateLayerProperty(SelectedLayer.Name, ltr => ltr.Color = newColor);
                SelectedLayer.AcadColor = newColor;
            }
        }

        private void ExecuteRefresh()
        {
            LoadLayers();
        }

        public void Cleanup()
        {
            AcadService.HighlightEntitiesOnLayer(null);
            AcadService.UnsubscribeFromAcadEvents();
            AcadService.LayerChanged -= OnAcadLayerChanged;
        }
    }
}
