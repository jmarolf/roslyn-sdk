using System;
using System.Text;

namespace SyntaxVisualizer.CLI
{
    internal class SyntaxVisualizerOptions
    {
        public string FilePath { get; }


        private LanguageName? _languageName;
        public LanguageName LanguageName => _languageName.Value;

        private bool? _isSingleLine;
        public bool IsSingleLine => _isSingleLine ?? false;

        public SyntaxVisualizerOptions() { }

        private SyntaxVisualizerOptions(string file, LanguageName? name, bool? singleLineOn)
        {
            FilePath = file;
            _languageName = name;
            _isSingleLine = singleLineOn;
        }

        public SyntaxVisualizerOptions WithFile(string file)
        {
            return new SyntaxVisualizerOptions(file, _languageName, _isSingleLine);
        }

        internal SyntaxVisualizerOptions WithLanguage(LanguageName languageName)
        {
            return new SyntaxVisualizerOptions(FilePath, languageName, _isSingleLine);
        }

        internal SyntaxVisualizerOptions WithSingleLine(bool singleLineOn)
        {
            return new SyntaxVisualizerOptions(FilePath, _languageName, singleLineOn);
        }

        public bool Valid => !string.IsNullOrEmpty(FilePath) &&
                             _languageName != null;
        public string ValidationErrors
        {
            get
            {
                var builder = new StringBuilder();
                if (string.IsNullOrEmpty(FilePath))
                {
                    builder.AppendLine($"{nameof(FilePath).ToLowerInvariant()} is required");
                }

                if (_languageName == null)
                {
                    builder.AppendLine($"{nameof(LanguageName).ToLowerInvariant()} is required");
                }
                return builder.ToString();
            }
        }
    }
}
