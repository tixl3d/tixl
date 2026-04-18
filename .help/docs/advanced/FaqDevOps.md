# FAQ: How to Write C# Ops

## Before you start

This page assumes that [you're running TiXL from an IDE](../install/InstallDev.md).

Best practices:

* When experimenting, it might be useful to recompile and restart TiXL's editor **in Debug Mode** before doing any of the steps mentioned on this page.
  This is important because TiXL will lock all library ops in Release mode as read-only.
* It's a good idea to always [use git](https://github.com/tixl3d/tixl/wiki/dev.WorkingWithGit) and commit or discard local changes.

## How can I create a new op?

* We recommend [duplicating an existing op](CreatingNewOps.md#3-duplicate-an-existing-operator).
* You don't need to restart — after duplicating, your IDE should be able to find the new operator. (This has been tested with library and example projects. Handling of user projects by the IDE may differ.)
* Once your new project is created, TiXL will immediately set up a file watch. Every time this file is modified, TiXL will recompile the operator project independently of the IDE. Avoid applying hot-code reloading from the IDE. Once the operator has been compiled by TiXL, it will automatically update all instances.

## How can I remove inputs and outputs

* Just remove the respective input definitions from the c# file. You can do this while the Editor is running. TiXL will recompile the project and remove the respective definitions from the .t3ui and .t3 files. 
* After recompiling the parameters should disappear from the user interface and connection to it should removed.
* Careful: there is no Undo for this.


