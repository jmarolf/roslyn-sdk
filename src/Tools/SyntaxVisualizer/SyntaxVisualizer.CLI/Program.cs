using System.IO;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using SyntaxVisualizer.CLI.Printing;

using CS = Microsoft.CodeAnalysis.CSharp;
using VB = Microsoft.CodeAnalysis.VisualBasic;

namespace SyntaxVisualizer.CLI
{
    partial class Program
    {
        static async Task Main(string[] args)
        {
            var (success, options) = TryParseOptions(args);
            if (!success)
            {
                return;
            }

            SourceText text = null;
            using (var stream = File.OpenRead(options.FilePath))
            {
                text = SourceText.From(stream);
            }

            var language = options.LanguageName;
            if (language == LanguageName.CSharp)
            {
                await PrintCSharpSyntaxAsync(CS.CSharpSyntaxTree.ParseText(text), options).ConfigureAwait(false);
            }

            if (language == LanguageName.VisualBasic)
            {
                await PrintVisualBasicSyntaxAsync(VB.VisualBasicSyntaxTree.ParseText(text), options).ConfigureAwait(false);
            }
        }

        private static Task PrintCSharpSyntaxAsync(SyntaxTree tree, SyntaxVisualizerOptions options)
            => (new CSharpConsoleSyntaxTreePrinter(tree, options)).PrintSyntaxTreeAsync();

        private static Task PrintVisualBasicSyntaxAsync(SyntaxTree tree, SyntaxVisualizerOptions options)
            => (new VisualBasicSyntaxTreePrinter(tree, options)).PrintSyntaxTreeAsync();
    }
}
