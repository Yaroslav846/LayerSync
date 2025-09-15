# LayerSync for AutoCAD

## Overview

LayerSync is a plugin for Autodesk AutoCAD designed to provide a more intuitive and efficient way to manage layers. It presents layers in a clear, modern UI and allows for quick modifications, real-time search, and management of objects on layers.

This tool is built using C# and WPF, following the MVVM design pattern for a clean and maintainable codebase.

## Features

*   **Modern Layer Display**: View all layers in a clean and organized list view.
*   **Property Management**: Quickly toggle layer properties directly from the list:
    *   Turn layers On/Off.
    *   Freeze/Thaw layers.
    *   Change layer colors using the standard AutoCAD color dialog.
    *   Set a layer as the current layer.
*   **Live Search**: Instantly filter the layer list by typing in the search box.
*   **In-Place Renaming**: Double-click on any layer name to edit it directly in the list.
*   **Layer Creation & Deletion**: Easily create new layers or delete existing ones from the toolbar.
*   **Object Counter**: See the number of objects on each layer at a glance in the "Count" column.
*   **Move Objects to Layer**: A simple workflow to move selected objects to a new layer:
    1.  Select a target layer in the list.
    2.  Click the "Move Selection" button (➡️).
    3.  The plugin prompts you to select objects in the drawing.
    4.  Selected objects are moved to the target layer, and the object counts are automatically updated.
*   **Theme Switching**: Toggle between a standard Light theme and an eye-friendly Dark theme using the theme button (🌗) on the toolbar.
*   **Real-time Updates**: The layer list automatically refreshes to reflect any changes made in the standard AutoCAD Layer Properties Manager.

## Dependencies

To build and run this plugin, you will need:

*   **Microsoft .NET 8 SDK** (or newer).
*   **Autodesk AutoCAD 2026** (or a compatible version).
*   **Visual Studio 2022** (or another compatible IDE).

The project references the following core AutoCAD assemblies, which must be available from your AutoCAD installation:
*   `accoremgd.dll`
*   `acdbmgd.dll`
*   `acmgd.dll`

The `.csproj` file is configured to find these files in the default installation path (`C:\Program Files\Autodesk\AutoCAD 2026\`). If your installation path is different, you will need to update these references.

## Building and Loading

### 1. Building the Plugin

1.  **Clone the repository.**
2.  **Open the solution** (`LayerSync.sln`) in Visual Studio.
3.  **Restore NuGet packages** (if necessary, though there are no external packages).
4.  **Build the project** using the `Build > Build Solution` menu item or by pressing `Ctrl+Shift+B`.
5.  The compiled plugin will be located at: `bin\Debug\net8.0-windows10.0.17763.0\LayerSync.dll`

Alternatively, you can build from the command line by running `dotnet build` in the root directory of the project.

### 2. Loading into AutoCAD

1.  **Launch Autodesk AutoCAD.**
2.  **Open a drawing.**
3.  **Run the `NETLOAD` command** in the AutoCAD command line.
4.  **Navigate to the build output path** (mentioned above) and select `LayerSync.dll`.
5.  The plugin is now loaded for the current session.

### 3. Running the Plugin

1.  **Run the `LAYERSYNC` command** in the AutoCAD command line.
2.  The LayerSync window will appear.
