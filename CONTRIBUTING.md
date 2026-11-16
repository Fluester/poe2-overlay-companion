# Contributing

Contributions are welcome!

## Adding a Language
1. Add the enum value in `Models/BuildModels.cs` (`AppLanguage`).
2. Extend `Localization/Loc.cs` and `Localization/PoeTermLocalizer.cs`.
3. Add a header language selector pill in `MainWindow.xaml` and hook it in `MainWindow.xaml.cs`.

## Updating the Passive Tree Dataset
`Assets/poe2_tree_05.bin.gz` is packed in compact binary format from Path of Exile 2 passive tree coordinates (`skill`, `stringId`, `name`, `kind`, `x`, `y`, `stats`, `ascendancyName`, `connections`).

## Building Locally
```powershell
dotnet build Poe2OverlayCompanion.csproj -c Release
```
