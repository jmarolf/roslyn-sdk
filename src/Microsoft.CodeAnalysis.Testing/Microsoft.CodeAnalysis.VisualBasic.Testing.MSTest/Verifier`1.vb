' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports Microsoft.CodeAnalysis.Diagnostics
Imports Microsoft.CodeAnalysis.Testing
Imports Microsoft.CodeAnalysis.Testing.Verifiers

Public Class Verifier(Of TAnalyzer As {DiagnosticAnalyzer, New})
    Inherits VisualBasicVerifier(Of TAnalyzer, EmptyCodeFixProvider, MSTestVerifier)
End Class
