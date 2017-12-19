using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

namespace SyntaxVisualizer.CLI.Printing
{
    abstract class AbstractConsoleSyntaxTreePrinter : AbstractSyntaxTreePrinter
    {
        public AbstractConsoleSyntaxTreePrinter(SyntaxTree tree, SyntaxVisualizerOptions options) : base(tree, options) { }

        public Task PrintSyntaxTreeAsync() => PrintSyntaxTreeAsync(Console.Out);

        private new Task PrintSyntaxTreeAsync(TextWriter output)
        {
            return base.PrintSyntaxTreeAsync(output);
        }

        protected override void PrintKind(SyntaxNode node, int nestingLevel)
        {
            var kindString = GetKindString(node);
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            PrintKind(kindString, nestingLevel);
            Console.ForegroundColor = oldColor;
            WriteText(_sourceText.GetSubText(node.Span).ToString());
        }

        protected override void PrintKind(SyntaxToken token, int nestingLevel)
        {
            var kindString = GetKindString(token);
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Blue;
            PrintKind(kindString, nestingLevel);
            Console.ForegroundColor = oldColor;
            WriteText(_sourceText.GetSubText(token.Span).ToString());
        }

        protected override void PrintKind(SyntaxTrivia trivia, int nestingLevel)
        {
            var kindString = GetKindString(trivia);
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            PrintKind(kindString, nestingLevel);
            Console.ForegroundColor = oldColor;
            WriteText(_sourceText.GetSubText(trivia.Span).ToString());
        }
    }
}
