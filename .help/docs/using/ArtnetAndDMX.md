# Notes on Implementing ArtNet Support

## Preface

Based on the support from @banidu, @nr4, @messy, and @yoda, we implemented and tested how to control lights with TiXL v4.0.3. This allows you to combine visuals with tightly synchronized and controlled stage light effects.

## What You Need to Know About ArtNet and DMX

If you're already familiar with this, you can skip this section (or verify and improve it). DMX is similar to MIDI: it's old, it works, but it sucks—because it feels like a technology that should have been long forgotten and replaced with something more precise, flexible, and well-documented. Dream on.

DMX defines that we can use blocks of 512 bytes (i.e., channels of integer values between 0 and 255) to control *fixtures* (i.e., types of lighting equipment). Such a block is called a universe, and you can have a limited number of those.

DMX defines how a light connected to a universe (i.e., a DMX wire) uses a range of bytes to control its parameters. For example, two lights of a certain fixture type might use 3 bytes to drive their RGB color and brightness. Then, in our universe, we could define that one starts at index 0 and the other at index 8. The start index (0 and 8) has to be configured via the on-screen menu on each light.

With DMX, updates are always complete: the whole 512 bytes for each universe are pushed in a single go, typically at 20–40 fps depending on the DMX hardware. ArtNet defines how DMX can be used over Ethernet by mapping IP address ranges to universes.

### Problems

**Poor resolution:** Obviously, a value range of 0–255 might be good enough for light intensity, but for rotations we need at least a second byte for higher precision. Most moving heads support that.

**Missing fixture definitions:** Each vendor defines their own channel mappings for their products. Most fixtures also support different *modes* using more or fewer channels. There’s no proper free database of fixture definitions, and vendors aren’t motivated or required to provide specs in a machine-readable format.

