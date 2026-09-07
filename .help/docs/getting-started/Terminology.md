# Useful terminology

- **Operators** are the basic building blocks. You can combine them into graphs.
- **Symbols** are definitions of operators. If you update these definitions, all their Instances will automatically update as well (aka Templates or Blueprints). Internally, symbols are C# classes that are compiled to run very fast.
- **Instances** are actual copies of *Symbols*. Imagine a *Symbol* being a stamp that creates a copy. Instances are where your data lives. E.g. there could be two instances of [LoadImage], one loading a frog and another loading a toad.
- **Graph** — a *Symbol* can contain many children that reference other symbols. These children are connected into a network. The Symbol then exposes inputs and outputs that let you connect its children to its outside. See [HowTixlWorks] for more details.
- **Projects** (aka Packages) define a set of Symbols that can be reused, like your personal projects. TiXL comes with a number of built-in, read-only projects like `Lib` and `Examples`.


