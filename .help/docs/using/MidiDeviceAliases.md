# MIDI Device Aliases

MIDI device names are fragile. A second unit of the same controller, a different operating system or a USB hub that renumbers its ports all change the name, and every `[MidiInput]` and `[MidiOutput]` that stored it stops matching. A **device alias** is a stable name the project stores instead, such as `TiXL:ApcMini` or `TiXL:Keyboard`. Which physical device an alias means is decided per machine in **Settings » Midi**.

An alias is just a device name that starts with `TiXL:`. Everything that takes a device name accepts it: the `Device` input of the MIDI operators, teaching, recorded sessions and the exported Player.

## Mapping aliases

Open **Settings » Midi**. The **Device Aliases** list shows every alias, each with the device it stands for on this machine:

- Pick a device from the combo. Devices that are not connected right now are shown as `(NOT FOUND)` and stay mapped, so a mapping made at home survives until the controller is plugged in at the venue.
- Aliases that the loaded projects reference but that are not mapped yet appear as **Not Set**. Pick a device to map them.
- Drag the grip to reorder. Teaching a `[MidiInput]` stores the *first* alias mapped to the device the message came from, so the order decides which alias a new op gets when several aliases share one controller.
- **Add Alias...** creates an alias before any project uses it.
- The edit button on a row renames the alias in all loaded projects. Typing or picking the name of another alias merges the two, which fixes typos and case mismatches such as `Apc40` next to `APC40`.

The mapping is saved as `midiAliases.json` in the settings folder (the folder icon at the bottom of the settings window opens it), not in the project, so shared projects stay portable. Copy that file to another computer's settings folder to take a mapping along.

## Replacing device-specific names

Below the aliases, **Device-Specific Names** lists every physical device name found in the loaded projects, with how often it is used. Hover a row to see the operators that reference it.

**Use Alias...** creates an alias for that name, maps it to the device, and replaces the name in every input that uses it. The replacement is one undo step and the modified symbols are saved. Inputs in read-only packages and inputs driven by a connection are skipped; the dialog says how many.

## What an operator shows

- An alias that is not mapped on this machine matches no device. The operator shows a warning naming the alias and pointing to the settings.
- An alias mapped to a device that is not connected shows the usual "not connected" warning, naming both.
- The device dropdown of a MIDI operator lists the aliases first, then the connected devices.

## Exported Players

A Player export includes a copy of `midiAliases.json` next to the executable. To remap a show on another computer, edit that file or replace it with the one from that computer's settings folder. It is a plain list:

```json
[
  { "Alias": "TiXL:ApcMini", "ProductName": "APC MINI" },
  { "Alias": "TiXL:Keyboard", "ProductName": "Arturia KeyStep" }
]
```

Single aliases can also be set on the command line, which is handy in launch scripts:

```
Player --midi-alias ApcMini="APC mini mk2" Keyboard="KeyStep 37"
```

The Player logs the effective table at startup.
