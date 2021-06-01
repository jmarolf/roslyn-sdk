# Replacing sub expressions in a Syntax Tree

```csharp
using System;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(@"
class Program
{
    static void Main()
    {
        int i = 0, j = 0;
        Console.WriteLine((i + j) - (i + j));
    }
}");
CompilationUnitSyntax compilationUnit = (CompilationUnitSyntax)tree.GetRoot();

// Get BinaryExpressionSyntax corresponding to the two addition expressions 'i + j' above.
BinaryExpressionSyntax addExpression1 = compilationUnit.DescendantNodes()
    .OfType<BinaryExpressionSyntax>().First(b => b.Kind() == SyntaxKind.AddExpression);
BinaryExpressionSyntax addExpression2 = compilationUnit.DescendantNodes()
    .OfType<BinaryExpressionSyntax>().Last(b => b.Kind() == SyntaxKind.AddExpression);

// Replace addition expressions 'i + j' with multiplication expressions 'i * j'.
BinaryExpressionSyntax multipyExpression1 = SyntaxFactory.BinaryExpression(SyntaxKind.MultiplyExpression,
    addExpression1.Left,
    SyntaxFactory.Token(SyntaxKind.AsteriskToken)
        .WithLeadingTrivia(addExpression1.OperatorToken.LeadingTrivia)
        .WithTrailingTrivia(addExpression1.OperatorToken.TrailingTrivia),
    addExpression1.Right);
BinaryExpressionSyntax multipyExpression2 = SyntaxFactory.BinaryExpression(SyntaxKind.MultiplyExpression,
    addExpression2.Left,
    SyntaxFactory.Token(SyntaxKind.AsteriskToken)
        .WithLeadingTrivia(addExpression2.OperatorToken.LeadingTrivia)
        .WithTrailingTrivia(addExpression2.OperatorToken.TrailingTrivia),
    addExpression2.Right);

CompilationUnitSyntax newCompilationUnit = compilationUnit
    .ReplaceNodes(nodes: new[] { addExpression1, addExpression2 },
                  computeReplacementNode:
                    (originalNode, originalNodeWithReplacedDescendants) =>
                    {
                        SyntaxNode newNode = null;

                        if (originalNode == addExpression1)
                        {
                            newNode = multipyExpression1;
                        }
                        else if (originalNode == addExpression2)
                        {
                            newNode = multipyExpression2;
                        }

                        return newNode;
                    });

Console.WriteLine(newCompilationUnit.ToFullString());
```
