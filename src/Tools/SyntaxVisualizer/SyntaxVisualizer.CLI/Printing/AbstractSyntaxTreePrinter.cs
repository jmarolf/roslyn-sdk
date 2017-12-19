using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SyntaxVisualizer.CLI.Printing
{
    internal abstract class AbstractSyntaxTreePrinter
    {
        [ThreadStatic]
        protected TextWriter _output;
        protected  SourceText _sourceText;
        protected readonly SyntaxTree _tree;
        private readonly SyntaxVisualizerOptions _options;

        protected AbstractSyntaxTreePrinter(SyntaxTree tree, SyntaxVisualizerOptions options)
        {
            _tree = tree;
            _options = options;
        }

        public virtual async Task PrintSyntaxTreeAsync(TextWriter output)
        {
            _output = output;
            _sourceText = await _tree.GetTextAsync().ConfigureAwait(false);
            var root = await _tree.GetRootAsync().ConfigureAwait(false);
            PrintNode(root, 0);
        }

        protected virtual void PrintNodeOrToken(SyntaxNodeOrToken nodeOrToken, int nestingLevel)
        {
            if (nodeOrToken.IsNode)
            {
                PrintNode(nodeOrToken.AsNode(), nestingLevel);
            }
            else
            {
                PrintToken(nodeOrToken.AsToken(), nestingLevel);
            }
        }

        protected virtual void PrintNode(SyntaxNode node, int nestingLevel)
        {
            PrintKind(node, nestingLevel);
            if (node.ChildNodesAndTokens().Count > 0)
            {
                nestingLevel++;
                foreach (var child in node.ChildNodesAndTokens())
                {
                    PrintNodeOrToken(child, nestingLevel);
                }
            }
        }

        protected virtual void PrintToken(SyntaxToken token, int nestingLevel)
        {
            PrintKind(token, nestingLevel);
            if (token.HasLeadingTrivia || token.HasTrailingTrivia)
            {
                nestingLevel++;
                foreach (var trivia in token.LeadingTrivia)
                {
                    PrintTrivia(trivia, nestingLevel);
                }

                foreach (var trivia in token.TrailingTrivia)
                {
                    PrintTrivia(trivia, nestingLevel);
                }
            }
        }

        protected virtual void PrintTrivia(SyntaxTrivia trivia, int nestingLevel)
        {
            PrintKind(trivia, nestingLevel);
            if (trivia.HasStructure)
            {
                nestingLevel++;
                PrintNode(trivia.GetStructure(), nestingLevel);
            }
        }

        protected virtual void PrintKind(SyntaxNode node, int nestingLevel)
        {
            var kindString = GetKindString(node);
            PrintKind(kindString, nestingLevel);
            WriteText(_sourceText.GetSubText(node.Span).ToString());
        }

        protected virtual void PrintKind(SyntaxToken token, int nestingLevel)
        {
            var kindString = GetKindString(token);
            PrintKind(kindString, nestingLevel);
            WriteText(_sourceText.GetSubText(token.Span).ToString());

        }

        protected virtual void PrintKind(SyntaxTrivia trivia, int nestingLevel)
        {
            var kindString = GetKindString(trivia);
            PrintKind(kindString, nestingLevel);
            WriteText(_sourceText.GetSubText(trivia.Span).ToString());
        }



        protected virtual void PrintKind(string text, int nestingLevel)
        {
            _output.Write(text.PadLeft(text.Length + nestingLevel));
        }

        protected virtual void WriteText(string text)
        {
            if (_options.IsSingleLine)
            {
                _output.WriteLine($" : \"{text.Replace("\r", @"\r").Replace("\n", @"\n")}\"");
            }
            else
            {
                _output.WriteLine($" : \"{text}\"");
            }
        }

        protected abstract string GetKindString(SyntaxNode node);
        protected abstract string GetKindString(SyntaxToken token);
        protected abstract string GetKindString(SyntaxTrivia trivia);
    }

}
