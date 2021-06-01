# Deleting Trivia from a Tree

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(@"
using System;
#region Program
class Program
{
    static void Main()
    {
    }
}
#endregion
#region Other
class C
{
}
#endregion");
SyntaxNode oldRoot = tree.GetRoot();

// Get all RegionDirective and EndRegionDirective trivia.
IEnumerable<SyntaxTrivia> trivia = oldRoot.DescendantTrivia()
    .Where(t => t.IsKind(SyntaxKind.RegionDirectiveTrivia) ||
                t.IsKind(SyntaxKind.EndRegionDirectiveTrivia));

SyntaxNode newRoot = oldRoot.ReplaceTrivia(trivia: trivia,
    computeReplacementTrivia:
        (originalTrivia, originalTriviaWithReplacedDescendants) => default(SyntaxTrivia));

Console.WriteLine(newRoot.ToFullString());
```

```csharp
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(@"
using System;
#region Program
class Program
{
    static void Main()
    {
    }
}
#endregion
#region Other
class C
{
}
#endregion");
SyntaxNode oldRoot = tree.GetRoot();

CSharpSyntaxRewriter rewriter1 = new RegionRemover1();
SyntaxNode newRoot1 = rewriter1.Visit(oldRoot);
Console.WriteLine(newRoot1.ToFullString());

CSharpSyntaxRewriter rewriter2 = new RegionRemover2();
SyntaxNode newRoot2 = rewriter2.Visit(oldRoot);
Console.WriteLine(newRoot2.ToFullString());

// Below CSharpSyntaxRewriter removes all #regions and #endregions from under the SyntaxNode being visited.
class RegionRemover1 : CSharpSyntaxRewriter
{
    public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
    {
        SyntaxTrivia updatedTrivia = base.VisitTrivia(trivia);
        if (trivia.Kind() == SyntaxKind.RegionDirectiveTrivia ||
            trivia.Kind() == SyntaxKind.EndRegionDirectiveTrivia)
        {
            // Remove the trivia entirely by returning default(SyntaxTrivia).
            updatedTrivia = default;
        }

        return updatedTrivia;
    }
}

// Below CSharpSyntaxRewriter removes all #regions and #endregions from under the SyntaxNode being visited.
class RegionRemover2 : CSharpSyntaxRewriter
{
    public override SyntaxToken VisitToken(SyntaxToken token)
    {
        // Remove all #regions and #endregions from underneath the token.
        return token
         .WithLeadingTrivia(RemoveRegions(token.LeadingTrivia))
         .WithTrailingTrivia(RemoveRegions(token.TrailingTrivia));
    }

    private SyntaxTriviaList RemoveRegions(SyntaxTriviaList oldTriviaList)
    {
        return SyntaxFactory.TriviaList(oldTriviaList
            .Where(trivia => trivia.Kind() != SyntaxKind.RegionDirectiveTrivia &&
                             trivia.Kind() != SyntaxKind.EndRegionDirectiveTrivia));
    }
}
```
