// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Microsoft.CodeAnalysis.Testing
{
    internal class VisualBasicTest<TAnalyzer> : Test<DefaultVerifier>
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        public override string Language => LanguageNames.VisualBasic;

        protected override string DefaultFileExt => "vb";

        protected override CompilationOptions CreateCompilationOptions()
            => new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers()
        {
            yield return new TAnalyzer();
        }

        public override Type SyntaxKindType => typeof(SyntaxKind);

        protected override IEnumerable<CodeFixProvider> GetCodeFixProviders()
        {
            yield return new EmptyCodeFixProvider();
        }
    }
}
