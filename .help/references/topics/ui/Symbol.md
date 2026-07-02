A **Symbol** is the reusable definition behind an operator — its inputs, outputs and internal graph — that every instance you place shares.

When you build your own operator by grouping a sub-graph, you're creating a symbol; editing it updates every place it's used. Symbols are organized by namespace in the [ui:SymbolLibrary|Symbol Library]. A symbol can also carry an example scene: name it "<OpName>Example" in an Examples namespace and it surfaces as that operator's built-in how-to.
