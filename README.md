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

Before publishing your mod, make sure to change **all** occurences of "ExampleMod" to your mod name and change the **GUID** in `Main.cs` to a new one (you can generate one [here](https://www.guidgen.com/)).

```bash
$ grep -ri ExampleMode
create_version_info.py:namespace ExampleMod {{
Main.cs:namespace ExampleMod
Main.cs:    public class ExampleModPlugin : BaseUnityPlugin
Main.cs:        public const string pluginName = "ExampleMod";
Main.csproj:    <AssemblyName>ExampleMod</AssemblyName>
Patches.cs:namespace ExampleMod
README.md:Before publishing your mod, make sure to change all occurences of "ExampleMod" to your mod name.
```


## Release a new Version

Just create a new git tag and push it to GitHub. The workflow will build the release automatically.

```bash
git tag v1.2.3
git push origin v1.2.3
```

## Installation

This mode requires [BepInEx](https://github.com/BepInEx/BepInEx).
Download the latest release from the [releases page](https://github.com/aproposmath/stationeers-example-mod/releases) and put it into your `BepInEx/plugins` folder.
