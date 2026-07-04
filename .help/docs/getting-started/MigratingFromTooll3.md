# Migrating from Tooll3

TiXL is almost a complete rewrite, so many features now work fundamentally differently than in Tooll3. This page walks you through the most important changes:

* Project Handling
* User Settings
* Resources

## Project Handling

In Tooll3, your user data and operators were intermixed with the editor files. This made upgrading the editor a real headache, because you had to migrate your projects into the new version.

With TiXL , this has changed completely: your projects are now gathered in a dedicated **project folder**. By default, this is your Documents folder, e.g.:

```
C:/Users/YourName/Documents/TiXL/
```

You can adjust this path in the settings menu:
**TiXL → Settings → Project Settings**

![alt text](/images/MigrateFromT3/change-projects-folder.gif)

### Structure of a Project Folder

In most cases, you probably won’t need to worry about this — but if you’re curious, a typical project folder looks like this:

* `MyProject/` – The root folder

  * `bin/` – Compiled output; regenerated on each startup
  * `obj/` – Intermediate build files; also regenerated
  * `dependencies/` – Optional folder for large binary assets, DLLs, and drivers required to export your project as an executable
  * `Resources/` – Place all images, videos, shaders, soundtracks, etc. here

    * `images/` – Optional subfolder to group your images

      * `myImage.png` – An actual resource file
  * `ProjectX.csproj` – Project definition file
  * `ProjectX.cs` – Your project’s C# operators
  * `ProjectX.t3` – Operator definitions (inputs, outputs, ops, connections)
  * `ProjectX.t3ui` – UI layout and metadata (comments, sections, layout)

## Resource Paths

There are three types of resource paths:

1. **Relative to your project** — e.g. `images/myImage.png`
2. **Cross-project reference** — e.g. `/t3/images/frog3.jpg`
   (note the leading `/t3/` to specify the source project)
3. **Absolute paths** — e.g. `C:/somewhere/something.jpg`

Although absolute paths are supported when entered manually, we strongly recommend using your project’s `Resources/` folder. It makes exporting and project management easier and ensures that your assets are included in backups.

### File Picking

As of v4.0.2, TiXL’s file browser is still a proof-of-concept. A more advanced version is in development.

When clicking a filepath field in a parameter, TiXL will list all available resources, grouped by project. You can search by filename or path to filter the list:

![alt text](/images/MigrateFromT3/file-path-picking.gif)

## Migrating projects

We're a still working on a proper import and migration utility (we have some early proof-of-concept but actions like this need thorough testing).

In the meantime you can copy and paste graphs directly from Tooll3 to TiXL. We worked hard to transfer all library operators and their parameters without breaking changes. However there are a couple of caveats where we decided that changing some parameters or defaults are justified:


### Point's "W" attribute

- if you have been building scenes with Tooll you'll have come across the W attribute in points that was used for many magical operations like scaling or affecting effects. This [changed with v3.9.3](https://github.com/tixl3d/tixl/wiki/update.BetterPoints) where we introduced a new Point structure that not only supports Colors but also a scale attribute. In collaboration with @ScreenDream we further optimized this structure to only use the Scale vector to scaling and leave Fx1 and Fx2 (sometimes referred to F1 / F2) for arbitrary effects.

This has multiple implications that you will need to adjust when converting your graphs to TiXL:
- Operators like [DrawPoints], [RepeatPointsAtPoints], [RepeatMeshAtPoints], etc. no longer automatically applies W (now Fx1) as a scale factor. You need to set the `.ScaleFactor` to Fx1 in these cases.


