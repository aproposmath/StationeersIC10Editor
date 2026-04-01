# Stationeers IC10 Editor

A custom IC10 editor for Stationeers with additional features.

## Features

- Undo/Redo functionality
- Common keyboard shortcuts (see below)
- Multiline selection
- Syntax highlighting
- Tooltips for IC10 instructions
- Error messages as tooltips
- Ctrl+Click on hash number or structure name to open Stationpedia page
- VIM bindings support (can be toggled on/off in settings)
- UI scaling support

## Keyboard Shortcuts

Here is a list of the available keyboard shortcuts:

- **Arrow Keys**: Move caret
- **Home/End**: Move caret to start/end of line
- **Page Up/Down**: Move caret up/down by 20 lines
- **Shift + Arrow**: Select text while moving caret
- **Ctrl + Q**: Quit
- **Ctrl + S**: Save
- **Ctrl + E**: Save + export code to IC chip + close
- **Ctrl + Z**: Undo
- **Ctrl + Y**: Redo
- **Ctrl + C**: Copy selected code
- **Ctrl + V**: Paste code from clipboard
- **Ctrl + A**: Select all code
- **Ctrl + X**: Cut selected code
- **Ctrl + Arrow**: Move caret by word
- **Ctrl + Click**: Open Stationpedia page of the word at the cursor

---

### Notes

- Closing the editor via **Ctrl + Q** key or the **Cancel** button will not ask for confirmation. However, you can always reopen the editor and use **Ctrl + Z** to undo the cancellation and restore the previous state.

---

### VIM Mode - Supported Commands

#### Movements (with optional number prefix):
- `h`, `j`, `k`, `l`, `w`, `b`, `0`, `$`, `gg`, `G`

#### Editing (with optional number and movement or search):
- `i`, `I`, `a`, `A`, `c`, `C`, `d`, `D`, `dd`, `o`, `O`, `x`, `y`, `yy`, `p`, `~`, `<<`, `>>`, `u`, `Ctrl+r`

#### Search:
- `f`, `t`, `gf`, `#`, `*`, `/`, `?`, `n`, `N`

#### Other:
- `.`, `;`, `:w`, `:wq`, `:q`

#### Notes:
- `gf` opens the Stationpedia page of the hash/name at the cursor.


## Installation

This mod requires [StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad).

To install it, you can either

- subscribe the [Stationeers IC10 Editor](https://steamcommunity.com/sharedfiles/filedetails/?id=3592775931) mod on the Steam Workshop.

**OR**

- [download the latest release .dll file](https://github.com/aproposmath/StationeersIC10Editor/releases) and put it into your `BepInEx/plugins` folder.
