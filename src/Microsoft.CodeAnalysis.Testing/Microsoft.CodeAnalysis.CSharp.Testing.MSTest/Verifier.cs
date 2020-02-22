// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis.CSharp.Testing.MSTest
{
    public static class Verifier
    {
        public static Verifier<TAnalyzer, TCodeFix> Create<TAnalyzer, TCodeFix>()
            where TAnalyzer : DiagnosticAnalyzer, new()
            where TCodeFix : CodeFixProvider, new()
        {
            return new Verifier<TAnalyzer, TCodeFix>();
        }

        public static Verifier<TAnalyzer> Create<TAnalyzer>()
            where TAnalyzer : DiagnosticAnalyzer, new()
        {
            return new Verifier<TAnalyzer>();
        }
    }
}
