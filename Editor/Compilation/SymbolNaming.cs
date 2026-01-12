using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using T3.Core.Operator;
using T3.Editor.UiModel;
using GraphUtils = T3.Editor.UiModel.Helpers.GraphUtils;

namespace T3.Editor.Compilation;

internal static class SymbolNaming
{
    private sealed class ConstructorRewriter : CSharpSyntaxRewriter
    {
        private readonly string _oldSymbolName;
        private readonly string _newSymbolName;

        public ConstructorRewriter(string oldSymbolName, string newSymbolName)
        {
            _oldSymbolName = oldSymbolName;
            _newSymbolName = newSymbolName;
        }

        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            // Only rename constructors that belonged to the original top-level class
            if (node.Identifier.Text != _oldSymbolName)
                return node;

            return node.WithIdentifier(
                                       SyntaxFactory.Identifier(_newSymbolName)
                                                    .WithTriviaFrom(node.Identifier));
        }
    }

    public static bool RenameSymbol(Symbol symbol, string newName)
    {
        if (symbol.SymbolPackage.IsReadOnly)
            throw new ArgumentException("Symbol is read-only and cannot be renamed");
        
        
        var syntaxTree = GraphUtils.GetSyntaxTree(symbol);
        if (syntaxTree == null)
        {
            Log.Error($"Error getting syntax tree from symbol '{symbol.Name}' source.");
            return false;
        }

        // Create new source on basis of original type
        var root = syntaxTree.GetRoot();
        var classRenamer = new ClassRenameRewriter(newName);
        root = classRenamer.Visit(root);

        var constructorRewriter = new ConstructorRewriter(symbol.Name, newName);
        root = constructorRewriter.Visit(root);

        var newSource = root.GetText().ToString();

        return EditableSymbolProject.RecompileSymbol(symbol, newSource, false, out _);
    }
}

internal sealed class ClassRenameRewriter : CSharpSyntaxRewriter
{
    private readonly string _newSymbolName;
    private bool _hasRenamed;

    public ClassRenameRewriter(string newSymbolName)
    {
        _newSymbolName = newSymbolName;
    }

    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        // Only rename the first (outermost) class
        if (_hasRenamed)
            return base.VisitClassDeclaration(node);

        _hasRenamed = true;

        var identifier =
            SyntaxFactory.Identifier(_newSymbolName)
                         .WithTriviaFrom(node.Identifier);

        var classDeclaration =
            node.WithIdentifier(identifier);

        var genericName =
            SyntaxFactory.GenericName(
                                      SyntaxFactory.Identifier("Instance"),
                                      SyntaxFactory.TypeArgumentList(
                                                                     SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                                                      SyntaxFactory.IdentifierName(_newSymbolName))));

        var baseInterfaces = node.BaseList?.Types.Skip(1);

        var baseList =
            SyntaxFactory.BaseList(
                                   SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                                                                                        SyntaxFactory.SimpleBaseType(genericName)));

        if (baseInterfaces != null)
            baseList = baseList.AddTypes(baseInterfaces.ToArray());

        classDeclaration = classDeclaration.WithBaseList(baseList);

        return base.VisitClassDeclaration(classDeclaration);
    }
}