We don’t currently support fixture type imports. While [MA Lighting Fixture Share](https://fixtureshare.malighting.com) offers \~50,000 definitions, they cannot legally be used elsewhere. The only free and open option, [OFL](https://open-fixture-library.org), has about 700 definitions. [OpenGDTF](https://gdtf-share.com) might eventually improve this situation.

Long story short: to drive lights with TiXL, you will need to enter the channel mapping for each light manually using the vendor's PDF documentation.

### TL;DR

Every frame, we send one or more 512-byte blocks (universes) with control signals (channels). Each light (fixture) uses a slice of these bytes to update its state.

## Driving Lights with TiXL

The integration uses a modular setup with multiple operators:

![alt text](/images/image-3.png)

You define **Points** with your light information.

**PointsToArtnetLight** converts *Points* (and their `Color`, `Orientation`, `FX1`, `FX2`) into DMX value sequences. Through various parameters, you define which channels correspond to which attributes.

**[ArtnetOutput]** broadcasts these value sequences.

### PointsToArtnetLight

We added a new grid display mode to inspect the output values of **PointsToArtnetLight**:

![alt text](/images/Animation/anim-1.gif)

In our case, the light fixture uses 15 channels (set via `FixtureChannelSize`). We adjust the grid’s column count accordingly and can verify how point attributes like color and rotation are mapped to 0–255 values.

⚠️ DMX channel indices start at 1. For convenience, we follow this convention in all `*Channel` parameters and convert internally (i.e., Channel #1 maps to index 0).

### Combining Multiple Fixture Types

Use the \[MergeIntLists] operator to merge outputs from multiple fixture definitions into a single universe:

![alt text](/images/Animation/anim-2.gif)

The `Length` parameter ensures a 512-byte buffer. The optional `StartIndices` parameter allows manual offset placement for each fixture.

### Light Orientations

Controlling “moving heads” (lights with servo motors) is more advanced.

Getting *some* rotation is straight forward: Just activate the  `GetRotation` Parameter in **PointsToArtnetLight**, enter the correct channel ids, and the light will do _something_. 

To set this up precisely, e.g. so that a group of spotlight will follow a point in space, will require more work:

![alt text](/images/Animation/anim-3.gif)

1. You probably want to create an accurate (\~5cm) 3D model of your setup (e.g., in Blender).
2. Position your light points using the **VisualizeSpotLights** helper.
3. Enable *Show Gizmos* in the output window, select points, and move them with transform handles.
4. Adjust orientations.
5. Apply effects to the points.
6. **Combine** effect points with reference points.
7. Enable `WithReferencePoints` in **PointsToArtnetLight**.


## Hardware considerations

I made good experiences with the following setup:

```
Notebook --> (Switch) --> ArtnetDMXConverter ==> Light ==> Light ==> ...
```

The [Showtec NET-2/3 Pocket ArtnetDMX converter](https://www.thomann.de/de/showtec_net_2_3_pocket.htm) (~200 Euro) worked out of the box: 
- Connect to power
- Setup your local subnet masks (e.g. to 255.255.255.0)
- Assign an IP address with the devices menu: e.g. 10.10.0.100
- Connect your computer to an Ethernet cable.
- Assign an IP address in the same subnet (e.g. 10.10.0.1)
- Connect to the Showtec (use an switch, if necessary)

Before this I spent a lot of time experimenting with [Enttec](https://www.thomann.de/de/enttec_open_dmx_usb_interface_bundle.htm). But it required installing additional drivers and I couldn't get it to work consistently. Some people mentioned that using an Raspberry PI as an USB-interface might also work.

So far I tested with roughly 30 lamps in 2 universes (moving heads, bars, disco-ball motors and washers). Everything was working surprisingly stable. And yes -- synced light effects are great fun.

## Testing ArtNet Locally
TiXL does not currently support outputting DMX signal via USB without the use of ArtNet. However, a similar effect can be achieved using an [ArtNet to DMX Script](https://github.com/nt2ds/ArtNetDMX) which works with all FTDI converters. You can also use a program like [QLC+](https://www.qlcplus.org/) and accept your TiXL ArtNet output as a QLC+ input, and then pipe that to a different device (for example a USB to DMX adapter).
This can also be useful if you want to test your lights with TiXL before setting them up in a more permanent position.

Please note that ArtNet does not play nicely with localhost (127.0.0.1). If you are running ArtNet entirely locally, you will still want to set your Local IP Address in the ArtnetOutput operator to your actual local IP address, eg 192.168.X.X.

One other thing to consider is that you may need to configure your lights into "DMX mode" and set an appropriate channel size in order for them to work properly. Improperly configured channel sizes can lead to a lot of chaos. It may also help to have an ArtNet monitoring tool, which QLC+ is capable of. Also consider [The ArtNetominator](https://www.lightjams.com/artnetominator).

___

## Ideas for Future Features

The current implementation is a solid proof-of-concept that already enables many use cases. However, to create a decent light show, additional features are needed because...

1. Handling separate point buffers for different fixture types is awkward, inefficient, and—most importantly—inconvenient. The definition and placement of point lights into a single buffer should be separated from fixture type mapping.
2. Various effects require sophisticated selection and filtering mechanisms for use cases like: "select all spots of a certain type on the left center stage and apply an effect."
3. Re-entering fixture mapping definitions multiple times is cumbersome (although presets or copy-pasting parameters offer workarounds).

After brainstorming with several light artists, the following features are envisioned:

* A new `DataTable` type to hold and edit generic data, similar to an Excel spreadsheet. This would allow creating templates for fixture definitions (essentially all parameters of `PointsToArtnetLight`) that can be reused. The table would support CSV export/import, opening the door to converting existing GDTF specs.
* The `DataTable` could also be used to define mappings for `PointIndex`, `FixtureDefinitionId`, `UniverseIndex`, and `UniverseChannelIndex`.
* A second data table could generate index-list buffers to filter points into subsets for selective effects.
* A new \[PointList] type should be defined that can be serialized. This would support better editing, such as multi-selection, group transformations, and setting parameters for multiple selected points at once.

The best part: none of these features are specific to stage lighting. Each one would be extremely useful for a wide range of scenarios—from UV editing to defining output regions for projection mapping. They align perfectly with TiXL’s current feature set and can be introduced as extensions (i.e., without breaking changes or regressions).

This still won't turn TiXL into a full-fledged stage lighting software, but it will allow for a very fast and flexible workflow.


## Additional Resources

### NuGet Packages

* [https://github.com/HakanL/Haukcode.ArtNet](https://github.com/HakanL/Haukcode.ArtNet)
* [https://github.com/DMXControl/ArtNetSharp/blob/main/Examples/ControllerExample/Program.cs](https://github.com/DMXControl/ArtNetSharp/blob/main/Examples/ControllerExample/Program.cs)

### Example Mapping Table

* [https://www.germanlightproducts.com/wp-content/uploads/2020/05/JDC-Line-500-DMX-Channel-Index-Rev-20211129-01.pdf](https://www.germanlightproducts.com/wp-content/uploads/2020/05/JDC-Line-500-DMX-Channel-Index-Rev-20211129-01.pdf)

### Possibly Useful

* [Open Fixture Library](https://open-fixture-library.org/generic/drgb-fader)
* [Fixture.id](https://www.fixture.id/) for finding compatible alternatives
* [MA Fixture Share](https://fixtureshare.malighting.com) (proprietary, not usable)
* [OpenGDTF](https://gdtf-share.com) and [GitHub](https://github.com/mvrdevelopment/spec) for open fixture standards
