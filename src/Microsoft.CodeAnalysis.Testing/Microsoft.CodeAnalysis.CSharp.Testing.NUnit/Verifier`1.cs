// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace Microsoft.CodeAnalysis.CSharp.Testing.NUnit
{
    public class Verifier<TAnalyzer> : CSharpVerifier<TAnalyzer, EmptyCodeFixProvider, NUnitVerifier>
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
    }
}
