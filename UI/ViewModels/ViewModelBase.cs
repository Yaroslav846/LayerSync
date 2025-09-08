using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LayerSync.UI.ViewModels
{
    /// <summary>
    /// Base class for all ViewModels, implementing the INotifyPropertyChanged interface.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Method to raise the PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The name of the changed property. Automatically provided by the compiler.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

