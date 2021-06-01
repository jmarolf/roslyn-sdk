# Getting all comments in a syntax tree using a syntax walker

```csharp
using System;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(@"
using System;
/// <summary>First Comment</summary>
class Program
{
    /* Second Comment */
    static void Main()
    {
        // Third Comment
    }
}");

CommentWalker walker = new CommentWalker();
walker.Visit(tree.GetRoot());

Console.WriteLine(walker.Results.ToString());

// Below SyntaxWalker traverses all comments present under the SyntaxNode being visited.
class CommentWalker : CSharpSyntaxWalker
{
    public StringBuilder Results { get; private set; }

    public CommentWalker() :
        base(SyntaxWalkerDepth.StructuredTrivia)
    {
        Results = new StringBuilder();
    }

    public override void VisitTrivia(SyntaxTrivia trivia)
    {
        bool isDocComment = SyntaxFacts.IsDocumentationCommentTrivia(trivia.Kind());
        if (isDocComment ||
            trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
        {
            Results.AppendLine();
            Results.Append(trivia.ToFullString().Trim());
            Results.Append(" (Parent Token: ");
            Results.Append(trivia.Token.Kind());
            Results.Append(')');
            if (isDocComment)
            {
                // Trivia for xml documentation comments have additional 'structure'
                // available under a child DocumentationCommentSyntax.
                DocumentationCommentTriviaSyntax documentationComment =
                    (DocumentationCommentTriviaSyntax)trivia.GetStructure();
                Results.Append(" (Structured)");
            }
        }

        base.VisitTrivia(trivia);
    }
}
```
