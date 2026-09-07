# Creating New Operators

Most of the time, creating new [Operators](../getting-started/Concepts.md#working-with-operators) will not be required to build content with TiXL. However, eventually, you might want to create your own operators to build your own effects or structure and reuse your content building blocks.

There are three options to create new operators:

## 1. Use Templates from Main → New

The new dialog provides a selection of templates for new projects and effects. It's a very good starting point.

## 2. Grouping Operators

If you want to restructure your content, you can select some operators on the graph and use *Symbol definition* → *Combine to new type* from the context menu.

This will then show a dialog asking for the new operator name, its namespace, and a description.

Tip: If possible, leave some connections to not selected operators so the inputs and outputs are automatically created.

## 3. Duplicate an Existing Operator

This option is a great choice if you either want to create a variation of an existing operator (e.g., to create a backup or do some experiments) or if you need to create an operator and found an existing operator that is already pretty close.

Select the operator and pick *Symbol Definition* → *Duplicate as new Type* from the context menu.

Tip: Please note that this will not duplicate any of the resources used by the operator. For instance, if the operator uses a shader, make sure to save that shader file under a new filename and change the reference to that new version.

## Adding Dropdown Parameters

Internally, dropdown parameters like _BlendMode_ are treated as integer parameters and only "presented" as dropdown options by the UI. This means you can treat them as integer values: animate them or connect them with ops like [CountInt]. To display the option as a dropdown list, the editor needs to know how to map each integer value to an option name. This is done in two steps:

1. You need to add an `enum` definition to your class or refer to a definition within another class. Insert the following lines in the `Operator\Types\user\yourname\YourOp.cs` file:

```cs
private enum MyOptions {
  Option1,
  Option2,
}
```

2. You have to tell the input to use this `enum` definition. To do this, add the `MappedType = typeof(...)` attribute to your input definition:

```cs
        [Input(Guid = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE", MappedType = typeof(MyOptions))]
        public readonly InputSlot<int> ParameterWithOptions = new();
```

You don't need an IDE to compile these. Just edit the CS file while TiXL is running. But be careful not to modify the GUID of the input. TiXL will notice that the file has been changed and automatically recompile on the fly.

In the long run, it would be nice to have a UI inside TiXL to do this.
