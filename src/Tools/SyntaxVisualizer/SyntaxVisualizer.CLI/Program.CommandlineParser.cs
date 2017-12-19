using System;
using System.Collections.Generic;
using System.IO;

using Mono.Options;

namespace SyntaxVisualizer.CLI
{
    partial class Program
    {
        private static (bool success, SyntaxVisualizerOptions options) TryParseOptions(string[] args)
        {
            var showHelp = false;
            var options = new SyntaxVisualizerOptions();
            if (args.Length > 0)
            {
                options = options.WithFile(args[0]);
            }
            var parser = new OptionSet
            {
                {
                    "h|?|help",
                    "Show help.",
                    h => showHelp = h != null
                },
                {
                   "f=|file=",
                   "The file to show the syntax tree for.",
                   file => options = options.WithFile(file)
                },
                {
                   "l=|language=",
                   "The programming language the file is written in.",
                   language => {if (TryParseLanguage(language, out var languageName)) { options = options.WithLanguage(languageName); } }
                },
                {
                   "sl|singleline",
                   "Prints all text for nodes on a single line.",
                   sl => { if(sl != null){options = options.WithSingleLine(true); } }
                },
                                {
                   "ml|multiline",
                   "Prints all text for nodes as they appear in the file, even if they span multiple lines.",
                   ml => { if(ml != null){options = options.WithSingleLine(false); } }
                }
            };
            List<string> extraArguments = null;
            try
            {
                extraArguments = parser.Parse(args);
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to parse arguments.");
                Console.WriteLine(e.Message);
                return (false, options);
            }

            if (!string.IsNullOrEmpty(options.FilePath) && extraArguments.Count > 0)
            {
                extraArguments.Remove(options.FilePath);
            }

            if (extraArguments.Count > 0)
            {
                Console.WriteLine($"Unknown arguments: {string.Join(" ", extraArguments)}");
                return (false, options);
            }

            if (showHelp)
            {
                parser.WriteOptionDescriptions(Console.Out);
                return (false, options);
            }

            if (!options.Valid)
            {
                var (success, language) = TryDetermineLanguage(options);
                if (success)
                {
                    options = options.WithLanguage(language);
                }
            }


            if (!options.Valid)
            {
                Console.WriteLine(options.ValidationErrors);
                parser.WriteOptionDescriptions(Console.Out);
                return (false, options);
            }

            return (true, options);
        }

        private static (bool success, LanguageName languageName) TryDetermineLanguage(SyntaxVisualizerOptions options)
        {
            var extension = Path.GetExtension(options.FilePath);
            switch (extension)
            {
                case ".cs":
                case ".csx":
                    return (true, LanguageName.CSharp);
                case ".vb":
                    return (true, LanguageName.VisualBasic);
                default:
                    return (false, LanguageName.CSharp);
            }
        }

        private static bool TryParseLanguage(string language, out LanguageName languageName)
        {
            switch (language.ToLower())
            {
                case "c#":
                case "cs":
                case "csharp":
                    languageName = LanguageName.CSharp;
                    return true;
                case "vb":
                case "visualbasic":
                case "basic":
                    languageName = LanguageName.VisualBasic;
                    return true;
                default:
                    languageName = default;
                    return false;
            }
        }
    }
}
