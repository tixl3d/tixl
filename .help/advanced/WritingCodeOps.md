# Writing C# operators

## First steps

Developing your own operators in C# can be very powerful and a lot of fun. 

### Setup

You probably already did the following steps when [setting up your dev environment](../setup/InstallDev.md).

1. Install Fork
2. Install Rider or Visual Studio (I'm sure, Visual Studio could work, but I'm not sure how to set it up)

### Run from IDE like Rider or Visual Studio

3. Start your IDE (e.g. Rider)
4. Find and open the solution `t3.sln`.
5. Switch to the `Debug` configuration because it will allow you to modify lib operators
6. Start **Debug** (it's slightly slower than Run, but you can set breakpoints)

  - The first startup will take a while because your computer has to download all dependencies and compile the whole project. The next startup time will be faster
  - After the splash screen, TiXL should start up and present the _Project HUB_.

### Create a project

1. In TiXL's _Project HUB_ click the + to create a new project.
2. Name it something like `MyTest`.
3. Click to open it.
4. TiXL created a new whole new c# project structure in `c:\Users\YOURNAME\Documents\TiXL\MyTest`.
5. In your IDE select _Add new project_ and find and select `MyTest.csproj`.

### Creating a new c# Operator

1. With your `MyTest` project opened...
1. Long press on the graph background and create a [Modulo](operators/Lib.numbers.float.basic.Modulo.md) operator.
2. Select the new Op
3. Right click -> Symbol Definition -> Duplicate as new Type
4. In the _Duplicate Symbol_ dialog...
   
   - Select your `MyTest` project as target.
   - Give a new name like `Modulo2`.
   - Click _Duplicate_.

5. In your IDE the new `Modulo2.cs` file should appear in the project.
6. ⚠ Sometimes TiXL does not apply the correct new user project namespace.
   - If `Modulo2` starts with...
   
        ```cs
        ❌ namespace Lib.numbers.@float.basic; 
        ```

   - ...change it to...
        ```cs
        ✔ namespace YOURNAME.MyProject;  
        ```

7. Let's try to add some code: Insert the following like in the `Update() {}` method:

```cs
private void Update(EvaluationContext context)
{
    Log.Debug("Hello op!", this); // <- Add this line
    // more core follows...
}
```
9. If things are setup correctly, you can now press Ctrl+S to save `Modulo2.cs`.
    - TiXL automatically notices the change and will recompile and reload the operator.
- Switch to TiXL
- Open *Windows* -> *Console* or switch to a layout with the *Console Window* enabled.
- If the Modulo2 operator selected and visible in the *Output window* you should see the `Modulo2: _Hello Op_` log messages.
  
# Pit falls

## ⚠ Avoid hot code reload from IDE

TiXL will handle the reloading and compiling of operator code. If Rider or Visual Studio suggests to "Apply Codes changes" you should ignore that, because mixing recompiling ops with C# hot code reload can lead to problems.

## ⚠ Do not try to save with errors

When TiXL can't compile the new operator, it will crash (we're working on change that). This means that you should be very careful about saving with errors. Your IDE normally indicates them with read squiggly lines, in the scrollbar and in the status bar.

Also be careful with auto save on focus change (some IDEs automatically save when you click outside the editor window).

## ⚠ Showing definitions

When inspecting a definition (e.g. by pressing F12) your IDE might decompile the Core DLL instead of jumping to the actual class.

I'm not really sure how to change that behaviour.


