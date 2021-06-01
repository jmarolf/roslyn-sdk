# The Difference Between Value, Text, and ValueText Properties on SyntaxToken

```csharp
using System;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

string source = @"
using System;
var @long = 1L;
Console.WriteLine(@long);
";

SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(source);

// Get token corresponding to identifier '@long' above.
SyntaxToken variableNameToken = tree.GetRoot().FindToken(source.IndexOf("@long"));

SyntaxToken constantValueToken = tree.GetRoot().FindToken(source.IndexOf("1L"));

Console.WriteLine(variableNameToken.Value.GetType().Name); // String
Console.WriteLine((string)variableNameToken.Value); // long
Console.WriteLine(variableNameToken.ValueText); // long
Console.WriteLine(variableNameToken.Text); // @long
Console.WriteLine(variableNameToken.ToString()); // @long

Console.WriteLine(constantValueToken.Value.GetType().Name); // Int64
Console.WriteLine((long)constantValueToken.Value); // 1
Console.WriteLine(constantValueToken.ValueText); // 1
Console.WriteLine(constantValueToken.Text); // 1L
Console.WriteLine(constantValueToken.ToString()); // 1L
```