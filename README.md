# LayerSync for AutoCAD

## Overview

LayerSync is a plugin for Autodesk AutoCAD designed to provide a more intuitive and efficient way to manage layers. It presents layers in a clear, modern UI and allows for quick modifications, real-time search, and in-place renaming.

This tool is built using C# and WPF, following the MVVM design pattern for a clean and maintainable codebase.

## Features

*   **Modern Layer Display**: View all layers in a clean and organized list view.
*   **Property Management**: Quickly toggle layer properties directly from the list:
    *   Turn layers On/Off.
    *   Freeze/Thaw layers.
    *   Change layer colors using the standard AutoCAD color dialog.
    *   Set a layer as the current layer.
*   **Live Search**: Instantly filter the layer list by typing in the search box. The list updates in real-time as you type.
*   **In-Place Renaming**:
    *   Simply double-click on any layer name to edit it directly in the list.
    *   Press `Enter` to confirm the new name or `Escape` to cancel the edit.
    *   Includes validation to prevent duplicate or invalid layer names.
*   **Real-time Updates**: The layer list automatically refreshes to reflect any changes made in the standard AutoCAD Layer Properties Manager.

## How to Use

1.  **Launch the Tool**: Use the command defined in `Main/Commands.cs` (e.g., `LAYERSYNC`) in the AutoCAD command line to open the LayerSync window.
2.  **Search for Layers**: Type in the search box at the top of the window to filter the layers by name.
3.  **Modify Properties**:
    *   Use the checkboxes in the `On` and `Frozen` columns to toggle those properties.
    *   Select a layer and click the "Change Color" button to open the color dialog.
    *   Select a layer and click the "Set Current" button to make it the active layer.
4.  **Rename a Layer**:
    *   Double-click the name of the layer you wish to rename.
    *   A text box will appear. Type the new name.
    *   Press `Enter` to save the change or `Escape` to cancel.
