# Stationeers Example Mod (Code-only with BepInEx)

This serves as a template for a code-only mod using BepInEx.

## Features

- Simple code base
- No need for Unity or Visual Studio
- Builds on Linux
- Github workflow to build releases automatically
- Uses Krafs.Publicizer to access private members

## Get started with your own mod

- Fork and clone the project
- Make sure Python is installed (is used to generate VersionInfo.cs)
- Run `dotnet build` to build the mod
- Copy the resulting dll from `bin/Debug/net46` to your `BepInEx/plugins` folder

**Before** publishing your mod change the following names in the source code:
- `Main.cs:5`: `namespace ExampleMod`
- `Main.cs:10`: `pluginGuid = "aproposmath-stationeers-example-mod";`
- `Main.cs:11`: `pluginName = "ExampleMod";`
- `Main.csproj:7`: `ExampleMod`
- `Main.csproj:25`: `ExampleMod` (this must match the namespace name in `Main.cs`)

## Release a new Version

Just create a new git tag and push it to GitHub. The workflow will build the release automatically.

```bash
git tag v1.2.3
git push origin v1.2.3
```

## Installation

This mode requires [BepInEx](https://github.com/BepInEx/BepInEx).
Download the latest release from the [releases page](https://github.com/aproposmath/stationeers-example-mod/releases) and put it into your `BepInEx/plugins` folder.
