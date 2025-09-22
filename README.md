# Stationeers Inventory Tweaks

# Description

This mod is designed to improve the inventory management aspect of Stationeers.

Current features include:
* Combine items into existing stacks when moving via Smart Stow (default G)
* Add keybind for hitting "Next" on your current active item (for example, the Advanced Tablet)
* Fixes weird gaps between inventory windows
* Fixes tracking of original slots so that items return to their proper place via Smart Stow.
* Ability to lock slots to an item type (e.g. Hand Drill, Cable Coil, etc.)

# Installation (Steam Workshop)

This mod supports installation via the Steam Workshop using StationeersLaunchPad.

* Install [StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad). 
* Subscribe to [Inventory Tweaks](https://steamcommunity.com/workshop/filedetails/?id=3573053238) on the Steam Workshop, then start the game.
* The mod will be automatically enabled and available from the Workshop button.
* Click on the mod in the Workshop menu to edit the configuration.

# Installation (Manual)

* If you don't have BepInEx installed, download the lastest 5.x 64 bit version available at https://github.com/BepInEx/BepInEx/releases and follow the [BepInEx installation instructions](https://docs.bepinex.dev/articles/user_guide/installation/index.html) to install.
     - Simplified instructions: Extract into your Stationeers folder where the rocketstation.exe file is located.
     - Start the game once to finish installing BepInEx and verify that the plugins folder was created in \Stationeers\BepInEx\plugins. If so, the BepInEx installation is completed.
* Download the lastest release from https://github.com/theit8514/Stationeers-InventoryTweaks/releases/ page.
* Install the mod:
  * If using StationeersLaunchPad, extract the mod to 'Documents\My Games\Stationeers\mods\'
  * If using plain BepInEx, extract the mod dll and pdb files to the \Stationeers\BepInEx\plugins folder.
* Start the game.

# Usage

This mod adds several new keybinds. All keybinds can be changed from the Controls setting page.

The "Z" keybind presses the "Next" button on your currently held item.
This is useful to switch cartridges on an Advanced Tablet via keybind.

The middle mouse button will lock or unlock the currently hovered slot.
Locked slots can only contain the type of item that it is locked to.
For example, if you want only cable coil to go into a tool belt and no other items,
place a cable coil in each slot and middle click each one.
To unlock a slot, remove the item and middle click the empty slolt to remove the lock.

# Configuration (StationeersLaunchPad)

Configuration is available by opening the Workshop menu and selecting the Inventory Tweaks mod.

# Configuration (Manual)

This mod creates a configuration file which can enable certain options.
The configuration file is \Stationeers\BepInEx\config\InventoryTweaks.cfg and will be created on first load of the mod.

* EnableRewriteOpenSlots (default: false): This will enable or disable the Rewrite Open Slots feature.
  If enabled, it will rewrite your save data for Open Slots so that tablets, tools, etc will remain open when loading your save.
  
  This option rewrites the StringHash (an integer value) with the ReferenceId (a long value) of the slot that is open.
  When loading it will attempt to open the windows matching the StringHash first, then the ReferenceId.
  There should not be a problem with loading a save that has been modified this way, as Stationeers ignores unknown Open Slot StringHash values.
* EnableSaveLockedSlots (default: false): This will enable or disable the save feature of Locked Slots.
  If enabled, this will save the locked slots into a new InventoryTweaks.xml file in your game save.
  
  This does not modify the existing world save, only creates a new file in the world folder as the game is saving.

# Future Plans

Thoughts on additional features:
* Add refill hand from inventory on construction/placement and sort items to inventory on deconstruction.
* Implement StationeersLaunchPad support for mod configuration.

# Contributions

If you want to contribute with this mod, feel free to create a pull request.

# Notes

AI was used to generate the preview/thumbnail for the Steam Workshop.