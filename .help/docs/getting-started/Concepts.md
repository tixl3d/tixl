> ⚠ This page needs work:
> - Links are broken
> - Screenshots are outdated

----

_This section is a WIP._
# TiXL - Basic Concepts

## Working with Operators
_Operators_ are the central building blocks of everything within TiXL. 

Depending on the context we sometimes call _Operators_...
- "Symbols" - if we're referring to the thing they're describing
- "Instances" - if we're talking about the children within an Operator.

Within the visual programming community, terms like "ops", "nodes" or "patches" are also used, which would be synonyms for "Operator". 

The _Symbol_ defines an operator. This includes...

- instances of other Operators and their changed parameters
- connections between these Operator instances
- definitions for inputs and outputs
- descriptions
- animations
- and many other things.

_Operators_ can be nested (I.e. they can contain other Operators), they can create code, and - of course - they can be reused.

If you change any of those all its instances are immediately updated.
- _Instances_ of Symbols (I.e. children within a symbol) are defined by other symbols. The parameters of _Instances_ define how Symbols are used. The [technical details] are more complex but not necessary to understand.


### Operator Names

The names of Operators are special, because internally they're full featured C# classes. That might sounds scary, but as an artist without development background you are unlikely to notice any of that. There is one exception though: Because we use the Operator title as a class name it must not contain spaces or special characters, and it must not start with a number. We recommend names starting with an upper case character.

- `MyFirstDemo` - ✔Perfect
- `myDemo` - 🤔 - would work but does not follow the naming convention
- `My Demo`  - 🚫 spaces are not allowed
- `My/Demo`, `My Demö` -  🚫 special characters are not allowed
- `1stProject` -  🚫 must not start with a number



You will find the definition of all Symbols in the folder `Types/Operators/`. Notice how each definition consists of 3 files:
- `.cs` - includes the c# code
- `.t3` - is definition of all children, connections and animations
- `.t3ui` - contains information that makes a symbol human-readable. Not required when [exporting a project as an executable](../using/ExportExecutables.md).


### Namespaces

Each operator has a _Namespace_. This attribute is important because it defines where the Operator will be stored withing TiXL's operator database and is used for grouping Symbols within the Operator tree that you seen under AppMenu → Add. 

<img src= "https://user-images.githubusercontent.com/56783893/176657583-df678f2d-3ca7-419d-b3a4-8abb3812a7d8.png" width="30%">


Please follow the following guidelines:

- levels should be separated by a "." character
- levels should start with a lower case character
- levels must not contain spaces or special characters
- You can use an underscore "_" prefix to flag namespaces as internal (I.e. not relevant to other users.)

We recommend the following naming convention for your projects: `user.johnDoe.project1`. This will make it easy to find your projects and migrate them between TiXL-installations. You can edit the namespace when creating new Operator symbols or in the Parameter window.

<img src= "https://user-images.githubusercontent.com/56783893/176657103-56ba1b2b-d7ce-4663-ad6a-587e57159c46.png" width="50%">

We recommend following this guideline
- `lib.*` - core operators that can and should be reused by other projects.
- `examples.` - tested examples that show how to use operators. TiXL automatically uses the Symbol-name to link Operators to examples. Thus [Blob] will automatically show a link to [BlobExample] that you can study.

<img src="https://user-images.githubusercontent.com/1732545/174810498-72b0defe-506b-4190-a578-b80fc394ca33.gif" width="25%">

### Descriptions
We highly recommend adding a short description to any Operator-Symbols you create. If available, this inline documentation will display wherever possible, and also make it easier to search for Operators by synonyms.

### GUIDs
You might have notices that TiXL works a lot with numbers like these: `723cd13e-0ca0-4995-a80b-a3b616400997`. These numbers are so-called random unique identifiers. TiXL uses these identifiers extensively for referencing between objects. This allows us to rename symbols or their parameters without breaking anything! You can read more about GUIDs on [Wikipedia](https://en.wikipedia.org/wiki/Universally_unique_identifier).

------

## Color coding in the interface

TiXL reuses a small set of accent colors so the same hue always means the same thing — whether it shows up on a parameter, an indicator, or a button:

- **Orange** — related to time: animated values, keyframes and playback.
- **Blue** — driven or linked: a value coming from an input connection or expression rather than typed in directly.
- **Green** — controllable by Snapshots (or other UI features). For example, a parameter enabled for snapshot control shows a small green knob next to its name.
- **Magenta** — needs attention: errors, an active recording, or muted audio.

------

## Performance 

TiXL uses realtime rendering, and a library called [Dear ImGui](https://github.com/ocornut/imgui) to render its user interface. The refresh rate of the user interface is directly synchronized with that of the Output window - this means that more performance-intensive scenes, where a single frame takes longer than 16ms to render, will cause TiXL's interface to become less responsive. Be aware of this before experimenting with rendering millions of points!

See also:

- [Optimizing Rendering Performance](/using/OptimizingRenderingPerformance)
- [Real-time Rendering for Artists](/using/RealtimeRendering)
