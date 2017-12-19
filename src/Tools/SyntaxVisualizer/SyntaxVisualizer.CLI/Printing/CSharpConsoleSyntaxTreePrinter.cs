using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SyntaxVisualizer.CLI.Printing
{
    internal class CSharpConsoleSyntaxTreePrinter : AbstractConsoleSyntaxTreePrinter
    {
        public CSharpConsoleSyntaxTreePrinter(SyntaxTree tree, SyntaxVisualizerOptions options) : base(tree, options) { }

        protected override string GetKindString(SyntaxNode node) => node.Kind().AsString() + "Syntax";
        protected override string GetKindString(SyntaxToken token) => token.Kind().AsString();
        protected override string GetKindString(SyntaxTrivia trivia) => trivia.Kind().AsString();
    }
}
;
