using System;
using System.Collections.Generic;
using System.Text;

namespace SyntaxVisualizer.CLI
{
    public static class SyntaxKindExtensions
    {
        public static string AsString<T>(this T @enum) => Enum.GetName(typeof(T), @enum);
    }
}
