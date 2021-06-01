# Adding a Method to a Class

```csharp
using System;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(@"
class C
{
}");
CompilationUnitSyntax compilationUnit = (CompilationUnitSyntax)tree.GetRoot();

// Get ClassDeclarationSyntax corresponding to 'class C' above.
ClassDeclarationSyntax classDeclaration = compilationUnit.ChildNodes()
    .OfType<ClassDeclarationSyntax>().Single();

// Construct a new MethodDeclarationSyntax.
MethodDeclarationSyntax newMethodDeclaration =
    SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName("void"), "M")
        .WithBody(SyntaxFactory.Block());

// Add this new MethodDeclarationSyntax to the above ClassDeclarationSyntax.
ClassDeclarationSyntax newClassDeclaration =
    classDeclaration.AddMembers(newMethodDeclaration);

// Update the CompilationUnitSyntax with the new ClassDeclarationSyntax.
CompilationUnitSyntax newCompilationUnit =
    compilationUnit.ReplaceNode(classDeclaration, newClassDeclaration);

// normalize the whitespace
newCompilationUnit = newCompilationUnit.NormalizeWhitespace("    ");

Console.WriteLine(newCompilationUnit.ToFullString());
```

```csharp
using System;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(@"
class Program
{
    static void Main()
    {
        System.Console.WriteLine();
        int total = 0;
        for (int i=0; i < 5; ++i)
        {
            total += i;
        }
        if (true) total += 5;
    }
}");
SyntaxNode oldRoot = tree.GetRoot();
ConsoleWriteLineInserter rewriter = new ConsoleWriteLineInserter();
SyntaxNode newRoot = rewriter.Visit(oldRoot);
newRoot = newRoot.NormalizeWhitespace(); // fix up the whitespace so it is legible.

Console.WriteLine(newRoot.ToString());

// Below CSharpSyntaxRewriter inserts a Console.WriteLine() statement to print the value of the
// LHS variable for compound assignment statements encountered in the input tree.
class ConsoleWriteLineInserter : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        SyntaxNode updatedNode = base.VisitExpressionStatement(node);

        if (node.Expression.Kind() == SyntaxKind.AddAssignmentExpression ||
            node.Expression.Kind() == SyntaxKind.SubtractAssignmentExpression ||
            node.Expression.Kind() == SyntaxKind.MultiplyAssignmentExpression ||
            node.Expression.Kind() == SyntaxKind.DivideAssignmentExpression)
        {
            // Print value of the variable on the 'Left' side of
            // compound assignment statements encountered.
            AssignmentExpressionSyntax compoundAssignmentExpression = (AssignmentExpressionSyntax)node.Expression;
            StatementSyntax consoleWriteLineStatement =
                SyntaxFactory.ParseStatement(string.Format("System.Console.WriteLine({0});", compoundAssignmentExpression.Left.ToString()));

            updatedNode =
                SyntaxFactory.Block(SyntaxFactory.List<StatementSyntax>(
                                        new StatementSyntax[]
                                        {
                                            node.WithLeadingTrivia().WithTrailingTrivia(), // Remove leading and trailing trivia.
                                            consoleWriteLineStatement
                                        }))
                    .WithLeadingTrivia(node.GetLeadingTrivia())        // Attach leading trivia from original node.
                    .WithTrailingTrivia(node.GetTrailingTrivia());     // Attach trailing trivia from original node.
        }

        return updatedNode;
    }
}
```
