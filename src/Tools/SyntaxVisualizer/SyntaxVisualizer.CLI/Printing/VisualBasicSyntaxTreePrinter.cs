using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace SyntaxVisualizer.CLI.Printing
{
    internal class VisualBasicSyntaxTreePrinter : AbstractConsoleSyntaxTreePrinter
    {
        public VisualBasicSyntaxTreePrinter(SyntaxTree tree, SyntaxVisualizerOptions options) : base(tree, options) { }

        protected override string GetKindString(SyntaxNode node) => node.Kind().AsString() + "Syntax";
        protected override string GetKindString(SyntaxToken token) => token.Kind().AsString();
        protected override string GetKindString(SyntaxTrivia trivia) => trivia.Kind().AsString();
    }
}
