# Getting all Expressions in a syntax tree

```csharp
using System;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(@"
using System;
class Program
{
    static void Main()
    {
        var i = 0.0;
        i += 1 + 2L;
    }
}");

MetadataReference corelib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
CSharpCompilation compilation = CSharpCompilation.Create("MyCompilation")
    .AddReferences(corelib)
    .AddSyntaxTrees(tree);
SemanticModel model = compilation.GetSemanticModel(tree);
ExpressionWalker walker = new ExpressionWalker() { SemanticModel = model };

walker.Visit(tree.GetRoot());

Console.WriteLine(walker.Results.ToString());

// Below SyntaxWalker traverses all expressions under the SyntaxNode being visited and lists the types of these expressions.
class ExpressionWalker : SyntaxWalker
{
    public SemanticModel SemanticModel { get; set; }
    public StringBuilder Results { get; private set; }

    public ExpressionWalker()
    {
        Results = new StringBuilder();
    }

    public override void Visit(SyntaxNode node)
    {
        if (node is ExpressionSyntax expression)
        {
            ITypeSymbol type = SemanticModel.GetTypeInfo(expression).Type;
            if (type is not null)
            {
                Results.AppendLine();
                Results.Append(node.GetType().Name);
                Results.Append(' ');
                Results.Append(node.ToString());
                Results.Append(" has type ");
                Results.Append(type.ToDisplayString());
            }
        }

        base.Visit(node);
    }
}
```